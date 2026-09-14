using LatencyPilot.Core.System;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;

namespace LatencyPilot.Service;

internal static class GpuInterruptAffinityMutationContract
{
    internal const string Kind = "gpu-interrupt-affinity";
}

internal sealed record GpuInterruptAffinityMutationStepResult(
    MutationJournalEntry JournalEntry,
    GpuDeviceRestartResult? Restart,
    bool OriginalStateRestored);

internal sealed class GpuInterruptAffinityMutationTransaction
{
    private readonly MutationJournal journal;

    internal GpuInterruptAffinityMutationTransaction(MutationJournal journal)
    {
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
    }

    internal MutationJournalEntry Prepare(
        string deviceInstanceId,
        GpuInterruptAffinityCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        ArgumentNullException.ThrowIfNull(candidate);
        ValidateCandidateAgainstCurrentTopology(candidate);

        var original = GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);
        GpuInterruptAffinityApplicability.EnsureSupportedOriginalState(original);
        if (GpuInterruptAffinityStateComparer.MatchesCandidate(original, candidate))
        {
            throw new InvalidOperationException(
                "The requested GPU affinity candidate already matches stored device policy; a no-op mutation will not be journaled.");
        }

        return journal.CreatePrepared(
            Guid.NewGuid(),
            GpuInterruptAffinityMutationContract.Kind,
            original.DeviceInstanceId,
            GpuInterruptAffinityJournalCodec.SerializeOriginal(original),
            GpuInterruptAffinityJournalCodec.SerializeCandidate(candidate));
    }

    internal GpuInterruptAffinityMutationStepResult ApplyAndActivate(Guid experimentId)
    {
        var prepared = GetRequiredGpuEntry(experimentId, MutationJournalState.Prepared);
        var original = DeserializeOriginal(prepared);
        var candidate = GpuInterruptAffinityJournalCodec.DeserializeCandidate(prepared.CandidateStateJson);

        try
        {
            GpuInterruptAffinityApplicability.EnsureSupportedOriginalState(original);
            ValidateCandidateAgainstCurrentTopology(candidate);
            var current = GpuInterruptAffinityPolicyStore.Capture(original.DeviceInstanceId);
            EnsurePreparedStateStillCurrent(prepared, original, current);
        }
        catch (MutationPreparedAbortException)
        {
            throw;
        }
        catch (Exception preflightFailure)
        {
            AbortPrepared(
                prepared,
                $"GPU affinity pre-apply validation failed before any write: {preflightFailure.GetType().Name}: {preflightFailure.Message}",
                preflightFailure);
        }

        var applying = journal.Transition(
            prepared.ExperimentId,
            prepared.Revision,
            MutationJournalState.Prepared,
            MutationJournalState.Applying);

        try
        {
            // Registry state is external to the SQLite transaction. Re-read after
            // the durable state transition and immediately before the write so a
            // user/driver change in the preflight window is not overwritten.
            var immediatelyBeforeWrite = GpuInterruptAffinityPolicyStore.Capture(original.DeviceInstanceId);
            if (!GpuInterruptAffinityStateComparer.MatchesOriginal(immediatelyBeforeWrite, original) ||
                !DriverVersionMatches(immediatelyBeforeWrite, original))
            {
                throw new InvalidOperationException(
                    "GPU affinity state or driver version changed after preflight; candidate write was refused.");
            }

            GpuInterruptAffinityPolicyStore.Apply(original, candidate);
            var stored = GpuInterruptAffinityPolicyStore.Capture(original.DeviceInstanceId);
            if (!GpuInterruptAffinityStateComparer.MatchesCandidate(stored, candidate))
            {
                throw new InvalidOperationException(
                    "GPU affinity candidate was written but could not be verified from stored device state.");
            }

            var restart = GpuDeviceRestartCoordinator.RestartAfterConfigurationChange(original.DeviceInstanceId);
            if (!restart.RestartedInPlace)
            {
                var reason = restart.SystemRestartRequired
                    ? "GPU configuration change is stored, but Windows requires a system restart before activation can be trusted."
                    : "GPU configuration-change restart completed without a healthy started devnode; recovery is required.";
                var recovery = journal.Transition(
                    applying.ExperimentId,
                    applying.Revision,
                    MutationJournalState.Applying,
                    MutationJournalState.RecoveryRequired,
                    reason);
                return new GpuInterruptAffinityMutationStepResult(recovery, restart, false);
            }

            var applied = journal.Transition(
                applying.ExperimentId,
                applying.Revision,
                MutationJournalState.Applying,
                MutationJournalState.Applied);
            return new GpuInterruptAffinityMutationStepResult(applied, restart, false);
        }
        catch (Exception applyFailure)
        {
            var recovery = TryMarkRecoveryRequired(applying, applyFailure);
            GpuInterruptAffinityMutationStepResult rollback;
            try
            {
                rollback = RollbackFromRecovery(recovery, original, candidate);
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(
                    "GPU affinity apply failed and rollback did not complete cleanly.",
                    applyFailure,
                    rollbackFailure);
            }

            if (rollback.JournalEntry.State != MutationJournalState.Reverted)
            {
                throw new AggregateException(
                    "GPU affinity apply failed and rollback remains unresolved; a restart or recovery pass is still required.",
                    applyFailure,
                    new InvalidOperationException(
                        rollback.JournalEntry.FailureReason ??
                        "Rollback did not reach the verified Reverted state."));
            }

            throw new InvalidOperationException(
                "GPU affinity apply failed; the transaction restored and reactivated the captured original state.",
                applyFailure);
        }
    }

    internal GpuInterruptAffinityMutationStepResult RollbackAndActivate(Guid experimentId)
    {
        var entry = GetRequiredGpuEntry(experimentId);
        var original = DeserializeOriginal(entry);
        var candidate = GpuInterruptAffinityJournalCodec.DeserializeCandidate(entry.CandidateStateJson);

        return entry.State switch
        {
            MutationJournalState.Applied or
            MutationJournalState.Measuring or
            MutationJournalState.AwaitingDecision =>
                RollbackFromActive(entry, original, candidate),
            MutationJournalState.RecoveryRequired =>
                RollbackFromRecovery(entry, original, candidate),
            MutationJournalState.Reverting =>
                RestoreFromReverting(entry, original, candidate),
            MutationJournalState.Reverted =>
                new GpuInterruptAffinityMutationStepResult(entry, null, true),
            _ => throw new InvalidOperationException(
                $"GPU affinity experiment {entry.ExperimentId:D} cannot be rolled back from journal state {entry.State}."),
        };
    }

    private GpuInterruptAffinityMutationStepResult RollbackFromActive(
        MutationJournalEntry entry,
        GpuInterruptAffinitySnapshot original,
        GpuInterruptAffinityCandidate candidate)
    {
        var reverting = journal.Transition(
            entry.ExperimentId,
            entry.Revision,
            entry.State,
            MutationJournalState.Reverting);
        return RestoreFromReverting(reverting, original, candidate);
    }

    private GpuInterruptAffinityMutationStepResult RollbackFromRecovery(
        MutationJournalEntry recovery,
        GpuInterruptAffinitySnapshot original,
        GpuInterruptAffinityCandidate candidate)
    {
        if (recovery.State != MutationJournalState.RecoveryRequired)
        {
            throw new InvalidOperationException(
                $"Expected RecoveryRequired before recovery rollback, found {recovery.State}.");
        }

        var reverting = journal.Transition(
            recovery.ExperimentId,
            recovery.Revision,
            MutationJournalState.RecoveryRequired,
            MutationJournalState.Reverting);
        return RestoreFromReverting(reverting, original, candidate);
    }

    private GpuInterruptAffinityMutationStepResult RestoreFromReverting(
        MutationJournalEntry reverting,
        GpuInterruptAffinitySnapshot original,
        GpuInterruptAffinityCandidate candidate)
    {
        try
        {
            var current = GpuInterruptAffinityPolicyStore.Capture(original.DeviceInstanceId);
            if (!DriverVersionMatches(current, original))
            {
                return ReturnToRecoveryWithoutWrite(
                    reverting,
                    "Rollback refused to restore an old GPU affinity snapshot because the target display-driver version changed.");
            }

            var matchesOriginal = GpuInterruptAffinityStateComparer.MatchesOriginal(current, original);
            var matchesCandidate = GpuInterruptAffinityStateComparer.MatchesCandidate(current, candidate);
            if (!matchesOriginal && !matchesCandidate)
            {
                return ReturnToRecoveryWithoutWrite(
                    reverting,
                    "Rollback refused to overwrite GPU affinity state because current storage matches neither the journaled original nor the experiment candidate.");
            }

            if (!matchesOriginal)
            {
                GpuInterruptAffinityPolicyStore.Restore(original);
            }

            var stored = GpuInterruptAffinityPolicyStore.Capture(original.DeviceInstanceId);
            if (!GpuInterruptAffinityStateComparer.MatchesOriginal(stored, original))
            {
                throw new InvalidOperationException(
                    "GPU affinity original state was written but could not be verified from stored device state.");
            }

            var restart = GpuDeviceRestartCoordinator.RestartAfterConfigurationChange(original.DeviceInstanceId);
            if (!restart.RestartedInPlace)
            {
                var reason = restart.SystemRestartRequired
                    ? "Original GPU affinity state is restored in storage, but Windows requires a system restart before rollback activation can be trusted."
                    : "Original GPU affinity state is restored in storage, but the device did not return as a healthy started devnode.";
                var recovery = journal.Transition(
                    reverting.ExperimentId,
                    reverting.Revision,
                    MutationJournalState.Reverting,
                    MutationJournalState.RecoveryRequired,
                    reason);
                return new GpuInterruptAffinityMutationStepResult(recovery, restart, true);
            }

            var reverted = journal.Transition(
                reverting.ExperimentId,
                reverting.Revision,
                MutationJournalState.Reverting,
                MutationJournalState.Reverted);
            return new GpuInterruptAffinityMutationStepResult(reverted, restart, true);
        }
        catch (Exception rollbackFailure)
        {
            TryReturnToRecoveryRequired(reverting, rollbackFailure);
            throw new InvalidOperationException(
                "GPU affinity rollback failed before a verified active original state was established.",
                rollbackFailure);
        }
    }

    private GpuInterruptAffinityMutationStepResult ReturnToRecoveryWithoutWrite(
        MutationJournalEntry reverting,
        string reason)
    {
        var recovery = journal.Transition(
            reverting.ExperimentId,
            reverting.Revision,
            MutationJournalState.Reverting,
            MutationJournalState.RecoveryRequired,
            BoundFailureReason(reason));
        return new GpuInterruptAffinityMutationStepResult(recovery, null, false);
    }

    private void EnsurePreparedStateStillCurrent(
        MutationJournalEntry prepared,
        GpuInterruptAffinitySnapshot original,
        GpuInterruptAffinitySnapshot current)
    {
        if (!GpuInterruptAffinityStateComparer.MatchesOriginal(current, original))
        {
            AbortPrepared(
                prepared,
                "Stored GPU affinity state changed after the exact original snapshot was journaled; apply was not attempted.");
        }

        if (!DriverVersionMatches(current, original))
        {
            AbortPrepared(
                prepared,
                "Display-driver version changed after the mutation snapshot was prepared; apply was not attempted.");
        }
    }

    private void AbortPrepared(
        MutationJournalEntry prepared,
        string reason,
        Exception? innerException = null)
    {
        var boundedReason = BoundFailureReason(reason);
        _ = journal.Transition(
            prepared.ExperimentId,
            prepared.Revision,
            MutationJournalState.Prepared,
            MutationJournalState.AbortedBeforeApply,
            boundedReason);
        throw new MutationPreparedAbortException(boundedReason, innerException);
    }

    private MutationJournalEntry TryMarkRecoveryRequired(
        MutationJournalEntry applying,
        Exception failure)
    {
        try
        {
            return journal.Transition(
                applying.ExperimentId,
                applying.Revision,
                MutationJournalState.Applying,
                MutationJournalState.RecoveryRequired,
                FormatFailure("apply", failure));
        }
        catch (Exception journalFailure)
        {
            throw new AggregateException(
                "GPU affinity apply failed and the durable journal could not be moved to RecoveryRequired.",
                failure,
                journalFailure);
        }
    }

    private void TryReturnToRecoveryRequired(
        MutationJournalEntry reverting,
        Exception failure)
    {
        try
        {
            _ = journal.Transition(
                reverting.ExperimentId,
                reverting.Revision,
                MutationJournalState.Reverting,
                MutationJournalState.RecoveryRequired,
                FormatFailure("rollback", failure));
        }
        catch
        {
            // Startup recovery still inspects any unresolved journal entry and
            // actual machine state before any future mutation is allowed.
        }
    }

    private MutationJournalEntry GetRequiredGpuEntry(
        Guid experimentId,
        MutationJournalState? requiredState = null)
    {
        var entry = journal.TryGet(experimentId)
            ?? throw new InvalidOperationException(
                $"GPU affinity mutation journal entry {experimentId:D} was not found.");
        if (!string.Equals(entry.Kind, GpuInterruptAffinityMutationContract.Kind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Mutation journal entry {experimentId:D} is kind '{entry.Kind}', not GPU interrupt affinity.");
        }

        if (requiredState is not null && entry.State != requiredState.Value)
        {
            throw new InvalidOperationException(
                $"GPU affinity mutation journal entry {experimentId:D} is {entry.State}; expected {requiredState.Value}.");
        }

        return entry;
    }

    private static GpuInterruptAffinitySnapshot DeserializeOriginal(MutationJournalEntry entry)
    {
        var original = GpuInterruptAffinityJournalCodec.DeserializeOriginal(entry.OriginalStateJson);
        if (!string.Equals(original.DeviceInstanceId, entry.TargetId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "GPU affinity journal target does not match its captured original-state device instance ID.");
        }

        return original;
    }

    private static void ValidateCandidateAgainstCurrentTopology(GpuInterruptAffinityCandidate candidate)
    {
        var topology = ProcessorTopologyReader.Capture();
        var validated = GpuInterruptAffinityCandidate.Create(
            topology,
            new LogicalProcessorId(candidate.ProcessorGroup, candidate.ProcessorNumber));
        if (validated.AffinityMask != candidate.AffinityMask)
        {
            throw new InvalidDataException(
                "GPU affinity candidate no longer matches current processor topology.");
        }
    }

    private static bool DriverVersionMatches(
        GpuInterruptAffinitySnapshot current,
        GpuInterruptAffinitySnapshot original) =>
        string.Equals(current.DriverVersion, original.DriverVersion, StringComparison.OrdinalIgnoreCase);

    private static string FormatFailure(string operation, Exception exception) =>
        BoundFailureReason($"GPU affinity {operation} failed: {exception.GetType().Name}: {exception.Message}");

    private static string BoundFailureReason(string text) =>
        text.Length <= 2048 ? text : text[..2048];

    private sealed class MutationPreparedAbortException : InvalidOperationException
    {
        internal MutationPreparedAbortException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}

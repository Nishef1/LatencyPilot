using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;

namespace LatencyPilot.Service;

internal sealed record MutationRecoveryExecutionResult(
    MutationRecoveryInspection Inspection,
    MutationJournalEntry JournalEntry,
    GpuDeviceRestartResult? Restart,
    bool OriginalStateRestored);

internal sealed class MutationRecoveryExecutor
{
    private readonly MutationJournal journal;

    internal MutationRecoveryExecutor(MutationJournal journal)
    {
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
    }

    internal MutationRecoveryExecutionResult Execute(Guid experimentId)
    {
        if (experimentId == Guid.Empty)
        {
            throw new ArgumentException("ExperimentId must not be empty.", nameof(experimentId));
        }

        var entry = journal.TryGet(experimentId)
            ?? throw new InvalidOperationException(
                $"Mutation journal entry {experimentId:D} was not found.");

        // Recovery decisions are never executed from stale startup intent. Re-read
        // and classify actual machine state immediately before selecting an action.
        var inspection = MutationRecoveryAssessment.Inspect(entry);

        return inspection.RecoveryPlan.Action switch
        {
            MutationRecoveryAction.None =>
                new MutationRecoveryExecutionResult(inspection, entry, null, entry.State == MutationJournalState.Reverted),

            MutationRecoveryAction.AbortPreparedWithoutApply =>
                AbortPreparedWithoutApply(inspection),

            MutationRecoveryAction.FinalizeVerifiedRollback =>
                FinalizeVerifiedRollback(inspection),

            MutationRecoveryAction.RestoreOriginalState =>
                RestoreOriginalState(inspection),

            MutationRecoveryAction.ManualInterventionRequired =>
                throw new InvalidOperationException(
                    $"Automatic mutation recovery refused: {inspection.RecoveryPlan.Reason}"),

            _ => throw new InvalidOperationException(
                $"Unknown mutation recovery action {inspection.RecoveryPlan.Action}."),
        };
    }

    private MutationRecoveryExecutionResult AbortPreparedWithoutApply(
        MutationRecoveryInspection inspection)
    {
        var entry = inspection.Entry;
        if (entry.State != MutationJournalState.Prepared)
        {
            throw new InvalidOperationException(
                $"Abort-before-apply recovery requires Prepared state, found {entry.State}.");
        }

        var aborted = journal.Transition(
            entry.ExperimentId,
            entry.Revision,
            MutationJournalState.Prepared,
            MutationJournalState.AbortedBeforeApply,
            BoundReason(inspection.RecoveryPlan.Reason));

        return new MutationRecoveryExecutionResult(inspection, aborted, null, true);
    }

    private MutationRecoveryExecutionResult RestoreOriginalState(
        MutationRecoveryInspection inspection)
    {
        var entry = inspection.Entry;
        if (entry.State == MutationJournalState.Applying)
        {
            entry = journal.Transition(
                entry.ExperimentId,
                entry.Revision,
                MutationJournalState.Applying,
                MutationJournalState.RecoveryRequired,
                "Recovery re-read actual GPU affinity state and found the experiment candidate active; rollback is required.");
        }

        if (entry.State is not (
                MutationJournalState.Applied or
                MutationJournalState.Measuring or
                MutationJournalState.AwaitingDecision or
                MutationJournalState.Reverting or
                MutationJournalState.RecoveryRequired))
        {
            throw new InvalidOperationException(
                $"GPU affinity recovery cannot restore original state from journal state {entry.State}.");
        }

        var transaction = new GpuInterruptAffinityMutationTransaction(journal);
        var rollback = transaction.RollbackAndActivate(entry.ExperimentId);
        return new MutationRecoveryExecutionResult(
            inspection,
            rollback.JournalEntry,
            rollback.Restart,
            rollback.OriginalStateRestored);
    }

    private MutationRecoveryExecutionResult FinalizeVerifiedRollback(
        MutationRecoveryInspection inspection)
    {
        var original = DeserializeOriginal(inspection.Entry);
        var reverting = EnterReverting(inspection.Entry);

        try
        {
            EnsureStoredOriginalStillCurrent(reverting, original);

            var restart = GpuDeviceRestartCoordinator.RestartAfterConfigurationChange(original.DeviceInstanceId);
            if (!restart.RestartedInPlace)
            {
                var reason = restart.SystemRestartRequired
                    ? "Stored GPU affinity already matches the captured original, but Windows requires a system restart before rollback activation can be trusted."
                    : "Stored GPU affinity already matches the captured original, but the display adapter did not return as a healthy started devnode.";
                var recovery = journal.Transition(
                    reverting.ExperimentId,
                    reverting.Revision,
                    MutationJournalState.Reverting,
                    MutationJournalState.RecoveryRequired,
                    reason);
                return new MutationRecoveryExecutionResult(inspection, recovery, restart, true);
            }

            EnsureStoredOriginalStillCurrent(reverting, original);
            var reverted = journal.Transition(
                reverting.ExperimentId,
                reverting.Revision,
                MutationJournalState.Reverting,
                MutationJournalState.Reverted);
            return new MutationRecoveryExecutionResult(inspection, reverted, restart, true);
        }
        catch (Exception exception)
        {
            TryReturnToRecoveryRequired(reverting, exception);
            throw new InvalidOperationException(
                "GPU affinity recovery could not verify the captured original state as active.",
                exception);
        }
    }

    private MutationJournalEntry EnterReverting(MutationJournalEntry entry)
    {
        if (entry.State == MutationJournalState.Reverting)
        {
            return entry;
        }

        if (entry.State == MutationJournalState.Applying)
        {
            entry = journal.Transition(
                entry.ExperimentId,
                entry.Revision,
                MutationJournalState.Applying,
                MutationJournalState.RecoveryRequired,
                "Recovery re-read stored GPU affinity as the captured original; activation still requires verification before terminalizing rollback.");
        }

        return entry.State switch
        {
            MutationJournalState.Applied or
            MutationJournalState.Measuring or
            MutationJournalState.AwaitingDecision or
            MutationJournalState.RecoveryRequired =>
                journal.Transition(
                    entry.ExperimentId,
                    entry.Revision,
                    entry.State,
                    MutationJournalState.Reverting),

            _ => throw new InvalidOperationException(
                $"Verified-original recovery cannot enter rollback from journal state {entry.State}."),
        };
    }

    private static void EnsureStoredOriginalStillCurrent(
        MutationJournalEntry entry,
        GpuInterruptAffinitySnapshot original)
    {
        var actual = GpuInterruptAffinityPolicyStore.Capture(entry.TargetId);
        if (!string.Equals(actual.DriverVersion, original.DriverVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Display-driver version changed after the captured original state; recovery will not trust the old snapshot.");
        }

        if (!GpuInterruptAffinityStateComparer.MatchesOriginal(actual, original))
        {
            throw new InvalidOperationException(
                "Stored GPU affinity no longer matches the captured original state during recovery verification.");
        }
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

    private void TryReturnToRecoveryRequired(
        MutationJournalEntry reverting,
        Exception failure)
    {
        try
        {
            var current = journal.TryGet(reverting.ExperimentId);
            if (current?.State == MutationJournalState.Reverting)
            {
                _ = journal.Transition(
                    current.ExperimentId,
                    current.Revision,
                    MutationJournalState.Reverting,
                    MutationJournalState.RecoveryRequired,
                    BoundReason($"GPU affinity recovery verification failed: {failure.GetType().Name}: {failure.Message}"));
            }
        }
        catch
        {
            // Leaving an unresolved Reverting entry is still fail-closed. Startup
            // inspection will re-read machine state before any future recovery.
        }
    }

    private static string BoundReason(string reason) =>
        reason.Length <= 2048 ? reason : reason[..2048];
}

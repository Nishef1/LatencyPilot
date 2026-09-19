using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;

namespace LatencyPilot.Service;

internal static class DeviceInterruptMutationContract
{
    internal const string MsiKind = "device-msi-enable";
    internal const string XhciAffinityKind = "xhci-interrupt-affinity";

    internal static bool IsSupportedKind(string kind) =>
        string.Equals(kind, MsiKind, StringComparison.Ordinal) ||
        string.Equals(kind, XhciAffinityKind, StringComparison.Ordinal);
}

internal sealed record DeviceInterruptPrepareResult(bool NoWriteRequired, MutationJournalEntry? Entry, DeviceInterruptConfigurationSnapshot Original);
internal sealed record DeviceInterruptMutationStepResult(MutationJournalEntry Entry, DeviceConfigurationRestartResult? Restart, bool OriginalStateRestored);

internal sealed class DeviceInterruptMutationTransaction
{
    private readonly MutationJournal journal;
    internal DeviceInterruptMutationTransaction(MutationJournal journal) => this.journal = journal ?? throw new ArgumentNullException(nameof(journal));

    internal DeviceInterruptPrepareResult PrepareMsi(string deviceInstanceId)
    {
        using var guard = MutationOperationLock.Acquire();
        var original = DeviceInterruptConfigurationStore.Capture(deviceInstanceId);
        DeviceInterruptConfigurationStore.EnsureMsiApplicable(original);
        if (DeviceInterruptConfigurationStore.IsMsiEnabled(original)) return new(true, null, original);
        var entry = journal.CreatePrepared(Guid.NewGuid(), DeviceInterruptMutationContract.MsiKind, original.DeviceInstanceId,
            DeviceInterruptMutationJournalCodec.SerializeOriginal(original), DeviceInterruptMutationJournalCodec.SerializeCandidate(DeviceInterruptMutationCandidate.EnableMsi()));
        return new(false, entry, original);
    }

    internal DeviceInterruptPrepareResult PrepareXhciAffinity(string deviceInstanceId, DeviceInterruptAffinityCandidate candidate)
    {
        using var guard = MutationOperationLock.Acquire();
        var original = DeviceInterruptConfigurationStore.Capture(deviceInstanceId);
        if (original.TargetKind != DeviceInterruptTargetKind.XhciController) throw new NotSupportedException("xHCI affinity target is not a USBXHCI controller.");
        if (DeviceInterruptConfigurationStore.IsXhciAffinityStored(original, candidate)) return new(true, null, original);
        var c = DeviceInterruptMutationCandidate.XhciAffinity(candidate);
        var entry = journal.CreatePrepared(Guid.NewGuid(), DeviceInterruptMutationContract.XhciAffinityKind, original.DeviceInstanceId,
            DeviceInterruptMutationJournalCodec.SerializeOriginal(original), DeviceInterruptMutationJournalCodec.SerializeCandidate(c));
        return new(false, entry, original);
    }

    internal DeviceInterruptMutationStepResult Apply(Guid experimentId)
    {
        using var guard = MutationOperationLock.Acquire();
        var prepared = GetEntry(experimentId, MutationJournalState.Prepared);
        var original = DeviceInterruptMutationJournalCodec.DeserializeOriginal(prepared.OriginalStateJson);
        var candidate = DeviceInterruptMutationJournalCodec.DeserializeCandidate(prepared.CandidateStateJson);
        var current = DeviceInterruptConfigurationStore.Capture(original.DeviceInstanceId);
        if (!DeviceInterruptConfigurationStore.MatchesOriginal(current, original, candidate.Operation))
            return AbortPrepared(prepared, "Interrupt configuration or driver changed after the exact original snapshot; no write was attempted.");
        var applying = journal.Transition(prepared.ExperimentId, prepared.Revision, MutationJournalState.Prepared, MutationJournalState.Applying);
        try
        {
            ApplyCandidate(original, candidate);
            var restart = DeviceConfigurationRestartCoordinator.RestartAfterConfigurationChange(original.DeviceInstanceId);
            if (restart.SystemRestartRequired)
            {
                var pending = journal.Transition(applying.ExperimentId, applying.Revision, MutationJournalState.Applying,
                    MutationJournalState.ApplyRebootPending, "Candidate is stored; Windows requires a system reboot before activation can be verified.");
                return new(pending, restart, false);
            }
            if (!restart.RestartedInPlace || !CandidateStored(original.DeviceInstanceId, candidate))
            {
                var recovery = journal.Transition(applying.ExperimentId, applying.Revision, MutationJournalState.Applying,
                    MutationJournalState.RecoveryRequired, "Candidate could not be verified active after device restart.");
                return new(recovery, restart, false);
            }
            var applied = journal.Transition(applying.ExperimentId, applying.Revision, MutationJournalState.Applying, MutationJournalState.Applied);
            return new(applied, restart, false);
        }
        catch (Exception ex)
        {
            var recovery = journal.Transition(applying.ExperimentId, applying.Revision, MutationJournalState.Applying,
                MutationJournalState.RecoveryRequired, Bound(ex));
            return new(recovery, null, false);
        }
    }

    internal MutationJournalEntry KeepVerified(Guid experimentId, bool measurementVerified)
    {
        using var guard = MutationOperationLock.Acquire();
        if (!measurementVerified) throw new InvalidOperationException("Candidate cannot be kept without an explicit verified measurement stage.");
        var applied = GetEntry(experimentId, MutationJournalState.Applied);
        var measuring = journal.Transition(applied.ExperimentId, applied.Revision, MutationJournalState.Applied, MutationJournalState.Measuring);
        var decision = journal.Transition(measuring.ExperimentId, measuring.Revision, MutationJournalState.Measuring, MutationJournalState.AwaitingDecision);
        return journal.Transition(decision.ExperimentId, decision.Revision, MutationJournalState.AwaitingDecision, MutationJournalState.Kept);
    }

    internal DeviceInterruptMutationStepResult ResumeAfterReboot(Guid experimentId)
    {
        using var guard = MutationOperationLock.Acquire();
        var entry = GetEntry(experimentId);
        var original = DeviceInterruptMutationJournalCodec.DeserializeOriginal(entry.OriginalStateJson);
        var candidate = DeviceInterruptMutationJournalCodec.DeserializeCandidate(entry.CandidateStateJson);
        if (entry.State == MutationJournalState.ApplyRebootPending)
        {
            var next = CandidateStored(original.DeviceInstanceId, candidate)
                ? journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.Applied)
                : journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.RecoveryRequired, "Candidate is not stored after reboot.");
            return new(next, null, false);
        }
        if (entry.State == MutationJournalState.RollbackRebootPending)
        {
            var restored = DeviceInterruptConfigurationStore.MatchesOriginal(DeviceInterruptConfigurationStore.Capture(original.DeviceInstanceId), original, candidate.Operation);
            var next = restored
                ? journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.Reverted)
                : journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.RecoveryRequired, "Original state is not restored after reboot.");
            return new(next, null, restored);
        }
        throw new InvalidOperationException($"Experiment is not awaiting reboot verification: {entry.State}.");
    }

    internal DeviceInterruptMutationStepResult Rollback(Guid experimentId)
    {
        using var guard = MutationOperationLock.Acquire();
        var entry = GetEntry(experimentId);
        if (entry.State == MutationJournalState.Prepared)
        {
            var aborted = journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.AbortedBeforeApply, "Prepared experiment cancelled before any write.");
            return new(aborted, null, true);
        }
        var original = DeviceInterruptMutationJournalCodec.DeserializeOriginal(entry.OriginalStateJson);
        var candidate = DeviceInterruptMutationJournalCodec.DeserializeCandidate(entry.CandidateStateJson);
        if (entry.State is MutationJournalState.Reverted or MutationJournalState.AbortedBeforeApply) return new(entry, null, true);

        var currentBeforeRollback = DeviceInterruptConfigurationStore.Capture(original.DeviceInstanceId);
        var matchesOriginal = DeviceInterruptConfigurationStore.MatchesOriginal(currentBeforeRollback, original, candidate.Operation);
        var matchesCandidate = DeviceInterruptConfigurationStore.MatchesCandidate(currentBeforeRollback, original, candidate);
        if (!matchesOriginal && !matchesCandidate)
        {
            throw new InvalidOperationException(
                "Rollback refused because current interrupt configuration matches neither the captured original nor the LatencyPilot candidate.");
        }
        if (entry.State == MutationJournalState.Applying)
        {
            entry = journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.RecoveryRequired,
                "Recovery entered while apply ownership was incomplete; actual state was re-read before rollback.");
        }
        var reverting = entry.State == MutationJournalState.Reverting ? entry :
            journal.Transition(entry.ExperimentId, entry.Revision, entry.State, MutationJournalState.Reverting);
        try
        {
            Restore(original, candidate.Operation);
            var restart = DeviceConfigurationRestartCoordinator.RestartAfterConfigurationChange(original.DeviceInstanceId);
            if (restart.SystemRestartRequired)
            {
                var pending = journal.Transition(reverting.ExperimentId, reverting.Revision, MutationJournalState.Reverting,
                    MutationJournalState.RollbackRebootPending, "Original state is stored; Windows requires reboot before rollback activation can be verified.");
                return new(pending, restart, true);
            }
            var restored = DeviceInterruptConfigurationStore.MatchesOriginal(DeviceInterruptConfigurationStore.Capture(original.DeviceInstanceId), original, candidate.Operation);
            var next = restored && restart.RestartedInPlace
                ? journal.Transition(reverting.ExperimentId, reverting.Revision, MutationJournalState.Reverting, MutationJournalState.Reverted)
                : journal.Transition(reverting.ExperimentId, reverting.Revision, MutationJournalState.Reverting, MutationJournalState.RecoveryRequired, "Rollback could not verify active original state.");
            return new(next, restart, restored);
        }
        catch (Exception ex)
        {
            var recovery = journal.Transition(reverting.ExperimentId, reverting.Revision, MutationJournalState.Reverting, MutationJournalState.RecoveryRequired, Bound(ex));
            return new(recovery, null, false);
        }
    }

    internal DeviceInterruptMutationStepResult Recover(Guid experimentId)
    {
        using var guard = MutationOperationLock.Acquire();
        var entry = GetEntry(experimentId);
        var inspection = DeviceInterruptRecoveryInspector.Inspect(entry);
        return inspection.Action switch
        {
            DeviceInterruptRecoveryAction.None => new(entry, null, entry.State == MutationJournalState.Reverted),
            DeviceInterruptRecoveryAction.AbortPreparedWithoutWrite => Rollback(experimentId),
            DeviceInterruptRecoveryAction.ResumeAfterReboot => ResumeAfterReboot(experimentId),
            DeviceInterruptRecoveryAction.RestoreOriginalState => Rollback(experimentId),
            DeviceInterruptRecoveryAction.FinalizeOriginalState => Rollback(experimentId),
            DeviceInterruptRecoveryAction.ManualInterventionRequired => throw new InvalidOperationException(
                $"Automatic device-interrupt recovery refused: {inspection.Reason}"),
            _ => throw new InvalidOperationException($"Unknown device-interrupt recovery action {inspection.Action}."),
        };
    }

    private DeviceInterruptMutationStepResult AbortPrepared(MutationJournalEntry entry, string reason)
    {
        var aborted = journal.Transition(entry.ExperimentId, entry.Revision, MutationJournalState.Prepared, MutationJournalState.AbortedBeforeApply, reason);
        return new(aborted, null, true);
    }
    private MutationJournalEntry GetEntry(Guid id, params MutationJournalState[] allowed)
    {
        var entry = journal.TryGet(id)
            ?? throw new InvalidOperationException($"Mutation journal entry {id:D} was not found.");
        if (entry.Kind is not (DeviceInterruptMutationContract.MsiKind or DeviceInterruptMutationContract.XhciAffinityKind)) throw new InvalidOperationException("Journal entry is not a bounded device interrupt mutation.");
        if (allowed.Length > 0 && !allowed.Contains(entry.State)) throw new InvalidOperationException($"Expected {string.Join('/', allowed)}, found {entry.State}.");
        return entry;
    }
    private static void ApplyCandidate(DeviceInterruptConfigurationSnapshot original, DeviceInterruptMutationCandidate c)
    { if (c.Operation == DeviceInterruptMutationOperation.EnableMsi) DeviceInterruptConfigurationStore.ApplyMsi(original); else DeviceInterruptConfigurationStore.ApplyXhciAffinity(original, c.ToAffinityCandidate()); }
    private static bool CandidateStored(string id, DeviceInterruptMutationCandidate c)
    { var current = DeviceInterruptConfigurationStore.Capture(id); return c.Operation == DeviceInterruptMutationOperation.EnableMsi ? DeviceInterruptConfigurationStore.IsMsiEnabled(current) : DeviceInterruptConfigurationStore.IsXhciAffinityStored(current, c.ToAffinityCandidate()); }
    private static void Restore(DeviceInterruptConfigurationSnapshot original, DeviceInterruptMutationOperation operation)
    { if (operation == DeviceInterruptMutationOperation.EnableMsi) DeviceInterruptConfigurationStore.RestoreMsi(original); else DeviceInterruptConfigurationStore.RestoreXhciAffinity(original); }
    private static string Bound(Exception ex) { var value = $"{ex.GetType().Name}: {ex.Message}"; return value.Length <= 1024 ? value : value[..1024]; }
}

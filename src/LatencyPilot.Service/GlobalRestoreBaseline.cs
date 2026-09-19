using LatencyPilot.Persistence;

namespace LatencyPilot.Service;

internal enum GlobalRestoreBaselineMutationKind
{
    GpuInterruptAffinity = 1,
    MsiEnable = 2,
    XhciInterruptAffinity = 3,
}

internal sealed record GlobalRestoreBaselineAction(
    Guid ExperimentId,
    GlobalRestoreBaselineMutationKind Kind,
    string TargetId,
    long ExpectedRevision);

internal sealed record GlobalRestoreBaselinePlan(IReadOnlyList<GlobalRestoreBaselineAction> Actions);
internal sealed record GlobalRestoreBaselineResult(IReadOnlyList<Guid> RestoredExperimentIds)
{
    internal int RestoredCount => RestoredExperimentIds.Count;
}

internal static class GlobalRestoreBaselinePlanner
{
    internal static GlobalRestoreBaselinePlan Create(IReadOnlyList<MutationJournalEntry> retainedChanges)
    {
        ArgumentNullException.ThrowIfNull(retainedChanges);
        var actions = new List<GlobalRestoreBaselineAction>(retainedChanges.Count);
        var seen = new HashSet<Guid>();
        DateTimeOffset? previous = null;
        foreach (var entry in retainedChanges)
        {
            if (entry.State != MutationJournalState.Kept)
                throw new InvalidOperationException($"Restore original settings accepts only Kept changes; {entry.ExperimentId:D} is {entry.State}.");
            if (!seen.Add(entry.ExperimentId))
                throw new ArgumentException($"Experiment {entry.ExperimentId:D} appears more than once.", nameof(retainedChanges));
            if (previous is { } p && entry.CreatedAtUtc > p)
                throw new ArgumentException("Retained changes must be supplied newest-first.", nameof(retainedChanges));
            previous = entry.CreatedAtUtc;
            var kind = entry.Kind switch
            {
                GpuInterruptAffinityMutationContract.Kind => GlobalRestoreBaselineMutationKind.GpuInterruptAffinity,
                DeviceInterruptMutationContract.MsiKind => GlobalRestoreBaselineMutationKind.MsiEnable,
                DeviceInterruptMutationContract.XhciAffinityKind => GlobalRestoreBaselineMutationKind.XhciInterruptAffinity,
                _ => throw new NotSupportedException($"Restore original settings does not know mutation kind '{entry.Kind}'. No changes were attempted."),
            };
            actions.Add(new GlobalRestoreBaselineAction(entry.ExperimentId, kind, entry.TargetId, entry.Revision));
        }
        return new GlobalRestoreBaselinePlan(actions.AsReadOnly());
    }
}

internal sealed class GlobalRestoreBaselineExecutor
{
    private readonly MutationJournal journal;
    private readonly GpuInterruptAffinityMutationTransaction gpuTransaction;
    private readonly DeviceInterruptMutationTransaction deviceTransaction;

    internal GlobalRestoreBaselineExecutor(MutationJournal journal)
    {
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        gpuTransaction = new GpuInterruptAffinityMutationTransaction(journal);
        deviceTransaction = new DeviceInterruptMutationTransaction(journal);
    }

    internal GlobalRestoreBaselineResult Restore()
    {
        using var operationLock = MutationOperationLock.Acquire();
        var unresolved = journal.GetUnresolved();
        if (unresolved.Count != 0)
            throw new InvalidOperationException("Restore original settings is blocked while any mutation experiment is unresolved. Recover or resume it first.");

        var plan = GlobalRestoreBaselinePlanner.Create(journal.GetRetainedChangesNewestFirst());
        var restored = new List<Guid>(plan.Actions.Count);
        foreach (var action in plan.Actions)
        {
            ValidateActionStillCurrent(action);
            switch (action.Kind)
            {
                case GlobalRestoreBaselineMutationKind.GpuInterruptAffinity:
                    RestoreGpu(action);
                    break;
                case GlobalRestoreBaselineMutationKind.MsiEnable:
                case GlobalRestoreBaselineMutationKind.XhciInterruptAffinity:
                    RestoreDeviceInterrupt(action);
                    break;
                default:
                    throw new NotSupportedException($"Restore action kind {action.Kind} is not supported.");
            }
            restored.Add(action.ExperimentId);
        }
        return new GlobalRestoreBaselineResult(restored.AsReadOnly());
    }

    private void ValidateActionStillCurrent(GlobalRestoreBaselineAction action)
    {
        var current = journal.TryGet(action.ExperimentId)
            ?? throw new InvalidOperationException($"Retained experiment {action.ExperimentId:D} disappeared before restore.");
        if (current.State != MutationJournalState.Kept || current.Revision != action.ExpectedRevision ||
            !string.Equals(current.TargetId, action.TargetId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Retained experiment {action.ExperimentId:D} changed after restore preflight; no write was attempted for this action.");
    }

    private void RestoreGpu(GlobalRestoreBaselineAction action)
    {
        var current = journal.TryGet(action.ExperimentId)!;
        if (!string.Equals(current.Kind, GpuInterruptAffinityMutationContract.Kind, StringComparison.Ordinal))
            throw new InvalidOperationException("Retained GPU action changed kind after preflight.");
        _ = journal.Transition(current.ExperimentId, current.Revision, MutationJournalState.Kept, MutationJournalState.Reverting);
        var rollback = gpuTransaction.RollbackAndActivate(current.ExperimentId);
        if (rollback.JournalEntry.State != MutationJournalState.Reverted || !rollback.OriginalStateRestored)
            throw new InvalidOperationException(rollback.JournalEntry.FailureReason ?? $"GPU experiment {current.ExperimentId:D} did not reach Reverted.");
    }

    private void RestoreDeviceInterrupt(GlobalRestoreBaselineAction action)
    {
        var rollback = deviceTransaction.Rollback(action.ExperimentId);
        if (rollback.Entry.State == MutationJournalState.RollbackRebootPending)
            throw new InvalidOperationException($"Experiment {action.ExperimentId:D} restored stored policy but requires reboot verification before Restore original settings can continue.");
        if (rollback.Entry.State != MutationJournalState.Reverted || !rollback.OriginalStateRestored)
            throw new InvalidOperationException(rollback.Entry.FailureReason ?? $"Device-interrupt experiment {action.ExperimentId:D} did not reach Reverted.");
    }
}

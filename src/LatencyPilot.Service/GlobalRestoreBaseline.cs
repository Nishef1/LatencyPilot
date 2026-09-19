using LatencyPilot.Persistence;

namespace LatencyPilot.Service;

internal enum GlobalRestoreBaselineMutationKind
{
    GpuInterruptAffinity = 1,
}

internal sealed record GlobalRestoreBaselineAction(
    Guid ExperimentId,
    GlobalRestoreBaselineMutationKind Kind,
    string TargetId,
    long ExpectedRevision);

internal sealed record GlobalRestoreBaselinePlan(
    IReadOnlyList<GlobalRestoreBaselineAction> Actions);

internal sealed record GlobalRestoreBaselineResult(
    IReadOnlyList<Guid> RestoredExperimentIds)
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
        DateTimeOffset? previousCreatedAtUtc = null;

        foreach (var entry in retainedChanges)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (entry.State != MutationJournalState.Kept)
            {
                throw new InvalidOperationException(
                    $"Global Restore Baseline accepts only retained Kept changes; experiment {entry.ExperimentId:D} is {entry.State}.");
            }

            if (!seen.Add(entry.ExperimentId))
            {
                throw new ArgumentException(
                    $"Experiment {entry.ExperimentId:D} appears more than once in the retained-change set.",
                    nameof(retainedChanges));
            }

            if (previousCreatedAtUtc is { } previous && entry.CreatedAtUtc > previous)
            {
                throw new ArgumentException(
                    "Retained changes must be supplied newest-first so dependent experiments unwind in reverse application order.",
                    nameof(retainedChanges));
            }
            previousCreatedAtUtc = entry.CreatedAtUtc;

            var kind = string.Equals(
                entry.Kind,
                GpuInterruptAffinityMutationContract.Kind,
                StringComparison.Ordinal)
                ? GlobalRestoreBaselineMutationKind.GpuInterruptAffinity
                : throw new NotSupportedException(
                    $"Global Restore Baseline does not know how to restore mutation kind '{entry.Kind}'. No changes were attempted.");

            actions.Add(new GlobalRestoreBaselineAction(
                entry.ExperimentId,
                kind,
                entry.TargetId,
                entry.Revision));
        }

        return new GlobalRestoreBaselinePlan(actions.AsReadOnly());
    }
}

internal sealed class GlobalRestoreBaselineExecutor
{
    private readonly MutationJournal journal;
    private readonly GpuInterruptAffinityMutationTransaction gpuTransaction;

    internal GlobalRestoreBaselineExecutor(MutationJournal journal)
    {
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        gpuTransaction = new GpuInterruptAffinityMutationTransaction(journal);
    }

    internal GlobalRestoreBaselineResult Restore()
    {
        using var operationLock = MutationOperationLock.Acquire();
        var unresolved = journal.GetUnresolved();
        if (unresolved.Count != 0)
        {
            throw new InvalidOperationException(
                "Global Restore Baseline is blocked while any mutation experiment is unresolved. Recover that experiment first.");
        }

        // Validate every retained change before the first transition/write so an
        // unsupported future mutation kind cannot cause a partial global restore.
        var plan = GlobalRestoreBaselinePlanner.Create(journal.GetRetainedChangesNewestFirst());
        if (plan.Actions.Count == 0)
        {
            return new GlobalRestoreBaselineResult([]);
        }

        var restored = new List<Guid>(plan.Actions.Count);
        foreach (var action in plan.Actions)
        {
            switch (action.Kind)
            {
                case GlobalRestoreBaselineMutationKind.GpuInterruptAffinity:
                    RestoreGpu(action);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Global Restore Baseline action kind '{action.Kind}' is not supported.");
            }

            restored.Add(action.ExperimentId);
        }

        return new GlobalRestoreBaselineResult(restored.AsReadOnly());
    }

    private void RestoreGpu(GlobalRestoreBaselineAction action)
    {
        var current = journal.TryGet(action.ExperimentId)
            ?? throw new InvalidOperationException(
                $"Retained experiment {action.ExperimentId:D} disappeared before restore.");
        if (current.State != MutationJournalState.Kept ||
            current.Revision != action.ExpectedRevision ||
            !string.Equals(current.TargetId, action.TargetId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(current.Kind, GpuInterruptAffinityMutationContract.Kind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Retained experiment {action.ExperimentId:D} changed after Restore Baseline preflight; no write was attempted for this action.");
        }

        // Move the retained decision back into the existing rollback state machine.
        // RollbackAndActivate then re-reads actual device state and performs the
        // same verified original-state restore/restart used by normal recovery.
        _ = journal.Transition(
            current.ExperimentId,
            current.Revision,
            MutationJournalState.Kept,
            MutationJournalState.Reverting);

        var rollback = gpuTransaction.RollbackAndActivate(current.ExperimentId);
        if (rollback.JournalEntry.State != MutationJournalState.Reverted ||
            !rollback.OriginalStateRestored)
        {
            throw new InvalidOperationException(
                rollback.JournalEntry.FailureReason ??
                $"Retained experiment {current.ExperimentId:D} did not reach verified Reverted state.");
        }
    }
}

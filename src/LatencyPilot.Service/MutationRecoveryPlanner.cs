using LatencyPilot.Persistence;

namespace LatencyPilot.Service;

internal enum MutationRecoveryAction
{
    None = 0,
    AbortPreparedWithoutApply = 1,
    FinalizeVerifiedRollback = 2,
    RestoreOriginalState = 3,
    ManualInterventionRequired = 4,
}

internal sealed record MutationRecoveryPlan(
    MutationRecoveryAction Action,
    string Reason)
{
    internal bool MayWriteMachineState => Action == MutationRecoveryAction.RestoreOriginalState;
}

internal static class MutationRecoveryPlanner
{
    internal static MutationRecoveryPlan Create(
        MutationJournalEntry entry,
        MutationStoredStateRelation storedStateRelation,
        bool targetEnvironmentStable = true)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.IsTerminal)
        {
            return new MutationRecoveryPlan(
                MutationRecoveryAction.None,
                "The mutation journal entry is already terminal.");
        }

        if (!targetEnvironmentStable)
        {
            return Manual(
                "The target device/driver environment changed after the original snapshot; recovery must not restore old policy blindly.");
        }

        if (storedStateRelation == MutationStoredStateRelation.Unknown)
        {
            return Manual(
                "Actual machine state could not be read or classified; recovery must not write blindly.");
        }

        if (storedStateRelation == MutationStoredStateRelation.Diverged)
        {
            return Manual(
                "Actual stored state matches neither the captured original nor the candidate; an external or partial change may have occurred.");
        }

        var preWriteAbort = entry.FailureReason?.StartsWith(
            GpuInterruptAffinityMutationContract.PreWriteAbortPrefix,
            StringComparison.Ordinal) == true;
        if (preWriteAbort)
        {
            return storedStateRelation is MutationStoredStateRelation.MatchesOriginal or
                MutationStoredStateRelation.MatchesOriginalAndCandidate
                ? new MutationRecoveryPlan(
                    MutationRecoveryAction.FinalizeVerifiedRollback,
                    "The transaction recorded that it had not written the candidate and stored state is still the captured original; recovery may verify activation and terminalize without a policy write.")
                : Manual(
                    "The transaction recorded that it had not written the candidate, so a candidate-looking or changed state cannot safely be claimed as LatencyPilot-owned.");
        }

        if (entry.State == MutationJournalState.Prepared)
        {
            return storedStateRelation is MutationStoredStateRelation.MatchesOriginal or
                MutationStoredStateRelation.MatchesOriginalAndCandidate
                ? new MutationRecoveryPlan(
                    MutationRecoveryAction.AbortPreparedWithoutApply,
                    "The journal was prepared but actual stored state is still the captured original; recovery can terminate without touching the device.")
                : Manual(
                    "A Prepared journal entry unexpectedly observes candidate state; do not infer who changed the device.");
        }

        if (storedStateRelation is MutationStoredStateRelation.MatchesOriginal or
            MutationStoredStateRelation.MatchesOriginalAndCandidate)
        {
            return new MutationRecoveryPlan(
                MutationRecoveryAction.FinalizeVerifiedRollback,
                "Actual stored state already matches the captured original; recovery should verify activation/runtime state before terminalizing rollback.");
        }

        if (storedStateRelation == MutationStoredStateRelation.MatchesCandidate)
        {
            return entry.State switch
            {
                MutationJournalState.Applying or
                MutationJournalState.Applied or
                MutationJournalState.Measuring or
                MutationJournalState.AwaitingDecision or
                MutationJournalState.Reverting or
                MutationJournalState.RecoveryRequired =>
                    new MutationRecoveryPlan(
                        MutationRecoveryAction.RestoreOriginalState,
                        "Actual stored state still matches the experiment candidate; recovery is biased toward restoring the exact captured original state."),
                _ => Manual(
                    $"Candidate state was observed while journal state is {entry.State}; the combination is not a supported automatic-recovery case."),
            };
        }

        return Manual("No safe automatic recovery action was established for the observed state.");
    }

    private static MutationRecoveryPlan Manual(string reason) =>
        new(MutationRecoveryAction.ManualInterventionRequired, reason);
}

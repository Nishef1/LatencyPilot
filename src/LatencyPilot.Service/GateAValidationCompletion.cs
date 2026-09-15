namespace LatencyPilot.Service;

internal sealed record GateAValidationFacts(
    bool ExactRevision,
    bool BaselineEligible,
    bool InitialJournalClean,
    bool CandidatePrepared,
    bool PreparedStateSurvivedRestart,
    bool ApplySucceeded,
    bool RuntimePlacementVerified,
    bool RollbackVerified,
    bool RecoveryExerciseVerified,
    int FinalUnresolvedExperimentCount);

internal sealed record GateAValidationCompletionResult(bool Passed, string Reason);

internal static class GateAValidationCompletion
{
    internal static GateAValidationCompletionResult Evaluate(GateAValidationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (!facts.ExactRevision)
        {
            return Failed("The validation input and local checkout are not the same exact clean source revision.");
        }

        if (!facts.BaselineEligible)
        {
            return Failed("The steady real-world baseline is not eligible for the bounded GPU affinity experiment.");
        }

        if (!facts.InitialJournalClean)
        {
            return Failed("The mutation journal was not clean before the validation run.");
        }

        if (!facts.CandidatePrepared)
        {
            return Failed("No bounded GPU affinity candidate was prepared.");
        }

        if (!facts.PreparedStateSurvivedRestart)
        {
            return Failed("Prepared mutation state did not survive Service restart with a trusted classification.");
        }

        if (!facts.ApplySucceeded)
        {
            return Failed("The bounded GPU candidate did not reach the verified applied state.");
        }

        if (!facts.RuntimePlacementVerified)
        {
            return Failed("Runtime GPU-driver ISR placement was not proven on the requested processor.");
        }

        if (!facts.RollbackVerified)
        {
            return Failed("The exact captured original GPU affinity state was not verified after rollback/recovery.");
        }

        if (!facts.RecoveryExerciseVerified)
        {
            return Failed("The supported prepared-state recovery exercise did not complete successfully.");
        }

        if (facts.FinalUnresolvedExperimentCount != 0)
        {
            return Failed($"The final mutation journal still has {facts.FinalUnresolvedExperimentCount} unresolved experiment(s).");
        }

        return new GateAValidationCompletionResult(
            true,
            "Gate A automated execution proof passed: exact revision, eligible baseline, durable journal state, apply, runtime placement, rollback, recovery exercise, and final clean journal were all verified.");
    }

    private static GateAValidationCompletionResult Failed(string reason) => new(false, reason);
}

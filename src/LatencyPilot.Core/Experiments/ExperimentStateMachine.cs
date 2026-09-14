namespace LatencyPilot.Core.Experiments;

public static class ExperimentStateMachine
{
    public static bool CanTransition(ExperimentState from, ExperimentState to) => (from, to) switch
    {
        (ExperimentState.Planned, ExperimentState.MeasuringBaseline) => true,
        (ExperimentState.Planned, ExperimentState.Aborted) => true,
        (ExperimentState.MeasuringBaseline, ExperimentState.CandidateApplied) => true,
        (ExperimentState.MeasuringBaseline, ExperimentState.Aborted) => true,

        // Once a candidate has changed machine state, "abort" is no longer a safe
        // terminal action. The experiment must move through verified rollback or Keep.
        (ExperimentState.CandidateApplied, ExperimentState.MeasuringCandidate) => true,
        (ExperimentState.CandidateApplied, ExperimentState.Reverted) => true,
        (ExperimentState.MeasuringCandidate, ExperimentState.AwaitingDecision) => true,
        (ExperimentState.MeasuringCandidate, ExperimentState.Reverted) => true,
        (ExperimentState.AwaitingDecision, ExperimentState.Kept) => true,
        (ExperimentState.AwaitingDecision, ExperimentState.Reverted) => true,
        _ => false
    };

    public static void EnsureTransition(ExperimentState from, ExperimentState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Illegal experiment transition: {from} -> {to}.");
        }
    }
}

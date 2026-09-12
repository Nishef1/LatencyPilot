namespace LatencyPilot.Core.Experiments;

public static class ExperimentStateMachine
{
    public static bool CanTransition(ExperimentState from, ExperimentState to) => (from, to) switch
    {
        (ExperimentState.Planned, ExperimentState.MeasuringBaseline) => true,
        (ExperimentState.Planned, ExperimentState.Aborted) => true,
        (ExperimentState.MeasuringBaseline, ExperimentState.CandidateApplied) => true,
        (ExperimentState.MeasuringBaseline, ExperimentState.Aborted) => true,
        (ExperimentState.CandidateApplied, ExperimentState.MeasuringCandidate) => true,
        (ExperimentState.CandidateApplied, ExperimentState.Reverted) => true,
        (ExperimentState.CandidateApplied, ExperimentState.Aborted) => true,
        (ExperimentState.MeasuringCandidate, ExperimentState.AwaitingDecision) => true,
        (ExperimentState.MeasuringCandidate, ExperimentState.Reverted) => true,
        (ExperimentState.MeasuringCandidate, ExperimentState.Aborted) => true,
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

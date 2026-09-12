namespace LatencyPilot.Core.Experiments;

public enum ExperimentState
{
    Planned,
    MeasuringBaseline,
    CandidateApplied,
    MeasuringCandidate,
    AwaitingDecision,
    Kept,
    Reverted,
    Aborted
}

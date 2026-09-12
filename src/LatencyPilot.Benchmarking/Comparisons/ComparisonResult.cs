using LatencyPilot.Core.Results;

namespace LatencyPilot.Benchmarking.Comparisons;

public sealed record ComparisonResult(
    ExperimentVerdict Verdict,
    double? BaselineValue,
    double? CandidateValue,
    double? RelativeImprovement,
    IReadOnlyList<string> RegressedGuardrails,
    string Reason);

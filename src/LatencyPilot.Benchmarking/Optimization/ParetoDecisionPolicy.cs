using LatencyPilot.Core.Results;

namespace LatencyPilot.Benchmarking.Optimization;

public enum ParetoRelation
{
    Dominates = 1,
    Dominated = 2,
    Equivalent = 3,
    Tradeoff = 4,
    Inconclusive = 5,
}

public sealed record ParetoMetricOutcome(
    string MetricName,
    ExperimentVerdict Verdict);

public sealed record ParetoDecisionResult(
    ParetoRelation Relation,
    IReadOnlyList<ParetoMetricOutcome> Outcomes,
    string Reason);

public static class ParetoDecisionPolicy
{
    public static ParetoDecisionResult Evaluate(IReadOnlyList<ParetoMetricOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        if (outcomes.Count == 0)
        {
            return Result(
                ParetoRelation.Inconclusive,
                outcomes,
                "No comparable metric outcomes were supplied.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var outcome in outcomes)
        {
            if (outcome is null)
            {
                throw new ArgumentException("Pareto metric outcomes must not contain null entries.", nameof(outcomes));
            }

            if (string.IsNullOrWhiteSpace(outcome.MetricName))
            {
                throw new ArgumentException("Pareto metric names must be non-empty.", nameof(outcomes));
            }

            if (!names.Add(outcome.MetricName))
            {
                throw new ArgumentException(
                    $"Pareto metric '{outcome.MetricName}' appears more than once.",
                    nameof(outcomes));
            }

            if (!Enum.IsDefined(outcome.Verdict))
            {
                throw new ArgumentOutOfRangeException(nameof(outcomes), "Pareto metric verdict is not defined.");
            }
        }

        var snapshot = Array.AsReadOnly(outcomes.ToArray());
        if (snapshot.Any(static outcome => outcome.Verdict == ExperimentVerdict.Inconclusive))
        {
            return Result(
                ParetoRelation.Inconclusive,
                snapshot,
                "At least one metric is inconclusive, so the candidate cannot be ordered safely.");
        }

        if (snapshot.Any(static outcome => outcome.Verdict == ExperimentVerdict.Tradeoff))
        {
            return Result(
                ParetoRelation.Tradeoff,
                snapshot,
                "At least one metric already represents a measured tradeoff.");
        }

        var improved = snapshot.Any(static outcome => outcome.Verdict == ExperimentVerdict.Improved);
        var regressed = snapshot.Any(static outcome => outcome.Verdict == ExperimentVerdict.Regressed);

        if (improved && regressed)
        {
            return Result(
                ParetoRelation.Tradeoff,
                snapshot,
                "The candidate improves at least one metric and regresses at least one other metric.");
        }

        if (improved)
        {
            return Result(
                ParetoRelation.Dominates,
                snapshot,
                "The candidate materially improves at least one metric and does not regress any compared metric.");
        }

        if (regressed)
        {
            return Result(
                ParetoRelation.Dominated,
                snapshot,
                "The candidate materially regresses at least one metric and improves none of the compared metrics.");
        }

        return Result(
            ParetoRelation.Equivalent,
            snapshot,
            "All compared metrics are inside their configured no-measurable-difference thresholds.");
    }

    private static ParetoDecisionResult Result(
        ParetoRelation relation,
        IReadOnlyList<ParetoMetricOutcome> outcomes,
        string reason) =>
        new(relation, outcomes.Count == 0 ? Array.Empty<ParetoMetricOutcome>() : Array.AsReadOnly(outcomes.ToArray()), reason);
}

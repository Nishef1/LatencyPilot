using LatencyPilot.Benchmarking.Statistics;
using LatencyPilot.Core.Metrics;
using LatencyPilot.Core.Results;

namespace LatencyPilot.Benchmarking.Comparisons;

public static class BenchmarkComparer
{
    public static ComparisonResult Compare(
        MetricSeries baseline,
        MetricSeries candidate,
        IEnumerable<(MetricSeries Baseline, MetricSeries Candidate)>? guardrails = null,
        ComparisonPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);

        policy ??= new ComparisonPolicy();
        policy.Validate();
        EnsureCompatible(baseline, candidate);

        if (!HasEnoughSamples(baseline, candidate, policy.MinimumSamples))
        {
            return Inconclusive("Primary metric does not have enough samples.");
        }

        var baselineValue = Percentiles.Calculate(baseline.Samples, policy.EvaluationPercentile);
        var candidateValue = Percentiles.Calculate(candidate.Samples, policy.EvaluationPercentile);
        if (baselineValue == 0)
        {
            return Inconclusive("Primary baseline percentile is zero; relative change is undefined.");
        }

        var relativeImprovement = CalculateImprovement(baseline.Direction, baselineValue, candidateValue);
        if (relativeImprovement <= -policy.MinimumRelativeChange)
        {
            return new ComparisonResult(
                ExperimentVerdict.Regressed,
                baselineValue,
                candidateValue,
                relativeImprovement,
                [],
                "Primary metric regressed beyond the configured threshold.");
        }

        if (Math.Abs(relativeImprovement) < policy.MinimumRelativeChange)
        {
            return new ComparisonResult(
                ExperimentVerdict.NoMeasurableDifference,
                baselineValue,
                candidateValue,
                relativeImprovement,
                [],
                "Observed change is inside the configured noise threshold.");
        }

        var regressedGuardrails = new List<string>();
        foreach (var (guardrailBaseline, guardrailCandidate) in guardrails ?? [])
        {
            EnsureCompatible(guardrailBaseline, guardrailCandidate);
            if (!HasEnoughSamples(guardrailBaseline, guardrailCandidate, policy.MinimumSamples))
            {
                return Inconclusive($"Guardrail '{guardrailBaseline.Name}' does not have enough samples.");
            }

            var guardrailBaselineValue = Percentiles.Calculate(guardrailBaseline.Samples, policy.EvaluationPercentile);
            var guardrailCandidateValue = Percentiles.Calculate(guardrailCandidate.Samples, policy.EvaluationPercentile);
            if (guardrailBaselineValue == 0)
            {
                return Inconclusive($"Guardrail '{guardrailBaseline.Name}' has a zero baseline percentile.");
            }

            var guardrailImprovement = CalculateImprovement(
                guardrailBaseline.Direction,
                guardrailBaselineValue,
                guardrailCandidateValue);

            if (guardrailImprovement <= -policy.GuardrailRegressionLimit)
            {
                regressedGuardrails.Add(guardrailBaseline.Name);
            }
        }

        return regressedGuardrails.Count > 0
            ? new ComparisonResult(
                ExperimentVerdict.Tradeoff,
                baselineValue,
                candidateValue,
                relativeImprovement,
                regressedGuardrails,
                "Primary metric improved, but one or more guardrails regressed.")
            : new ComparisonResult(
                ExperimentVerdict.Improved,
                baselineValue,
                candidateValue,
                relativeImprovement,
                [],
                "Primary metric improved beyond the configured threshold without a measured guardrail regression.");
    }

    private static ComparisonResult Inconclusive(string reason) =>
        new(ExperimentVerdict.Inconclusive, null, null, null, [], reason);

    private static bool HasEnoughSamples(MetricSeries baseline, MetricSeries candidate, int minimumSamples) =>
        baseline.Samples.Count >= minimumSamples && candidate.Samples.Count >= minimumSamples;

    private static double CalculateImprovement(MetricDirection direction, double baseline, double candidate) =>
        direction switch
        {
            MetricDirection.LowerIsBetter => (baseline - candidate) / Math.Abs(baseline),
            MetricDirection.HigherIsBetter => (candidate - baseline) / Math.Abs(baseline),
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

    private static void EnsureCompatible(MetricSeries baseline, MetricSeries candidate)
    {
        if (!string.Equals(baseline.Name, candidate.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException("Baseline and candidate metric names must match.");
        }

        if (baseline.Direction != candidate.Direction)
        {
            throw new ArgumentException("Baseline and candidate metric directions must match.");
        }
    }
}

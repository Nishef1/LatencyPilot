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
        if (!double.IsFinite(baselineValue) || !double.IsFinite(candidateValue))
        {
            return Inconclusive("Primary metric percentile is non-finite; numerical evidence cannot be compared.");
        }


        double relativeImprovement;
        if (baselineValue == 0)
        {
            if (candidateValue == 0)
            {
                relativeImprovement = 0d;
            }
            else if ((baseline.Direction == MetricDirection.LowerIsBetter && candidateValue > 0) ||
                     (baseline.Direction == MetricDirection.HigherIsBetter && candidateValue < 0))
            {
                relativeImprovement = double.NegativeInfinity;
            }
            else
            {
                relativeImprovement = double.PositiveInfinity;
            }
        }
        else
        {
            relativeImprovement = CalculateImprovement(baseline.Direction, baselineValue, candidateValue);
            if (!double.IsFinite(relativeImprovement))
            {
                return Inconclusive("Primary relative change is non-finite; numerical evidence cannot be compared.");
            }
        }
        if (double.IsNaN(relativeImprovement))
        {
            return Inconclusive("Primary relative change is not a number; numerical evidence cannot be compared.");
        }

        var regressedGuardrails = new List<string>();
        foreach (var (guardrailBaseline, guardrailCandidate) in guardrails ?? [])
        {
            EnsureCompatible(guardrailBaseline, guardrailCandidate);
            if (!HasEnoughSamples(guardrailBaseline, guardrailCandidate, policy.MinimumSamples))
            {
                return Inconclusive($"Guardrail '{guardrailBaseline.Name}' does not have enough samples: original={guardrailBaseline.Samples.Count}, candidate={guardrailCandidate.Samples.Count}, required={policy.MinimumSamples}.");
            }

            var guardrailBaselineValue = Percentiles.Calculate(guardrailBaseline.Samples, policy.EvaluationPercentile);
            var guardrailCandidateValue = Percentiles.Calculate(guardrailCandidate.Samples, policy.EvaluationPercentile);
            if (!double.IsFinite(guardrailBaselineValue) || !double.IsFinite(guardrailCandidateValue))
            {
                return Inconclusive($"Guardrail '{guardrailBaseline.Name}' has a non-finite percentile.");
            }

            if (guardrailBaselineValue == 0)
            {
                if (guardrailCandidateValue == 0)
                {
                    continue;
                }

                if ((guardrailBaseline.Direction == MetricDirection.LowerIsBetter && guardrailCandidateValue > 0) ||
                    (guardrailBaseline.Direction == MetricDirection.HigherIsBetter && guardrailCandidateValue < 0))
                {
                    regressedGuardrails.Add(guardrailBaseline.Name);
                    continue;
                }

                return Inconclusive($"Guardrail '{guardrailBaseline.Name}' has a zero baseline percentile.");
            }

            var guardrailImprovement = CalculateImprovement(
                guardrailBaseline.Direction,
                guardrailBaselineValue,
                guardrailCandidateValue);
            if (!double.IsFinite(guardrailImprovement))
            {
                return Inconclusive($"Guardrail '{guardrailBaseline.Name}' has a non-finite relative change.");
            }

            if (guardrailImprovement <= -policy.GuardrailRegressionLimit)
            {
                regressedGuardrails.Add(guardrailBaseline.Name);
            }
        }

        if (relativeImprovement <= -policy.MinimumRelativeChange)
        {
            return new ComparisonResult(
                ExperimentVerdict.Regressed,
                baselineValue,
                candidateValue,
                relativeImprovement,
                regressedGuardrails,
                "Primary metric regressed beyond the configured threshold.");
        }

        if (Math.Abs(relativeImprovement) < policy.MinimumRelativeChange)
        {
            return regressedGuardrails.Count > 0
                ? new ComparisonResult(
                    ExperimentVerdict.Regressed,
                    baselineValue,
                    candidateValue,
                    relativeImprovement,
                    regressedGuardrails,
                    "Primary metric did not measurably improve and one or more guardrails regressed.")
                : new ComparisonResult(
                    ExperimentVerdict.NoMeasurableDifference,
                    baselineValue,
                    candidateValue,
                    relativeImprovement,
                    [],
                    "Observed change is inside the configured noise threshold.");
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

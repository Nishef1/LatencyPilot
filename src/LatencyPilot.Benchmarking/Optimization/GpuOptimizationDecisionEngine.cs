using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Core.Metrics;
using LatencyPilot.Core.Results;

namespace LatencyPilot.Benchmarking.Optimization;

public static class GpuOptimizationDecisionEngine
{
    private static readonly GpuConfirmationOrder[] BalancedConfirmationSchedule =
    [
        GpuConfirmationOrder.Original,
        GpuConfirmationOrder.Candidate,
        GpuConfirmationOrder.Candidate,
        GpuConfirmationOrder.Original,
        GpuConfirmationOrder.Candidate,
        GpuConfirmationOrder.Original,
        GpuConfirmationOrder.Original,
        GpuConfirmationOrder.Candidate,
    ];

    public static GpuOptimizationScreeningResult Screen(
        GpuOptimizationMeasurementSet original,
        IEnumerable<GpuOptimizationCandidateMeasurement> candidates,
        ComparisonPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();

        var candidateArray = candidates.ToArray();
        var evaluations = candidateArray
            .Select(candidate => new GpuOptimizationCandidateEvaluation(
                candidate.Candidate,
                Compare(original, candidate.Measurement, policy)))
            .ToArray();

        var finalist = evaluations
            .Where(static evaluation =>
                evaluation.Comparison.Verdict == ExperimentVerdict.Improved &&
                evaluation.Comparison.RegressedGuardrails.Count == 0 &&
                evaluation.Comparison.RelativeImprovement is { } improvement &&
                double.IsFinite(improvement))
            .OrderByDescending(static evaluation => evaluation.Comparison.RelativeImprovement!.Value)
            .ThenBy(static evaluation => evaluation.Candidate.PhysicalCoreIndex)
            .ThenBy(static evaluation => evaluation.Candidate.Processor.Group)
            .ThenBy(static evaluation => evaluation.Candidate.Processor.Number)
            .FirstOrDefault();

        if (finalist is null)
        {
            return new GpuOptimizationScreeningResult(
                evaluations,
                null,
                GpuOptimizationRecommendation.RestoreOriginal,
                evaluations.Length == 0
                    ? "No bounded GPU affinity candidates were measured; keep the exact original state."
                    : "No candidate produced a clean measurable improvement without guardrail regression; restore the exact original state.");
        }

        return new GpuOptimizationScreeningResult(
            evaluations,
            finalist,
            GpuOptimizationRecommendation.KeepCandidate,
            "A clean improved candidate is eligible for balanced finalist confirmation before any keep decision is finalized.");
    }

    public static IReadOnlyList<GpuConfirmationOrder> CreateBalancedConfirmationSchedule() =>
        Array.AsReadOnly(BalancedConfirmationSchedule);

    private static ComparisonResult Compare(
        GpuOptimizationMeasurementSet original,
        GpuOptimizationMeasurementSet candidate,
        ComparisonPolicy policy)
    {
        if (!MetricsMatch(original.Primary, candidate.Primary))
        {
            return Inconclusive("Primary metric identity or direction does not match the original evidence.");
        }

        if (original.Guardrails.Count != candidate.Guardrails.Count ||
            original.Guardrails.Keys.Any(name => !candidate.Guardrails.ContainsKey(name)))
        {
            return Inconclusive("Candidate guardrail evidence does not exactly match the original guardrail set.");
        }

        var guardrailPairs = new List<(MetricSeries Baseline, MetricSeries Candidate)>(original.Guardrails.Count);
        foreach (var (name, baselineSeries) in original.Guardrails.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            var candidateSeries = candidate.Guardrails[name];
            if (!MetricsMatch(baselineSeries, candidateSeries))
            {
                return Inconclusive($"Guardrail '{name}' metric identity or direction does not match the original evidence.");
            }

            guardrailPairs.Add((baselineSeries, candidateSeries));
        }

        return BenchmarkComparer.Compare(
            original.Primary,
            candidate.Primary,
            guardrailPairs,
            policy);
    }

    private static bool MetricsMatch(MetricSeries original, MetricSeries candidate) =>
        string.Equals(original.Name, candidate.Name, StringComparison.Ordinal) &&
        original.Direction == candidate.Direction;

    private static ComparisonResult Inconclusive(string reason) =>
        new(
            ExperimentVerdict.Inconclusive,
            null,
            null,
            null,
            [],
            reason);
}

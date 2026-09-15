using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Core.Metrics;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed record GpuOptimizationMeasurementSet
{
    public GpuOptimizationMeasurementSet(
        MetricSeries primary,
        IReadOnlyDictionary<string, MetricSeries> guardrails)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(guardrails);

        var validatedGuardrails = new Dictionary<string, MetricSeries>(StringComparer.Ordinal);
        foreach (var (name, series) in guardrails)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(series);
            if (!string.Equals(name, series.Name, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Guardrail key '{name}' must exactly match metric name '{series.Name}'.",
                    nameof(guardrails));
            }

            if (!validatedGuardrails.TryAdd(name, series))
            {
                throw new ArgumentException($"Duplicate guardrail '{name}'.", nameof(guardrails));
            }
        }

        Primary = primary;
        Guardrails = validatedGuardrails.AsReadOnly();
    }

    public MetricSeries Primary { get; }

    public IReadOnlyDictionary<string, MetricSeries> Guardrails { get; }
}

public sealed record GpuOptimizationCandidateMeasurement
{
    public GpuOptimizationCandidateMeasurement(
        GpuAffinityCandidate candidate,
        GpuOptimizationMeasurementSet measurement)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(measurement);
        Candidate = candidate;
        Measurement = measurement;
    }

    public GpuAffinityCandidate Candidate { get; }

    public GpuOptimizationMeasurementSet Measurement { get; }
}

public sealed record GpuOptimizationCandidateEvaluation(
    GpuAffinityCandidate Candidate,
    ComparisonResult Comparison);

public enum GpuOptimizationRecommendation
{
    RestoreOriginal,
    KeepCandidate,
    ConfirmFinalist,
}

public enum GpuConfirmationOrder
{
    Original,
    Candidate,
}

public sealed record GpuOptimizationScreeningResult(
    IReadOnlyList<GpuOptimizationCandidateEvaluation> Evaluations,
    GpuOptimizationCandidateEvaluation? Finalist,
    GpuOptimizationRecommendation Recommendation,
    string Reason);

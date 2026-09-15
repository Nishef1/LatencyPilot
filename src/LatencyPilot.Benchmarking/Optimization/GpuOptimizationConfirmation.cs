using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Benchmarking.Statistics;
using LatencyPilot.Core.Metrics;
using LatencyPilot.Core.Results;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

// These records describe completed observations. The collection/execution layer
// must derive verification and identity from actual state, never requested intent.
public sealed record GpuOptimizationBaselineEvidence(
    BaselineQualityResult Quality,
    Guid SessionId,
    string WorkloadIdentity,
    string EnvironmentIdentity,
    string SourceRevisionId);

public sealed record GpuOptimizationConfirmationRun(
    int RunNumber,
    GpuConfirmationOrder Role,
    Guid SessionId,
    Guid CaptureId,
    string WorkloadIdentity,
    string EnvironmentIdentity,
    string SourceRevisionId,
    LogicalProcessorId? AppliedProcessor,
    bool AppliedStateVerified,
    bool CaptureIntegrityValid,
    int RequestedDurationMilliseconds,
    double ActualDurationMilliseconds,
    GpuOptimizationMeasurementSet Measurement);

public sealed record GpuOptimizationMetricComparison(
    string MetricName,
    bool IsPrimary,
    MetricDirection Direction,
    string Statistic,
    double? EvaluationPercentile,
    long OriginalSampleCount,
    long CandidateSampleCount,
    double OriginalValue,
    double CandidateValue,
    double RawDelta,
    double? RelativeImprovement,
    double? OriginalRelativeNoise,
    double? CandidateRelativeNoise,
    double? OriginalRelativeDrift,
    double? CandidateRelativeDrift,
    double? EffectiveRelativeThreshold,
    ExperimentVerdict Verdict);

public sealed record GpuOptimizationConfirmationResult(
    string MethodVersion,
    GpuAffinityCandidate Finalist,
    ExperimentVerdict Verdict,
    GpuOptimizationRecommendation Recommendation,
    IReadOnlyList<GpuOptimizationMetricComparison> Metrics,
    IReadOnlyList<GpuOptimizationConfirmationRun> Runs,
    IReadOnlyList<string> Reasons);

public static class GpuOptimizationConfirmation
{
    public const string MethodVersion = "gpu-affinity-confirmation-v1";
    public const int MinimumRunDurationMilliseconds = 30_000;
    public const int MinimumSamplesPerMetricRun = 1_000;
    private const double MinimumDurationRatio = 0.95;
    private const double MaximumNoise = 0.30;
    private const double MaximumDrift = 0.20;
    private const double MaximumExtremeDeviation = 0.50;

    public static GpuOptimizationConfirmationResult Confirm(
        GpuAffinityCandidate finalist,
        GpuOptimizationBaselineEvidence baseline,
        IReadOnlyList<GpuOptimizationConfirmationRun> runs,
        ComparisonPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(finalist);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(runs);
        policy ??= new ComparisonPolicy();
        policy.Validate();
        // This version compares adverse run tails (and mean drop ratios).
        // A different estimator/tail must
        // not silently reinterpret already persisted confirmation results.
        if (policy.EvaluationPercentile != 0.99)
        {
            throw new ArgumentException("Confirmation v1 fixes adverse tails at p99/p01.", nameof(policy));
        }

        var capturedRuns = runs.ToArray();
        var reasons = new List<string>();
        var metrics = new List<GpuOptimizationMetricComparison>();
        if (finalist.Processor.Group != 0 || finalist.Processor.Number >= 64 || finalist.PhysicalCoreIndex < 0)
        {
            reasons.Add("The finalist is outside the supported group-0 physical-core boundary.");
        }
        var quality = baseline.Quality;
        if (quality is null || !quality.IsValidForComparison ||
            quality.MethodVersion != BaselineQualityAnalyzer.MethodVersion ||
            quality.TotalWindowCount != 5 || quality.ValidCaptureWindowCount != 5 ||
            !quality.DpcP99.IsStable || !quality.IsrP99.IsStable || quality.Reasons.Count != 0 ||
            quality.DpcP99.EligibleWindowCount != 5 || quality.IsrP99.EligibleWindowCount != 5)
        {
            reasons.Add("Confirmation requires a valid, complete baseline-quality-v2 decision baseline.");
        }

        ValidateRuns(finalist, baseline, capturedRuns, reasons);
        if (reasons.Count == 0)
        {
            var template = capturedRuns[0].Measurement;
            EvaluateMetric(template.Primary.Name, isPrimary: true,
                capturedRuns, policy, metrics, reasons);
            foreach (var name in template.Guardrails.Keys.Order(StringComparer.Ordinal))
            {
                EvaluateMetric(name, isPrimary: false, capturedRuns, policy, metrics, reasons);
            }
        }

        var verdict = ExperimentVerdict.Inconclusive;
        if (reasons.Count == 0)
        {
            var primary = metrics[0];
            var guardrailRegression = metrics.Any(static metric =>
                !metric.IsPrimary && metric.Verdict == ExperimentVerdict.Regressed);
            verdict = primary.Verdict switch
            {
                ExperimentVerdict.Improved when guardrailRegression => ExperimentVerdict.Tradeoff,
                ExperimentVerdict.NoMeasurableDifference when guardrailRegression => ExperimentVerdict.Regressed,
                _ => primary.Verdict,
            };
        }

        return new GpuOptimizationConfirmationResult(
            MethodVersion, finalist, verdict,
            verdict == ExperimentVerdict.Improved
                ? GpuOptimizationRecommendation.KeepCandidate
                : GpuOptimizationRecommendation.RestoreOriginal,
            metrics.AsReadOnly(), Array.AsReadOnly(capturedRuns), reasons.AsReadOnly());
    }

    private static void ValidateRuns(
        GpuAffinityCandidate finalist,
        GpuOptimizationBaselineEvidence baseline,
        GpuOptimizationConfirmationRun[] runs,
        List<string> reasons)
    {
        var schedule = GpuOptimizationDecisionEngine.CreateBalancedConfirmationSchedule();
        if (runs.Length != schedule.Count)
        {
            reasons.Add("Exactly eight completed runs in ABBA + BAAB order are required; no window may be discarded.");
            return;
        }

        var first = runs[0];
        if (first is null || first.Measurement is null)
        {
            reasons.Add("The initial control run or its metric evidence is missing.");
            return;
        }

        if (first.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(first.WorkloadIdentity) ||
            string.IsNullOrWhiteSpace(first.EnvironmentIdentity) || first.Measurement.Guardrails.Count == 0)
        {
            reasons.Add("Session, workload, environment and at least one workload guardrail are required.");
        }

        if (first.SessionId != baseline.SessionId ||
            !string.Equals(first.WorkloadIdentity, baseline.WorkloadIdentity, StringComparison.Ordinal) ||
            !string.Equals(first.EnvironmentIdentity, baseline.EnvironmentIdentity, StringComparison.Ordinal) ||
            !string.Equals(first.SourceRevisionId, baseline.SourceRevisionId, StringComparison.Ordinal) ||
            baseline.SourceRevisionId is not { Length: 40 } revision || !revision.All(char.IsAsciiHexDigit))
        {
            reasons.Add("The baseline and confirmation must share session, workload, environment and exact clean source provenance.");
        }

        var captureIds = new HashSet<Guid>();
        for (var index = 0; index < runs.Length; index++)
        {
            var run = runs[index];
            if (run is null || run.Measurement is null)
            {
                reasons.Add($"Run {index + 1} has missing evidence.");
                continue;
            }

            if (run.RunNumber != index + 1 || run.Role != schedule[index])
            {
                reasons.Add($"Run {index + 1} does not match the authoritative balanced sequence.");
            }

            if (run.CaptureId == Guid.Empty || !captureIds.Add(run.CaptureId) ||
                run.SessionId != first.SessionId ||
                !string.Equals(run.SourceRevisionId, first.SourceRevisionId, StringComparison.Ordinal) ||
                !string.Equals(run.WorkloadIdentity, first.WorkloadIdentity, StringComparison.Ordinal) ||
                !string.Equals(run.EnvironmentIdentity, first.EnvironmentIdentity, StringComparison.Ordinal))
            {
                reasons.Add($"Run {index + 1} has reused/missing capture identity or changed session/workload/environment.");
            }

            if (!run.CaptureIntegrityValid || !run.AppliedStateVerified ||
                (run.Role == GpuConfirmationOrder.Candidate
                    ? run.AppliedProcessor != finalist.Processor
                    : run.AppliedProcessor is not null))
            {
                reasons.Add($"Run {index + 1} lacks clean capture integrity or verified expected applied state.");
            }

            if (run.RequestedDurationMilliseconds < MinimumRunDurationMilliseconds ||
                run.RequestedDurationMilliseconds != first.RequestedDurationMilliseconds ||
                !double.IsFinite(run.ActualDurationMilliseconds) ||
                run.ActualDurationMilliseconds < run.RequestedDurationMilliseconds * MinimumDurationRatio)
            {
                reasons.Add($"Run {index + 1} is shorter than the confirmation duration contract.");
            }

            var template = first.Measurement;
            if (!SameMetric(template.Primary, run.Measurement.Primary) ||
                template.Guardrails.Count != run.Measurement.Guardrails.Count ||
                template.Guardrails.Any(pair =>
                    !run.Measurement.Guardrails.TryGetValue(pair.Key, out var series) ||
                    !SameMetric(pair.Value, series)))
            {
                reasons.Add($"Run {index + 1} has a missing or incompatible primary/guardrail metric.");
            }
        }
    }

    private static void EvaluateMetric(
        string name,
        bool isPrimary,
        GpuOptimizationConfirmationRun[] runs,
        ComparisonPolicy policy,
        List<GpuOptimizationMetricComparison> results,
        List<string> reasons)
    {
        var original = new List<double>(4);
        var candidate = new List<double>(4);
        long originalSamples = 0;
        long candidateSamples = 0;
        var minimumSamples = Math.Max(policy.MinimumSamples, MinimumSamplesPerMetricRun);
        var direction = isPrimary ? runs[0].Measurement.Primary.Direction : runs[0].Measurement.Guardrails[name].Direction;
        var isDropRatio = string.Equals(name, PresentMonGuardrailSeriesBuilder.DroppedFrameRatioMetric, StringComparison.Ordinal);
        double? evaluationPercentile = isDropRatio ? null : direction == MetricDirection.LowerIsBetter ? 0.99 : 0.01;
        foreach (var run in runs)
        {
            var series = isPrimary ? run.Measurement.Primary : run.Measurement.Guardrails[name];
            if (series.Samples.Count < minimumSamples || series.Samples.Any(value =>
                    value < 0 || !double.IsFinite(value) || (isDropRatio && value > 1)) ||
                (isDropRatio && direction != MetricDirection.LowerIsBetter))
            {
                reasons.Add($"Run {run.RunNumber}, '{name}': insufficient or invalid samples; confirmation requires at least {minimumSamples} observations per metric per run.");
                return;
            }

            var value = isDropRatio ? series.Samples.Average() : Percentiles.Calculate(series.Samples, evaluationPercentile!.Value);
            if (!double.IsFinite(value))
            {
                reasons.Add($"Run {run.RunNumber}, '{name}': non-finite tail percentile.");
                return;
            }

            if (run.Role == GpuConfirmationOrder.Original)
            {
                original.Add(value);
                originalSamples += series.Samples.Count;
            }
            else
            {
                candidate.Add(value);
                candidateSamples += series.Samples.Count;
            }
        }

        var originalQuality = AnalyzeVariation(original);
        var candidateQuality = AnalyzeVariation(candidate);
        var delta = candidateQuality.Median - originalQuality.Median;
        double? relativeImprovement = originalQuality.Median == 0
            ? null
            : (direction == MetricDirection.LowerIsBetter ? -delta : delta) / originalQuality.Median;
        var threshold = Math.Max(isPrimary ? policy.MinimumRelativeChange : policy.GuardrailRegressionLimit,
            Math.Max(Math.Max(originalQuality.Noise, candidateQuality.Noise),
                Math.Max(originalQuality.Drift, candidateQuality.Drift)));
        var verdict = ExperimentVerdict.Inconclusive;
        if (!originalQuality.Valid || !candidateQuality.Valid || !double.IsFinite(delta) ||
            (relativeImprovement is { } relative && !double.IsFinite(relative)))
        {
            reasons.Add($"'{name}' failed the finite, noise, drift or extreme-window quality gate.");
        }
        else if (originalQuality.Median == 0)
        {
            // Zero dropped frames is valid evidence. A new adverse nonzero
            // value must not disappear behind undefined percentage arithmetic.
            verdict = delta == 0 ? ExperimentVerdict.NoMeasurableDifference
                : direction == MetricDirection.LowerIsBetter ? ExperimentVerdict.Regressed
                : ExperimentVerdict.Inconclusive;
            if (verdict == ExperimentVerdict.Inconclusive)
            {
                reasons.Add($"'{name}' has a zero control value; a relative improvement cannot be established.");
            }
        }
        else
        {
            verdict = relativeImprovement > threshold ? ExperimentVerdict.Improved
                : relativeImprovement < -threshold ? ExperimentVerdict.Regressed
                : ExperimentVerdict.NoMeasurableDifference;
        }

        results.Add(new GpuOptimizationMetricComparison(name, isPrimary, direction,
            isDropRatio ? "arithmetic-mean-v1" : "linear-n-minus-one-v1", evaluationPercentile,
            originalSamples, candidateSamples, originalQuality.Median, candidateQuality.Median,
            delta, relativeImprovement is { } improvement ? Finite(improvement) : null,
            Finite(originalQuality.Noise), Finite(candidateQuality.Noise),
            Finite(originalQuality.Drift), Finite(candidateQuality.Drift), Finite(threshold), verdict));
    }

    private static (double Median, double Noise, double Drift, bool Valid) AnalyzeVariation(List<double> values)
    {
        var median = Percentiles.Calculate(values, 0.5);
        if (median == 0)
        {
            var allZero = values.All(static value => value == 0);
            return (0, allZero ? 0 : double.NaN, allZero ? 0 : double.NaN, allZero);
        }

        var noise = (Percentiles.Calculate(values, 0.9) - Percentiles.Calculate(values, 0.1)) / median;
        var drift = Math.Abs(Percentiles.Calculate(values.Take(2).ToArray(), 0.5) -
            Percentiles.Calculate(values.Skip(2).ToArray(), 0.5)) / median;
        return (median, noise, drift, double.IsFinite(noise) && double.IsFinite(drift) &&
            noise <= MaximumNoise && drift <= MaximumDrift &&
            values.All(value => Math.Abs(value - median) / median <= MaximumExtremeDeviation));
    }

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;

    private static bool SameMetric(MetricSeries left, MetricSeries right) =>
        string.Equals(left.Name, right.Name, StringComparison.Ordinal) &&
        left.Direction == right.Direction &&
        left.Direction is MetricDirection.LowerIsBetter or MetricDirection.HigherIsBetter;
}

using LatencyPilot.Benchmarking.Baselines;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed record GpuOptimizationBaselineEligibility(
    string Target,
    bool IsEligible,
    string Reason);

public static class GpuOptimizationBaselineReadiness
{
    public const string Target = "gpu-affinity-v1";

    private const int RequiredBaselineWindows = 5;

    public static bool IsEligible(
        BaselineQualityResult? quality,
        WorkloadStabilityResult? workloadStability) =>
        HasEligibleLatencyQuality(quality) &&
        workloadStability is
        {
            MethodVersion: WorkloadStabilityAnalyzer.MethodVersion,
            Status: WorkloadStabilityStatus.Stable,
            IsEligibleForExperiment: true,
        } stableWorkload &&
        stableWorkload.Reasons.Count == 0;

    public static GpuOptimizationBaselineEligibility Evaluate(
        BaselineQualityResult? quality,
        WorkloadStabilityResult? workloadStability)
    {
        if (!HasEligibleLatencyQuality(quality))
        {
            return new GpuOptimizationBaselineEligibility(
                Target,
                false,
                $"Latency repeatability did not pass {BaselineQualityAnalyzer.MethodVersion}.");
        }

        if (workloadStability is null)
        {
            return new GpuOptimizationBaselineEligibility(
                Target,
                false,
                $"{WorkloadStabilityAnalyzer.MethodVersion} evidence is unavailable.");
        }

        if (!string.Equals(
                workloadStability.MethodVersion,
                WorkloadStabilityAnalyzer.MethodVersion,
                StringComparison.Ordinal))
        {
            return new GpuOptimizationBaselineEligibility(
                Target,
                false,
                $"Workload readiness method '{workloadStability.MethodVersion}' does not match {WorkloadStabilityAnalyzer.MethodVersion}.");
        }

        if (workloadStability.Status != WorkloadStabilityStatus.Stable ||
            !workloadStability.IsEligibleForExperiment ||
            workloadStability.Reasons.Count != 0)
        {
            var reason = workloadStability.Reasons.Count == 0
                ? $"Workload activity is {workloadStability.Status} under {WorkloadStabilityAnalyzer.MethodVersion}."
                : string.Join(" ", workloadStability.Reasons);
            return new GpuOptimizationBaselineEligibility(Target, false, reason);
        }

        return new GpuOptimizationBaselineEligibility(
            Target,
            true,
            $"Latency repeatability passed {BaselineQualityAnalyzer.MethodVersion} and workload activity is Stable under {WorkloadStabilityAnalyzer.MethodVersion}.");
    }

    private static bool HasEligibleLatencyQuality(BaselineQualityResult? quality) =>
        quality is
        {
            IsValidForComparison: true,
            MethodVersion: BaselineQualityAnalyzer.MethodVersion,
            TotalWindowCount: RequiredBaselineWindows,
            ValidCaptureWindowCount: RequiredBaselineWindows,
        } validQuality &&
        validQuality.DpcP99.IsStable &&
        validQuality.IsrP99.IsStable &&
        validQuality.Reasons.Count == 0 &&
        validQuality.DpcP99.EligibleWindowCount == RequiredBaselineWindows &&
        validQuality.IsrP99.EligibleWindowCount == RequiredBaselineWindows;
}

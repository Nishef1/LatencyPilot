using LatencyPilot.Benchmarking.Baselines;

namespace LatencyPilot.Benchmarking.Optimization;

public static class GpuOptimizationBaselineReadiness
{
    public static bool IsEligible(
        BaselineQualityResult? quality,
        WorkloadStabilityResult? workloadStability) =>
        quality is
        {
            IsValidForComparison: true,
            MethodVersion: BaselineQualityAnalyzer.MethodVersion,
            TotalWindowCount: BaselineQualityAnalyzer.RequiredWindowCount,
            ValidCaptureWindowCount: BaselineQualityAnalyzer.RequiredWindowCount,
        } validQuality &&
        validQuality.DpcP99.IsStable &&
        validQuality.IsrP99.IsStable &&
        validQuality.Reasons.Count == 0 &&
        validQuality.DpcP99.EligibleWindowCount == BaselineQualityAnalyzer.RequiredWindowCount &&
        validQuality.IsrP99.EligibleWindowCount == BaselineQualityAnalyzer.RequiredWindowCount &&
        workloadStability is
        {
            MethodVersion: WorkloadStabilityAnalyzer.MethodVersion,
            Status: WorkloadStabilityStatus.Stable,
            IsEligibleForExperiment: true,
        } stableWorkload &&
        stableWorkload.Reasons.Count == 0;
}

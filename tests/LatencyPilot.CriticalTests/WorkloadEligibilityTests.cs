#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Benchmarking.Optimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class WorkloadEligibilityTests
{
    [AuditCase]
    public void WorkloadEligibilityRemainsFailClosedForChangingOrIncompleteEvidence()
    {
        var stableWorkload = WorkloadStabilityAnalyzer.Analyze(
        [
            new WorkloadWindowEvidence(1, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(2, 20_000, 31_000, 12_200, 10.4),
            new WorkloadWindowEvidence(3, 20_000, 29_500, 11_900, 9.8),
            new WorkloadWindowEvidence(4, 20_000, 30_500, 12_100, 10.2),
            new WorkloadWindowEvidence(5, 20_000, 30_200, 12_050, 10.1),
        ]);
        Assert.AreEqual(WorkloadStabilityStatus.Stable, stableWorkload.Status);
        Assert.IsTrue(stableWorkload.IsEligibleForExperiment);

        var stableQuality = BaselineQualityAnalyzer.Analyze(
        [
            new BaselineWindowEvidence(1, DateTimeOffset.UnixEpoch, 20_000, 20_000, true, null, 30_000, 100, 12_000, 50),
            new BaselineWindowEvidence(2, DateTimeOffset.UnixEpoch.AddSeconds(21), 20_000, 20_000, true, null, 31_000, 101, 12_200, 50.5),
            new BaselineWindowEvidence(3, DateTimeOffset.UnixEpoch.AddSeconds(42), 20_000, 20_000, true, null, 29_500, 99, 11_900, 49.5),
            new BaselineWindowEvidence(4, DateTimeOffset.UnixEpoch.AddSeconds(63), 20_000, 20_000, true, null, 30_500, 100.5, 12_100, 50.2),
            new BaselineWindowEvidence(5, DateTimeOffset.UnixEpoch.AddSeconds(84), 20_000, 20_000, true, null, 30_200, 100.2, 12_050, 50.1),
        ]);
        Assert.IsTrue(GpuOptimizationBaselineReadiness.IsEligible(stableQuality, stableWorkload));

        var changing = WorkloadStabilityAnalyzer.Analyze(
        [
            new WorkloadWindowEvidence(1, 20_302.5, 39_533, 17_003, 12.519),
            new WorkloadWindowEvidence(2, 20_302.5, 28_979, 11_158, 13.383),
            new WorkloadWindowEvidence(3, 20_302.5, 27_815, 11_315, 9.493),
            new WorkloadWindowEvidence(4, 20_302.5, 27_537, 11_645, 7.537),
            new WorkloadWindowEvidence(5, 20_302.5, 26_604, 11_207, 6.423),
        ]);
        Assert.AreEqual(WorkloadStabilityStatus.Changing, changing.Status);
        Assert.IsFalse(GpuOptimizationBaselineReadiness.IsEligible(stableQuality, changing));

        var spiky = WorkloadStabilityAnalyzer.Analyze(
        [
            new WorkloadWindowEvidence(1, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(2, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(3, 20_000, 80_000, 40_000, 35.0),
            new WorkloadWindowEvidence(4, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(5, 20_000, 30_000, 12_000, 10.0),
        ]);
        Assert.IsTrue(spiky.Signals.Any(static signal => signal.HasExtremeWindow));
        Assert.IsFalse(GpuOptimizationBaselineReadiness.IsEligible(stableQuality, spiky));

        var incomplete = WorkloadStabilityAnalyzer.Analyze(
        [
            new WorkloadWindowEvidence(1, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(2, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(3, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(4, 20_000, 30_000, 12_000, 10.0),
        ]);
        Assert.AreEqual(WorkloadStabilityStatus.Insufficient, incomplete.Status);
        Assert.IsFalse(GpuOptimizationBaselineReadiness.IsEligible(stableQuality, incomplete));

        var partialCpu = WorkloadStabilityAnalyzer.Analyze(
        [
            new WorkloadWindowEvidence(1, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(2, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(3, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(4, 20_000, 30_000, 12_000, 10.0),
            new WorkloadWindowEvidence(5, 20_000, 30_000, 12_000, null),
        ]);
        Assert.AreEqual(WorkloadStabilityStatus.Insufficient, partialCpu.Status);
        Assert.IsTrue(partialCpu.Reasons.Any(static reason => reason.Contains("CPU", StringComparison.OrdinalIgnoreCase)));
    }
}

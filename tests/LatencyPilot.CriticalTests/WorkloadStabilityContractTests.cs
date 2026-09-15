using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Benchmarking.Optimization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class WorkloadStabilityContractTests
{
    [TestMethod]
    public void CpuActivityDriftBlocksExperimentReadinessEvenWhenInterruptRatesAreStable()
    {
        var windows = Enumerable.Range(1, 5)
            .Select(number => new BaselineWindowEvidence(
                number,
                DateTimeOffset.UnixEpoch.AddSeconds((number - 1) * 21),
                20_000,
                20_000,
                true,
                null,
                30_000,
                100,
                12_000,
                70))
            .ToArray();
        double?[] cpuBusyPercent = [80, 80, 20, 20, 20];

        var result = WorkloadStabilityAnalyzer.Analyze(windows, cpuBusyPercent);

        Assert.AreEqual(WorkloadStabilityStatus.Changing, result.Status);
        Assert.IsFalse(result.IsEligibleForExperiment);
        Assert.IsTrue(result.Reasons.Any(static reason =>
            reason.Contains("System CPU busy", StringComparison.Ordinal)));

        var quality = BaselineQualityAnalyzer.Analyze(windows);
        var eligibility = GpuOptimizationBaselineReadiness.Evaluate(quality, result);
        Assert.IsFalse(eligibility.IsEligible);
        Assert.AreEqual("gpu-affinity-v1", eligibility.Target);
        StringAssert.Contains(eligibility.Reason, "System CPU busy");

        var stableWorkload = WorkloadStabilityAnalyzer.Analyze(windows, [50, 50, 50, 50, 50]);
        var stableEligibility = GpuOptimizationBaselineReadiness.Evaluate(quality, stableWorkload);
        Assert.IsTrue(stableEligibility.IsEligible);
        Assert.AreEqual("gpu-affinity-v1", stableEligibility.Target);
    }
}

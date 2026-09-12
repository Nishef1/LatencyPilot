using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Core.Experiments;
using LatencyPilot.Core.Metrics;
using LatencyPilot.Core.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class CriticalPathTests
{
    private static readonly ComparisonPolicy Policy = new(
        MinimumSamples: 20,
        MinimumRelativeChange: 0.03,
        GuardrailRegressionLimit: 0.05,
        EvaluationPercentile: 0.99);

    [TestMethod]
    public void IllegalExperimentTransitionIsRejected()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ExperimentStateMachine.EnsureTransition(ExperimentState.Planned, ExperimentState.Kept));
    }

    [TestMethod]
    public void InsufficientSamplesAreInconclusive()
    {
        var result = BenchmarkComparer.Compare(
            Series("DPC p99", 100, 5),
            Series("DPC p99", 80, 5),
            policy: Policy);

        Assert.AreEqual(ExperimentVerdict.Inconclusive, result.Verdict);
    }

    [TestMethod]
    public void ChangeInsideNoiseThresholdIsNotCalledImprovement()
    {
        var result = BenchmarkComparer.Compare(
            Series("DPC p99", 100),
            Series("DPC p99", 98),
            policy: Policy);

        Assert.AreEqual(ExperimentVerdict.NoMeasurableDifference, result.Verdict);
    }

    [TestMethod]
    public void ClearPrimaryImprovementIsDetected()
    {
        var result = BenchmarkComparer.Compare(
            Series("DPC p99", 100),
            Series("DPC p99", 80),
            policy: Policy);

        Assert.AreEqual(ExperimentVerdict.Improved, result.Verdict);
    }

    [TestMethod]
    public void GuardrailRegressionTurnsImprovementIntoTradeoff()
    {
        var result = BenchmarkComparer.Compare(
            Series("DPC p99", 100),
            Series("DPC p99", 80),
            [(Series("USB jitter", 10), Series("USB jitter", 12))],
            Policy);

        Assert.AreEqual(ExperimentVerdict.Tradeoff, result.Verdict);
        CollectionAssert.Contains(result.RegressedGuardrails.ToList(), "USB jitter");
    }

    [TestMethod]
    public void ClearPrimaryRegressionIsDetected()
    {
        var result = BenchmarkComparer.Compare(
            Series("DPC p99", 100),
            Series("DPC p99", 120),
            policy: Policy);

        Assert.AreEqual(ExperimentVerdict.Regressed, result.Verdict);
    }

    [TestMethod]
    public void NonFiniteMeasurementIsRejected()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new MetricSeries("DPC p99", MetricDirection.LowerIsBetter, [100, double.NaN]));
    }

    private static MetricSeries Series(string name, double value, int count = 20) =>
        new(name, MetricDirection.LowerIsBetter, Enumerable.Repeat(value, count));
}

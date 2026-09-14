using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Metrics;
using LatencyPilot.Core.Results;
using LatencyPilot.Core.System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class OptimizerSafetyTests
{
    private static readonly ComparisonPolicy Policy = new(
        MinimumSamples: 20,
        MinimumRelativeChange: 0.03,
        GuardrailRegressionLimit: 0.05,
        EvaluationPercentile: 0.99);

    [TestMethod]
    public void GoDecisionContractRejectsTradeoffsAndBalancesFinalConfirmation()
    {
        var original = Measurement(100, 10);
        var tradeoff = new GpuOptimizationCandidateMeasurement(
            Candidate(core: 2, cpu: 4, pressure: 0.02),
            Measurement(75, 12));
        var clean = new GpuOptimizationCandidateMeasurement(
            Candidate(core: 1, cpu: 2, pressure: 0.05),
            Measurement(85, 10.1));
        var incomplete = new GpuOptimizationCandidateMeasurement(
            Candidate(core: 3, cpu: 6, pressure: 0.01),
            new GpuOptimizationMeasurementSet(
                Series("DPC p99", 70),
                new Dictionary<string, MetricSeries>(StringComparer.Ordinal)));

        var screening = GpuOptimizationDecisionEngine.Screen(
            original,
            [tradeoff, clean, incomplete],
            Policy);

        Assert.AreEqual(3, screening.Evaluations.Count);
        Assert.AreEqual(ExperimentVerdict.Tradeoff, screening.Evaluations[0].Comparison.Verdict);
        Assert.AreEqual(ExperimentVerdict.Improved, screening.Evaluations[1].Comparison.Verdict);
        Assert.AreEqual(ExperimentVerdict.Inconclusive, screening.Evaluations[2].Comparison.Verdict);
        Assert.AreEqual(clean.Candidate, screening.Finalist?.Candidate);
        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, screening.Recommendation);

        CollectionAssert.AreEqual(
            new[]
            {
                GpuConfirmationOrder.Original,
                GpuConfirmationOrder.Candidate,
                GpuConfirmationOrder.Candidate,
                GpuConfirmationOrder.Original,
                GpuConfirmationOrder.Candidate,
                GpuConfirmationOrder.Original,
                GpuConfirmationOrder.Original,
                GpuConfirmationOrder.Candidate,
            },
            GpuOptimizationDecisionEngine.CreateBalancedConfirmationSchedule().ToArray());

        var noWinner = GpuOptimizationDecisionEngine.Screen(
            original,
            [tradeoff, incomplete],
            Policy);
        Assert.IsNull(noWinner.Finalist);
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, noWinner.Recommendation);
    }

    private static GpuOptimizationMeasurementSet Measurement(double primary, double frameTime) =>
        new(
            Series("DPC p99", primary),
            new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
            {
                ["Frame time"] = Series("Frame time", frameTime),
            });

    private static GpuAffinityCandidate Candidate(int core, byte cpu, double pressure) =>
        new(core, new LogicalProcessorId(0, cpu), 0, true, pressure);

    private static MetricSeries Series(string name, double value, int count = 20) =>
        new(name, MetricDirection.LowerIsBetter, Enumerable.Repeat(value, count));
}

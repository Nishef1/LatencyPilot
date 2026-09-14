using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
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

        var presentMon = PresentMonGuardrailSeriesBuilder.Create(
        [
            PresentMonSnapshot(PresentMonWorkloadCaptureStatus.Available, 10, 120, 4, 7, 0.01),
            PresentMonSnapshot(PresentMonWorkloadCaptureStatus.Available, 11, 118, 5, 8, 0.02),
            PresentMonSnapshot(PresentMonWorkloadCaptureStatus.ApiUnavailable, 999, 1, 999, 999, 1),
        ]);

        CollectionAssert.AreEqual(
            new[] { 10d, 11d },
            presentMon[PresentMonGuardrailSeriesBuilder.CpuFrameTimeMetric].Samples.ToArray());
        Assert.AreEqual(
            MetricDirection.HigherIsBetter,
            presentMon[PresentMonGuardrailSeriesBuilder.DisplayedFpsMetric].Direction);
        CollectionAssert.AreEqual(
            new[] { 120d, 118d },
            presentMon[PresentMonGuardrailSeriesBuilder.DisplayedFpsMetric].Samples.ToArray());
        CollectionAssert.AreEqual(
            new[] { 4d, 5d },
            presentMon[PresentMonGuardrailSeriesBuilder.GpuLatencyMetric].Samples.ToArray());
        CollectionAssert.AreEqual(
            new[] { 7d, 8d },
            presentMon[PresentMonGuardrailSeriesBuilder.DisplayLatencyMetric].Samples.ToArray());
        CollectionAssert.AreEqual(
            new[] { 0.01d, 0.02d },
            presentMon[PresentMonGuardrailSeriesBuilder.DroppedFrameRatioMetric].Samples.ToArray());
        Assert.IsFalse(presentMon.ContainsKey(PresentMonGuardrailSeriesBuilder.PresentedFpsMetric));
    }

    private static GpuOptimizationMeasurementSet Measurement(double primary, double frameTime) =>
        new(
            Series("DPC p99", primary),
            new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
            {
                ["Frame time"] = Series("Frame time", frameTime),
            });

    private static PresentMonWorkloadMetricsSnapshot PresentMonSnapshot(
        PresentMonWorkloadCaptureStatus status,
        double cpuFrameTime,
        double displayedFps,
        double gpuLatency,
        double displayLatency,
        double droppedFrameRatio) =>
        new(
            status,
            42,
            1_000,
            new PresentMonApiVersionSnapshot(3, 4, 0),
            [
                new PresentMonSwapChainMetricsSnapshot(
                    1,
                    null,
                    displayedFps,
                    cpuFrameTime,
                    null,
                    null,
                    null,
                    null,
                    null,
                    droppedFrameRatio,
                    gpuLatency,
                    displayLatency),
            ],
            [],
            "PresentMonAPI2.dll",
            null,
            status == PresentMonWorkloadCaptureStatus.Available ? null : "not available",
            DateTimeOffset.UnixEpoch);

    private static GpuAffinityCandidate Candidate(int core, byte cpu, double pressure) =>
        new(core, new LogicalProcessorId(0, cpu), 0, true, pressure);

    private static MetricSeries Series(string name, double value, int count = 20) =>
        new(name, MetricDirection.LowerIsBetter, Enumerable.Repeat(value, count));
}

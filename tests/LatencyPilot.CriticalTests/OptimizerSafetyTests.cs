using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Baselines;
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

    private static readonly GpuConfirmationOrder[] ExpectedConfirmationSchedule =
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

    private static readonly double[] ExpectedCpuFrameTimes = [10d, 11d];
    private static readonly double[] ExpectedDisplayedFps = [120d, 118d];
    private static readonly double[] ExpectedGpuLatency = [4d, 5d];
    private static readonly double[] ExpectedDisplayLatency = [7d, 8d];
    private static readonly double[] ExpectedDroppedFrameRatio = [0.01d, 0.02d];

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
        Assert.AreEqual(GpuOptimizationRecommendation.ConfirmFinalist, screening.Recommendation);

        CollectionAssert.AreEqual(
            ExpectedConfirmationSchedule,
            GpuOptimizationDecisionEngine.CreateBalancedConfirmationSchedule().ToArray());

        var noWinner = GpuOptimizationDecisionEngine.Screen(
            original,
            [tradeoff, incomplete],
            Policy);
        Assert.IsNull(noWinner.Finalist);
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, noWinner.Recommendation);
        Assert.ThrowsExactly<ArgumentException>(() =>
            GpuOptimizationDecisionEngine.Screen(original, [clean, clean], Policy));
        Assert.ThrowsExactly<ArgumentException>(() =>
            GpuOptimizationDecisionEngine.Screen(original, Enumerable.Repeat(clean, 17), Policy));

        var baselineQuality = BaselineQualityAnalyzer.Analyze(Enumerable.Range(1, 5)
            .Select(number => new BaselineWindowEvidence(number, DateTimeOffset.UnixEpoch,
                20_000, 20_000, true, null, 1_000, 100, 1_000, 10)).ToArray());
        var runs = ConfirmationRuns(clean.Candidate);
        var baseline = new GpuOptimizationBaselineEvidence(baselineQuality, runs[0].SessionId,
            runs[0].WorkloadIdentity, runs[0].EnvironmentIdentity, runs[0].SourceRevisionId);
        var confirmation = GpuOptimizationConfirmation.Confirm(clean.Candidate, baseline, runs, Policy);
        Assert.AreEqual(ExperimentVerdict.Improved, confirmation.Verdict);
        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, confirmation.Recommendation);
        Assert.AreEqual(-15d, confirmation.Metrics[0].RawDelta);
        Assert.AreEqual(4_000L, confirmation.Metrics[0].OriginalSampleCount);
        Assert.AreEqual(4_000L, confirmation.Metrics[0].CandidateSampleCount);

        var regressedGuardrail = runs.Select(run => run.Role == GpuConfirmationOrder.Candidate
            ? run with { Measurement = ConfirmationMeasurement(85, 12) } : run).ToArray();
        var tradeoffConfirmation = GpuOptimizationConfirmation.Confirm(clean.Candidate, baseline, regressedGuardrail, Policy);
        Assert.AreEqual(ExperimentVerdict.Tradeoff, tradeoffConfirmation.Verdict);
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, tradeoffConfirmation.Recommendation);

        var invalidSequences = new[]
        {
            runs.Take(7).ToArray(),
            runs.Select((run, index) => index == 1 ? run with { AppliedStateVerified = false } : run).ToArray(),
            runs.Select((run, index) => index == 1 ? run with { AppliedProcessor = new LogicalProcessorId(0, 4) } : run).ToArray(),
            runs.Select((run, index) => index == 1 ? run with { CaptureIntegrityValid = false } : run).ToArray(),
            runs.Select((run, index) => index == 1 ? run with { CaptureId = runs[0].CaptureId } : run).ToArray(),
            runs.Select((run, index) => index == 1 ? run with { EnvironmentIdentity = "changed-power-mode" } : run).ToArray(),
            runs.Select((run, index) => index == 1 ? run with { WorkloadIdentity = "other-scene" } : run).ToArray(),
            runs.Select((run, index) => index == 1 ? run with { SourceRevisionId = "dirty" } : run).ToArray(),
            runs.Select((run, index) => index == 1 ? run with { ActualDurationMilliseconds = 28_000 } : run).ToArray(),
            runs.Select((run, index) => index == 1 ? run with { RequestedDurationMilliseconds = 5_000 } : run).ToArray(),
            runs.Select((run, index) => index == 1 ? run with { Role = GpuConfirmationOrder.Original } : run).ToArray(),
            runs.Select((run, index) => index == 1 ? run with { Measurement = Measurement(85, 10) } : run).ToArray(),
            runs.Select((run, index) => index >= 4 && run.Role == GpuConfirmationOrder.Original
                ? run with { Measurement = ConfirmationMeasurement(140, 10) } : run).ToArray(),
        };
        foreach (var invalidSequence in invalidSequences)
        {
            var rejected = GpuOptimizationConfirmation.Confirm(clean.Candidate, baseline, invalidSequence, Policy);
            Assert.AreEqual(ExperimentVerdict.Inconclusive, rejected.Verdict);
            Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, rejected.Recommendation);
            Assert.IsTrue(rejected.Reasons.Count > 0);
        }
        Assert.AreEqual(ExperimentVerdict.Inconclusive,
            GpuOptimizationConfirmation.Confirm(clean.Candidate,
                baseline with { WorkloadIdentity = "stale-baseline-scene" }, runs, Policy).Verdict);

        var insideMeasuredNoise = runs.Select((run, index) => run with
        {
            Measurement = ConfirmationMeasurement(run.Role == GpuConfirmationOrder.Candidate ? 94 : index < 4 ? 95 : 105, 10),
        }).ToArray();
        var noiseResult = GpuOptimizationConfirmation.Confirm(clean.Candidate, baseline, insideMeasuredNoise, Policy);
        Assert.AreEqual(ExperimentVerdict.NoMeasurableDifference, noiseResult.Verdict);
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, noiseResult.Recommendation);

        var zeroGuardrails = runs.Select(run => run with
        {
            Measurement = ConfirmationMeasurement(run.Role == GpuConfirmationOrder.Original ? 100 : 85, 0),
        }).ToArray();
        Assert.AreEqual(ExperimentVerdict.Improved,
            GpuOptimizationConfirmation.Confirm(clean.Candidate, baseline, zeroGuardrails, Policy).Verdict);
        var newAdverseEvents = zeroGuardrails.Select(run => run.Role == GpuConfirmationOrder.Candidate
            ? run with { Measurement = ConfirmationMeasurement(85, 0.01) } : run).ToArray();
        Assert.AreEqual(ExperimentVerdict.Tradeoff,
            GpuOptimizationConfirmation.Confirm(clean.Candidate, baseline, newAdverseEvents, Policy).Verdict);

        var zeroGuardrailScreening = GpuOptimizationDecisionEngine.Screen(Measurement(100, 0),
            [new GpuOptimizationCandidateMeasurement(clean.Candidate, Measurement(85, 0))], Policy);
        Assert.AreEqual(GpuOptimizationRecommendation.ConfirmFinalist, zeroGuardrailScreening.Recommendation);
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal,
            GpuOptimizationDecisionEngine.Screen(Measurement(100, 0),
                [new GpuOptimizationCandidateMeasurement(clean.Candidate, Measurement(85, 0.01))], Policy).Recommendation);

        var throughputTail = runs.Select(run => run with
        {
            Measurement = new GpuOptimizationMeasurementSet(run.Measurement.Primary,
                new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
                {
                    ["Displayed FPS"] = new("Displayed FPS", MetricDirection.HigherIsBetter,
                        run.Role == GpuConfirmationOrder.Original ? Enumerable.Repeat(100d, 1_000)
                            : Enumerable.Repeat(50d, 20).Concat(Enumerable.Repeat(110d, 980))),
                }),
        }).ToArray();
        Assert.AreEqual(ExperimentVerdict.Tradeoff,
            GpuOptimizationConfirmation.Confirm(clean.Candidate, baseline, throughputTail, Policy).Verdict);

        var droppedFrames = runs.Select(run => run with
        {
            Measurement = new GpuOptimizationMeasurementSet(run.Measurement.Primary,
                new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
                {
                    [PresentMonGuardrailSeriesBuilder.DroppedFrameRatioMetric] = new(
                        PresentMonGuardrailSeriesBuilder.DroppedFrameRatioMetric, MetricDirection.LowerIsBetter,
                        run.Role == GpuConfirmationOrder.Original ? Enumerable.Repeat(0d, 1_000)
                            : Enumerable.Repeat(0d, 999).Append(1d)),
                }),
        }).ToArray();
        var droppedResult = GpuOptimizationConfirmation.Confirm(clean.Candidate, baseline, droppedFrames, Policy);
        Assert.AreEqual(ExperimentVerdict.Tradeoff, droppedResult.Verdict);
        Assert.AreEqual(0.001, droppedResult.Metrics[1].CandidateValue);

        var overflowRuns = runs.Select(run => run with
        {
            Measurement = ConfirmationMeasurement(run.Role == GpuConfirmationOrder.Original
                ? double.Epsilon : double.MaxValue, 10),
        }).ToArray();
        var overflowResult = GpuOptimizationConfirmation.Confirm(clean.Candidate, baseline, overflowRuns, Policy);
        Assert.AreEqual(ExperimentVerdict.Inconclusive, overflowResult.Verdict);
        Assert.IsNull(overflowResult.Metrics[0].RelativeImprovement);
        Assert.IsFalse(string.IsNullOrWhiteSpace(System.Text.Json.JsonSerializer.Serialize(overflowResult)));

        var presentMon = PresentMonGuardrailSeriesBuilder.Create(
        [
            PresentMonSnapshot(PresentMonWorkloadCaptureStatus.Available, 10, 120, 4, 7, 0.01),
            PresentMonSnapshot(PresentMonWorkloadCaptureStatus.Available, 11, 118, 5, 8, 0.02),
        ]);

        CollectionAssert.AreEqual(
            ExpectedCpuFrameTimes,
            presentMon[PresentMonGuardrailSeriesBuilder.CpuFrameTimeMetric].Samples.ToArray());
        Assert.AreEqual(
            MetricDirection.HigherIsBetter,
            presentMon[PresentMonGuardrailSeriesBuilder.DisplayedFpsMetric].Direction);
        CollectionAssert.AreEqual(
            ExpectedDisplayedFps,
            presentMon[PresentMonGuardrailSeriesBuilder.DisplayedFpsMetric].Samples.ToArray());
        CollectionAssert.AreEqual(
            ExpectedGpuLatency,
            presentMon[PresentMonGuardrailSeriesBuilder.GpuLatencyMetric].Samples.ToArray());
        CollectionAssert.AreEqual(
            ExpectedDisplayLatency,
            presentMon[PresentMonGuardrailSeriesBuilder.DisplayLatencyMetric].Samples.ToArray());
        CollectionAssert.AreEqual(
            ExpectedDroppedFrameRatio,
            presentMon[PresentMonGuardrailSeriesBuilder.DroppedFrameRatioMetric].Samples.ToArray());
        Assert.IsFalse(presentMon.ContainsKey(PresentMonGuardrailSeriesBuilder.PresentedFpsMetric));

        var validWindow = PresentMonSnapshot(PresentMonWorkloadCaptureStatus.Available, 10, 120, 4, 7, 0.01);
        var unavailableWindow = PresentMonSnapshot(PresentMonWorkloadCaptureStatus.ApiUnavailable, 999, 1, 999, 999, 1);
        Assert.AreEqual(0, PresentMonGuardrailSeriesBuilder.Create([validWindow, unavailableWindow]).Count);
        Assert.AreEqual(0, PresentMonGuardrailSeriesBuilder.Create([validWindow, validWindow with { ProcessId = 43 }]).Count);
        Assert.AreEqual(0, PresentMonGuardrailSeriesBuilder.Create([validWindow, validWindow with { SwapChains = [] }]).Count);
        var partialMetric = validWindow with
        {
            SwapChains = [validWindow.SwapChains[0] with { GpuLatencyMilliseconds = null, DroppedFrameRatio = 2 }],
        };
        var partialSeries = PresentMonGuardrailSeriesBuilder.Create([validWindow, partialMetric]);
        Assert.IsFalse(partialSeries.ContainsKey(PresentMonGuardrailSeriesBuilder.GpuLatencyMetric));
        Assert.IsFalse(partialSeries.ContainsKey(PresentMonGuardrailSeriesBuilder.DroppedFrameRatioMetric));
        Assert.AreEqual(2, partialSeries[PresentMonGuardrailSeriesBuilder.CpuFrameTimeMetric].Samples.Count);
    }

    private static GpuOptimizationMeasurementSet Measurement(double primary, double frameTime) =>
        new(
            Series("DPC p99", primary),
            new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
            {
                ["Frame time"] = Series("Frame time", frameTime),
            });

    private static GpuOptimizationMeasurementSet ConfirmationMeasurement(double primary, double guardrail) =>
        new(Series("DPC duration (us)", primary, 1_000),
            new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
            {
                ["Frame time"] = Series("Frame time", guardrail, 1_000),
            });

    private static GpuOptimizationConfirmationRun[] ConfirmationRuns(GpuAffinityCandidate finalist)
    {
        var sessionId = Guid.NewGuid();
        return GpuOptimizationDecisionEngine.CreateBalancedConfirmationSchedule()
            .Select((role, index) => new GpuOptimizationConfirmationRun(
                index + 1, role, sessionId, Guid.NewGuid(), "scene-v1", "driver-power-v1", new string('a', 40),
                role == GpuConfirmationOrder.Candidate ? finalist.Processor : null,
                true, true, 30_000, 30_000,
                ConfirmationMeasurement(role == GpuConfirmationOrder.Original ? 100 : 85, 10)))
            .ToArray();
    }

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

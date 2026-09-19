#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class OptimizerSafetyTests
{
    private static readonly double[] ExpectedCpuFrameTimes = [10d, 11d];
    private static readonly double[] ExpectedDisplayedFps = [120d, 118d];
    private static readonly double[] ExpectedGpuLatency = [4d, 5d];
    private static readonly double[] ExpectedDisplayLatency = [7d, 8d];
    private static readonly double[] ExpectedDroppedFrameRatio = [0.01d, 0.02d];

    [AuditCase]
    public void PresentMonGuardrailProjectionRejectsIncompatibleEvidence()
    {
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
}

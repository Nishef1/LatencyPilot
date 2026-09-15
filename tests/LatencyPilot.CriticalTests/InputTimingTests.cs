using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class InputTimingTests
{
    [TestMethod]
    public void HostReportTimingPreservesIntervalsAndDetectsGapsAndBursts()
    {
        var stableTicks = Enumerable.Range(0, 101)
            .Select(index => index * 1_000_000L)
            .ToArray();
        var stable = InputTimingAnalyzer.Analyze(
            new InputReportTimestampSeries("mouse-1", 1_000_000_000L, stableTicks));

        Assert.AreEqual(InputTimingAnalysisStatus.Available, stable.Status);
        Assert.AreEqual(100, stable.IntervalCount);
        Assert.IsNotNull(stable.MedianIntervalMilliseconds);
        Assert.IsNotNull(stable.P95IntervalMilliseconds);
        Assert.IsNotNull(stable.P99IntervalMilliseconds);
        Assert.IsNotNull(stable.ObservedReportRateHz);
        Assert.AreEqual(1.0, stable.MedianIntervalMilliseconds.Value, 0.000001);
        Assert.AreEqual(1.0, stable.P95IntervalMilliseconds.Value, 0.000001);
        Assert.AreEqual(1.0, stable.P99IntervalMilliseconds.Value, 0.000001);
        Assert.AreEqual(1_000.0, stable.ObservedReportRateHz.Value, 0.000001);
        Assert.AreEqual(0, stable.LongGapCount);
        Assert.AreEqual(0, stable.BurstIntervalCount);
        Assert.IsFalse(stable.HasBurstOrCoalescingEvidence);
        Assert.AreEqual(100, stable.IntervalsMilliseconds.Count);

        var gappedTicks = stableTicks.ToArray();
        for (var index = 51; index < gappedTicks.Length; index++)
        {
            gappedTicks[index] += 9_000_000L;
        }
        var gapped = InputTimingAnalyzer.Analyze(
            new InputReportTimestampSeries("mouse-1", 1_000_000_000L, gappedTicks));
        Assert.IsTrue(gapped.LongGapCount >= 1);
        Assert.IsTrue(gapped.P99IntervalMilliseconds > stable.P99IntervalMilliseconds);

        var burstTicks = new long[101];
        for (var index = 1; index < burstTicks.Length; index++)
        {
            burstTicks[index] = burstTicks[index - 1] + (index % 10 == 0 ? 100_000L : 1_100_000L);
        }
        var burst = InputTimingAnalyzer.Analyze(
            new InputReportTimestampSeries("mouse-1", 1_000_000_000L, burstTicks));
        Assert.IsTrue(burst.BurstIntervalCount > 0);
        Assert.IsTrue(burst.HasBurstOrCoalescingEvidence);

        var insufficient = InputTimingAnalyzer.Analyze(
            new InputReportTimestampSeries("mouse-1", 1_000_000_000L, [0L, 1_000_000L, 2_000_000L]));
        Assert.AreEqual(InputTimingAnalysisStatus.InsufficientSamples, insufficient.Status);
        Assert.IsNull(insufficient.MedianIntervalMilliseconds);

        Assert.ThrowsExactly<ArgumentException>(() => InputTimingAnalyzer.Analyze(
            new InputReportTimestampSeries("mouse-1", 1_000_000_000L, [0L, 2_000_000L, 1_000_000L])));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InputTimingAnalyzer.Analyze(
            new InputReportTimestampSeries("mouse-1", 0L, stableTicks)));

        var selectedHandle = new nint(0x1234);
        var otherHandle = new nint(0x5678);
        Assert.IsTrue(RawInputTimingCapture.IsSelectedDeviceRemoval(
            selectedHandle,
            RawInputTimingCapture.DeviceRemovalChangeCode,
            selectedHandle));
        Assert.IsFalse(RawInputTimingCapture.IsSelectedDeviceRemoval(
            selectedHandle,
            RawInputTimingCapture.DeviceArrivalChangeCode,
            selectedHandle));
        Assert.IsFalse(RawInputTimingCapture.IsSelectedDeviceRemoval(
            selectedHandle,
            RawInputTimingCapture.DeviceRemovalChangeCode,
            otherHandle));

        var awakeBefore = new SystemAwakeTimeSnapshot(1_000, 10_000_000);
        var awakeAfter = new SystemAwakeTimeSnapshot(6_000, 60_000_000);
        var sleptAfter = new SystemAwakeTimeSnapshot(11_000, 60_000_000);
        Assert.IsTrue(RawInputTimingCapture.IsAwakeIntervalUsable(awakeBefore, awakeAfter));
        Assert.IsFalse(RawInputTimingCapture.IsAwakeIntervalUsable(awakeBefore, sleptAfter));
    }
}

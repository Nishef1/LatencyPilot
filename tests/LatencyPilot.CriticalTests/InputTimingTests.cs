using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
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
        Assert.AreEqual(1.0, stable.MedianIntervalMilliseconds, 0.000001);
        Assert.AreEqual(1.0, stable.P95IntervalMilliseconds, 0.000001);
        Assert.AreEqual(1.0, stable.P99IntervalMilliseconds, 0.000001);
        Assert.AreEqual(1_000.0, stable.ObservedReportRateHz, 0.000001);
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
    }
}

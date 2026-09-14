using LatencyPilot.Benchmarking.Baselines;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class TemporaryBaselineTransientSignalTests
{
    [TestMethod]
    public void ValidBaselineCanStillSurfaceTransientTailOutlier()
    {
        var summary = BaselineTransientSignalAnalyzer.Analyze([
            new BaselineTransientWindowSignal(1, 0, 0, 290.6, 0, 0, 106.3),
            new BaselineTransientWindowSignal(2, 0, 0, 331.4, 0, 0, 125.7),
            new BaselineTransientWindowSignal(3, 1, 1, 11_268.0, 0, 0, 83.9),
            new BaselineTransientWindowSignal(4, 0, 0, 202.0, 0, 0, 84.4),
            new BaselineTransientWindowSignal(5, 0, 0, 335.3, 0, 0, 136.8),
        ]);

        Assert.IsTrue(summary.HasOverOneMillisecondSignal);
        Assert.IsTrue(summary.HasOverThreeMillisecondSignal);
        Assert.AreEqual(1, summary.DpcOverOneMillisecondCount);
        Assert.AreEqual(1, summary.DpcOverThreeMillisecondCount);
        Assert.AreEqual(3, summary.LargestWindowNumber);
        Assert.AreEqual("DPC", summary.LargestMetricName);
        Assert.AreEqual(11_268.0, summary.LargestDurationMicroseconds);
    }
}

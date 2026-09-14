using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Core.System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuAffinityCandidatePlannerTests
{
    [TestMethod]
    public void BaselineInterruptSharesDrivePhysicalCoreCandidateRanking()
    {
        var cpu0 = new LogicalProcessorId(0, 0);
        var cpu1 = new LogicalProcessorId(0, 1);
        var cpu2 = new LogicalProcessorId(0, 2);
        var cpu3 = new LogicalProcessorId(0, 3);
        var topology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0, [cpu0, cpu1, cpu2, cpu3])],
            [
                new ProcessorCoreSnapshot(0, 0, [cpu0, cpu1]),
                new ProcessorCoreSnapshot(1, 0, [cpu2, cpu3]),
            ],
            DateTimeOffset.UnixEpoch);

        IReadOnlyList<IReadOnlyList<ProcessorInterruptCountEvidence>> windows =
        [
            [
                new ProcessorInterruptCountEvidence(cpu0, 90, 10),
                new ProcessorInterruptCountEvidence(cpu1, 10, 0),
                new ProcessorInterruptCountEvidence(cpu2, 20, 0),
                new ProcessorInterruptCountEvidence(cpu3, 0, 0),
            ],
            [
                new ProcessorInterruptCountEvidence(cpu0, 80, 10),
                new ProcessorInterruptCountEvidence(cpu1, 10, 0),
                new ProcessorInterruptCountEvidence(cpu2, 30, 0),
                new ProcessorInterruptCountEvidence(cpu3, 0, 0),
            ],
        ];

        var pressure = ProcessorPressureEvidenceBuilder.Create(topology, windows);
        Assert.AreEqual(1d, pressure.Sum(static item => item.PressureScore), 0.000001d);

        var candidates = GpuAffinityCandidatePlanner.Create(topology, pressure, maximumCandidates: 2);
        Assert.AreEqual(2, candidates.Count);
        Assert.AreEqual(cpu3, candidates[0].Processor);
        Assert.AreEqual(cpu1, candidates[1].Processor);
        Assert.IsTrue(candidates[0].ObservedPressureScore < candidates[1].ObservedPressureScore);
    }
}

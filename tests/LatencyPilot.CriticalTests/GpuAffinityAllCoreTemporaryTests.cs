using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Core.System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuAffinityAllCoreTemporaryTests
{
    [TestMethod]
    public void ScreeningCoversAllBoundedPhysicalCoresAndRefinesWinnerSiblings()
    {
        var cores = Enumerable.Range(0, 8)
            .Select(index => new ProcessorCoreSnapshot(
                index,
                0,
                [
                    new LogicalProcessorId(0, checked((byte)(index * 2))),
                    new LogicalProcessorId(0, checked((byte)(index * 2 + 1))),
                ]))
            .ToArray();
        var logical = cores.SelectMany(static core => core.LogicalProcessors).ToArray();
        var topology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0, logical)],
            cores,
            DateTimeOffset.UnixEpoch);
        var pressure = logical
            .Select(processor => new ProcessorPressureEvidence(processor, processor.Number / 100d))
            .ToArray();

        var candidates = GpuAffinityCandidatePlanner.Create(topology, pressure);

        Assert.AreEqual(8, candidates.Count);
        CollectionAssert.AreEquivalent(
            Enumerable.Range(0, 8).ToArray(),
            candidates.Select(static candidate => candidate.PhysicalCoreIndex).ToArray());
        Assert.IsTrue(candidates.Any(static candidate => candidate.Processor == new LogicalProcessorId(0, 0)));

        var winner = candidates.Single(static candidate => candidate.PhysicalCoreIndex == 3);
        var siblings = GpuAffinityCandidatePlanner.CreateSiblingRefinement(topology, pressure, winner);
        Assert.AreEqual(2, siblings.Count);
        CollectionAssert.AreEquivalent(
            new[] { new LogicalProcessorId(0, 6), new LogicalProcessorId(0, 7) },
            siblings.Select(static candidate => candidate.Processor).ToArray());
    }
}

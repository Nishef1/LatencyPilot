using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuBenchmarkContractTests
{
    [TestMethod]
    public void FrozenWorkloadAndCandidateIdentityRemainIndependent()
    {
        var workers = new[]
        {
            new LogicalProcessorId(0, 0),
            new LogicalProcessorId(0, 2),
            new LogicalProcessorId(0, 4),
            new LogicalProcessorId(0, 6),
        };
        var workload = GpuBenchmarkFrozenWorkload.Create(
            width: 1920,
            height: 1080,
            workerProcessors: workers,
            simulationIterationsPerWorker: 50_000,
            commandBatchesPerWorker: 96,
            seed: 0x51A7);

        var original = GpuBenchmarkTrialDefinition.Original(workload);
        var candidate = GpuBenchmarkTrialDefinition.Candidate(
            workload,
            new LogicalProcessorId(0, 3));

        Assert.AreEqual(workload.WorkloadIdentity, original.Workload.WorkloadIdentity);
        Assert.AreEqual(workload.WorkloadIdentity, candidate.Workload.WorkloadIdentity);
        CollectionAssert.AreEqual(
            original.Workload.WorkerProcessors.ToArray(),
            candidate.Workload.WorkerProcessors.ToArray());
        Assert.AreEqual(original.Workload.SimulationIterationsPerWorker, candidate.Workload.SimulationIterationsPerWorker);
        Assert.AreEqual(original.Workload.CommandBatchesPerWorker, candidate.Workload.CommandBatchesPerWorker);
        Assert.AreEqual(original.Workload.Seed, candidate.Workload.Seed);
        Assert.AreEqual(GpuBenchmarkTrialRole.Original, original.Role);
        Assert.IsNull(original.CandidateProcessor);
        Assert.AreEqual(GpuBenchmarkTrialRole.Candidate, candidate.Role);
        Assert.AreEqual(new LogicalProcessorId(0, 3), candidate.CandidateProcessor);

        Assert.ThrowsExactly<ArgumentException>(() =>
            GpuBenchmarkFrozenWorkload.Create(
                1920,
                1080,
                [new LogicalProcessorId(0, 0), new LogicalProcessorId(1, 0)],
                50_000,
                96,
                0x51A7));
    }
}

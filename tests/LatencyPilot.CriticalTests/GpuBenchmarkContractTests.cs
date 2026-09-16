using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Benchmarking.Statistics;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Devices;
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

    [TestMethod]
    public void BenchmarkEvidenceUsesRawFramesAndInternalGpuTimestamps()
    {
        var startedAt = DateTimeOffset.UnixEpoch;
        var frameTimes = Enumerable.Range(0, 1_000)
            .Select(index => 8d + (index / 1_000d))
            .ToArray();
        var capture = new PresentMonFrameCaptureSnapshot(
            PresentMonWorkloadCaptureStatus.Available,
            77,
            30_000,
            30_000,
            new PresentMonApiVersionSnapshot(3, 4, 0),
            frameTimes.Select((frameTime, index) => new PresentMonFrameMetricsSnapshot(
                1,
                frameTime,
                frameTime * 0.7d,
                frameTime * 0.3d,
                null,
                5d + (index / 10_000d),
                null,
                false,
                null,
                null)).ToArray(),
            [PresentMonGuardrailSeriesBuilder.DisplayLatencyMetric],
            "PresentMonAPI2.dll",
            null,
            null,
            startedAt,
            startedAt.AddSeconds(30));

        var evidence = new GpuBenchmarkEvidence(
            GpuBenchmarkEvidence.SchemaId,
            new string('a', 40),
            "gpu-affinity-benchmark-v1",
            "windows-test",
            "gpu-test",
            "driver-test",
            "topology-test",
            77,
            "Original",
            1,
            null,
            "workload-test",
            [new LogicalProcessorId(0, 0), new LogicalProcessorId(0, 2)],
            0x51A7,
            1_000_000,
            Enumerable.Repeat(4d, 1_000).ToArray(),
            capture,
            "2.5.1",
            Guid.NewGuid(),
            true,
            0,
            []);

        var interpreted = GpuBenchmarkEvidenceInterpreter.Interpret(evidence);
        Assert.IsTrue(interpreted.IsValid, string.Join("; ", interpreted.ValidityReasons));
        Assert.AreEqual(
            Percentiles.Calculate(frameTimes, 0.99),
            interpreted.FrameP99Milliseconds,
            0.000001d);
        Assert.AreEqual(
            1000d / interpreted.FrameP99Milliseconds,
            interpreted.OnePercentLowFps,
            0.000001d);
        Assert.AreEqual(1_000, interpreted.PrimaryFrameTime.Samples.Count);
        Assert.AreEqual(1_000, interpreted.D3D12GpuWork.Samples.Count);
        Assert.IsTrue(interpreted.Context.ContainsKey(PresentMonGuardrailSeriesBuilder.GpuBusyMetric));
        Assert.IsFalse(interpreted.Guardrails.ContainsKey(PresentMonGuardrailSeriesBuilder.GpuBusyMetric));
        Assert.IsFalse(interpreted.Guardrails.ContainsKey(PresentMonGuardrailSeriesBuilder.DisplayLatencyMetric));

        var oldPresentMon = GpuBenchmarkEvidenceInterpreter.Interpret(evidence with { PresentMonBinaryVersion = "2.4.1" });
        Assert.IsFalse(oldPresentMon.IsValid);
    }
}

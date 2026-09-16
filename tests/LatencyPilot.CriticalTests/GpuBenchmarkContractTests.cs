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
        var frozenWorkload = new GpuBenchmarkArtifactWorkload(
            96,
            50_000,
            [new LogicalProcessorId(0, 0), new LogicalProcessorId(0, 2)],
            0x51A7,
            1920,
            1080);

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
            [],
            frozenWorkload);

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

        var provenance = GpuAutoAffinityReportProvenance.FromEvidence(evidence);
        Assert.AreEqual(evidence.SourceRevisionId, provenance.SourceRevisionId);
        Assert.AreEqual(evidence.GpuIdentity, provenance.GpuIdentity);
        Assert.AreEqual(evidence.DriverIdentity, provenance.DriverIdentity);
        Assert.AreEqual(evidence.TopologyIdentity, provenance.TopologyIdentity);
        Assert.AreEqual(evidence.BenchmarkProcessId, provenance.BenchmarkProcessId);
        Assert.AreEqual(evidence.FrozenWorkloadIdentity, provenance.FrozenWorkloadIdentity);
        Assert.AreEqual(evidence.PresentMonBinaryVersion, provenance.PresentMonBinaryVersion);
        Assert.AreEqual(evidence.D3D12TimestampFrequency, provenance.D3D12TimestampFrequency);
        Assert.AreEqual(96, provenance.FrozenWorkload.CommandBatchesPerWorker);
        Assert.AreEqual(50_000, provenance.FrozenWorkload.SimulationIterationsPerWorker);
        Assert.AreEqual(1920, provenance.FrozenWorkload.Width);
        Assert.AreEqual(1080, provenance.FrozenWorkload.Height);
        CollectionAssert.AreEqual(evidence.WorkerMap.ToArray(), provenance.FrozenWorkload.WorkerMap.ToArray());

        var originalState = new GpuAutoAffinityStoredStateReport(
            "PCI\\VEN_10DE&DEV_2484",
            "NVIDIA GeForce RTX 3070",
            "test-driver",
            false,
            new GpuAutoAffinityStoredValueReport(false, null, string.Empty),
            new GpuAutoAffinityStoredValueReport(false, null, string.Empty));
        var candidateState = new GpuAutoAffinityStoredStateReport(
            originalState.DeviceInstanceId,
            originalState.DisplayName,
            originalState.DriverVersion,
            true,
            new GpuAutoAffinityStoredValueReport(true, "DWord", "04000000"),
            new GpuAutoAffinityStoredValueReport(true, "Binary", "0800000000000000"));
        var experimentId = Guid.NewGuid();
        var audit = new[]
        {
            new GpuAutoAffinityMutationAuditEntry(
                startedAt,
                "ApplyCandidate",
                experimentId,
                new LogicalProcessorId(0, 3),
                true,
                candidateState),
            new GpuAutoAffinityMutationAuditEntry(
                startedAt.AddSeconds(1),
                "Rollback",
                experimentId,
                new LogicalProcessorId(0, 3),
                true,
                originalState),
        };
        var report = new GpuAutoAffinityReport(
            GpuAutoAffinityReport.SchemaId,
            Guid.NewGuid(),
            startedAt,
            startedAt.AddSeconds(1),
            1337,
            [],
            [],
            "RestoreOriginal",
            null,
            true,
            true,
            [],
            provenance,
            originalState,
            originalState,
            audit,
            "clean-zero-unresolved");
        Assert.AreEqual(originalState, report.OriginalStoredState);
        Assert.AreEqual(originalState, report.FinalStoredState);
        Assert.AreEqual(2, report.MutationAudit?.Count);
        Assert.AreEqual("clean-zero-unresolved", report.RecoveryStatus);
        Assert.AreEqual("Rollback", report.MutationAudit?[1].Action);

        var oldPresentMon = GpuBenchmarkEvidenceInterpreter.Interpret(evidence with { PresentMonBinaryVersion = "2.4.1" });
        Assert.IsFalse(oldPresentMon.IsValid);

        var cpuBusyOnly = GpuBenchmarkReadiness.Evaluate(
            evidence,
            evidence,
            new GpuBenchmarkContaminationContext(
                SystemCpuBusyDrifted: true,
                ControlTrialDrifted: false,
                SleepOrResumeDetected: false,
                DeviceResetDetected: false,
                RetryAttempt: 0));
        Assert.AreEqual(GpuBenchmarkReadinessState.Ready, cpuBusyOnly.State);
        Assert.IsTrue(cpuBusyOnly.Context.Any(static item => item.Contains("CPU", StringComparison.OrdinalIgnoreCase)));

        Assert.AreEqual(
            GpuBenchmarkReadinessState.Inconclusive,
            GpuBenchmarkReadiness.Evaluate(
                evidence,
                evidence with { GpuIdentity = "different-gpu" },
                GpuBenchmarkContaminationContext.Clean).State);
        Assert.AreEqual(
            GpuBenchmarkReadinessState.Inconclusive,
            GpuBenchmarkReadiness.Evaluate(
                evidence,
                evidence with { BenchmarkProcessId = 78, PresentMonCapture = capture with { ProcessId = 78 } },
                GpuBenchmarkContaminationContext.Clean).State);
        Assert.AreEqual(
            GpuBenchmarkReadinessState.Inconclusive,
            GpuBenchmarkReadiness.Evaluate(
                evidence,
                evidence with { EtwLostEventCount = 1 },
                GpuBenchmarkContaminationContext.Clean).State);
        Assert.AreEqual(
            GpuBenchmarkReadinessState.Inconclusive,
            GpuBenchmarkReadiness.Evaluate(
                evidence,
                evidence with { FrozenWorkloadIdentity = "different-workload" },
                GpuBenchmarkContaminationContext.Clean).State);
        Assert.AreEqual(
            GpuBenchmarkReadinessState.Inconclusive,
            GpuBenchmarkReadiness.Evaluate(
                evidence,
                evidence with { WorkerMap = [new LogicalProcessorId(0, 1), new LogicalProcessorId(0, 3)] },
                GpuBenchmarkContaminationContext.Clean).State);

        var retryableControlDrift = GpuBenchmarkReadiness.Evaluate(
            evidence,
            evidence,
            GpuBenchmarkContaminationContext.Clean with { ControlTrialDrifted = true });
        Assert.AreEqual(GpuBenchmarkReadinessState.RetryableContamination, retryableControlDrift.State);
        var repeatedControlDrift = GpuBenchmarkReadiness.Evaluate(
            evidence,
            evidence,
            GpuBenchmarkContaminationContext.Clean with { ControlTrialDrifted = true, RetryAttempt = 1 });
        Assert.AreEqual(GpuBenchmarkReadinessState.Inconclusive, repeatedControlDrift.State);

        Assert.AreEqual(
            GpuBenchmarkReadinessState.Inconclusive,
            GpuBenchmarkReadiness.Evaluate(
                evidence,
                evidence,
                GpuBenchmarkContaminationContext.Clean with { SleepOrResumeDetected = true }).State);
        Assert.AreEqual(
            GpuBenchmarkReadinessState.Inconclusive,
            GpuBenchmarkReadiness.Evaluate(
                evidence,
                evidence,
                GpuBenchmarkContaminationContext.Clean with { DeviceResetDetected = true }).State);
    }

    [TestMethod]
    public void ControlledBenchmarkProtocolIsSessionBoundAndTrialBounded()
    {
        var sessionId = Guid.NewGuid();
        const string token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var run = GpuBenchmarkControlCommand.RunTrial(
            sessionId,
            token,
            runNumber: 7,
            TimeSpan.FromSeconds(15));

        Assert.AreEqual(GpuBenchmarkControlCommand.SchemaId, run.Schema);
        Assert.AreEqual(GpuBenchmarkControlCommandKind.RunTrial, run.Kind);
        Assert.IsTrue(GpuBenchmarkControlProtocol.TryValidate(run, sessionId, token, out var reason), reason);
        Assert.IsFalse(GpuBenchmarkControlProtocol.TryValidate(run, Guid.NewGuid(), token, out _));
        Assert.IsFalse(GpuBenchmarkControlProtocol.TryValidate(run, sessionId, new string('f', 64), out _));
        Assert.IsFalse(GpuBenchmarkControlProtocol.TryValidate(
            run with { DurationMilliseconds = 4_999 }, sessionId, token, out _));
        Assert.IsFalse(GpuBenchmarkControlProtocol.TryValidate(
            run with { RunNumber = 0 }, sessionId, token, out _));

        var stop = GpuBenchmarkControlCommand.Stop(sessionId, token);
        Assert.AreEqual(GpuBenchmarkControlCommandKind.Stop, stop.Kind);
        Assert.IsTrue(GpuBenchmarkControlProtocol.TryValidate(stop, sessionId, token, out var stopReason), stopReason);
        Assert.AreEqual(0, stop.RunNumber);
        Assert.AreEqual(0, stop.DurationMilliseconds);
    }
}

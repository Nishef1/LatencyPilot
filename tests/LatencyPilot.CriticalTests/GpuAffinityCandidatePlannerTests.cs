using System.Buffers.Binary;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Metrics;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuAffinityCandidatePlannerTests
{
    [TestMethod]
    public async Task BaselineInterruptSharesDrivePhysicalCoreCandidateRanking()
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

        Span<byte> descriptor = stackalloc byte[AllocatedIrqDescriptorParser.Descriptor64Size];
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor[0..4], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor[4..8], AllocatedIrqDescriptorParser.IrqTypeRange);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[8..10], 0x0002);
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[10..12], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor[12..16], 42);
        BinaryPrimitives.WriteUInt64LittleEndian(descriptor[16..24], 1UL << 3);

        Assert.IsTrue(AllocatedIrqDescriptorParser.TryParseResourceList(descriptor, out var parsed));
        Assert.AreEqual(42u, parsed.Irq);
        Assert.AreEqual((ushort)0, parsed.ProcessorGroup);
        Assert.AreEqual(1UL << 3, parsed.AffinityMask);
        Assert.AreEqual((ushort)0x0002, parsed.RawFlags);

        Assert.IsFalse(AllocatedIrqDescriptorParser.TryParseResourceList(descriptor[..^1], out _));

        var requirementsList = descriptor.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(requirementsList.AsSpan(0, 4), 1);
        Assert.IsFalse(AllocatedIrqDescriptorParser.TryParseResourceList(requirementsList, out _));

        var wrongType = descriptor.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongType.AsSpan(4, 4), 8);
        Assert.IsFalse(AllocatedIrqDescriptorParser.TryParseResourceList(wrongType, out _));

        var noProcessorTarget = descriptor.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(noProcessorTarget.AsSpan(16, 8), 0);
        Assert.IsFalse(AllocatedIrqDescriptorParser.TryParseResourceList(noProcessorTarget, out _));

        var startedAt = DateTimeOffset.UnixEpoch;
        var rawFrames = new PresentMonFrameCaptureSnapshot(
            PresentMonWorkloadCaptureStatus.Available,
            77,
            30_000,
            30_000,
            new PresentMonApiVersionSnapshot(3, 4, 0),
            Enumerable.Range(0, 1_000)
                .Select(index => new PresentMonFrameMetricsSnapshot(
                    1,
                    8d + index / 10_000d,
                    5d,
                    3d,
                    6d,
                    5d,
                    1d,
                    index == 999,
                    4d,
                    7d))
                .ToArray(),
            [],
            "PresentMonAPI2.dll",
            null,
            null,
            startedAt,
            startedAt.AddSeconds(30));

        var rawSeries = PresentMonGuardrailSeriesBuilder.Create(rawFrames);
        Assert.AreEqual(1_000, rawSeries[PresentMonGuardrailSeriesBuilder.CpuFrameTimeMetric].Samples.Count);
        Assert.AreEqual(1_000, rawSeries[PresentMonGuardrailSeriesBuilder.DroppedFrameRatioMetric].Samples.Count);
        Assert.AreEqual(1d, rawSeries[PresentMonGuardrailSeriesBuilder.DroppedFrameRatioMetric].Samples[^1]);
        Assert.IsFalse(rawSeries.ContainsKey(PresentMonGuardrailSeriesBuilder.DisplayedFpsMetric));
        Assert.IsFalse(rawSeries.ContainsKey(PresentMonGuardrailSeriesBuilder.PresentedFpsMetric));

        var incompleteFrames = rawFrames with
        {
            Frames = rawFrames.Frames.Select((frame, index) =>
                index == 500 ? frame with { CpuFrameTimeMilliseconds = null } : frame).ToArray(),
        };
        Assert.IsFalse(PresentMonGuardrailSeriesBuilder.Create(incompleteFrames)
            .ContainsKey(PresentMonGuardrailSeriesBuilder.CpuFrameTimeMetric));

        var kernel = new KernelLatencyCaptureResult(
            startedAt,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            Enumerable.Range(0, 1_000)
                .Select(index => new KernelLatencyEvent(
                    KernelLatencyEventKind.Dpc,
                    index % 4,
                    index * 30d,
                    25d + index / 1_000d,
                    0,
                    null,
                    null))
                .ToArray(),
            0,
            0,
            0,
            false);
        var request = new GpuOptimizationEvidenceRequest(
            1,
            GpuConfirmationOrder.Candidate,
            Guid.NewGuid(),
            77,
            "scene-v1",
            "environment-v1",
            new string('a', 40),
            candidates[0],
            TimeSpan.FromSeconds(30));
        var verification = new GpuOptimizationStateVerification(
            "PCI\\VEN_10DE&DEV_TEST",
            GpuConfirmationOrder.Candidate,
            candidates[0].Processor,
            true,
            startedAt);
        var runtimePlacement = new GpuInterruptRuntimePlacementEvidence(
            verification.TargetDeviceInstanceId,
            "nvlddmkm",
            candidates[0].Processor.Number,
            200,
            200,
            0,
            5,
            [new ProcessorObservedInterruptCount(candidates[0].Processor.Number, 200)]);

        var mapped = GpuOptimizationEvidenceCollector.TryCreateRun(
            request,
            verification,
            kernel,
            rawFrames,
            runtimePlacement,
            Guid.NewGuid());
        Assert.IsTrue(mapped.IsUsable, mapped.Reason);
        Assert.IsNotNull(mapped.Run);
        Assert.AreEqual(1_000, mapped.Run.Measurement.Primary.Samples.Count);
        Assert.IsTrue(mapped.Run.Measurement.Guardrails.Values.All(static series => series.Samples.Count >= 1_000));
        Assert.AreEqual(candidates[0].Processor, mapped.Run.AppliedProcessor);
        Assert.AreEqual(runtimePlacement, mapped.RuntimePlacement);

        Assert.IsFalse(GpuOptimizationEvidenceCollector.TryCreateRun(
            request,
            verification with { IsVerified = false },
            kernel,
            rawFrames,
            runtimePlacement,
            Guid.NewGuid()).IsUsable);
        Assert.IsFalse(GpuOptimizationEvidenceCollector.TryCreateRun(
            request,
            verification,
            kernel,
            rawFrames with { ProcessId = 78 },
            runtimePlacement,
            Guid.NewGuid()).IsUsable);
        Assert.IsFalse(GpuOptimizationEvidenceCollector.TryCreateRun(
            request,
            verification,
            kernel,
            rawFrames with
            {
                ActualWindowMilliseconds = 20_000,
                EndedAtUtc = startedAt.AddSeconds(20),
            },
            runtimePlacement,
            Guid.NewGuid()).IsUsable);
        Assert.IsFalse(GpuOptimizationEvidenceCollector.TryCreateRun(
            request with { SourceRevisionId = "dirty" },
            verification,
            kernel,
            rawFrames,
            runtimePlacement,
            Guid.NewGuid()).IsUsable);
        Assert.IsFalse(GpuOptimizationEvidenceCollector.TryCreateRun(
            request,
            verification,
            kernel,
            rawFrames,
            null,
            Guid.NewGuid()).IsUsable);
        Assert.IsFalse(GpuOptimizationEvidenceCollector.TryCreateRun(
            request,
            verification,
            kernel,
            rawFrames,
            runtimePlacement with
            {
                TargetProcessorIsrEventCount = 199,
                OffTargetIsrEventCount = 1,
            },
            Guid.NewGuid()).IsUsable);

        var quality = BaselineQualityAnalyzer.Analyze(Enumerable.Range(1, 5)
            .Select(number => new BaselineWindowEvidence(
                number,
                startedAt.AddSeconds((number - 1) * 21),
                20_000,
                20_000,
                true,
                null,
                1_000,
                100,
                1_000,
                10))
            .ToArray());
        var workloadStability = WorkloadStabilityAnalyzer.Analyze(Enumerable.Range(1, 5)
            .Select(number => new WorkloadWindowEvidence(
                number,
                20_000,
                1_000,
                1_000,
                null))
            .ToArray());
        var baseline = new GpuOptimizationBaselineEvidence(
            quality,
            Guid.NewGuid(),
            "scene-v1",
            "environment-v1",
            new string('a', 40),
            workloadStability);
        var policy = new ComparisonPolicy(
            MinimumSamples: 20,
            MinimumRelativeChange: 0.03,
            GuardrailRegressionLimit: 0.05,
            EvaluationPercentile: 0.99);
        var orchestrationRequest = new GpuOptimizationOrchestrationRequest(
            "PCI\\VEN_10DE&DEV_TEST",
            77,
            baseline,
            candidates,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30),
            policy);

        var backend = new FakeGpuOptimizationExecutionBackend(
            OrchestrationMeasurement(100, 10),
            OrchestrationMeasurement(100, 10));
        var orchestration = await new GpuOptimizationOrchestrator(backend).RunAsync(orchestrationRequest);

        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, orchestration.Recommendation);
        Assert.IsNotNull(orchestration.Screening);
        Assert.IsNull(orchestration.Confirmation);
        Assert.AreEqual(2, backend.ApplyCount);
        Assert.AreEqual(2, backend.RollbackCount);
        Assert.AreEqual(0, backend.KeepCount);
        Assert.AreEqual(0, backend.ActiveExperimentCount);

        var keepBackend = new FakeGpuOptimizationExecutionBackend(
            OrchestrationMeasurement(100, 10),
            OrchestrationMeasurement(85, 10));
        var keepResult = await new GpuOptimizationOrchestrator(keepBackend).RunAsync(orchestrationRequest);

        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, keepResult.Recommendation);
        Assert.IsNotNull(keepResult.Screening?.Finalist);
        Assert.IsNotNull(keepResult.Confirmation);
        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, keepResult.Confirmation.Recommendation);
        Assert.AreEqual(5, keepBackend.ApplyCount);
        Assert.AreEqual(4, keepBackend.RollbackCount);
        Assert.AreEqual(1, keepBackend.AwaitDecisionCount);
        Assert.AreEqual(1, keepBackend.KeepCount);
        Assert.AreEqual(0, keepBackend.ActiveExperimentCount);
        Assert.IsTrue(keepBackend.AppliedCandidates.Skip(candidates.Count)
            .All(candidate => candidate == keepResult.Screening.Finalist.Candidate));

        var setupFailureBackend = new FakeGpuOptimizationExecutionBackend(
            OrchestrationMeasurement(100, 10),
            OrchestrationMeasurement(100, 10),
            failBeginMeasurementCall: 1);
        try
        {
            _ = await new GpuOptimizationOrchestrator(setupFailureBackend).RunAsync(orchestrationRequest);
            Assert.Fail("A simulated post-apply measurement setup failure must escape the orchestrator.");
        }
        catch (InvalidOperationException exception)
        {
            StringAssert.Contains(exception.Message, "simulated measurement setup failure");
        }

        Assert.AreEqual(1, setupFailureBackend.ApplyCount);
        Assert.AreEqual(1, setupFailureBackend.RollbackCount);
        Assert.AreEqual(0, setupFailureBackend.ActiveExperimentCount);
    }

    private static GpuOptimizationMeasurementSet OrchestrationMeasurement(double primary, double frameTime) =>
        new(
            new MetricSeries(
                "DPC duration (us)",
                MetricDirection.LowerIsBetter,
                Enumerable.Repeat(primary, 1_000)),
            new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
            {
                ["Frame time"] = new(
                    "Frame time",
                    MetricDirection.LowerIsBetter,
                    Enumerable.Repeat(frameTime, 1_000)),
            });

    private sealed class FakeGpuOptimizationExecutionBackend : IGpuOptimizationExecutionBackend
    {
        private readonly GpuOptimizationMeasurementSet original;
        private readonly GpuOptimizationMeasurementSet candidate;
        private readonly HashSet<Guid> activeExperiments = [];
        private readonly int failBeginMeasurementCall;
        private int beginMeasurementCount;

        internal FakeGpuOptimizationExecutionBackend(
            GpuOptimizationMeasurementSet original,
            GpuOptimizationMeasurementSet candidate,
            int failBeginMeasurementCall = 0)
        {
            this.original = original;
            this.candidate = candidate;
            this.failBeginMeasurementCall = failBeginMeasurementCall;
        }

        internal int ApplyCount { get; private set; }

        internal int RollbackCount { get; private set; }

        internal int AwaitDecisionCount { get; private set; }

        internal int KeepCount { get; private set; }

        internal int ActiveExperimentCount => activeExperiments.Count;

        internal List<GpuAffinityCandidate> AppliedCandidates { get; } = [];

        public GpuGraphicsTargetIdentityResolution ResolveGraphicsTarget(
            string deviceInstanceId,
            string? presentMonApiPath,
            string? presentMonControlPipeName)
        {
            _ = presentMonApiPath;
            _ = presentMonControlPipeName;
            return new GpuGraphicsTargetIdentityResolution(
                true,
                new GpuGraphicsTargetIdentitySnapshot(
                    deviceInstanceId,
                    new GraphicsAdapterLuid(1, 2),
                    7,
                    "Test GPU",
                    1),
                null);
        }

        public GpuInterruptAffinitySnapshot CaptureOriginal(string deviceInstanceId) =>
            new(
                deviceInstanceId,
                "Test GPU",
                "1.0.0",
                false,
                RegistryValueSnapshot.Missing,
                RegistryValueSnapshot.Missing);

        public Guid ApplyCandidate(string deviceInstanceId, GpuAffinityCandidate candidate)
        {
            _ = deviceInstanceId;
            ArgumentNullException.ThrowIfNull(candidate);
            var id = Guid.NewGuid();
            activeExperiments.Add(id);
            AppliedCandidates.Add(candidate);
            ApplyCount++;
            return id;
        }

        public void BeginMeasurement(Guid experimentId)
        {
            Assert.IsTrue(activeExperiments.Contains(experimentId));
            beginMeasurementCount++;
            if (beginMeasurementCount == failBeginMeasurementCall)
            {
                throw new InvalidOperationException("simulated measurement setup failure");
            }
        }

        public Task<GpuOptimizationEvidenceCollectionResult> CaptureAsync(
            GpuOptimizationEvidenceRequest request,
            GpuInterruptAffinitySnapshot originalState,
            string? presentMonApiPath,
            string? presentMonControlPipeName,
            CancellationToken cancellationToken)
        {
            _ = originalState;
            _ = presentMonApiPath;
            _ = presentMonControlPipeName;
            cancellationToken.ThrowIfCancellationRequested();
            var measurement = request.Role == GpuConfirmationOrder.Original ? original : candidate;
            var run = new GpuOptimizationConfirmationRun(
                request.RunNumber,
                request.Role,
                request.SessionId,
                Guid.NewGuid(),
                request.WorkloadIdentity,
                request.EnvironmentIdentity,
                request.SourceRevisionId,
                request.Role == GpuConfirmationOrder.Candidate ? request.Finalist.Processor : null,
                true,
                true,
                checked((int)request.RequestedDuration.TotalMilliseconds),
                request.RequestedDuration.TotalMilliseconds,
                measurement);
            return Task.FromResult(new GpuOptimizationEvidenceCollectionResult(
                true,
                run,
                null,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch + request.RequestedDuration,
                request.Role == GpuConfirmationOrder.Candidate
                    ? new GpuInterruptRuntimePlacementEvidence(
                        "PCI\\VEN_10DE&DEV_TEST",
                        "nvlddmkm",
                        request.Finalist.Processor.Number,
                        200,
                        200,
                        0,
                        0,
                        [new ProcessorObservedInterruptCount(request.Finalist.Processor.Number, 200)])
                    : null));
        }

        public void AwaitDecision(Guid experimentId)
        {
            Assert.IsTrue(activeExperiments.Contains(experimentId));
            AwaitDecisionCount++;
        }

        public void KeepCandidate(Guid experimentId)
        {
            Assert.IsTrue(activeExperiments.Remove(experimentId));
            KeepCount++;
        }

        public void Rollback(Guid experimentId)
        {
            Assert.IsTrue(activeExperiments.Remove(experimentId));
            RollbackCount++;
        }
    }
}

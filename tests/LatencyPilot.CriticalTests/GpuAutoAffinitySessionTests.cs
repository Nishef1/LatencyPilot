using System.Text.Json;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.Results;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuAutoAffinitySessionTests
{
    [TestMethod]
    public async Task SessionRanksByLowsScreensOnceRetestsFinalistsAndRequiresFinalPlacement()
    {
        var (topology, pressure, request) = CreateTwoCoreRequest();
        var progressPlan = GpuAutoAffinityProgressPlan.Create(topology, pressure, cpuSets: null);
        Assert.AreEqual(2, progressPlan.PhysicalCandidateCount);
        Assert.AreEqual(0, progressPlan.MaximumRefinementCandidateCount);
        Assert.AreEqual(2, progressPlan.FinalistCandidateCount);
        Assert.AreEqual(12, progressPlan.InitialTotalUnits);
        Assert.AreEqual(0, progressPlan.GetRefinementCandidateCount(0));
        Assert.AreEqual(12, progressPlan.GetTotalUnitsForFinalist(1));

        var nonSmtTopology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0, [new LogicalProcessorId(0, 0)])],
            [new ProcessorCoreSnapshot(0, 0, [new LogicalProcessorId(0, 0)])],
            DateTimeOffset.UnixEpoch);
        var nonSmtPlan = GpuAutoAffinityProgressPlan.Create(
            nonSmtTopology,
            [new ProcessorPressureEvidence(new LogicalProcessorId(0, 0), 0d)],
            cpuSets: null);
        Assert.AreEqual(0, nonSmtPlan.MaximumRefinementCandidateCount);
        Assert.AreEqual(1, nonSmtPlan.FinalistCandidateCount);
        Assert.AreEqual(7, nonSmtPlan.InitialTotalUnits);

        var backend = new RecordingBackend();
        var observer = new RecordingObserver();
        var result = await new GpuAutoAffinitySession(backend, observer).RunAsync(request);

        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, result.Recommendation);
        Assert.IsNotNull(result.Finalist);
        Assert.AreEqual(new LogicalProcessorId(0, 2), result.Finalist.Processor,
            "CPU2 must win because its 1%/0.1% lows are stronger even though CPU0 has the lower frame-p99.");
        Assert.IsTrue(result.Report.FinalStateVerified);
        Assert.IsFalse(result.Report.OriginalStateRestored);
        Assert.IsTrue(backend.Events.Contains("keep:0:2"));

        var screeningReports = observer.Reports.Where(static report => report.Phase == "screening").ToArray();
        Assert.AreEqual(2, screeningReports.Length);
        Assert.IsTrue(screeningReports.All(static report => report.TrialCount == 1));
        var finalistReports = observer.Reports.Where(static report => report.Phase == "screening-finalists").ToArray();
        Assert.AreEqual(2, finalistReports.Length);
        Assert.IsTrue(finalistReports.All(static report => report.TrialCount == 2));

        Assert.IsTrue(result.Report.Trials.Any(static trial =>
            trial.Phase == "screening-warmup" && trial.Processor is null));
        Assert.IsTrue(result.Report.Trials.Any(static trial => trial.Phase == "screening-finalists-warmup"));
        Assert.IsTrue(result.Report.Trials.Any(static trial => trial.Phase == "final-verification"));
        Assert.IsFalse(result.Report.Trials.Any(static trial =>
            trial.Phase == "confirmation" || trial.Phase == "smt-refinement"));
        var finalVerification = result.Report.Trials.Single(static trial => trial.Phase == "final-verification");
        Assert.AreEqual(new LogicalProcessorId(0, 2), finalVerification.Processor);
        Assert.IsTrue(finalVerification.Placement is { ConfirmsRequestedPlacement: true });
        Assert.IsTrue(finalVerification.InterruptEvidence is { IsrSampleCount: > 0 });

        var serialized = JsonSerializer.Serialize(result.Report);
        var roundTrip = JsonSerializer.Deserialize<GpuAutoAffinityReport>(serialized)!;
        Assert.AreEqual(result.Report.Trials.Count, roundTrip.Trials.Count);
        Assert.AreEqual(result.Report.FinalProcessor, roundTrip.FinalProcessor);
    }

    [TestMethod]
    public async Task SessionRestoresOnUnverifiedFinalPlacementAndKeepsRollbackOwnershipOnFailureOrCancel()
    {
        var (_, _, request) = CreateTwoCoreRequest();

        var missingFinalEtw = new RecordingBackend(finalEtwUnavailable: true);
        var missingFinalResult = await new GpuAutoAffinitySession(missingFinalEtw).RunAsync(request);
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, missingFinalResult.Recommendation);
        Assert.AreEqual(new LogicalProcessorId(0, 2), missingFinalResult.Finalist?.Processor);
        Assert.IsTrue(missingFinalResult.Report.OriginalStateRestored);
        Assert.IsTrue(missingFinalResult.Report.FinalStateVerified);
        Assert.IsFalse(missingFinalEtw.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)));
        Assert.IsTrue(missingFinalEtw.Events.Contains("rollback:0:2"));
        Assert.IsTrue(missingFinalResult.Report.Reasons.Any(static reason =>
            reason.Contains("placement", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("ETW", StringComparison.OrdinalIgnoreCase)));

        var invalidKeep = new RecordingBackend(failKeep: true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new GpuAutoAffinitySession(invalidKeep).RunAsync(request));
        Assert.IsTrue(invalidKeep.Events.Contains("keep-failed:0:2"));
        Assert.IsTrue(invalidKeep.Events.Contains("rollback:0:2"));
        Assert.IsFalse(invalidKeep.Events.Contains("keep:0:2"));

        var unverifiedRecovery = new RecordingBackend(failKeep: true, failRecoveryVerification: true);
        var recoveryFailure = await Assert.ThrowsExactlyAsync<AggregateException>(() =>
            new GpuAutoAffinitySession(unverifiedRecovery).RunAsync(request));
        Assert.IsTrue(recoveryFailure.InnerExceptions.Any(static exception =>
            exception.Message.Contains("original state", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(unverifiedRecovery.Events.Contains("verify-original-failed"));

        var cancelling = new RecordingBackend(cancelDuringFirstScoredCandidate: true);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            new GpuAutoAffinitySession(cancelling).RunAsync(request));
        Assert.IsTrue(cancelling.Events.Any(static item => item.StartsWith("apply:", StringComparison.Ordinal)));
        Assert.IsTrue(cancelling.Events.Any(static item => item.StartsWith("rollback:", StringComparison.Ordinal)));
        Assert.IsFalse(cancelling.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SessionRestoresOriginalWhenFinalistLowFpsMeasurementsRemainUnstable()
    {
        var (_, _, request) = CreateTwoCoreRequest();
        var backend = new RecordingBackend(unstableFinalists: true);

        var result = await new GpuAutoAffinitySession(backend).RunAsync(request);

        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, result.Recommendation);
        Assert.IsNull(result.Finalist);
        Assert.IsTrue(result.Report.FinalStateVerified);
        Assert.IsTrue(result.Report.OriginalStateRestored);
        Assert.IsFalse(backend.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)));
        Assert.IsFalse(result.Report.Trials.Any(static trial =>
            trial.Phase == "confirmation" || trial.Phase == "smt-refinement" || trial.Phase == "final-verification"));
        Assert.IsTrue(result.Report.Candidates
            .Where(static item => item.Phase == "screening-finalists")
            .All(static item => item.Verdict == "Inconclusive"));
        Assert.IsTrue(result.Report.Candidates.Any(static item =>
            item.Reason.Contains("1% low", StringComparison.OrdinalIgnoreCase) ||
            item.Reason.Contains("drift", StringComparison.OrdinalIgnoreCase)));
    }

    private static (ProcessorTopologySnapshot Topology, ProcessorPressureEvidence[] Pressure, GpuAutoAffinitySessionRequest Request)
        CreateTwoCoreRequest()
    {
        var topology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0, [new LogicalProcessorId(0, 0), new LogicalProcessorId(0, 2)])],
            [
                new ProcessorCoreSnapshot(0, 0, [new LogicalProcessorId(0, 0)]),
                new ProcessorCoreSnapshot(1, 0, [new LogicalProcessorId(0, 2)]),
            ],
            DateTimeOffset.UnixEpoch);
        var pressure = new[]
        {
            new ProcessorPressureEvidence(new LogicalProcessorId(0, 0), 0.1),
            new ProcessorPressureEvidence(new LogicalProcessorId(0, 2), 0.2),
        };
        var request = new GpuAutoAffinitySessionRequest(
            Guid.NewGuid(),
            topology,
            pressure,
            null,
            0x51A7,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30),
            new ComparisonPolicy(20, 0.03, 0.05, 0.99));
        return (topology, pressure, request);
    }

    private sealed class RecordingObserver : IGpuAutoAffinitySessionObserver
    {
        internal List<GpuAutoAffinityCandidateReport> Reports { get; } = [];

        public Task CandidateEvaluatedAsync(GpuAutoAffinityCandidateReport report)
        {
            Reports.Add(report);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBackend(
        bool finalEtwUnavailable = false,
        bool failKeep = false,
        bool failRecoveryVerification = false,
        bool cancelDuringFirstScoredCandidate = false,
        bool unstableFinalists = false) : IGpuAutoAffinitySessionBackend
    {
        private readonly Dictionary<Guid, GpuAffinityCandidate> active = [];
        private int captureSequence;
        private int finalistSequence;
        private bool cancelled;
        private bool keepFailed;

        internal List<string> Events { get; } = [];

        public Task<GpuAutoAffinityTrialObservation> CaptureOriginalAsync(
            GpuAutoAffinityTrialRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"original:{request.Phase}:{request.RunNumber}");
            return Task.FromResult(CreateObservation(request, null, Enumerable.Repeat(8d, 100).ToArray(), etwHealthy: true));
        }

        public Task<Guid> ApplyCandidateAsync(GpuAffinityCandidate candidate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Guid.NewGuid();
            active.Add(id, candidate);
            Events.Add($"apply:{candidate.Processor}");
            return Task.FromResult(id);
        }

        public Task<GpuAutoAffinityTrialObservation> CaptureCandidateAsync(
            Guid experimentId,
            GpuAutoAffinityTrialRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = active[experimentId];
            Events.Add($"candidate:{request.Phase}:{request.RunNumber}:{candidate.Processor}");

            if (cancelDuringFirstScoredCandidate &&
                !cancelled &&
                string.Equals(request.Phase, "screening", StringComparison.Ordinal))
            {
                cancelled = true;
                throw new OperationCanceledException("synthetic safe-stop request");
            }

            var periods = candidate.Processor.Number == 0
                ? Enumerable.Repeat(5d, 99).Append(20d).ToArray()
                : Enumerable.Repeat(6d, 99).Append(10d).ToArray();

            if (unstableFinalists && string.Equals(request.Phase, "screening-finalists", StringComparison.Ordinal))
            {
                var sequence = Interlocked.Increment(ref finalistSequence);
                var tail = sequence % 2 == 0 ? 30d : 6d;
                periods = Enumerable.Repeat(6d, 99).Append(tail).ToArray();
            }

            var etwHealthy = !(finalEtwUnavailable && string.Equals(request.Phase, "final-verification", StringComparison.Ordinal));
            return Task.FromResult(CreateObservation(request, candidate, periods, etwHealthy));
        }

        public Task RollbackAsync(Guid experimentId, CancellationToken cancellationToken)
        {
            var candidate = active[experimentId];
            Events.Add($"rollback:{candidate.Processor}");
            active.Remove(experimentId);
            return Task.CompletedTask;
        }

        public Task KeepAsync(Guid experimentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = active[experimentId];
            if (failKeep)
            {
                keepFailed = true;
                Events.Add($"keep-failed:{candidate.Processor}");
                throw new InvalidOperationException("synthetic Keep failure");
            }

            Events.Add($"keep:{candidate.Processor}");
            active.Remove(experimentId);
            return Task.CompletedTask;
        }

        public Task<bool> VerifyOriginalStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failRecoveryVerification && keepFailed)
            {
                Events.Add("verify-original-failed");
                return Task.FromResult(false);
            }

            Events.Add("verify-original");
            return Task.FromResult(active.Count == 0);
        }

        public Task<bool> VerifyCandidateStateAsync(
            Guid experimentId,
            GpuAffinityCandidate candidate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var verified = active.TryGetValue(experimentId, out var current) && current == candidate;
            Events.Add($"verify-candidate:{candidate.Processor}:{verified}");
            return Task.FromResult(verified);
        }

        private GpuAutoAffinityTrialObservation CreateObservation(
            GpuAutoAffinityTrialRequest request,
            GpuAffinityCandidate? candidate,
            double[] framePeriods,
            bool etwHealthy)
        {
            var processId = 77u;
            var started = DateTimeOffset.UnixEpoch.AddSeconds(Interlocked.Increment(ref captureSequence) * 40);
            var frameCount = framePeriods.Length;
            var capture = new PresentMonFrameCaptureSnapshot(
                PresentMonWorkloadCaptureStatus.Available,
                processId,
                request.Duration.TotalMilliseconds,
                request.Duration.TotalMilliseconds,
                new PresentMonApiVersionSnapshot(3, 4, 0),
                framePeriods.Select(period => new PresentMonFrameMetricsSnapshot(
                    1,
                    period,
                    period * 0.7,
                    period * 0.3,
                    null,
                    5,
                    null,
                    false,
                    null,
                    null)).ToArray(),
                [],
                "PresentMonAPI2.dll",
                null,
                null,
                started,
                started + request.Duration);
            var evidence = new GpuBenchmarkEvidence(
                GpuBenchmarkEvidence.SchemaId,
                new string('a', 40),
                GpuBenchmarkEvidence.MethodIdValue,
                "windows-test",
                "gpu-test",
                "driver-test",
                "topology-test",
                processId,
                candidate is null ? "Original" : "Candidate",
                request.RunNumber,
                candidate?.Processor,
                "frozen-workload-test",
                [new LogicalProcessorId(0, 0), new LogicalProcessorId(0, 2)],
                0x51A7,
                1_000_000,
                Enumerable.Repeat(4d, frameCount).ToArray(),
                capture,
                "2.5.1",
                Guid.NewGuid(),
                etwHealthy,
                etwHealthy ? 0 : 1,
                [],
                FramePeriodMilliseconds: framePeriods);

            if (!etwHealthy || candidate is null)
            {
                return new GpuAutoAffinityTrialObservation(
                    evidence,
                    GpuBenchmarkContaminationContext.Clean,
                    true,
                    true,
                    Placement: null,
                    GpuDriverDpcDurationMicroseconds: [],
                    GpuDriverIsrDurationMicroseconds: [],
                    InterruptEvidence: null);
            }

            var events = Enumerable.Range(0, frameCount)
                .Select(index => new KernelLatencyEvent(
                    KernelLatencyEventKind.Isr,
                    candidate.Processor.Number,
                    index,
                    5d,
                    0x1000,
                    null,
                    null,
                    @"C:\Windows\System32\drivers\dxgkrnl.sys"))
                .ToArray();
            var target = new PnPDeviceSnapshot(
                "PCI\\TEST",
                new Guid("4D36E968-E325-11CE-BFC1-08002BE10318"),
                "Test GPU",
                "NVIDIA",
                "PCI",
                "nvlddmkm",
                new DriverMetadataSnapshot("1", "NVIDIA", "display.inf"),
                InterruptConfigurationSnapshot.Available(1, null, null, null),
                InterruptResourceSnapshot.Available([]));
            var attribution = GpuInterruptRuntimePlacementVerifier.ResolveIsrAttribution(
                new KernelLatencyCaptureResult(started, request.Duration, request.Duration, events, 0, 0, 0, false),
                target.InstanceId,
                [target]);
            return new GpuAutoAffinityTrialObservation(
                evidence,
                GpuBenchmarkContaminationContext.Clean,
                true,
                true,
                new GpuAutoAffinityPlacementProof(candidate.Processor, attribution.Events.Count, 0),
                Enumerable.Repeat(20d, frameCount).ToArray(),
                attribution.Events.Select(static item => item.DurationMicroseconds).ToArray(),
                new GpuAutoAffinityInterruptEvidence(
                    attribution.ModuleName,
                    attribution.Mode,
                    frameCount,
                    attribution.Events.Count,
                    attribution.UnresolvedIsrEventCount));
        }
    }
}

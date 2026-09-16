using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuAutoAffinitySessionTests
{
    private static readonly string[] ExpectedConfirmationRoles =
    [
        "Original",
        "Candidate",
        "Candidate",
        "Original",
        "Candidate",
        "Original",
        "Original",
        "Candidate",
    ];

    [TestMethod]
    public async Task SessionOwnsCandidateRollbackRefinementConfirmationAndCancellation()
    {
        var topology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0,
                [new LogicalProcessorId(0, 0), new LogicalProcessorId(0, 1), new LogicalProcessorId(0, 2), new LogicalProcessorId(0, 3)])],
            [
                new ProcessorCoreSnapshot(0, 0, [new LogicalProcessorId(0, 0), new LogicalProcessorId(0, 1)]),
                new ProcessorCoreSnapshot(1, 0, [new LogicalProcessorId(0, 2), new LogicalProcessorId(0, 3)]),
            ],
            DateTimeOffset.UnixEpoch);
        var pressure = new[]
        {
            new ProcessorPressureEvidence(new LogicalProcessorId(0, 0), 0.4),
            new ProcessorPressureEvidence(new LogicalProcessorId(0, 1), 0.5),
            new ProcessorPressureEvidence(new LogicalProcessorId(0, 2), 0.1),
            new ProcessorPressureEvidence(new LogicalProcessorId(0, 3), 0.2),
        };
        var progressPlan = GpuAutoAffinityProgressPlan.Create(topology, pressure, cpuSets: null);
        Assert.AreEqual(2, progressPlan.PhysicalCandidateCount);
        Assert.AreEqual(2, progressPlan.MaximumRefinementCandidateCount);
        Assert.AreEqual(18, progressPlan.InitialTotalUnits);
        Assert.AreEqual(2, progressPlan.GetRefinementCandidateCount(0));
        Assert.AreEqual(18, progressPlan.GetTotalUnitsForFinalist(1));

        var nonSmtTopology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0, [new LogicalProcessorId(0, 0)])],
            [new ProcessorCoreSnapshot(0, 0, [new LogicalProcessorId(0, 0)])],
            DateTimeOffset.UnixEpoch);
        var nonSmtProgressPlan = GpuAutoAffinityProgressPlan.Create(
            nonSmtTopology,
            [new ProcessorPressureEvidence(new LogicalProcessorId(0, 0), 0d)],
            cpuSets: null);
        Assert.AreEqual(1, nonSmtProgressPlan.MaximumRefinementCandidateCount);
        Assert.AreEqual(14, nonSmtProgressPlan.InitialTotalUnits);
        Assert.AreEqual(14, nonSmtProgressPlan.GetTotalUnitsForFinalist(0));

        var boundedCores = Enumerable.Range(0, 8)
            .Select(index => new ProcessorCoreSnapshot(
                index,
                0,
                [
                    new LogicalProcessorId(0, checked((byte)(index * 2))),
                    new LogicalProcessorId(0, checked((byte)(index * 2 + 1))),
                ]))
            .ToArray();
        var boundedLogical = boundedCores.SelectMany(static core => core.LogicalProcessors).ToArray();
        var boundedTopology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0, boundedLogical)],
            boundedCores,
            DateTimeOffset.UnixEpoch);
        var boundedPressure = boundedLogical
            .Select(processor => new ProcessorPressureEvidence(processor, processor.Number / 100d))
            .ToArray();
        var boundedCandidates = GpuAffinityCandidatePlanner.Create(boundedTopology, boundedPressure);
        Assert.AreEqual(8, boundedCandidates.Count);
        CollectionAssert.AreEquivalent(
            Enumerable.Range(0, 8).ToArray(),
            boundedCandidates.Select(static candidate => candidate.PhysicalCoreIndex).ToArray());
        Assert.IsTrue(boundedCandidates.Any(static candidate =>
            candidate.Processor == new LogicalProcessorId(0, 0)));
        var boundedWinner = boundedCandidates.Single(static candidate => candidate.PhysicalCoreIndex == 3);
        CollectionAssert.AreEquivalent(
            new[] { new LogicalProcessorId(0, 6), new LogicalProcessorId(0, 7) },
            GpuAffinityCandidatePlanner.CreateSiblingRefinement(
                    boundedTopology,
                    boundedPressure,
                    boundedWinner)
                .Select(static candidate => candidate.Processor)
                .ToArray());

        var request = new GpuAutoAffinitySessionRequest(
            Guid.NewGuid(),
            topology,
            pressure,
            null,
            0x51A7,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30),
            new ComparisonPolicy(20, 0.03, 0.05, 0.99));
        var backend = new RecordingBackend();
        var observer = new RecordingObserver();
        var session = new GpuAutoAffinitySession(backend, observer);

        var result = await session.RunAsync(request);

        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, result.Recommendation);
        Assert.IsNotNull(result.Finalist);
        Assert.AreEqual(new LogicalProcessorId(0, 3), result.Finalist.Processor);
        Assert.AreEqual(GpuAutoAffinityReport.SchemaId, result.Report.Schema);
        Assert.AreEqual(request.ShuffleSeed, result.Report.ShuffleSeed);
        Assert.IsTrue(result.Report.Trials.Count >= 2 + 4 + 4 + 8);
        Assert.IsTrue(result.Report.FinalStateVerified);
        Assert.IsFalse(result.Report.OriginalStateRestored);
        Assert.IsTrue(backend.Events.Contains("keep:0:3"));
        Assert.IsTrue(backend.Events.Any(static item => item.StartsWith("rollback:", StringComparison.Ordinal)));
        Assert.IsTrue(observer.Reports.Any(static report => report.Phase == "screening"));
        Assert.IsTrue(observer.Reports.Any(static report => report.Phase == "smt-refinement"));
        Assert.IsTrue(observer.Reports.Any(static report =>
            report.Phase == "confirmation" && report.Verdict == "Improved"));

        var confirmationRoles = result.Report.Trials
            .Where(static trial => trial.Phase == "confirmation")
            .Select(static trial => trial.Role)
            .ToArray();
        CollectionAssert.AreEqual(ExpectedConfirmationRoles, confirmationRoles);

        var cancellingBackend = new RecordingBackend(cancelAfterFirstCandidateCapture: true);
        var cancellingSession = new GpuAutoAffinitySession(cancellingBackend);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => cancellingSession.RunAsync(request));
        Assert.IsTrue(cancellingBackend.Events.Any(static item => item.StartsWith("apply:", StringComparison.Ordinal)));
        Assert.IsTrue(cancellingBackend.Events.Any(static item => item.StartsWith("rollback:", StringComparison.Ordinal)));
        Assert.IsFalse(cancellingBackend.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)));

        using var finalStop = new CancellationTokenSource();
        var finalStopBackend = new RecordingBackend();
        var finalStopObserver = new RecordingObserver(report =>
        {
            if (report.Phase == "confirmation")
            {
                finalStop.Cancel();
            }
        });
        var finalStopSession = new GpuAutoAffinitySession(finalStopBackend, finalStopObserver);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            finalStopSession.RunAsync(request, finalStop.Token));
        Assert.IsTrue(finalStopObserver.Reports.Any(static report => report.Phase == "confirmation"));
        Assert.IsTrue(finalStopBackend.Events.Any(static item => item.StartsWith("rollback:", StringComparison.Ordinal)));
        Assert.IsFalse(finalStopBackend.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)));
    }

    private sealed class RecordingObserver(Action<GpuAutoAffinityCandidateReport>? onReport = null)
        : IGpuAutoAffinitySessionObserver
    {
        internal List<GpuAutoAffinityCandidateReport> Reports { get; } = [];

        public Task CandidateEvaluatedAsync(GpuAutoAffinityCandidateReport report)
        {
            Reports.Add(report);
            onReport?.Invoke(report);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingBackend(bool cancelAfterFirstCandidateCapture = false) : IGpuAutoAffinitySessionBackend
    {
        private int captureSequence;
        private bool cancelled;
        private readonly Dictionary<Guid, GpuAffinityCandidate> active = [];

        internal List<string> Events { get; } = [];

        public Task<GpuAutoAffinityTrialObservation> CaptureOriginalAsync(
            GpuAutoAffinityTrialRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"control:{request.Phase}:{request.RunNumber}");
            return Task.FromResult(CreateObservation(request, null, 10d));
        }

        public Task<Guid> ApplyCandidateAsync(
            GpuAffinityCandidate candidate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var experimentId = Guid.NewGuid();
            active.Add(experimentId, candidate);
            Events.Add($"apply:{candidate.Processor}");
            return Task.FromResult(experimentId);
        }

        public Task<GpuAutoAffinityTrialObservation> CaptureCandidateAsync(
            Guid experimentId,
            GpuAutoAffinityTrialRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = active[experimentId];
            Events.Add($"candidate:{request.Phase}:{request.RunNumber}:{candidate.Processor}");
            if (cancelAfterFirstCandidateCapture && !cancelled)
            {
                cancelled = true;
                throw new OperationCanceledException("synthetic safe-stop request");
            }

            var frameTime = candidate.Processor.Number switch
            {
                2 => 8.5d,
                3 => 8d,
                _ => 10.2d,
            };
            return Task.FromResult(CreateObservation(request, candidate, frameTime));
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
            Events.Add($"keep:{candidate.Processor}");
            active.Remove(experimentId);
            return Task.CompletedTask;
        }

        public Task<bool> VerifyOriginalStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add("verify-original");
            return Task.FromResult(active.Count == 0);
        }

        public Task<bool> VerifyCandidateStateAsync(
            Guid experimentId,
            GpuAffinityCandidate candidate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"verify-candidate:{candidate.Processor}");
            return Task.FromResult(active.TryGetValue(experimentId, out var current) && current == candidate);
        }

        private GpuAutoAffinityTrialObservation CreateObservation(
            GpuAutoAffinityTrialRequest request,
            GpuAffinityCandidate? candidate,
            double frameTime)
        {
            var processId = 77u;
            var started = DateTimeOffset.UnixEpoch.AddSeconds(Interlocked.Increment(ref captureSequence) * 40);
            var frameCount = 100;
            var capture = new PresentMonFrameCaptureSnapshot(
                PresentMonWorkloadCaptureStatus.Available,
                processId,
                request.Duration.TotalMilliseconds,
                request.Duration.TotalMilliseconds,
                new PresentMonApiVersionSnapshot(3, 4, 0),
                Enumerable.Range(0, frameCount).Select(_ => new PresentMonFrameMetricsSnapshot(
                    1,
                    frameTime,
                    frameTime * 0.7,
                    frameTime * 0.3,
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
                true,
                0,
                []);
            return new GpuAutoAffinityTrialObservation(
                evidence,
                GpuBenchmarkContaminationContext.Clean,
                StoredStateVerifiedBefore: true,
                StoredStateVerifiedAfter: true,
                candidate is null
                    ? null
                    : new GpuAutoAffinityPlacementProof(candidate.Processor, 20, 0),
                Enumerable.Repeat(20d, frameCount).ToArray(),
                Enumerable.Repeat(5d, frameCount).ToArray());
        }
    }
}

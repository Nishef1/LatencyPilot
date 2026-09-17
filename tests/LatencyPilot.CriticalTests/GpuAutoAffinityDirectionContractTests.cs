using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuAutoAffinityDirectionContractTests
{
    [TestMethod]
    public async Task AutoAffinityUsesLowFpsRankingOnePassScreeningAndNoBalancedConfirmation()
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
        var backend = new DirectionBackend();

        var result = await new GpuAutoAffinitySession(backend).RunAsync(request);

        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, result.Recommendation);
        Assert.IsNotNull(result.Finalist);
        Assert.AreEqual(new LogicalProcessorId(0, 2), result.Finalist.Processor,
            "CPU2 has the stronger 1%/0.1% lows even though CPU0 has the lower p99.");
        Assert.IsTrue(result.Report.Candidates
            .Where(static item => item.Phase == "screening")
            .All(static item => item.TrialCount == 1),
            "Initial all-core screening must score each core once.");
        Assert.IsTrue(result.Report.Candidates
            .Where(static item => item.Phase == "screening-finalists")
            .All(static item => item.TrialCount == 2),
            "Top finalists get exactly two additional scored runs.");
        Assert.IsFalse(result.Report.Trials.Any(static item => item.Phase == "confirmation"),
            "Balanced ABBA/BAAB confirmation is outside the simplified v1 flow.");
        Assert.IsTrue(result.Report.Trials.Any(static item => item.Phase == "final-verification"),
            "The kept winner still requires a final placement-verification capture.");
        Assert.IsTrue(backend.Events.Contains("keep:0:2"));
    }

    private sealed class DirectionBackend : IGpuAutoAffinitySessionBackend
    {
        private readonly Dictionary<Guid, GpuAffinityCandidate> active = [];
        internal List<string> Events { get; } = [];

        public Task<GpuAutoAffinityTrialObservation> CaptureOriginalAsync(
            GpuAutoAffinityTrialRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(CreateObservation(request, null, Enumerable.Repeat(8d, 100).ToArray()));

        public Task<Guid> ApplyCandidateAsync(GpuAffinityCandidate candidate, CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid();
            active[id] = candidate;
            Events.Add($"apply:{candidate.Processor}");
            return Task.FromResult(id);
        }

        public Task<GpuAutoAffinityTrialObservation> CaptureCandidateAsync(
            Guid experimentId,
            GpuAutoAffinityTrialRequest request,
            CancellationToken cancellationToken)
        {
            var candidate = active[experimentId];
            var periods = candidate.Processor.Number == 0
                ? Enumerable.Repeat(5d, 99).Append(20d).ToArray()
                : Enumerable.Repeat(6d, 99).Append(10d).ToArray();
            return Task.FromResult(CreateObservation(request, candidate, periods));
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
            var candidate = active[experimentId];
            Events.Add($"keep:{candidate.Processor}");
            active.Remove(experimentId);
            return Task.CompletedTask;
        }

        public Task<bool> VerifyOriginalStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(active.Count == 0);

        public Task<bool> VerifyCandidateStateAsync(
            Guid experimentId,
            GpuAffinityCandidate candidate,
            CancellationToken cancellationToken) =>
            Task.FromResult(active.TryGetValue(experimentId, out var current) && current == candidate);

        private static GpuAutoAffinityTrialObservation CreateObservation(
            GpuAutoAffinityTrialRequest request,
            GpuAffinityCandidate? candidate,
            double[] framePeriods)
        {
            var processId = 77u;
            var started = DateTimeOffset.UnixEpoch.AddSeconds(request.RunNumber * 40);
            var presentMon = new PresentMonFrameCaptureSnapshot(
                PresentMonWorkloadCaptureStatus.Available,
                processId,
                request.Duration.TotalMilliseconds,
                request.Duration.TotalMilliseconds,
                new PresentMonApiVersionSnapshot(3, 4, 0),
                framePeriods.Select(period => new PresentMonFrameMetricsSnapshot(
                    1, period, period * 0.7, period * 0.3, null, 5, null, false, null, null)).ToArray(),
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
                Enumerable.Repeat(4d, framePeriods.Length).ToArray(),
                presentMon,
                "2.5.1",
                Guid.NewGuid(),
                true,
                0,
                [],
                FramePeriodMilliseconds: framePeriods);
            var interrupt = new GpuAutoAffinityInterruptEvidence("dxgkrnl", "test", framePeriods.Length, framePeriods.Length, 0);
            return new GpuAutoAffinityTrialObservation(
                evidence,
                GpuBenchmarkContaminationContext.Clean,
                true,
                true,
                candidate is null ? null : new GpuAutoAffinityPlacementProof(candidate.Processor, framePeriods.Length, 0),
                Enumerable.Repeat(20d, framePeriods.Length).ToArray(),
                Enumerable.Repeat(5d, framePeriods.Length).ToArray(),
                interrupt);
        }
    }
}

#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuTemporalStabilityContractTests
{
    [AuditCase]
    public async Task TemporalDriftStopsScreeningAtFirstLocalControlBoundary()
    {
        var processors = Enumerable.Range(0, 6)
            .Select(static index => new LogicalProcessorId(0, checked((byte)(index * 2))))
            .ToArray();
        var topology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0, processors)],
            processors.Select((processor, index) =>
                new ProcessorCoreSnapshot(index, 0, [processor])).ToArray(),
            DateTimeOffset.UnixEpoch);
        var pressure = processors
            .Select((processor, index) => new ProcessorPressureEvidence(processor, 0.1 + (index * 0.01)))
            .ToArray();
        var request = new GpuAutoAffinitySessionRequest(
            Guid.NewGuid(),
            topology,
            pressure,
            null,
            0x51A7,
            TimeSpan.FromSeconds(15));
        var progressPlan = GpuAutoAffinityProgressPlan.Create(topology, pressure, cpuSets: null);
        Assert.AreEqual(6, progressPlan.CandidateCount);
        Assert.AreEqual(5, progressPlan.FinalistCandidateCount,
            "The progress budget must reserve the same five-candidate finalist ceiling used by the optimizer.");
        Assert.AreEqual(1, progressPlan.IntermediateScreeningControlCount,
            "Six screening candidates require one time-local Original control after the first four candidates.");
        var backend = new TemporalDriftBackend(driftAfterScreeningCandidates: 4);

        var result = await new GpuAutoAffinitySession(backend).RunAsync(request);

        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, result.Recommendation);
        Assert.IsTrue(result.Report.FinalStateVerified);
        Assert.IsTrue(result.Report.OriginalStateRestored);
        Assert.IsNull(result.Report.FinalProcessor);
        Assert.IsNull(result.Finalist);
        Assert.AreEqual(
            4,
            backend.ScreeningCandidateCount,
            "A drifting environment must be stopped at the first bounded screening-control boundary instead of burning the remaining candidates.");
        Assert.IsTrue(
            result.Report.Trials.Any(static trial =>
                string.Equals(trial.Phase, "screening-block-control", StringComparison.Ordinal)),
            "The report must preserve the time-local Original control that invalidated the screening block.");
        Assert.IsFalse(
            backend.Events.Any(static item => item.Contains("screening-finalists", StringComparison.Ordinal)),
            "Finalist confirmation must never start after a local Original control invalidates screening.");
        Assert.IsFalse(
            backend.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)),
            "A temporally invalid screening sweep must never Keep a candidate.");
        Assert.IsTrue(
            result.Report.Reasons.Any(static reason =>
                reason.Contains("drift", StringComparison.OrdinalIgnoreCase) &&
                reason.Contains("block", StringComparison.OrdinalIgnoreCase)),
            "The report must explain that a local screening block was invalidated by Original drift.");
    }

    [AuditCase]
    public void InvalidatedScreeningMustNotBePresentedAsAValidRankedWinner()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GpuOptimizationProgressWindow.xaml.cs"));

        StringAssert.Contains(
            source,
            "Measurements invalidated by drift — no valid winner",
            "The final UI needs an explicit invalidated-result state instead of presenting the fastest early sample as a valid winner.");
        Assert.IsTrue(
            source.Contains("IsScreeningInvalidated", StringComparison.Ordinal),
            "Ranked-result rendering must branch on report validity before selecting a top measured candidate.");
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Unable to locate repository file: " + Path.Combine(relativeParts));
    }

    private sealed class TemporalDriftBackend(int driftAfterScreeningCandidates) : IGpuAutoAffinitySessionBackend
    {
        private readonly Dictionary<Guid, GpuAffinityCandidate> active = [];
        private int captureSequence;

        internal List<string> Events { get; } = [];

        internal int ScreeningCandidateCount { get; private set; }

        public Task<GpuAutoAffinityTrialObservation> CaptureOriginalAsync(
            GpuAutoAffinityTrialRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"original:{request.Phase}:{request.RunNumber}");
            var isControl = request.Phase.Contains("control", StringComparison.Ordinal);
            var drifted = isControl && ScreeningCandidateCount >= driftAfterScreeningCandidates;
            var periods = Enumerable.Repeat(drifted ? 20d : 12d, 100).ToArray();
            return Task.FromResult(CreateObservation(request, periods));
        }

        public Task<Guid> ApplyCandidateAsync(
            GpuAffinityCandidate candidate,
            CancellationToken cancellationToken)
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
            if (string.Equals(request.Phase, "screening", StringComparison.Ordinal))
            {
                ScreeningCandidateCount++;
            }

            var periods = Enumerable.Repeat(10d, 100).ToArray();
            return Task.FromResult(CreateObservation(request, periods));
        }

        public Task RollbackAsync(Guid experimentId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            var verified = active.TryGetValue(experimentId, out var current) && current == candidate;
            Events.Add($"verify-candidate:{candidate.Processor}:{verified}");
            return Task.FromResult(verified);
        }

        private GpuAutoAffinityTrialObservation CreateObservation(
            GpuAutoAffinityTrialRequest request,
            double[] framePeriods)
        {
            const uint processId = 77;
            var started = DateTimeOffset.UnixEpoch.AddSeconds(Interlocked.Increment(ref captureSequence) * 40);
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
                request.Candidate is null ? "Original" : "Candidate",
                request.RunNumber,
                request.Candidate?.Processor,
                "frozen-workload-test",
                [new LogicalProcessorId(0, 0), new LogicalProcessorId(0, 2)],
                0x51A7,
                1_000_000,
                Enumerable.Repeat(4d, framePeriods.Length).ToArray(),
                capture,
                "2.5.1",
                Guid.NewGuid(),
                true,
                0,
                [],
                FramePeriodMilliseconds: framePeriods);

            return new GpuAutoAffinityTrialObservation(
                evidence,
                GpuBenchmarkContaminationContext.Clean,
                true,
                true,
                Placement: null,
                GpuDriverDpcDurationMicroseconds: Enumerable.Repeat(20d, framePeriods.Length).ToArray(),
                GpuDriverIsrDurationMicroseconds: Enumerable.Repeat(5d, framePeriods.Length).ToArray(),
                InterruptEvidence: null);
        }
    }
}

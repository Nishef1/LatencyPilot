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
    public async Task TimeLocalControlsAbsorbGradualBackgroundDriftWithoutManufacturingWinner()
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
        var backend = new GradualBackgroundDriftBackend();

        var result = await new GpuAutoAffinitySession(backend).RunAsync(request);

        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, result.Recommendation,
            "The synthetic backend deliberately omits final ETW placement proof, so the session must restore even after selecting a decision-grade finalist.");
        Assert.IsTrue(result.Report.FinalStateVerified);
        Assert.IsTrue(result.Report.OriginalStateRestored);
        Assert.IsNull(result.Report.FinalProcessor);
        Assert.IsNull(result.Finalist);
        Assert.AreEqual(
            6,
            backend.ScreeningCandidateCount,
            "Ordinary gradual background drift must not abort the CPU sweep at the first local-control boundary.");
        Assert.IsTrue(
            result.Report.Trials.Any(static trial =>
                string.Equals(trial.Phase, "screening-block-control", StringComparison.Ordinal)),
            "The report must preserve the time-local Original control used to normalize the first screening block.");
        Assert.IsTrue(
            result.Report.Reasons.Any(static reason =>
                reason.Contains("background variability", StringComparison.OrdinalIgnoreCase) &&
                reason.Contains("time-local", StringComparison.OrdinalIgnoreCase)),
            "Control drift should be represented as time-local uncertainty instead of invalidating otherwise valid measurements.");
        Assert.IsFalse(
            result.Report.Reasons.Any(static reason =>
                reason.Contains("invalidated by Original drift", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("discarded because the Original control drifted", StringComparison.OrdinalIgnoreCase)),
            "Normal Windows drift must not be described as a structural experiment failure.");
        Assert.IsTrue(
            result.Report.Reasons.Any(static reason =>
                reason.Contains("CPU 0 is the highest-ranked finalist", StringComparison.Ordinal)),
            "All synthetic candidates have the same true relative improvement. Local normalization must remove run-order drift so the deterministic passive fallback chooses the lowest-pressure CPU 0 rather than an earlier raw sample.");
    }

    [AuditCase]
    public void TimeLocalDecisionEvidenceMustBePresentedSeparatelyFromRawTrialDiagnostics()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GpuOptimizationProgressWindow.xaml.cs"));
        var xaml = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GpuOptimizationProgressWindow.xaml"));

        Assert.IsTrue(
            source.Contains("DecisionOnePercentLowFps", StringComparison.Ordinal) &&
            source.Contains("LocalControlUncertainty", StringComparison.Ordinal),
            "The ranking UI must consume time-local decision metrics and expose their uncertainty instead of sorting only raw trial medians.");
        StringAssert.Contains(
            xaml,
            "Candidate decision evidence",
            "The development UI must label normalized decision evidence accurately rather than claiming that raw 1%-low bars alone define ranking.");
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

    private sealed class GradualBackgroundDriftBackend : IGpuAutoAffinitySessionBackend
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

            // The synthetic machine begins at a 12 ms Original frame period and
            // gradually moves to 13.2 ms (10% slower) while the first four CPUs are
            // screened. It then remains at that ordinary-background regime. This is
            // intentionally larger than the old ±3% hard-abort band but still below
            // the existing 15% exhaustive-confirmation budget.
            var originalPeriod = request.Phase switch
            {
                "screening-warmup" or "screening-original" => 12d,
                _ => 13.2d,
            };
            var periods = Enumerable.Repeat(originalPeriod, 100).ToArray();
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

            double candidatePeriod;
            if (string.Equals(request.Phase, "screening", StringComparison.Ordinal))
            {
                ScreeningCandidateCount++;
                // The candidate is always 25% faster in frame-period terms than
                // the time-local Original. During the first block the Original
                // drifts linearly 12.0 -> 13.2 ms, so the raw candidate period also
                // drifts 9.18 -> 9.72 ms. The last two candidates run at 9.90 ms.
                // A raw ranking therefore favors early samples even though the true
                // treatment effect is identical for every CPU.
                candidatePeriod = ScreeningCandidateCount switch
                {
                    1 => 9.18d,
                    2 => 9.36d,
                    3 => 9.54d,
                    4 => 9.72d,
                    _ => 9.90d,
                };
            }
            else
            {
                candidatePeriod = 9.90d;
            }

            var periods = Enumerable.Repeat(candidatePeriod, 100).ToArray();
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

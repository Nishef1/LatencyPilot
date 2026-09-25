#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using System.Text.Json;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Results;
using LatencyPilot.Core.System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuAutoAffinitySessionTests
{
    [AuditCase]
    public async Task SessionUsesPairedEvidenceRefinesSiblingsAndKeepsOnlyVerifiedWinner()
    {
        var (topology, pressure) = CreateSmtTopology();
        var request = new GpuAutoAffinitySessionRequest(
            Guid.NewGuid(),
            topology,
            pressure,
            null,
            0x51A7,
            GpuAutoAffinitySession.ScreeningDuration);
        var backend = new ScriptedBackend();

        var result = await new GpuAutoAffinitySession(backend).RunAsync(request);

        var preparationIndex = backend.Events.IndexOf("prepare-original-comparison");
        var initialWarmupIndex = backend.Events.FindIndex(static item =>
            item.StartsWith("original:screening-warmup:", StringComparison.Ordinal));
        Assert.IsGreaterThanOrEqualTo(0, preparationIndex);
        Assert.IsGreaterThan(preparationIndex, initialWarmupIndex,
            "Full search must canonicalize the Original GPU/renderer state before any benchmark warm-up or scored Original evidence.");

        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, result.Recommendation);
        Assert.AreEqual(new LogicalProcessorId(0, 3), result.Finalist?.Processor);
        Assert.AreEqual(new LogicalProcessorId(0, 3), result.Report.FinalProcessor);
        Assert.IsTrue(result.Report.FinalStateVerified);
        Assert.IsFalse(result.Report.OriginalStateRestored);
        Assert.AreEqual(GpuAutoAffinitySearchScope.Full, result.Report.SearchScope);
        Assert.IsTrue(result.Report.FullTopologyCoverage);
        Assert.IsFalse(result.Report.PracticalTie);
        Assert.IsTrue(result.Report.Pairs.Any(static pair => pair.Stage == "screening-representative"));
        Assert.IsTrue(result.Report.Pairs.Any(static pair => pair.Stage == "screening-sibling"));
        Assert.IsTrue(result.Report.Pairs.Any(static pair => pair.Stage == "screening-shortlist"),
            "Full v4 must recheck the uncertainty-aware shortlist before the top-two cut.");
        Assert.IsTrue(result.Report.Pairs.Any(static pair => pair.Stage == "screening-finalists"),
            "Only the bounded top two should receive adaptive finalist confirmation.");
        Assert.IsTrue(result.Report.Pairs.All(static pair =>
            pair.Verdict == GpuAutoAffinityPairVerdict.Valid && pair.ControlMovement == 0d));
        var validated = result.Report.ValidatedProcessors
            .Select(static processor => processor.Number)
            .ToArray();
        CollectionAssert.Contains(validated, (byte)0,
            "Every physical core must contribute its representative before adaptive pruning.");
        CollectionAssert.Contains(validated, (byte)2,
            "Every physical core must contribute its representative before adaptive pruning.");
        CollectionAssert.Contains(validated, (byte)3,
            "The promising core's SMT sibling must be refined before the shortlist.");
        Assert.AreEqual(3, validated.Length,
            "The clearly weaker physical core's sibling should be pruned instead of adding an unnecessary measurement.");
        Assert.AreEqual(2, result.Report.Finalists.Count);
        Assert.IsTrue(result.Report.Finalists.All(static finalist =>
            finalist.PairNumbers.Count is >= 2 and <= 3),
            "Adaptive finalist confirmation must use two rounds by default and at most one uncertainty-driven extension.");
        Assert.AreEqual(new LogicalProcessorId(0, 3), result.Report.BestObservedProcessor);
        Assert.AreEqual("High", result.Report.SelectionConfidence);
        var winningFinalist = result.Report.Finalists.Single(static finalist => finalist.Processor == new LogicalProcessorId(0, 3));
        Assert.AreEqual(100d, winningFinalist.MedianOriginalOnePercentLowFps!.Value, 0.001d);
        Assert.AreEqual(115d, winningFinalist.MedianCandidateOnePercentLowFps!.Value, 0.001d);
        Assert.AreEqual(0.15d, winningFinalist.MedianOnePercentLowEffect!.Value, 0.0001d);
        Assert.IsTrue(winningFinalist.RecommendedForKeep);
        Assert.IsTrue(result.Report.Trials.Any(static trial => trial.Phase == "screening-original-control"));
        Assert.IsTrue(result.Report.Trials.Any(static trial => trial.Phase == "finalist-original-control"));
        var finalVerification = result.Report.Trials.Single(static trial => trial.Phase == "final-verification");
        Assert.AreEqual(new LogicalProcessorId(0, 3), finalVerification.Processor);
        Assert.IsTrue(finalVerification.Placement is { ConfirmsRequestedPlacement: true });
        Assert.IsTrue(finalVerification.InterruptEvidence is { IsrSampleCount: > 0 });
        Assert.IsTrue(backend.Events.Contains("keep:0:3"));

        var serialized = JsonSerializer.Serialize(result.Report);
        var roundTrip = JsonSerializer.Deserialize<GpuAutoAffinityReport>(serialized)!;
        Assert.AreEqual(GpuAutoAffinityReport.SchemaId, roundTrip.Schema);
        Assert.AreEqual(result.Report.Pairs.Count, roundTrip.Pairs.Count);
        Assert.AreEqual(result.Report.Finalists.Count, roundTrip.Finalists.Count);

        var customBackend = new ScriptedBackend();
        var customRequest = request with
        {
            SessionId = Guid.NewGuid(),
            SearchScope = GpuAutoAffinitySearchScope.Custom,
            RequestedProcessors = [new LogicalProcessorId(0, 2), new LogicalProcessorId(0, 3)],
        };
        var custom = await new GpuAutoAffinitySession(customBackend).RunAsync(customRequest);

        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, custom.Recommendation);
        Assert.AreEqual(GpuAutoAffinitySearchScope.Custom, custom.Report.SearchScope);
        Assert.IsTrue(custom.Report.OriginalStateRestored);
        Assert.IsTrue(custom.Report.FinalStateVerified);
        Assert.IsNull(custom.Report.FinalProcessor);
        Assert.AreEqual(new LogicalProcessorId(0, 3), custom.Report.BestObservedProcessor);
        Assert.AreEqual("Low", custom.Report.SelectionConfidence);
        Assert.AreEqual(0, custom.Report.Finalists.Count,
            "Custom scope is fast screening-only evidence and must skip the finalist tournament.");
        CollectionAssert.AreEquivalent(
            new byte[] { 2, 3 },
            custom.Report.ValidatedProcessors.Select(static processor => processor.Number).ToArray());
        Assert.IsFalse(customBackend.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)));
        Assert.IsFalse(custom.Report.Trials.Any(static trial => trial.Phase == "final-verification"));
        Assert.IsTrue(customBackend.Events.Contains("prepare-original-comparison"),
            "Custom paired comparison also crosses GPU restart boundaries and must canonicalize Original first.");
        Assert.IsTrue(custom.Report.Reasons.Any(static reason =>
            reason.Contains("Best within selected CPUs", StringComparison.Ordinal)));

        var originalOnlyBackend = new ScriptedBackend();
        var originalOnly = await new GpuAutoAffinitySession(originalOnlyBackend).RunAsync(request with
        {
            SessionId = Guid.NewGuid(),
            SearchScope = GpuAutoAffinitySearchScope.OriginalDiagnostics,
        });
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, originalOnly.Recommendation);
        Assert.IsFalse(originalOnlyBackend.Events.Contains("prepare-original-comparison"),
            "Original-only diagnostics promise no GPU restart and must not use the Full/Custom comparison-state canonicalization.");
        Assert.IsFalse(originalOnlyBackend.Events.Any(static item => item.StartsWith("apply:", StringComparison.Ordinal)));
    }

    [AuditCase]
    public async Task SessionRestoresOnUnverifiedFinalPlacementAndKeepsRollbackOwnershipOnFailureOrCancel()
    {
        var (topology, pressure) = CreateSmtTopology();
        var request = new GpuAutoAffinitySessionRequest(
            Guid.NewGuid(), topology, pressure, null, 0x51A7,
            GpuAutoAffinitySession.ScreeningDuration);

        var missingFinalEtw = new ScriptedBackend(finalEtwUnavailable: true);
        var missingFinalResult = await new GpuAutoAffinitySession(missingFinalEtw).RunAsync(request);
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, missingFinalResult.Recommendation);
        Assert.IsNull(missingFinalResult.Finalist);
        Assert.IsNull(missingFinalResult.Report.FinalProcessor);
        Assert.IsTrue(missingFinalResult.Report.OriginalStateRestored);
        Assert.IsTrue(missingFinalResult.Report.FinalStateVerified);
        Assert.IsFalse(missingFinalEtw.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)));
        Assert.IsTrue(missingFinalEtw.Events.Any(static item => item == "rollback:0:3"));

        var invalidKeep = new ScriptedBackend(failKeep: true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new GpuAutoAffinitySession(invalidKeep).RunAsync(request with { SessionId = Guid.NewGuid() }));
        Assert.IsTrue(invalidKeep.Events.Contains("keep-failed:0:3"));
        Assert.IsTrue(invalidKeep.Events.Contains("rollback:0:3"));

        var cancelling = new ScriptedBackend(cancelDuringFirstScoredCandidate: true);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            new GpuAutoAffinitySession(cancelling).RunAsync(request with { SessionId = Guid.NewGuid() }));
        Assert.IsTrue(cancelling.Events.Any(static item => item.StartsWith("apply:", StringComparison.Ordinal)));
        Assert.IsTrue(cancelling.Events.Any(static item => item.StartsWith("rollback:", StringComparison.Ordinal)));
        Assert.IsFalse(cancelling.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)));
    }

    [AuditCase]
    public async Task SessionUsesNoiseToLowerConfidenceWithoutErasingTheBestObservedCpu()
    {
        var (topology, pressure) = CreateSmtTopology();
        var request = new GpuAutoAffinitySessionRequest(
            Guid.NewGuid(), topology, pressure, null, 0x51A7,
            GpuAutoAffinitySession.ScreeningDuration);

        var recoverableOriginalBackend = new ScriptedBackend(recoverableOriginalOutlier: true);
        var recoverable = await new GpuAutoAffinitySession(recoverableOriginalBackend).RunAsync(request);
        Assert.AreEqual(
            3,
            recoverableOriginalBackend.Events.Count(static item =>
                item.StartsWith("original:screening-original:", StringComparison.Ordinal)),
            "Median/MAD should tolerate one isolated Original outlier without forcing extra acquisition.");
        Assert.IsTrue(recoverableOriginalBackend.Events.Any(static item => item.StartsWith("apply:", StringComparison.Ordinal)));
        Assert.IsNotNull(recoverable.Report.BestObservedProcessor);

        var persistentOriginalBackend = new ScriptedBackend(persistentlyNoisyOriginal: true);
        var persistent = await new GpuAutoAffinitySession(persistentOriginalBackend).RunAsync(request with { SessionId = Guid.NewGuid() });
        Assert.AreEqual(
            5,
            persistentOriginalBackend.Events.Count(static item =>
                item.StartsWith("original:screening-original:", StringComparison.Ordinal)));
        Assert.IsTrue(
            persistentOriginalBackend.Events.Any(static item => item.StartsWith("apply:", StringComparison.Ordinal)),
            "Broad but structurally valid Original variability must reduce confidence rather than block candidate measurement.");
        Assert.IsNotNull(persistent.Report.BestObservedProcessor);
        Assert.AreNotEqual("Unavailable", persistent.Report.SelectionConfidence);

        var unstableBackend = new ScriptedBackend(unstablePairControls: true);
        var unstable = await new GpuAutoAffinitySession(unstableBackend).RunAsync(request with { SessionId = Guid.NewGuid() });
        var noisyValidated = unstable.Report.ValidatedProcessors
            .Select(static processor => processor.Number)
            .ToArray();
        CollectionAssert.Contains(noisyValidated, (byte)0,
            "The first physical-core representative must still be screened under noise.");
        CollectionAssert.Contains(noisyValidated, (byte)2,
            "The second physical-core representative must still be screened under noise.");
        Assert.IsGreaterThanOrEqualTo(3, noisyValidated.Length,
            "Noise must not trigger the historical global early stop; adaptive pruning may still skip a clearly implausible sibling.");
        Assert.IsNotNull(unstable.Report.BestObservedProcessor,
            "Structurally valid noisy measurements must still yield a best-observed CPU.");
        Assert.IsTrue(unstable.Report.Pairs.Any(static pair => pair.Verdict == GpuAutoAffinityPairVerdict.Unstable),
            "The first high-drift attempts remain visible as noise evidence.");
        Assert.IsTrue(unstable.Report.Pairs.Any(static pair =>
            pair.Verdict == GpuAutoAffinityPairVerdict.Valid &&
            pair.ControlMovement > pair.DriftBudget),
            "After one retry, a still-noisy but structurally valid pair remains rankable and lowers confidence.");
        Assert.AreEqual(
            0,
            unstable.Report.Pairs.Count(static pair => pair.Verdict == GpuAutoAffinityPairVerdict.Inconclusive),
            "Ordinary control drift alone must not erase a candidate from ranking.");
        Assert.IsFalse(unstable.Report.Reasons.Any(static reason =>
            reason.Contains("stopped early", StringComparison.OrdinalIgnoreCase)));
    }

    private static (ProcessorTopologySnapshot Topology, ProcessorPressureEvidence[] Pressure) CreateSmtTopology()
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
        var pressure = new[]
        {
            new ProcessorPressureEvidence(cpu0, 0.10d),
            new ProcessorPressureEvidence(cpu1, 0.20d),
            new ProcessorPressureEvidence(cpu2, 0.30d),
            new ProcessorPressureEvidence(cpu3, 0.40d),
        };
        return (topology, pressure);
    }

    private sealed class ScriptedBackend : IGpuAutoAffinitySessionBackend
    {
        private readonly Dictionary<Guid, GpuAffinityCandidate> active = [];
        private readonly bool finalEtwUnavailable;
        private readonly bool failKeep;
        private readonly bool cancelDuringFirstScoredCandidate;
        private readonly bool recoverableOriginalOutlier;
        private readonly bool persistentlyNoisyOriginal;
        private readonly bool unstablePairControls;
        private int captureSequence;
        private int originalQualificationIndex;
        private int unstableControlIndex;
        private bool cancellationThrown;

        internal ScriptedBackend(
            bool finalEtwUnavailable = false,
            bool failKeep = false,
            bool cancelDuringFirstScoredCandidate = false,
            bool recoverableOriginalOutlier = false,
            bool persistentlyNoisyOriginal = false,
            bool unstablePairControls = false)
        {
            this.finalEtwUnavailable = finalEtwUnavailable;
            this.failKeep = failKeep;
            this.cancelDuringFirstScoredCandidate = cancelDuringFirstScoredCandidate;
            this.recoverableOriginalOutlier = recoverableOriginalOutlier;
            this.persistentlyNoisyOriginal = persistentlyNoisyOriginal;
            this.unstablePairControls = unstablePairControls;
        }

        internal List<string> Events { get; } = [];

        public Task PrepareOriginalComparisonStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add("prepare-original-comparison");
            return Task.CompletedTask;
        }

        public Task<GpuAutoAffinityTrialObservation> CaptureOriginalAsync(
            GpuAutoAffinityTrialRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"original:{request.Phase}:{request.RunNumber}");
            var fps = 100d;
            if (string.Equals(request.Phase, "screening-original", StringComparison.Ordinal))
            {
                originalQualificationIndex++;
                if (persistentlyNoisyOriginal)
                {
                    fps = originalQualificationIndex switch
                    {
                        1 => 100d,
                        2 => 130d,
                        3 => 70d,
                        4 => 145d,
                        _ => 60d,
                    };
                }
                else if (recoverableOriginalOutlier)
                {
                    fps = originalQualificationIndex switch
                    {
                        1 => 100d,
                        2 => 70d,
                        _ => 100d,
                    };
                }
            }
            else if (unstablePairControls && request.Phase.EndsWith("-original-after", StringComparison.Ordinal))
            {
                unstableControlIndex++;
                fps = unstableControlIndex switch
                {
                    1 => 120d,
                    2 => 140d,
                    3 => 170d,
                    _ => 200d,
                };
            }

            return Task.FromResult(CreateObservation(request, fps, placement: null, interruptEvidence: null));
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
            if (cancelDuringFirstScoredCandidate &&
                !cancellationThrown &&
                !request.Phase.EndsWith("-warmup", StringComparison.Ordinal))
            {
                cancellationThrown = true;
                throw new OperationCanceledException("Synthetic cancellation during owned candidate measurement.");
            }

            var fps = candidate.Processor.Number switch
            {
                0 => 105d,
                1 => 103d,
                2 => 110d,
                3 => 115d,
                _ => 100d,
            };
            var finalVerification = string.Equals(request.Phase, "final-verification", StringComparison.Ordinal);
            var placement = finalVerification && !finalEtwUnavailable
                ? new GpuAutoAffinityPlacementProof(candidate.Processor, 100, 0)
                : null;
            var interruptEvidence = finalVerification && !finalEtwUnavailable
                ? new GpuAutoAffinityInterruptEvidence("nvlddmkm.sys", "resolved", 100, 100, 0)
                : null;
            return Task.FromResult(CreateObservation(request, fps, placement, interruptEvidence));
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
            if (failKeep)
            {
                Events.Add($"keep-failed:{candidate.Processor}");
                throw new InvalidOperationException("Synthetic Keep failure.");
            }

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
            double fps,
            GpuAutoAffinityPlacementProof? placement,
            GpuAutoAffinityInterruptEvidence? interruptEvidence)
        {
            const uint processId = 77;
            var framePeriod = 1000d / fps;
            var framePeriods = Enumerable.Repeat(framePeriod, 120).ToArray();
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
                placement,
                Enumerable.Repeat(20d, 120).ToArray(),
                Enumerable.Repeat(5d, 120).ToArray(),
                interruptEvidence);
        }
    }
}

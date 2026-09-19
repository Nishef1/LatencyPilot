#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using System.Text.Json;
using LatencyPilot.Benchmarking.Candidates;
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
    [AuditCase]
    public async Task SessionRanksByLowsScreensOnceRetestsFinalistsAndRequiresFinalPlacement()
    {
        var (topology, pressure, request) = CreateTwoCoreRequest();
        var progressPlan = GpuAutoAffinityProgressPlan.Create(topology, pressure, cpuSets: null);
        Assert.AreEqual(2, progressPlan.PhysicalCandidateCount);
        Assert.AreEqual(2, progressPlan.FinalistCandidateCount);
        Assert.AreEqual(20, progressPlan.InitialTotalUnits);

        var nonSmtTopology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0, [new LogicalProcessorId(0, 0)])],
            [new ProcessorCoreSnapshot(0, 0, [new LogicalProcessorId(0, 0)])],
            DateTimeOffset.UnixEpoch);
        var nonSmtPlan = GpuAutoAffinityProgressPlan.Create(
            nonSmtTopology,
            [new ProcessorPressureEvidence(new LogicalProcessorId(0, 0), 0d)],
            cpuSets: null);
        Assert.AreEqual(1, nonSmtPlan.FinalistCandidateCount);
        Assert.AreEqual(14, nonSmtPlan.InitialTotalUnits);

        var backend = new RecordingBackend();
        var observer = new RecordingObserver();
        var result = await new GpuAutoAffinitySession(backend, observer).RunAsync(request);

        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, result.Recommendation);
        Assert.IsNotNull(result.Finalist);
        Assert.AreEqual(new LogicalProcessorId(0, 2), result.Finalist.Processor,
            "CPU2 must win because its 1% low is stronger even though CPU0 has higher AVG FPS and the lower frame-p99.");
        Assert.IsTrue(result.Report.FinalStateVerified);
        Assert.IsFalse(result.Report.OriginalStateRestored);
        Assert.IsTrue(backend.Events.Contains("keep:0:2"));
        Assert.AreEqual(
            3,
            backend.Events.Count(static item => item == "apply:0:0"),
            "The non-winning finalist must be applied once for screening and once in each independent finalist round.");
        Assert.AreEqual(
            4,
            backend.Events.Count(static item => item == "apply:0:2"),
            "The winner must be applied for screening, two independent finalist rounds, and final placement verification.");

        var screeningReports = observer.Reports.Where(static report => report.Phase == "screening").ToArray();
        Assert.AreEqual(2, screeningReports.Length);
        Assert.IsTrue(screeningReports.All(static report => report.TrialCount == 1));
        var finalistReports = observer.Reports.Where(static report => report.Phase == "screening-finalists").ToArray();
        Assert.AreEqual(2, finalistReports.Length);
        Assert.IsTrue(finalistReports.All(static report => report.TrialCount == 2));

        Assert.IsTrue(result.Report.Trials.Any(static trial =>
            trial.Phase == "screening-warmup" && trial.Processor is null));
        Assert.AreEqual(
            4,
            result.Report.Trials.Count(static trial => trial.Phase == "screening-finalists-warmup"),
            "Each of the two finalists must receive a fresh apply/restart/warm-up in each of two independent re-test rounds.");
        Assert.IsTrue(result.Report.Trials.Any(static trial => trial.Phase == "final-verification-warmup"));
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

        var (_, _, toleranceRequest) = CreateFourCoreRequest();
        var toleranceBackend = new RecordingBackend(comparisonToleranceCase: true);
        var toleranceResult = await new GpuAutoAffinitySession(toleranceBackend).RunAsync(toleranceRequest);

        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, toleranceResult.Recommendation);
        Assert.AreEqual(
            new LogicalProcessorId(0, 0),
            toleranceResult.Finalist?.Processor,
            "A sub-1% 1%-low edge must be treated as equivalent so materially stronger AVG/pacing can decide the winner.");
        Assert.AreEqual(
            3,
            toleranceBackend.Events.Count(static item => item == "apply:0:6"),
            "A fourth-place screen within the 1% primary-noise margin of the third-place cutoff must receive both finalist re-test rounds.");

        var (_, _, noiseAwareRequest) = CreateFourCoreRequest();
        var noiseAwareBackend = new RecordingBackend(
            noisyOriginalCluster: true,
            noiseAwareShortlistCase: true);
        var noiseAwareResult = await new GpuAutoAffinitySession(noiseAwareBackend).RunAsync(noiseAwareRequest);
        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, noiseAwareResult.Recommendation);
        Assert.AreEqual(
            3,
            noiseAwareBackend.Events.Count(static item => item == "apply:0:6"),
            "A fourth-place candidate outside the fixed 1% band but inside measured Original noise must receive both finalist rounds.");

        var controlDriftBackend = new RecordingBackend(postScreeningControlDrift: true);
        var controlDrift = await new GpuAutoAffinitySession(controlDriftBackend).RunAsync(request);
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, controlDrift.Recommendation);
        Assert.IsTrue(controlDrift.Report.Reasons.Any(static reason =>
            reason.Contains("control drifted", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(controlDriftBackend.Events.Any(static item =>
            item.Contains("screening-finalists", StringComparison.Ordinal)));

        var finalistControlDriftBackend = new RecordingBackend(postFinalistControlDrift: true);
        var finalistControlDrift = await new GpuAutoAffinitySession(finalistControlDriftBackend).RunAsync(request);
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, finalistControlDrift.Recommendation);
        Assert.IsTrue(finalistControlDrift.Report.Reasons.Any(static reason =>
            reason.Contains("after finalist re-tests", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(finalistControlDriftBackend.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)));

        var interruptFallbackBackend = new RecordingBackend(interruptGuardrailFallbackCase: true);
        var interruptFallback = await new GpuAutoAffinitySession(interruptFallbackBackend).RunAsync(request);
        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, interruptFallback.Recommendation);
        Assert.AreEqual(new LogicalProcessorId(0, 0), interruptFallback.Finalist?.Processor,
            "A faster finalist with a material GPU-driver interrupt-tail regression must be skipped in favor of the next clean improvement.");
        Assert.IsTrue(interruptFallback.Report.Reasons.Any(static reason =>
            reason.Contains("CPU 2 was rejected", StringComparison.OrdinalIgnoreCase) &&
            reason.Contains("interrupt-tail", StringComparison.OrdinalIgnoreCase)));

        var originalWinsBackend = new RecordingBackend(originalBeatsCandidates: true);
        var originalWins = await new GpuAutoAffinitySession(originalWinsBackend).RunAsync(request);
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, originalWins.Recommendation);
        Assert.IsTrue(originalWins.Report.OriginalStateRestored);
        Assert.IsFalse(originalWinsBackend.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)));
        Assert.IsTrue(originalWins.Report.Reasons.Any(static reason =>
            reason.Contains("measurable", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("original", StringComparison.OrdinalIgnoreCase)));

        var unknownPlacementBackend = new RecordingBackend(unknownScreeningPlacement: true);
        var unknownPlacement = await new GpuAutoAffinitySession(unknownPlacementBackend).RunAsync(request);
        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, unknownPlacement.Recommendation,
            "Missing attributable ISR samples during screening is Unknown evidence, not proof that placement is wrong.");
        Assert.AreEqual(new LogicalProcessorId(0, 2), unknownPlacement.Finalist?.Processor);
        Assert.IsTrue(unknownPlacement.Report.Trials
            .Where(static trial => trial.Phase == "screening")
            .All(static trial => trial.ReadinessState == GpuBenchmarkReadinessState.Ready.ToString()));

        var noWriteBackend = new RecordingBackend(noWriteProcessorNumber: 2);
        var noWrite = await new GpuAutoAffinitySession(noWriteBackend).RunAsync(request);
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, noWrite.Recommendation,
            "A candidate that is already the original state must be measured without a redundant write and must not be kept as a fake improvement.");
        Assert.IsTrue(noWriteBackend.Events.Contains("apply-no-write:0:2"));
        Assert.IsFalse(noWriteBackend.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)));
    }

    [AuditCase]
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

    [AuditCase]
    public async Task SessionUsesRobustThreeOfFourSamplingAndRestoresWhenNoisePersists()
    {
        var (_, _, request) = CreateTwoCoreRequest();

        var recoverableOriginalBackend = new RecordingBackend(recoverableOriginalOutlier: true);
        var recoverableOriginal = await new GpuAutoAffinitySession(recoverableOriginalBackend).RunAsync(request);
        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, recoverableOriginal.Recommendation);
        Assert.AreEqual(
            4,
            recoverableOriginalBackend.Events.Count(static item =>
                item.StartsWith("original:screening-original:", StringComparison.Ordinal)),
            "One noisy Original sample must be replaced instead of aborting the whole search.");
        Assert.IsTrue(recoverableOriginal.Report.Reasons.Any(static reason =>
            reason.Contains("3 valid", StringComparison.OrdinalIgnoreCase) &&
            reason.Contains("outlier", StringComparison.OrdinalIgnoreCase)));

        var persistentOriginalBackend = new RecordingBackend(persistentlyNoisyOriginal: true);
        var persistentOriginal = await new GpuAutoAffinitySession(persistentOriginalBackend).RunAsync(request);
        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, persistentOriginal.Recommendation);
        Assert.IsTrue(persistentOriginal.Report.OriginalStateRestored);
        Assert.AreEqual(
            5,
            persistentOriginalBackend.Events.Count(static item =>
                item.StartsWith("original:screening-original:", StringComparison.Ordinal)),
            "Original sampling must stop after four scored attempts when no stable 3-run cluster exists.");
        Assert.IsFalse(persistentOriginalBackend.Events.Any(static item => item.StartsWith("apply:", StringComparison.Ordinal)));
        Assert.IsTrue(persistentOriginal.Report.Reasons.Any(static reason =>
            reason.Contains("stable 3-run", StringComparison.OrdinalIgnoreCase)));

        var recoverableFinalistBackend = new RecordingBackend(recoverableFinalistOutlier: true);
        var recoverableFinalist = await new GpuAutoAffinitySession(recoverableFinalistBackend).RunAsync(request);
        Assert.AreEqual(GpuOptimizationRecommendation.KeepCandidate, recoverableFinalist.Recommendation);
        Assert.AreEqual(new LogicalProcessorId(0, 2), recoverableFinalist.Finalist?.Processor);
        Assert.AreEqual(
            3,
            recoverableFinalistBackend.Events.Count(static item =>
                item.StartsWith("candidate:screening-finalists:", StringComparison.Ordinal) &&
                item.EndsWith(":0:2", StringComparison.Ordinal)),
            "A finalist with one noisy re-test must receive exactly one adaptive replacement run.");
        var recoveredFinalistReport = recoverableFinalist.Report.Candidates.Single(static item =>
            item.Phase == "screening-finalists" && item.Processor == new LogicalProcessorId(0, 2));
        Assert.AreEqual(3, recoveredFinalistReport.TrialCount);
        StringAssert.Contains(recoveredFinalistReport.Reason!, "3/4");

        var backend = new RecordingBackend(unstableFinalists: true);
        var result = await new GpuAutoAffinitySession(backend).RunAsync(request);

        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, result.Recommendation);
        Assert.IsNull(result.Finalist);
        Assert.IsTrue(result.Report.FinalStateVerified);
        Assert.IsTrue(result.Report.OriginalStateRestored);
        Assert.IsFalse(backend.Events.Any(static item => item.StartsWith("keep:", StringComparison.Ordinal)));
        Assert.IsFalse(result.Report.Trials.Any(static trial =>
            trial.Phase == "confirmation" || trial.Phase == "smt-refinement" || trial.Phase == "final-verification"));
        var unstableReports = result.Report.Candidates
            .Where(static item => item.Phase == "screening-finalists")
            .ToArray();
        Assert.IsTrue(unstableReports.All(static item => item.Verdict == "Inconclusive"));
        Assert.IsTrue(unstableReports.All(static item => item.TrialCount == 3));
        Assert.IsTrue(unstableReports.Any(static item =>
            item.Reason is { } reason &&
            reason.Contains("stable 3-run", StringComparison.OrdinalIgnoreCase)));
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
            TimeSpan.FromSeconds(15));
        return (topology, pressure, request);
    }

    private static (ProcessorTopologySnapshot Topology, ProcessorPressureEvidence[] Pressure, GpuAutoAffinitySessionRequest Request)
        CreateFourCoreRequest()
    {
        var processors = new[]
        {
            new LogicalProcessorId(0, 0),
            new LogicalProcessorId(0, 2),
            new LogicalProcessorId(0, 4),
            new LogicalProcessorId(0, 6),
        };
        var topology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0, processors)],
            processors.Select((processor, index) =>
                new ProcessorCoreSnapshot(index, 0, [processor])).ToArray(),
            DateTimeOffset.UnixEpoch);
        var pressure = processors
            .Select((processor, index) => new ProcessorPressureEvidence(processor, 0.1 + (index * 0.01)))
            .ToArray();
        return (
            topology,
            pressure,
            new GpuAutoAffinitySessionRequest(
                Guid.NewGuid(),
                topology,
                pressure,
                null,
                0x51A7,
                TimeSpan.FromSeconds(15)));
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
        bool unstableFinalists = false,
        bool recoverableOriginalOutlier = false,
        bool persistentlyNoisyOriginal = false,
        bool recoverableFinalistOutlier = false,
        bool comparisonToleranceCase = false,
        bool originalBeatsCandidates = false,
        bool unknownScreeningPlacement = false,
        bool noisyOriginalCluster = false,
        bool noiseAwareShortlistCase = false,
        bool postScreeningControlDrift = false,
        bool postFinalistControlDrift = false,
        bool interruptGuardrailFallbackCase = false,
        byte? noWriteProcessorNumber = null) : IGpuAutoAffinitySessionBackend
    {
        private readonly Dictionary<Guid, GpuAffinityCandidate> active = [];
        private readonly Dictionary<LogicalProcessorId, int> finalistScoredSequences = [];
        private int captureSequence;
        private int originalScoredSequence;
        private bool cancelled;
        private bool keepFailed;

        internal List<string> Events { get; } = [];

        public Task<GpuAutoAffinityTrialObservation> CaptureOriginalAsync(
            GpuAutoAffinityTrialRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add($"original:{request.Phase}:{request.RunNumber}");
            double[] periods;
            if (string.Equals(request.Phase, "screening-original", StringComparison.Ordinal) &&
                (recoverableOriginalOutlier || persistentlyNoisyOriginal || noisyOriginalCluster))
            {
                var sequence = Interlocked.Increment(ref originalScoredSequence);
                var period = recoverableOriginalOutlier
                    ? sequence == 2 ? 18d : 12d
                    : persistentlyNoisyOriginal
                        ? 10d + (sequence * 3d)
                        : sequence switch
                        {
                            1 => 12d,
                            2 => 12.2d,
                            _ => 11.8d,
                        };
                periods = Enumerable.Repeat(period, 100).ToArray();
            }
            else if ((postScreeningControlDrift &&
                      string.Equals(request.Phase, "screening-control", StringComparison.Ordinal)) ||
                     (postFinalistControlDrift &&
                      string.Equals(request.Phase, "finalist-control", StringComparison.Ordinal)))
            {
                periods = Enumerable.Repeat(16d, 100).ToArray();
            }
            else
            {
                periods = originalBeatsCandidates
                    ? Enumerable.Repeat(4d, 100).ToArray()
                    : noWriteProcessorNumber == 2
                        ? Enumerable.Repeat(6d, 99).Append(10d).ToArray()
                        : Enumerable.Repeat(12d, 100).ToArray();
            }
            return Task.FromResult(CreateObservation(request, null, periods, etwHealthy: true));
        }

        public Task<Guid> ApplyCandidateAsync(GpuAffinityCandidate candidate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (noWriteProcessorNumber == candidate.Processor.Number)
            {
                Events.Add($"apply-no-write:{candidate.Processor}");
                return Task.FromResult(Guid.Empty);
            }

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
            var candidate = experimentId == Guid.Empty
                ? request.Candidate ?? throw new InvalidOperationException("No-write candidate request is missing its candidate identity.")
                : active[experimentId];
            Events.Add($"candidate:{request.Phase}:{request.RunNumber}:{candidate.Processor}");

            if (cancelDuringFirstScoredCandidate &&
                !cancelled &&
                string.Equals(request.Phase, "screening", StringComparison.Ordinal))
            {
                cancelled = true;
                throw new OperationCanceledException("synthetic safe-stop request");
            }

            var periods = noiseAwareShortlistCase
                ? candidate.Processor.Number switch
                {
                    0 => Enumerable.Repeat(6d, 99).Append(10d).ToArray(),
                    2 => Enumerable.Repeat(6d, 99).Append(10.204d).ToArray(),
                    4 => Enumerable.Repeat(6d, 99).Append(10.417d).ToArray(),
                    6 => Enumerable.Repeat(6d, 99).Append(10.582d).ToArray(),
                    _ => throw new InvalidOperationException("Unexpected synthetic noise-aware candidate."),
                }
                : interruptGuardrailFallbackCase
                    ? candidate.Processor.Number switch
                    {
                        0 => Enumerable.Repeat(6d, 99).Append(10.5d).ToArray(),
                        2 => Enumerable.Repeat(6d, 99).Append(10d).ToArray(),
                        _ => throw new InvalidOperationException("Unexpected synthetic interrupt-guardrail candidate."),
                    }
                : comparisonToleranceCase
                ? candidate.Processor.Number switch
                {
                    0 => Enumerable.Repeat(5d, 99).Append(10d).ToArray(),
                    2 => Enumerable.Repeat(6d, 99).Append(9.95d).ToArray(),
                    4 => Enumerable.Repeat(6d, 99).Append(11.11d).ToArray(),
                    6 => Enumerable.Repeat(6d, 99).Append(11.17d).ToArray(),
                    _ => throw new InvalidOperationException("Unexpected synthetic comparison candidate."),
                }
                : candidate.Processor.Number == 0
                    ? Enumerable.Repeat(5d, 99).Append(20d).ToArray()
                    : Enumerable.Repeat(6d, 99).Append(10d).ToArray();

            if (string.Equals(request.Phase, "screening-finalists", StringComparison.Ordinal))
            {
                finalistScoredSequences.TryGetValue(candidate.Processor, out var sequence);
                sequence++;
                finalistScoredSequences[candidate.Processor] = sequence;

                if (unstableFinalists)
                {
                    var period = 10d + (sequence * 4d) + candidate.Processor.Number;
                    periods = Enumerable.Repeat(period, 100).ToArray();
                }
                else if (recoverableFinalistOutlier &&
                         candidate.Processor.Number == 2 &&
                         sequence == 1)
                {
                    periods = Enumerable.Repeat(20d, 100).ToArray();
                }
            }

            var etwHealthy = !(finalEtwUnavailable && string.Equals(request.Phase, "final-verification", StringComparison.Ordinal));
            return Task.FromResult(CreateObservation(request, candidate, periods, etwHealthy));
        }

        public Task RollbackAsync(Guid experimentId, CancellationToken cancellationToken)
        {
            if (experimentId == Guid.Empty)
            {
                Events.Add("rollback-no-write");
                return Task.CompletedTask;
            }

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
            var verified = experimentId == Guid.Empty
                ? noWriteProcessorNumber == candidate.Processor.Number
                : active.TryGetValue(experimentId, out var current) && current == candidate;
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

            if (!etwHealthy ||
                (unknownScreeningPlacement && candidate is not null &&
                 string.Equals(request.Phase, "screening", StringComparison.Ordinal)))
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

            if (candidate is null)
            {
                return new GpuAutoAffinityTrialObservation(
                    evidence,
                    GpuBenchmarkContaminationContext.Clean,
                    true,
                    true,
                    Placement: null,
                    GpuDriverDpcDurationMicroseconds: Enumerable.Repeat(20d, frameCount).ToArray(),
                    GpuDriverIsrDurationMicroseconds: Enumerable.Repeat(5d, frameCount).ToArray(),
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
            var interruptMultiplier = interruptGuardrailFallbackCase && candidate.Processor.Number == 2
                ? 2d
                : 1d;
            return new GpuAutoAffinityTrialObservation(
                evidence,
                GpuBenchmarkContaminationContext.Clean,
                true,
                true,
                new GpuAutoAffinityPlacementProof(candidate.Processor, attribution.Events.Count, 0),
                Enumerable.Repeat(20d * interruptMultiplier, frameCount).ToArray(),
                attribution.Events.Select(item => item.DurationMicroseconds * interruptMultiplier).ToArray(),
                new GpuAutoAffinityInterruptEvidence(
                    attribution.ModuleName,
                    attribution.Mode,
                    frameCount,
                    attribution.Events.Count,
                    attribution.UnresolvedIsrEventCount));
        }
    }
}

using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Statistics;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Results;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed record GpuAutoAffinitySessionRequest(
    Guid SessionId,
    ProcessorTopologySnapshot Topology,
    IEnumerable<ProcessorPressureEvidence> PressureEvidence,
    ProcessorCpuSetSnapshot? CpuSets,
    int ShuffleSeed,
    TimeSpan ScreeningDuration);

public sealed record GpuAutoAffinityTrialRequest(
    int RunNumber,
    string Phase,
    GpuConfirmationOrder Role,
    GpuAffinityCandidate? Candidate,
    TimeSpan Duration,
    int RetryAttempt);

public sealed record GpuAutoAffinityTrialObservation(
    GpuBenchmarkEvidence Evidence,
    GpuBenchmarkContaminationContext Contamination,
    bool StoredStateVerifiedBefore,
    bool StoredStateVerifiedAfter,
    GpuAutoAffinityPlacementProof? Placement,
    IReadOnlyList<double> GpuDriverDpcDurationMicroseconds,
    IReadOnlyList<double> GpuDriverIsrDurationMicroseconds,
    GpuAutoAffinityInterruptEvidence? InterruptEvidence = null);

public interface IGpuAutoAffinitySessionBackend
{
    Task<GpuAutoAffinityTrialObservation> CaptureOriginalAsync(
        GpuAutoAffinityTrialRequest request,
        CancellationToken cancellationToken);

    Task<Guid> ApplyCandidateAsync(
        GpuAffinityCandidate candidate,
        CancellationToken cancellationToken);

    Task<GpuAutoAffinityTrialObservation> CaptureCandidateAsync(
        Guid experimentId,
        GpuAutoAffinityTrialRequest request,
        CancellationToken cancellationToken);

    Task RollbackAsync(Guid experimentId, CancellationToken cancellationToken);

    Task KeepAsync(Guid experimentId, CancellationToken cancellationToken);

    Task<bool> VerifyOriginalStateAsync(CancellationToken cancellationToken);

    Task<bool> VerifyCandidateStateAsync(
        Guid experimentId,
        GpuAffinityCandidate candidate,
        CancellationToken cancellationToken);
}

public interface IGpuAutoAffinitySessionObserver
{
    Task CandidateEvaluatedAsync(GpuAutoAffinityCandidateReport report);
}

public sealed record GpuAutoAffinitySessionResult(
    GpuOptimizationRecommendation Recommendation,
    GpuAffinityCandidate? Finalist,
    GpuAutoAffinityReport Report);

/// <summary>
/// Measurement-first GPU interrupt-affinity search. Every eligible logical CPU in
/// the supported processor group receives a scored screen unless a time-local
/// Original control proves that the environment has drifted. Original is scored with
/// the same workload and duration, and a forced candidate is retained only when its
/// repeatable improvement clears the measured/practical noise floor without material
/// guardrail regression. Ranking applies tolerances against fixed best references,
/// never a pairwise fuzzy comparer. Final Keep additionally requires ETW-backed
/// target-only runtime ISR placement proof.
/// </summary>
public sealed class GpuAutoAffinitySession
{
    private const double CandidateMetricEquivalenceTolerance = 0.01;
    private const double RareTailEquivalenceTolerance = 0.05;
    private const double InterruptTailRegressionTolerance = 0.10;
    private const int MinimumInterruptTailSamples = 20;
    private const int MinimumInterruptTailRuns = 3;
    private const int MaximumRejectedRepeatabilityRuns = 1;
    private const int MinimumFinalistCandidates = 3;
    private const int MaximumFinalistCandidates = 5;
    private const int ScreeningCandidatesPerControlBlock = 4;
    private const double MaximumShortlistTolerance = GpuRepeatabilityClusterSelector.RelativeTolerance;
    private const double MaximumNoiseForFullFinalistConfirmation = 0.15;
    private const string FinalistPhaseName = "screening-finalists";
    private static readonly TimeSpan TransitionWarmupDuration = TimeSpan.FromSeconds(5);
    private readonly IGpuAutoAffinitySessionBackend backend;
    private readonly IGpuAutoAffinitySessionObserver? observer;

    public GpuAutoAffinitySession(
        IGpuAutoAffinitySessionBackend backend,
        IGpuAutoAffinitySessionObserver? observer = null)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.observer = observer;
    }

    public async Task<GpuAutoAffinitySessionResult> RunAsync(
        GpuAutoAffinitySessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var trialReports = new List<GpuAutoAffinityTrialReport>();
        var candidateReports = new List<GpuAutoAffinityCandidateReport>();
        var reasons = new List<string>();
        var nextRunNumber = 0;
        var pressure = request.PressureEvidence.ToArray();
        var candidates = GpuAffinityCandidatePlanner
            .Create(request.Topology, pressure, request.CpuSets)
            .ToArray();

        if (candidates.Length == 0)
        {
            reasons.Add("No eligible logical-CPU GPU interrupt-affinity candidate is available.");
            var originalVerified = await backend.VerifyOriginalStateAsync(cancellationToken).ConfigureAwait(false);
            return CreateResult(
                request,
                startedAtUtc,
                GpuOptimizationRecommendation.RestoreOriginal,
                null,
                originalVerified,
                originalVerified,
                candidateReports,
                trialReports,
                reasons);
        }

        ShuffleDeterministically(candidates, request.ShuffleSeed);

        try
        {
            // Establish continuity once, then collect enough exact-Original samples
            // to identify three repeatable runs. A single noisy Windows/background
            // outlier is replaced rather than aborting the whole search.
            var referenceObservation = await CaptureAcceptedAsync(
                () => ++nextRunNumber,
                "screening-warmup",
                GpuConfirmationOrder.Original,
                null,
                TransitionWarmupDuration,
                reference: null,
                experimentId: null,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            var reference = referenceObservation.Evidence;
            var originalObservations = new List<GpuAutoAffinityTrialObservation>(GpuRepeatabilityClusterSelector.MaximumAttemptCount);
            var original = OriginalEvaluation.Unrankable(
                "Original repeatability has not collected three scored observations yet.");
            while (originalObservations.Count < GpuRepeatabilityClusterSelector.MaximumAttemptCount)
            {
                originalObservations.Add(await CaptureAcceptedAsync(
                    () => ++nextRunNumber,
                    "screening-original",
                    GpuConfirmationOrder.Original,
                    null,
                    request.ScreeningDuration,
                    reference,
                    experimentId: null,
                    trialReports,
                    cancellationToken).ConfigureAwait(false));
                if (originalObservations.Count < GpuRepeatabilityClusterSelector.RequiredRunCount)
                {
                    continue;
                }
                original = CreateOriginalEvaluation(originalObservations.ToArray());
                if (original.IsRankable)
                {
                    break;
                }
            }

            if (!original.IsRankable)
            {
                reasons.Add($"Original-state benchmark is not repeatable enough for an automatic Keep decision: {original.Reason}");
                var originalVerified = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    originalVerified, originalVerified, candidateReports, trialReports, reasons);
            }
            if (original.UsedNoiseAwareFallback)
            {
                reasons.Add(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Original did not form the preferred ±{GpuRepeatabilityClusterSelector.RelativeTolerance:P0} 1%-low cluster after {original.TotalObservationCount} scored runs. The search continued with all valid Original runs instead of aborting; observed 1%-low noise is {original.PrimaryRelativeNoise:P2} and is carried into shortlist and Keep thresholds."));
            }
            else if (original.TotalObservationCount > original.ValidObservationCount)
            {
                reasons.Add(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Original repeatability recovered {original.ValidObservationCount} valid runs from {original.TotalObservationCount} attempts; {original.TotalObservationCount - original.ValidObservationCount} outlier sample(s) were excluded from ranking. Max valid 1%-low deviation from the cluster median was {original.PrimaryRelativeNoise:P2}."));
            }

            var screeningEvaluations = new List<CandidateEvaluation>(candidates.Length);
            for (var candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = candidates[candidateIndex];
                screeningEvaluations.Add(await EvaluateCandidateBlockAsync(
                    candidate,
                    "screening",
                    repetitions: 1,
                    request.ScreeningDuration,
                    reference,
                    () => ++nextRunNumber,
                    candidateReports,
                    trialReports,
                    cancellationToken).ConfigureAwait(false));

                var completedCandidates = candidateIndex + 1;
                var hasRemainingCandidates = completedCandidates < candidates.Length;
                if (!hasRemainingCandidates || completedCandidates % ScreeningCandidatesPerControlBlock != 0)
                {
                    continue;
                }

                var blockNumber = completedCandidates / ScreeningCandidatesPerControlBlock;
                await CaptureAcceptedAsync(
                    () => ++nextRunNumber,
                    "screening-block-control-warmup",
                    GpuConfirmationOrder.Original,
                    null,
                    TransitionWarmupDuration,
                    reference: null,
                    experimentId: null,
                    trialReports,
                    cancellationToken).ConfigureAwait(false);
                var blockControl = await CaptureAcceptedAsync(
                    () => ++nextRunNumber,
                    "screening-block-control",
                    GpuConfirmationOrder.Original,
                    null,
                    request.ScreeningDuration,
                    reference,
                    experimentId: null,
                    trialReports,
                    cancellationToken).ConfigureAwait(false);
                if (IsControlComparableToOriginal(
                        original,
                        blockControl,
                        $"after screening block {blockNumber}",
                        out var blockControlReason))
                {
                    continue;
                }

                reasons.Add(blockControlReason);
                reasons.Add(
                    $"Screening block {blockNumber} was invalidated by Original drift; remaining candidates were not started and the exact original state was retained rather than ranking across a moving environment.");
                var originalVerified = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request,
                    startedAtUtc,
                    GpuOptimizationRecommendation.RestoreOriginal,
                    null,
                    originalVerified,
                    originalVerified,
                    candidateReports,
                    trialReports,
                    reasons);
            }

            await CaptureAcceptedAsync(
                () => ++nextRunNumber,
                "screening-control-warmup",
                GpuConfirmationOrder.Original,
                null,
                TransitionWarmupDuration,
                reference: null,
                experimentId: null,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            var screeningControl = await CaptureAcceptedAsync(
                () => ++nextRunNumber,
                "screening-control",
                GpuConfirmationOrder.Original,
                null,
                request.ScreeningDuration,
                reference,
                experimentId: null,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            if (!IsControlComparableToOriginal(original, screeningControl, "after the candidate sweep", out var controlReason))
            {
                reasons.Add(controlReason);
                reasons.Add("The candidate sweep was discarded because the Original control drifted after screening; the exact original state was retained rather than ranking across a moving environment.");
                var originalVerified = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    originalVerified, originalVerified, candidateReports, trialReports, reasons);
            }

            if (original.PrimaryRelativeNoise > MaximumNoiseForFullFinalistConfirmation)
            {
                reasons.Add(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"All eligible logical CPUs were screened once, but Original 1%-low noise was {original.PrimaryRelativeNoise:P2}, above the {MaximumNoiseForFullFinalistConfirmation:P0} exhaustive-confirmation budget. Finalist re-tests were skipped because repeating every noisy candidate would add substantial runtime without producing a trustworthy automatic Keep decision."));
                var originalVerified = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    originalVerified, originalVerified, candidateReports, trialReports, reasons);
            }

            var finalists = await RescreenTopCandidatesAsync(
                request,
                original.PrimaryRelativeNoise,
                screeningEvaluations,
                reference,
                candidateReports,
                trialReports,
                () => ++nextRunNumber,
                cancellationToken).ConfigureAwait(false);

            await CaptureAcceptedAsync(
                () => ++nextRunNumber,
                "finalist-control-warmup",
                GpuConfirmationOrder.Original,
                null,
                TransitionWarmupDuration,
                reference: null,
                experimentId: null,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            var finalistControl = await CaptureAcceptedAsync(
                () => ++nextRunNumber,
                "finalist-control",
                GpuConfirmationOrder.Original,
                null,
                request.ScreeningDuration,
                reference,
                experimentId: null,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            if (!IsControlComparableToOriginal(original, finalistControl, "after finalist re-tests", out var finalistControlReason))
            {
                reasons.Add(finalistControlReason);
                reasons.Add("Finalist evidence was discarded because Original drifted during re-testing; the exact original state was retained rather than keeping a result from a moving environment.");
                var originalVerified = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    originalVerified, originalVerified, candidateReports, trialReports, reasons);
            }

            var rankedFinalists = OrderRankableCandidates(finalists).ToArray();
            CandidateEvaluation? finalist = null;
            foreach (var candidate in rankedFinalists)
            {
                if (IsMeasurablyBetterThanOriginal(original, candidate, out var comparisonReason))
                {
                    finalist = candidate;
                    break;
                }

                reasons.Add(
                    $"CPU {candidate.Candidate.Processor.Number} was rejected after finalist ranking: {comparisonReason}");
            }

            if (finalist is null)
            {
                reasons.Add(
                    rankedFinalists.Length == 0
                        ? "No GPU-affinity candidate remained valid and repeatable after the bounded top-candidate re-test; the exact original state was retained rather than guessing."
                        : "No repeatable finalist cleared the Original/noise and guardrail checks; the exact original state was retained rather than keeping a worse tradeoff.");
                reasons.AddRange(candidateReports
                    .Where(static report => string.Equals(report.Verdict, "Inconclusive", StringComparison.Ordinal))
                    .Select(static report => $"CPU {report.Processor.Number}: {report.Reason}"));
                var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request,
                    startedAtUtc,
                    GpuOptimizationRecommendation.RestoreOriginal,
                    null,
                    restored,
                    restored,
                    candidateReports,
                    trialReports,
                    reasons);
            }

            reasons.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"CPU {finalist.Candidate.Processor.Number} is the highest-ranked finalist that clears Original/noise and guardrail checks. Medians: 1% low {finalist.MedianLow1Fps:F1} FPS; AVG {finalist.MedianAvgFps:F1} FPS; p99 {finalist.MedianFrameP99Milliseconds:F2} ms; 0.1% low {finalist.MedianLow01Fps:F1} FPS."));

            return await VerifyAndKeepFinalistAsync(
                request,
                startedAtUtc,
                finalist.Candidate,
                reference,
                () => ++nextRunNumber,
                candidateReports,
                trialReports,
                reasons,
                cancellationToken).ConfigureAwait(false);
        }
        catch (SessionAbortException abort)
        {
            reasons.Add(abort.Message);
            var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
            if (!restored)
            {
                reasons.Add("The exact original GPU interrupt-affinity state could not be verified after the session aborted.");
            }

            return CreateResult(
                request,
                startedAtUtc,
                GpuOptimizationRecommendation.RestoreOriginal,
                null,
                restored,
                restored,
                candidateReports,
                trialReports,
                reasons);
        }
    }

    private async Task<CandidateEvaluation> EvaluateCandidateBlockAsync(
        GpuAffinityCandidate candidate,
        string phase,
        int repetitions,
        TimeSpan duration,
        GpuBenchmarkEvidence reference,
        Func<int> nextRunNumber,
        List<GpuAutoAffinityCandidateReport> candidateReports,
        List<GpuAutoAffinityTrialReport> trialReports,
        CancellationToken cancellationToken)
    {
        try
        {
            var observations = await MeasureCandidateAsync(
                candidate,
                phase,
                repetitions,
                duration,
                reference,
                nextRunNumber,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            var evaluation = CreateEvaluation(candidate, observations);
            var report = ToReport(phase, evaluation, repetitions);
            candidateReports.Add(report);
            await PublishCandidateReportAsync(report).ConfigureAwait(false);
            return evaluation;
        }
        catch (SessionAbortException abort)
        {
            var evaluation = CandidateEvaluation.Unrankable(candidate, abort.Message);
            var report = ToReport(phase, evaluation, 0);
            candidateReports.Add(report);
            await PublishCandidateReportAsync(report).ConfigureAwait(false);
            return evaluation;
        }
    }

    private async Task<IReadOnlyList<CandidateEvaluation>> RescreenTopCandidatesAsync(
        GpuAutoAffinitySessionRequest request,
        double screeningNoiseFloor,
        IReadOnlyList<CandidateEvaluation> screeningEvaluations,
        GpuBenchmarkEvidence reference,
        List<GpuAutoAffinityCandidateReport> candidateReports,
        List<GpuAutoAffinityTrialReport> trialReports,
        Func<int> nextRunNumber,
        CancellationToken cancellationToken)
    {
        var shortlist = CreateAdaptiveShortlist(screeningEvaluations, screeningNoiseFloor);
        if (shortlist.Length == 0)
        {
            return [];
        }

        // The screen contributes observation #1. Every finalist always receives
        // two independent fresh apply/restart/warm-up/score/rollback rounds first.
        // Only finalists that still lack a stable 3-run cluster receive one
        // adaptive replacement round. A fifth score cannot satisfy the
        // at-most-one-rejected-run policy, so collecting it would add time
        // without changing the decision.
        var freshByProcessor = shortlist.ToDictionary(
            static item => item.Candidate.Processor,
            static _ => new List<GpuAutoAffinityTrialObservation>(capacity: 4));
        var stableByProcessor = new Dictionary<LogicalProcessorId, CandidateEvaluation>();
        var failureByProcessor = new Dictionary<LogicalProcessorId, string>();

        for (var round = 0; round < 2; round++)
        {
            var roundCandidates = shortlist
                .Where(item => !failureByProcessor.ContainsKey(item.Candidate.Processor))
                .Select(static item => item.Candidate)
                .ToArray();
            var roundSeed = unchecked(request.ShuffleSeed ^ (int)(0x9E3779B9u * (uint)(round + 1)));
            ShuffleDeterministically(roundCandidates, roundSeed);

            foreach (var candidate in roundCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var fresh = await MeasureCandidateAsync(
                        candidate,
                        FinalistPhaseName,
                        repetitions: 1,
                        request.ScreeningDuration,
                        reference,
                        nextRunNumber,
                        trialReports,
                        cancellationToken).ConfigureAwait(false);
                    freshByProcessor[candidate.Processor].AddRange(fresh);
                }
                catch (SessionAbortException abort)
                {
                    failureByProcessor[candidate.Processor] = abort.Message;
                }
            }
        }

        foreach (var screened in shortlist)
        {
            if (failureByProcessor.ContainsKey(screened.Candidate.Processor))
            {
                continue;
            }

            var combined = screened.Observations
                .Concat(freshByProcessor[screened.Candidate.Processor])
                .ToArray();
            var evaluation = CreateEvaluation(screened.Candidate, combined);
            if (evaluation.IsRankable)
            {
                stableByProcessor[screened.Candidate.Processor] = evaluation;
            }
        }

        for (var replacementRound = 0; replacementRound < 1; replacementRound++)
        {
            var roundCandidates = shortlist
                .Where(item =>
                    !failureByProcessor.ContainsKey(item.Candidate.Processor) &&
                    !stableByProcessor.ContainsKey(item.Candidate.Processor))
                .Select(static item => item.Candidate)
                .ToArray();
            if (roundCandidates.Length == 0)
            {
                break;
            }

            var roundSeed = unchecked(request.ShuffleSeed ^ (int)(0x9E3779B9u * (uint)(replacementRound + 3)));
            ShuffleDeterministically(roundCandidates, roundSeed);
            foreach (var candidate in roundCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var fresh = await MeasureCandidateAsync(
                        candidate,
                        FinalistPhaseName,
                        repetitions: 1,
                        request.ScreeningDuration,
                        reference,
                        nextRunNumber,
                        trialReports,
                        cancellationToken).ConfigureAwait(false);
                    freshByProcessor[candidate.Processor].AddRange(fresh);

                    var screened = shortlist.Single(item => item.Candidate.Processor == candidate.Processor);
                    var combined = screened.Observations
                        .Concat(freshByProcessor[candidate.Processor])
                        .ToArray();
                    var evaluation = CreateEvaluation(candidate, combined);
                    if (evaluation.IsRankable)
                    {
                        stableByProcessor[candidate.Processor] = evaluation;
                    }
                }
                catch (SessionAbortException abort)
                {
                    failureByProcessor[candidate.Processor] = abort.Message;
                }
            }
        }

        var finalists = new List<CandidateEvaluation>(shortlist.Length);
        foreach (var screened in shortlist)
        {
            CandidateEvaluation evaluation;
            if (failureByProcessor.TryGetValue(screened.Candidate.Processor, out var failure))
            {
                evaluation = CandidateEvaluation.Unrankable(
                    screened.Candidate,
                    failure,
                    1 + freshByProcessor[screened.Candidate.Processor].Count);
            }
            else if (stableByProcessor.TryGetValue(screened.Candidate.Processor, out var stable))
            {
                evaluation = stable;
            }
            else
            {
                evaluation = CreateEvaluation(
                    screened.Candidate,
                    screened.Observations.Concat(freshByProcessor[screened.Candidate.Processor]).ToArray());
            }

            var report = ToReport(
                FinalistPhaseName,
                evaluation,
                freshByProcessor[screened.Candidate.Processor].Count);
            candidateReports.Add(report);
            await PublishCandidateReportAsync(report).ConfigureAwait(false);
            finalists.Add(evaluation);
        }

        return finalists;
    }

    private async Task<GpuAutoAffinitySessionResult> VerifyAndKeepFinalistAsync(
        GpuAutoAffinitySessionRequest request,
        DateTimeOffset startedAtUtc,
        GpuAffinityCandidate finalist,
        GpuBenchmarkEvidence reference,
        Func<int> nextRunNumber,
        List<GpuAutoAffinityCandidateReport> candidateReports,
        List<GpuAutoAffinityTrialReport> trialReports,
        List<string> reasons,
        CancellationToken cancellationToken)
    {
        Guid? activeExperiment = null;
        try
        {
            activeExperiment = await backend.ApplyCandidateAsync(finalist, cancellationToken).ConfigureAwait(false);
            if (!await backend.VerifyCandidateStateAsync(activeExperiment.Value, finalist, cancellationToken).ConfigureAwait(false))
            {
                throw new SessionAbortException(
                    $"Final GPU winner CPU {finalist.Processor.Number} could not verify its stored affinity state before runtime placement verification.");
            }

            await CaptureAcceptedAsync(
                nextRunNumber,
                "final-verification-warmup",
                GpuConfirmationOrder.Candidate,
                finalist,
                TransitionWarmupDuration,
                reference: null,
                activeExperiment,
                trialReports,
                cancellationToken).ConfigureAwait(false);

            var verification = await CaptureAcceptedAsync(
                nextRunNumber,
                "final-verification",
                GpuConfirmationOrder.Candidate,
                finalist,
                TransitionWarmupDuration,
                reference,
                activeExperiment,
                trialReports,
                cancellationToken).ConfigureAwait(false);

            if (!HasVerifiedFinalPlacement(verification, finalist))
            {
                await backend.RollbackAsync(activeExperiment.Value, CancellationToken.None).ConfigureAwait(false);
                activeExperiment = null;
                var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                reasons.Add(
                    "The ranked GPU winner was not kept because final kernel ETW could not prove target-only runtime ISR placement on the selected CPU.");
                return CreateResult(
                    request,
                    startedAtUtc,
                    GpuOptimizationRecommendation.RestoreOriginal,
                    finalist,
                    restored,
                    restored,
                    candidateReports,
                    trialReports,
                    reasons);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var keptId = activeExperiment.Value;
            if (keptId == Guid.Empty)
            {
                activeExperiment = null;
                var originalVerified = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                reasons.Add("The selected processor already represented the exact original stored policy, so no write or Keep terminalization was necessary.");
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, finalist,
                    originalVerified, originalVerified, candidateReports, trialReports, reasons);
            }

            await backend.KeepAsync(keptId, CancellationToken.None).ConfigureAwait(false);
            activeExperiment = null;
            reasons.Add(
                $"CPU {finalist.Processor.Number} was kept after exact stored-state verification and final kernel-ETW target-only ISR placement proof.");
            return CreateResult(
                request,
                startedAtUtc,
                GpuOptimizationRecommendation.KeepCandidate,
                finalist,
                finalStateVerified: true,
                originalStateRestored: false,
                candidateReports,
                trialReports,
                reasons);
        }
        catch (Exception failure) when (activeExperiment is not null)
        {
            var experimentId = activeExperiment.Value;
            try
            {
                await backend.RollbackAsync(experimentId, CancellationToken.None).ConfigureAwait(false);
                activeExperiment = null;
                if (!await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "GPU winner final verification failed and rollback completed, but the exact original state could not be verified.");
                }
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(
                    "GPU winner final verification failed and exact rollback also failed.",
                    failure,
                    rollbackFailure);
            }

            throw;
        }
    }

    private static bool HasVerifiedFinalPlacement(
        GpuAutoAffinityTrialObservation observation,
        GpuAffinityCandidate finalist) =>
        observation.StoredStateVerifiedBefore &&
        observation.StoredStateVerifiedAfter &&
        observation.Evidence.EtwIntegrityComplete &&
        observation.Evidence.EtwLostEventCount == 0 &&
        observation.Placement is { } placement &&
        placement.TargetProcessor == finalist.Processor &&
        placement.ConfirmsRequestedPlacement &&
        observation.InterruptEvidence is { IsrSampleCount: > 0 };

    private async Task<GpuAutoAffinityTrialObservation[]> MeasureCandidateAsync(
        GpuAffinityCandidate candidate,
        string phase,
        int repetitions,
        TimeSpan duration,
        GpuBenchmarkEvidence reference,
        Func<int> nextRunNumber,
        List<GpuAutoAffinityTrialReport> trialReports,
        CancellationToken cancellationToken)
    {
        var experimentId = await backend.ApplyCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
        var ownsMutation = experimentId != Guid.Empty;

        try
        {
            if (!await backend.VerifyCandidateStateAsync(experimentId, candidate, cancellationToken).ConfigureAwait(false))
            {
                throw new SessionAbortException(
                    $"{phase}: candidate {candidate.Processor} was not verified before measurement.");
            }

            await CaptureAcceptedAsync(
                nextRunNumber,
                $"{phase}-warmup",
                GpuConfirmationOrder.Candidate,
                candidate,
                TransitionWarmupDuration,
                reference: null,
                experimentId,
                trialReports,
                cancellationToken).ConfigureAwait(false);

            var observations = new GpuAutoAffinityTrialObservation[repetitions];
            for (var pass = 0; pass < repetitions; pass++)
            {
                observations[pass] = await CaptureAcceptedAsync(
                    nextRunNumber,
                    phase,
                    GpuConfirmationOrder.Candidate,
                    candidate,
                    duration,
                    reference,
                    experimentId,
                    trialReports,
                    cancellationToken).ConfigureAwait(false);
            }

            if (ownsMutation)
            {
                await RollbackAndVerifyOriginalAsync(experimentId, phase, candidate).ConfigureAwait(false);
            }
            else if (!await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"{phase}: a no-write candidate no longer matches the captured original state.");
            }
            return observations;
        }
        catch (Exception failure)
        {
            try
            {
                if (ownsMutation)
                {
                    await RollbackAndVerifyOriginalAsync(experimentId, phase, candidate).ConfigureAwait(false);
                }
                else if (!await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    throw new InvalidOperationException($"{phase}: a no-write candidate failed and the captured original state is no longer present.");
                }
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(
                    $"{phase}: candidate {candidate.Processor} failed and exact rollback also failed.",
                    failure,
                    rollbackFailure);
            }

            throw;
        }
    }

    private async Task RollbackAndVerifyOriginalAsync(
        Guid experimentId,
        string phase,
        GpuAffinityCandidate candidate)
    {
        await backend.RollbackAsync(experimentId, CancellationToken.None).ConfigureAwait(false);
        if (!await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"{phase}: exact original GPU affinity state was not verified after rolling back {candidate.Processor}.");
        }
    }

    private async Task<GpuAutoAffinityTrialObservation> CaptureAcceptedAsync(
        Func<int> nextRunNumber,
        string phase,
        GpuConfirmationOrder role,
        GpuAffinityCandidate? candidate,
        TimeSpan duration,
        GpuBenchmarkEvidence? reference,
        Guid? experimentId,
        List<GpuAutoAffinityTrialReport> trialReports,
        CancellationToken cancellationToken)
    {
        for (var retryAttempt = 0; retryAttempt <= 1; retryAttempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trialRequest = new GpuAutoAffinityTrialRequest(
                nextRunNumber(),
                phase,
                role,
                candidate,
                duration,
                retryAttempt);
            var observation = role == GpuConfirmationOrder.Original
                ? await backend.CaptureOriginalAsync(trialRequest, cancellationToken).ConfigureAwait(false)
                : await backend.CaptureCandidateAsync(
                    experimentId ?? throw new InvalidOperationException("Candidate capture requires an owned experiment."),
                    trialRequest,
                    cancellationToken).ConfigureAwait(false);
            var readiness = EvaluateObservation(reference, observation, candidate, retryAttempt);
            trialReports.Add(ToTrialReport(trialRequest, observation, readiness));

            if (readiness.State == GpuBenchmarkReadinessState.Ready)
            {
                return observation;
            }

            if (readiness.State == GpuBenchmarkReadinessState.RetryableContamination && retryAttempt == 0)
            {
                continue;
            }

            if (readiness.State == GpuBenchmarkReadinessState.Inconclusive &&
                retryAttempt == 0 &&
                IsTransientCollectorFailure(readiness.Reasons))
            {
                continue;
            }

            throw new SessionAbortException(
                $"{phase} run {trialRequest.RunNumber} is not decision-grade: {string.Join(" ", readiness.Reasons)}");
        }

        throw new InvalidOperationException("GPU benchmark retry loop exited without a terminal result.");
    }

    private static bool IsTransientCollectorFailure(IReadOnlyList<string> reasons) =>
        reasons.Any(static reason =>
            reason.Contains("benchmark evidence is invalid", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("frame period", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("device reset", StringComparison.OrdinalIgnoreCase));

    private Task PublishCandidateReportAsync(GpuAutoAffinityCandidateReport report) =>
        observer is null ? Task.CompletedTask : observer.CandidateEvaluatedAsync(report);

    private static GpuBenchmarkReadinessResult EvaluateObservation(
        GpuBenchmarkEvidence? reference,
        GpuAutoAffinityTrialObservation observation,
        GpuAffinityCandidate? candidate,
        int retryAttempt)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var reasons = new List<string>();
        var context = new List<string>();

        if (!observation.StoredStateVerifiedBefore || !observation.StoredStateVerifiedAfter)
        {
            reasons.Add("Expected stored GPU affinity state was not verified before and after the trial.");
        }

        if (candidate is not null)
        {
            switch (ClassifyPlacement(observation, candidate))
            {
                case PlacementEvidenceState.Contradicted:
                    reasons.Add("Kernel ETW contradicts the requested GPU ISR placement: attributable ISR work was observed on a different processor.");
                    break;
                case PlacementEvidenceState.Unknown:
                    context.Add("Runtime ISR placement is Unknown for this screening trial because resolved single-adapter ISR placement evidence is unavailable; absence of attributable ISR samples is not treated as proof of off-target placement.");
                    break;
            }
        }

        var interpretation = GpuBenchmarkEvidenceInterpreter.Interpret(observation.Evidence);
        if (!interpretation.IsValid)
        {
            reasons.AddRange(interpretation.ValidityReasons);
        }

        if (reasons.Count > 0)
        {
            return new GpuBenchmarkReadinessResult(
                GpuBenchmarkEvidence.MethodIdValue,
                GpuBenchmarkReadinessState.Inconclusive,
                reasons.AsReadOnly(),
                context.AsReadOnly());
        }

        if (reference is null)
        {
            return new GpuBenchmarkReadinessResult(
                GpuBenchmarkEvidence.MethodIdValue,
                GpuBenchmarkReadinessState.Ready,
                [],
                context.AsReadOnly());
        }

        var contamination = observation.Contamination with { RetryAttempt = retryAttempt };
        var readiness = GpuBenchmarkReadiness.Evaluate(reference, observation.Evidence, contamination);
        return readiness with { Context = readiness.Context.Concat(context).ToArray() };
    }

    private static PlacementEvidenceState ClassifyPlacement(
        GpuAutoAffinityTrialObservation observation,
        GpuAffinityCandidate candidate)
    {
        if (!observation.Evidence.EtwIntegrityComplete || observation.Evidence.EtwLostEventCount != 0 ||
            observation.Placement is null)
        {
            return PlacementEvidenceState.Unknown;
        }

        var placement = observation.Placement;
        if (placement.TargetProcessor != candidate.Processor || placement.OffTargetIsrEventCount > 0)
        {
            return PlacementEvidenceState.Contradicted;
        }

        return placement.TargetIsrEventCount > 0
            ? PlacementEvidenceState.Verified
            : PlacementEvidenceState.Unknown;
    }

    private static CandidateEvaluation CreateEvaluation(
        GpuAffinityCandidate candidate,
        GpuAutoAffinityTrialObservation[] observations)
    {
        if (observations.Length == 0)
        {
            return CandidateEvaluation.Unrankable(candidate, "No scored benchmark observations were captured.");
        }
        var stats = observations
            .Select(static observation => GpuBenchmarkEvidenceInterpreter.Interpret(observation.Evidence).VideoStats)
            .ToArray();
        if (stats.Any(static item => item is null))
        {
            return CandidateEvaluation.Unrankable(candidate,
                "The controlled benchmark did not provide complete AVG / 1% low / 0.1% low frame-period statistics.", observations.Length);
        }
        var values = stats.Select(static item => item!).ToArray();
        if (!HasFiniteRankingStatistics(values))
        {
            return CandidateEvaluation.Unrankable(candidate, "Benchmark ranking statistics are missing or non-finite.", observations.Length);
        }

        GpuBenchmarkVideoStats[] selectedValues;
        GpuAutoAffinityTrialObservation[] selectedObservations;
        var usedNoiseAwareFallback = false;
        if (observations.Length == 1)
        {
            selectedValues = values;
            selectedObservations = observations;
        }
        else
        {
            var cluster = GpuRepeatabilityClusterSelector.Select(values.Select(static item => item.Low1PctFps).ToArray());
            if (cluster is null)
            {
                if (observations.Length < GpuRepeatabilityClusterSelector.MaximumAttemptCount)
                {
                    return CandidateEvaluation.Unrankable(candidate, BuildNoStableClusterReason(observations.Length), observations.Length);
                }

                // Four valid scored runs are evidence, even when they do not fit
                // the preferred ±3% cluster. Keep them all and make the observed
                // variance part of the decision threshold instead of aborting.
                selectedValues = values;
                selectedObservations = observations;
                usedNoiseAwareFallback = true;
            }
            else
            {
                if (observations.Length - cluster.Indexes.Length > MaximumRejectedRepeatabilityRuns)
                {
                    return CandidateEvaluation.Unrankable(
                        candidate,
                        $"Repeatability rejected {observations.Length - cluster.Indexes.Length} of {observations.Length} scored observations; automatic ranking allows at most {MaximumRejectedRepeatabilityRuns} rejected run.",
                        observations.Length);
                }
                selectedValues = cluster.Indexes.Select(index => values[index]).ToArray();
                selectedObservations = cluster.Indexes.Select(index => observations[index]).ToArray();
            }
        }

        var medianLow1 = Median(selectedValues.Select(static item => item.Low1PctFps));
        var medianLow01 = Median(selectedValues.Select(static item => item.Low01PctFps));
        var medianAvg = Median(selectedValues.Select(static item => item.AvgFps));
        var medianP99 = Median(selectedValues.Select(static item => item.P99Milliseconds));
        return new CandidateEvaluation(
            candidate, selectedObservations,
            medianLow1,
            medianLow01,
            medianAvg,
            medianP99,
            RelativeNoise(selectedValues.Select(static item => item.Low1PctFps), medianLow1),
            RelativeNoise(selectedValues.Select(static item => item.Low01PctFps), medianLow01),
            RelativeNoise(selectedValues.Select(static item => item.AvgFps), medianAvg),
            RelativeNoise(selectedValues.Select(static item => item.P99Milliseconds), medianP99),
            selectedObservations.Length,
            observations.Length,
            true,
            usedNoiseAwareFallback,
            null);
    }

    private static OriginalEvaluation CreateOriginalEvaluation(GpuAutoAffinityTrialObservation[] observations)
    {
        if (observations.Length == 0)
        {
            return OriginalEvaluation.Unrankable("No scored Original observations were captured.");
        }
        var stats = observations
            .Select(static observation => GpuBenchmarkEvidenceInterpreter.Interpret(observation.Evidence).VideoStats)
            .ToArray();
        if (stats.Any(static item => item is null))
        {
            return OriginalEvaluation.Unrankable("Original benchmark lacks complete frame-period statistics.", observations.Length);
        }
        var values = stats.Select(static item => item!).ToArray();
        if (!HasFiniteRankingStatistics(values))
        {
            return OriginalEvaluation.Unrankable("Original benchmark ranking statistics are missing or non-finite.", observations.Length);
        }
        var cluster = GpuRepeatabilityClusterSelector.Select(values.Select(static item => item.Low1PctFps).ToArray());
        GpuBenchmarkVideoStats[] selected;
        GpuAutoAffinityTrialObservation[] selectedObservations;
        var usedNoiseAwareFallback = false;
        if (cluster is null)
        {
            if (observations.Length < GpuRepeatabilityClusterSelector.MaximumAttemptCount)
            {
                return OriginalEvaluation.Unrankable(BuildNoStableClusterReason(observations.Length), observations.Length);
            }

            selected = values;
            selectedObservations = observations;
            usedNoiseAwareFallback = true;
        }
        else
        {
            if (observations.Length - cluster.Indexes.Length > MaximumRejectedRepeatabilityRuns)
            {
                return OriginalEvaluation.Unrankable(
                    $"Repeatability rejected {observations.Length - cluster.Indexes.Length} of {observations.Length} Original observations; automatic ranking allows at most {MaximumRejectedRepeatabilityRuns} rejected run.",
                    observations.Length);
            }
            selected = cluster.Indexes.Select(index => values[index]).ToArray();
            selectedObservations = cluster.Indexes.Select(index => observations[index]).ToArray();
        }

        var medianLow1 = Median(selected.Select(static item => item.Low1PctFps));
        var medianLow01 = Median(selected.Select(static item => item.Low01PctFps));
        var medianAvg = Median(selected.Select(static item => item.AvgFps));
        var medianP99 = Median(selected.Select(static item => item.P99Milliseconds));
        return new OriginalEvaluation(
            selectedObservations,
            medianLow1,
            medianLow01,
            medianAvg,
            medianP99,
            RelativeNoise(selected.Select(static item => item.Low1PctFps), medianLow1),
            RelativeNoise(selected.Select(static item => item.Low01PctFps), medianLow01),
            RelativeNoise(selected.Select(static item => item.AvgFps), medianAvg),
            RelativeNoise(selected.Select(static item => item.P99Milliseconds), medianP99),
            selectedObservations.Length,
            observations.Length,
            true,
            usedNoiseAwareFallback,
            null);
    }

    private static bool HasFiniteRankingStatistics(IEnumerable<GpuBenchmarkVideoStats> values) =>
        values.All(static item =>
            double.IsFinite(item.Low1PctFps) && item.Low1PctFps > 0 &&
            double.IsFinite(item.Low01PctFps) && item.Low01PctFps > 0 &&
            double.IsFinite(item.AvgFps) && item.AvgFps > 0 &&
            double.IsFinite(item.P99Milliseconds) && item.P99Milliseconds > 0);

    private static string BuildNoStableClusterReason(int observationCount) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"No stable {GpuRepeatabilityClusterSelector.RequiredRunCount}-run 1% low cluster exists within ±{GpuRepeatabilityClusterSelector.RelativeTolerance:P0} after {observationCount} scored observation(s)." );

    private static bool IsControlComparableToOriginal(
        OriginalEvaluation original,
        GpuAutoAffinityTrialObservation control,
        string phase,
        out string reason)
    {
        var video = GpuBenchmarkEvidenceInterpreter.Interpret(control.Evidence).VideoStats;
        if (video is null || !HasFiniteRankingStatistics([video]))
        {
            reason = $"The Original control {phase} did not provide complete valid ranking statistics.";
            return false;
        }

        var allowedPrimaryDrift = Math.Max(
            GpuRepeatabilityClusterSelector.RelativeTolerance,
            original.PrimaryRelativeNoise);
        var allowedAvgDrift = Math.Max(
            GpuRepeatabilityClusterSelector.RelativeTolerance,
            original.AvgRelativeNoise);
        var allowedFrameP99Drift = Math.Max(
            GpuRepeatabilityClusterSelector.RelativeTolerance,
            original.FrameP99RelativeNoise);
        var low1Drift = RelativeDifference(video.Low1PctFps, original.MedianLow1Fps);
        var avgDrift = RelativeDifference(video.AvgFps, original.MedianAvgFps);
        var frameP99Drift = RelativeDifference(video.P99Milliseconds, original.MedianFrameP99Milliseconds);
        if (low1Drift > allowedPrimaryDrift ||
            avgDrift > allowedAvgDrift ||
            frameP99Drift > allowedFrameP99Drift)
        {
            reason = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Original control drifted {phase}: 1% low {low1Drift:P2} (allowed {allowedPrimaryDrift:P2}), AVG {avgDrift:P2} (allowed {allowedAvgDrift:P2}), frame-p99 {frameP99Drift:P2} (allowed {allowedFrameP99Drift:P2}).");
            return false;
        }

        reason = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Original control remained comparable {phase}: 1% low {low1Drift:P2}/{allowedPrimaryDrift:P2}, AVG {avgDrift:P2}/{allowedAvgDrift:P2}, frame-p99 {frameP99Drift:P2}/{allowedFrameP99Drift:P2}.");
        return true;
    }

    private static double RelativeDifference(double left, double right) =>
        Math.Abs(left - right) / Math.Max(Math.Abs(right), double.Epsilon);

    private static bool IsMeasurablyBetterThanOriginal(
        OriginalEvaluation original,
        CandidateEvaluation candidate,
        out string reason)
    {
        var noiseFloor = Math.Max(
            CandidateMetricEquivalenceTolerance,
            Math.Max(original.PrimaryRelativeNoise, candidate.PrimaryRelativeNoise));
        var primaryGain = (candidate.MedianLow1Fps - original.MedianLow1Fps) / original.MedianLow1Fps;
        if (!double.IsFinite(primaryGain) || primaryGain <= noiseFloor)
        {
            reason = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"No measurable improvement over Original: best 1% low gain {primaryGain:P2} does not clear the {noiseFloor:P2} repeatability/noise floor.");
            return false;
        }

        var avgRegression = (original.MedianAvgFps - candidate.MedianAvgFps) / original.MedianAvgFps;
        var frameP99Regression = (candidate.MedianFrameP99Milliseconds - original.MedianFrameP99Milliseconds) / original.MedianFrameP99Milliseconds;
        var rareTailRegression = (original.MedianLow01Fps - candidate.MedianLow01Fps) / original.MedianLow01Fps;
        var allowedAvgRegression = Math.Max(
            CandidateMetricEquivalenceTolerance,
            Math.Max(original.AvgRelativeNoise, candidate.AvgRelativeNoise));
        var allowedFrameP99Regression = Math.Max(
            CandidateMetricEquivalenceTolerance,
            Math.Max(original.FrameP99RelativeNoise, candidate.FrameP99RelativeNoise));
        var allowedRareTailRegression = Math.Max(
            RareTailEquivalenceTolerance,
            Math.Max(original.Low01RelativeNoise, candidate.Low01RelativeNoise));
        if (avgRegression > allowedAvgRegression ||
            frameP99Regression > allowedFrameP99Regression ||
            rareTailRegression > allowedRareTailRegression)
        {
            reason = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Best forced affinity improves 1% low but regresses a noise-aware guardrail (AVG {avgRegression:P2}/{allowedAvgRegression:P2}, frame-p99 {frameP99Regression:P2}/{allowedFrameP99Regression:P2}, 0.1% low {rareTailRegression:P2}/{allowedRareTailRegression:P2}).");
            return false;
        }

        if (HasMaterialInterruptTailRegression(original, candidate, out var interruptReason))
        {
            reason = interruptReason;
            return false;
        }

        reason = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Measured 1% low improvement {primaryGain:P2} clears the {noiseFloor:P2} repeatability/noise floor without a material guardrail regression.");
        return true;
    }

    private static bool HasMaterialInterruptTailRegression(
        OriginalEvaluation original,
        CandidateEvaluation candidate,
        out string reason)
    {
        var regressions = new List<string>(2);
        CompareInterruptTail(
            "GPU-driver DPC p99",
            original.Observations,
            candidate.Observations,
            static observation => observation.GpuDriverDpcDurationMicroseconds,
            regressions);
        CompareInterruptTail(
            "GPU-driver ISR p99",
            original.Observations,
            candidate.Observations,
            static observation => observation.GpuDriverIsrDurationMicroseconds,
            regressions);

        if (regressions.Count == 0)
        {
            reason = string.Empty;
            return false;
        }

        reason = "Best forced affinity regresses measured interrupt-tail guardrails: " +
            string.Join("; ", regressions) + ".";
        return true;
    }

    private static void CompareInterruptTail(
        string label,
        IReadOnlyList<GpuAutoAffinityTrialObservation> original,
        IReadOnlyList<GpuAutoAffinityTrialObservation> candidate,
        Func<GpuAutoAffinityTrialObservation, IReadOnlyList<double>> selector,
        List<string> regressions)
    {
        var originalRuns = BuildInterruptTailRunSeries(original, selector);
        var candidateRuns = BuildInterruptTailRunSeries(candidate, selector);
        if (originalRuns.Length < MinimumInterruptTailRuns ||
            candidateRuns.Length < MinimumInterruptTailRuns)
        {
            return;
        }

        var originalMedian = Median(originalRuns);
        var candidateMedian = Median(candidateRuns);
        if (!double.IsFinite(originalMedian) || !double.IsFinite(candidateMedian) || originalMedian <= 0d)
        {
            return;
        }

        var originalNoise = MaximumRelativeDeviation(originalRuns, originalMedian);
        var candidateNoise = MaximumRelativeDeviation(candidateRuns, candidateMedian);
        var allowedRegression = Math.Max(
            InterruptTailRegressionTolerance,
            Math.Max(originalNoise, candidateNoise));
        var regression = (candidateMedian - originalMedian) / originalMedian;
        if (regression > allowedRegression)
        {
            regressions.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{label} median {originalMedian:F2} → {candidateMedian:F2} µs ({regression:P1}; noise-aware limit {allowedRegression:P1})"));
        }
    }

    private static double[] BuildInterruptTailRunSeries(
        IEnumerable<GpuAutoAffinityTrialObservation> observations,
        Func<GpuAutoAffinityTrialObservation, IReadOnlyList<double>> selector) =>
        observations
            .Select(selector)
            .Where(static samples =>
                samples.Count >= MinimumInterruptTailSamples &&
                samples.All(static value => double.IsFinite(value) && value >= 0d))
            .Select(static samples => Percentiles.Calculate(samples, 0.99))
            .Where(static value => double.IsFinite(value) && value > 0d)
            .ToArray();

    private static double MaximumRelativeDeviation(IEnumerable<double> values, double median) =>
        values.Max(value => Math.Abs(value - median) / Math.Abs(median));

    private static double RelativeNoise(IEnumerable<double> values, double median) =>
        !double.IsFinite(median) || median <= 0d
            ? double.PositiveInfinity
            : MaximumRelativeDeviation(values, median);

    private static double Median(IEnumerable<double> source)
    {
        var ordered = source.Order().ToArray();
        if (ordered.Length == 0)
        {
            return double.NaN;
        }
        return ordered.Length % 2 == 0
            ? (ordered[(ordered.Length / 2) - 1] + ordered[ordered.Length / 2]) / 2d
            : ordered[ordered.Length / 2];
    }

    private static CandidateEvaluation[] CreateAdaptiveShortlist(
        IEnumerable<CandidateEvaluation> evaluations,
        double screeningNoiseFloor)
    {
        if (!double.IsFinite(screeningNoiseFloor) || screeningNoiseFloor < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(screeningNoiseFloor));
        }
        var primaryOrdered = evaluations
            .Where(static evaluation => evaluation.IsRankable)
            .OrderByDescending(static evaluation => evaluation.MedianLow1Fps)
            .ThenBy(static evaluation => evaluation.Candidate.PhysicalCoreIndex)
            .ThenBy(static evaluation => evaluation.Candidate.Processor.Number)
            .ToArray();
        if (primaryOrdered.Length <= MinimumFinalistCandidates)
        {
            return OrderRankableCandidates(primaryOrdered).ToArray();
        }

        var cutoff = primaryOrdered[MinimumFinalistCandidates - 1].MedianLow1Fps;
        var adaptiveTolerance = Math.Min(
            MaximumShortlistTolerance,
            Math.Max(CandidateMetricEquivalenceTolerance, screeningNoiseFloor));
        return OrderRankableCandidates(primaryOrdered
                .Where((evaluation, index) =>
                    index < MinimumFinalistCandidates ||
                    ArePracticallyEquivalent(
                        evaluation.MedianLow1Fps,
                        cutoff,
                        adaptiveTolerance)))
            .Take(MaximumFinalistCandidates)
            .ToArray();
    }

    private static IEnumerable<CandidateEvaluation> OrderRankableCandidates(
        IEnumerable<CandidateEvaluation> evaluations)
    {
        var remaining = evaluations.Where(static evaluation => evaluation.IsRankable).ToList();
        while (remaining.Count > 0)
        {
            IReadOnlyList<CandidateEvaluation> pool = SelectNearBestHigher(
                remaining, static item => item.MedianLow1Fps, CandidateMetricEquivalenceTolerance);
            pool = SelectNearBestHigher(pool, static item => item.MedianAvgFps, CandidateMetricEquivalenceTolerance);
            pool = SelectNearBestLower(pool, static item => item.MedianFrameP99Milliseconds, CandidateMetricEquivalenceTolerance);
            pool = SelectNearBestHigher(pool, static item => item.MedianLow01Fps, RareTailEquivalenceTolerance);

            var winner = pool
                .OrderBy(static item => item.Candidate.ObservedPressureScore)
                .ThenBy(static item => item.Candidate.PhysicalCoreIndex)
                .ThenBy(static item => item.Candidate.Processor.Number)
                .First();
            yield return winner;
            remaining.Remove(winner);
        }
    }

    private static CandidateEvaluation[] SelectNearBestHigher(
        IReadOnlyList<CandidateEvaluation> source,
        Func<CandidateEvaluation, double> selector,
        double tolerance)
    {
        var best = source.Max(selector);
        return source.Where(item => ArePracticallyEquivalent(selector(item), best, tolerance)).ToArray();
    }

    private static CandidateEvaluation[] SelectNearBestLower(
        IReadOnlyList<CandidateEvaluation> source,
        Func<CandidateEvaluation, double> selector,
        double tolerance)
    {
        var best = source.Min(selector);
        return source.Where(item => ArePracticallyEquivalent(selector(item), best, tolerance)).ToArray();
    }

    private static bool ArePracticallyEquivalent(double left, double right, double tolerance)
    {
        var scale = Math.Max(Math.Abs(left), Math.Abs(right));
        return scale == 0d || Math.Abs(left - right) / scale <= tolerance;
    }

    private static GpuAutoAffinityCandidateReport ToReport(
        string phase,
        CandidateEvaluation evaluation,
        int trialCount) =>
        new(
            phase, evaluation.Candidate.PhysicalCoreIndex, evaluation.Candidate.Processor, trialCount,
            evaluation.IsRankable ? "Ranked" : "Inconclusive",
            RelativeFrameP99Improvement: null,
            RegressedGuardrails: [],
            evaluation.IsRankable
                ? evaluation.TotalObservationCount >= GpuRepeatabilityClusterSelector.RequiredRunCount
                    ? evaluation.UsedNoiseAwareFallback
                        ? string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"Noise-aware {evaluation.TotalObservationCount}-run fallback: no preferred ±{GpuRepeatabilityClusterSelector.RelativeTolerance:P0} 1%-low cluster; all valid runs retained. Observed 1%-low noise {evaluation.PrimaryRelativeNoise:P2}. Median 1% low {evaluation.MedianLow1Fps:F1} FPS; AVG {evaluation.MedianAvgFps:F1} FPS; p99 {evaluation.MedianFrameP99Milliseconds:F2} ms; 0.1% low {evaluation.MedianLow01Fps:F1} FPS.")
                        : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"Stable 1%-low cluster {evaluation.ValidObservationCount}/{evaluation.TotalObservationCount}; max median-centered deviation {evaluation.PrimaryRelativeNoise:P2}. Median 1% low {evaluation.MedianLow1Fps:F1} FPS; AVG {evaluation.MedianAvgFps:F1} FPS; p99 {evaluation.MedianFrameP99Milliseconds:F2} ms; 0.1% low {evaluation.MedianLow01Fps:F1} FPS (rare-tail context).")
                    : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"Single-pass screen. 1% low {evaluation.MedianLow1Fps:F1} FPS; AVG {evaluation.MedianAvgFps:F1} FPS; p99 {evaluation.MedianFrameP99Milliseconds:F2} ms; 0.1% low {evaluation.MedianLow01Fps:F1} FPS (rare-tail context).")
                : evaluation.Reason);

    private static GpuAutoAffinityTrialReport ToTrialReport(
        GpuAutoAffinityTrialRequest request,
        GpuAutoAffinityTrialObservation observation,
        GpuBenchmarkReadinessResult readiness)
    {
        var interpretation = GpuBenchmarkEvidenceInterpreter.Interpret(observation.Evidence);
        var reasons = readiness.Reasons.Concat(readiness.Context).ToArray();
        return new GpuAutoAffinityTrialReport(
            request.RunNumber,
            request.Phase,
            request.Role.ToString(),
            request.Candidate?.Processor,
            observation.Evidence.EtwCaptureId,
            readiness.State.ToString(),
            observation.StoredStateVerifiedBefore,
            observation.StoredStateVerifiedAfter,
            observation.Placement,
            double.IsFinite(interpretation.FrameP99Milliseconds) ? interpretation.FrameP99Milliseconds : null,
            double.IsFinite(interpretation.OnePercentLowFps) ? interpretation.OnePercentLowFps : null,
            request.Duration.TotalMilliseconds,
            observation.Evidence.PresentMonCapture.ActualWindowMilliseconds,
            reasons,
            InterruptEvidence: observation.InterruptEvidence,
            AvgFps: interpretation.VideoStats is { } video && double.IsFinite(video.AvgFps) ? video.AvgFps : null,
            Low01PctFps: interpretation.VideoStats is { } lows && double.IsFinite(lows.Low01PctFps) ? lows.Low01PctFps : null);
    }

    private static GpuAutoAffinitySessionResult CreateResult(
        GpuAutoAffinitySessionRequest request,
        DateTimeOffset startedAtUtc,
        GpuOptimizationRecommendation recommendation,
        GpuAffinityCandidate? finalist,
        bool finalStateVerified,
        bool originalStateRestored,
        List<GpuAutoAffinityCandidateReport> candidateReports,
        List<GpuAutoAffinityTrialReport> trialReports,
        List<string> reasons)
    {
        // Finalist/FinalProcessor means the processor that is actually kept in
        // terminal machine state. Ranked-but-rejected candidates remain in the
        // candidate reports; RestoreOriginal must never advertise one as final.
        var terminalFinalist = recommendation == GpuOptimizationRecommendation.KeepCandidate
            ? finalist
            : null;
        var report = new GpuAutoAffinityReport(
            GpuAutoAffinityReport.SchemaId,
            request.SessionId,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            request.ShuffleSeed,
            candidateReports.AsReadOnly(),
            trialReports.AsReadOnly(),
            recommendation.ToString(),
            terminalFinalist?.Processor,
            finalStateVerified,
            originalStateRestored,
            reasons.AsReadOnly());
        return new GpuAutoAffinitySessionResult(recommendation, terminalFinalist, report);
    }

    private static void ShuffleDeterministically(GpuAffinityCandidate[] candidates, int seed)
    {
        var state = unchecked((uint)seed);
        for (var index = candidates.Length - 1; index > 0; index--)
        {
            state = unchecked((state * 1_664_525u) + 1_013_904_223u);
            var target = (int)(state % (uint)(index + 1));
            (candidates[index], candidates[target]) = (candidates[target], candidates[index]);
        }
    }

    private static void ValidateRequest(GpuAutoAffinitySessionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Topology);
        ArgumentNullException.ThrowIfNull(request.PressureEvidence);
        if (request.SessionId == Guid.Empty)
        {
            throw new ArgumentException("GPU auto-affinity session identity is required.", nameof(request));
        }
        if (request.ScreeningDuration < TimeSpan.FromSeconds(15) ||
            request.ScreeningDuration > TimeSpan.FromSeconds(60))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "GPU candidate screening duration must be from 15 to 60 seconds.");
        }
    }

    private sealed record CandidateEvaluation(
        GpuAffinityCandidate Candidate,
        GpuAutoAffinityTrialObservation[] Observations,
        double MedianLow1Fps,
        double MedianLow01Fps,
        double MedianAvgFps,
        double MedianFrameP99Milliseconds,
        double PrimaryRelativeNoise,
        double Low01RelativeNoise,
        double AvgRelativeNoise,
        double FrameP99RelativeNoise,
        int ValidObservationCount,
        int TotalObservationCount,
        bool IsRankable,
        bool UsedNoiseAwareFallback,
        string? Reason)
    {
        internal static CandidateEvaluation Unrankable(GpuAffinityCandidate candidate, string reason, int totalObservationCount = 0) =>
            new(candidate, [], double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity,
                double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity,
                double.PositiveInfinity, 0, totalObservationCount, false, false, reason);
    }

    private sealed record OriginalEvaluation(
        GpuAutoAffinityTrialObservation[] Observations,
        double MedianLow1Fps,
        double MedianLow01Fps,
        double MedianAvgFps,
        double MedianFrameP99Milliseconds,
        double PrimaryRelativeNoise,
        double Low01RelativeNoise,
        double AvgRelativeNoise,
        double FrameP99RelativeNoise,
        int ValidObservationCount,
        int TotalObservationCount,
        bool IsRankable,
        bool UsedNoiseAwareFallback,
        string? Reason)
    {
        internal static OriginalEvaluation Unrankable(string reason, int totalObservationCount = 0) =>
            new([], double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity,
                double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity,
                double.PositiveInfinity, 0, totalObservationCount, false, false, reason);
    }

    private enum PlacementEvidenceState
    {
        Unknown,
        Verified,
        Contradicted,
    }

    private sealed class SessionAbortException(string message) : Exception(message);
}

using LatencyPilot.Benchmarking.Candidates;
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
/// Measurement-first GPU interrupt-affinity search. Every eligible physical core in
/// the supported processor group receives a scored screen. Original is scored with
/// the same workload and duration, and a forced candidate is retained only when its
/// repeatable improvement clears the measured/practical noise floor without material
/// guardrail regression. Ranking applies tolerances against fixed best references,
/// never a pairwise fuzzy comparer. Final Keep additionally requires ETW-backed
/// target-only runtime ISR placement proof.
/// </summary>
public sealed class GpuAutoAffinitySession
{
    private const double MaximumRunToRunPrimaryDrift = 0.05;
    private const double CandidateMetricEquivalenceTolerance = 0.01;
    private const double RareTailEquivalenceTolerance = 0.05;
    private const int MinimumFinalistCandidates = 3;
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
        var physicalCandidates = GpuAffinityCandidatePlanner
            .Create(request.Topology, pressure, request.CpuSets)
            .ToArray();

        if (physicalCandidates.Length == 0)
        {
            reasons.Add("No eligible physical-core GPU interrupt-affinity candidate is available.");
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

        ShuffleDeterministically(physicalCandidates, request.ShuffleSeed);

        try
        {
            // Establish continuity once, then score the exact original state three
            // times under the same frozen workload/duration used for candidates.
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
            var originalObservations = new GpuAutoAffinityTrialObservation[3];
            for (var pass = 0; pass < originalObservations.Length; pass++)
            {
                originalObservations[pass] = await CaptureAcceptedAsync(
                    () => ++nextRunNumber,
                    "screening-original",
                    GpuConfirmationOrder.Original,
                    null,
                    request.ScreeningDuration,
                    reference,
                    experimentId: null,
                    trialReports,
                    cancellationToken).ConfigureAwait(false);
            }

            var original = CreateOriginalEvaluation(originalObservations);
            if (!original.IsRankable)
            {
                reasons.Add($"Original-state benchmark is not repeatable enough for an automatic Keep decision: {original.Reason}");
                var originalVerified = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    originalVerified, originalVerified, candidateReports, trialReports, reasons);
            }

            var screeningEvaluations = new List<CandidateEvaluation>(physicalCandidates.Length);
            foreach (var candidate in physicalCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
            }

            var finalists = await RescreenTopCandidatesAsync(
                request,
                screeningEvaluations,
                reference,
                candidateReports,
                trialReports,
                () => ++nextRunNumber,
                cancellationToken).ConfigureAwait(false);
            var finalist = SelectBestCandidate(finalists);
            if (finalist is null)
            {
                reasons.Add(
                    "No GPU-affinity candidate remained valid and repeatable after the bounded top-candidate re-test; the exact original state was retained rather than guessing.");
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

            if (!IsMeasurablyBetterThanOriginal(original, finalist, out var comparisonReason))
            {
                reasons.Add(comparisonReason);
                reasons.Add("The exact original state is the successful outcome because the best forced affinity did not demonstrate a repeatable net improvement.");
                var originalVerified = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, finalist.Candidate,
                    originalVerified, originalVerified, candidateReports, trialReports, reasons);
            }

            reasons.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"CPU {finalist.Candidate.Processor.Number} ranked best using fixed-reference practical-equivalence bands. Medians: 1% low {finalist.MedianLow1Fps:F1} FPS; AVG {finalist.MedianAvgFps:F1} FPS; p99 {finalist.MedianFrameP99Milliseconds:F2} ms; 0.1% low {finalist.MedianLow01Fps:F1} FPS."));

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
        IReadOnlyList<CandidateEvaluation> screeningEvaluations,
        GpuBenchmarkEvidence reference,
        List<GpuAutoAffinityCandidateReport> candidateReports,
        List<GpuAutoAffinityTrialReport> trialReports,
        Func<int> nextRunNumber,
        CancellationToken cancellationToken)
    {
        var shortlist = CreateAdaptiveShortlist(screeningEvaluations);
        if (shortlist.Length == 0)
        {
            return [];
        }

        // Each finalist gets two additional measurements, but never back-to-back
        // under one affinity activation. Every round performs a fresh
        // apply/restart/warm-up/score/rollback transition so all three scored
        // observations for a finalist are independent of a single restart.
        // Round order is shuffled deterministically to reduce time/thermal bias
        // without making the evidence irreproducible.
        var freshByProcessor = shortlist.ToDictionary(
            static item => item.Candidate.Processor,
            static _ => new List<GpuAutoAffinityTrialObservation>(capacity: 2));
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

        var finalists = new List<CandidateEvaluation>(shortlist.Length);
        foreach (var screened in shortlist)
        {
            CandidateEvaluation evaluation;
            if (failureByProcessor.TryGetValue(screened.Candidate.Processor, out var failure))
            {
                evaluation = CandidateEvaluation.Unrankable(screened.Candidate, failure);
            }
            else
            {
                var fresh = freshByProcessor[screened.Candidate.Processor];
                var combined = screened.Observations.Concat(fresh).ToArray();
                evaluation = combined.Length == 3
                    ? CreateEvaluation(screened.Candidate, combined)
                    : CandidateEvaluation.Unrankable(
                        screened.Candidate,
                        $"Finalist CPU {screened.Candidate.Processor.Number} produced {combined.Length} scored observations; exactly three transition-isolated observations are required.");
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
            return CandidateEvaluation.Unrankable(
                candidate,
                "The controlled benchmark did not provide complete AVG / 1% low / 0.1% low frame-period statistics.");
        }

        var values = stats.Select(static item => item!).ToArray();
        if (values.Any(static item =>
                !double.IsFinite(item.Low1PctFps) || item.Low1PctFps <= 0 ||
                !double.IsFinite(item.Low01PctFps) || item.Low01PctFps <= 0 ||
                !double.IsFinite(item.AvgFps) || item.AvgFps <= 0 ||
                !double.IsFinite(item.P99Milliseconds) || item.P99Milliseconds <= 0))
        {
            return CandidateEvaluation.Unrankable(candidate, "Benchmark ranking statistics are missing or non-finite.");
        }

        var stabilityReason = EvaluatePrimaryStability(values);
        if (stabilityReason is not null)
        {
            return CandidateEvaluation.Unrankable(candidate, stabilityReason);
        }

        return new CandidateEvaluation(
            candidate,
            observations,
            Median(values.Select(static item => item.Low1PctFps)),
            Median(values.Select(static item => item.Low01PctFps)),
            Median(values.Select(static item => item.AvgFps)),
            Median(values.Select(static item => item.P99Milliseconds)),
            CalculatePrimaryRelativeSpread(values),
            IsRankable: true,
            Reason: null);
    }

    private static string? EvaluatePrimaryStability(GpuBenchmarkVideoStats[] stats)
    {
        var relativeSpread = CalculatePrimaryRelativeSpread(stats);
        return double.IsFinite(relativeSpread) && relativeSpread <= MaximumRunToRunPrimaryDrift
            ? null
            : $"Repeated 1% low drift is {relativeSpread:P1}, exceeding the {MaximumRunToRunPrimaryDrift:P0} repeatability bound.";
    }

    private static double CalculatePrimaryRelativeSpread(GpuBenchmarkVideoStats[] stats)
    {
        if (stats.Length < 2)
        {
            return 0d;
        }

        var lows = stats.Select(static item => item.Low1PctFps).ToArray();
        var minimum = lows.Min();
        var maximum = lows.Max();
        return minimum > 0d ? (maximum - minimum) / minimum : double.PositiveInfinity;
    }

    private static OriginalEvaluation CreateOriginalEvaluation(
        GpuAutoAffinityTrialObservation[] observations)
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
            return OriginalEvaluation.Unrankable("Original benchmark lacks complete frame-period statistics.");
        }

        var values = stats.Select(static item => item!).ToArray();
        if (values.Any(static item =>
                !double.IsFinite(item.Low1PctFps) || item.Low1PctFps <= 0 ||
                !double.IsFinite(item.Low01PctFps) || item.Low01PctFps <= 0 ||
                !double.IsFinite(item.AvgFps) || item.AvgFps <= 0 ||
                !double.IsFinite(item.P99Milliseconds) || item.P99Milliseconds <= 0))
        {
            return OriginalEvaluation.Unrankable("Original benchmark ranking statistics are missing or non-finite.");
        }

        var stabilityReason = EvaluatePrimaryStability(values);
        if (stabilityReason is not null)
        {
            return OriginalEvaluation.Unrankable(stabilityReason);
        }

        return new OriginalEvaluation(
            Median(values.Select(static item => item.Low1PctFps)),
            Median(values.Select(static item => item.Low01PctFps)),
            Median(values.Select(static item => item.AvgFps)),
            Median(values.Select(static item => item.P99Milliseconds)),
            CalculatePrimaryRelativeSpread(values),
            true,
            null);
    }

    private static bool IsMeasurablyBetterThanOriginal(
        OriginalEvaluation original,
        CandidateEvaluation candidate,
        out string reason)
    {
        var noiseFloor = Math.Max(
            CandidateMetricEquivalenceTolerance,
            Math.Max(original.PrimaryRelativeSpread, candidate.PrimaryRelativeSpread));
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
        if (avgRegression > CandidateMetricEquivalenceTolerance ||
            frameP99Regression > CandidateMetricEquivalenceTolerance ||
            rareTailRegression > RareTailEquivalenceTolerance)
        {
            reason = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Best forced affinity improves 1% low but regresses a guardrail beyond tolerance (AVG {avgRegression:P2}, frame-p99 {frameP99Regression:P2}, 0.1% low {rareTailRegression:P2}).");
            return false;
        }

        reason = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Measured 1% low improvement {primaryGain:P2} clears the {noiseFloor:P2} repeatability/noise floor without a material guardrail regression.");
        return true;
    }

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
        IEnumerable<CandidateEvaluation> evaluations)
    {
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
        return OrderRankableCandidates(primaryOrdered
                .Where((evaluation, index) =>
                    index < MinimumFinalistCandidates ||
                    ArePracticallyEquivalent(
                        evaluation.MedianLow1Fps,
                        cutoff,
                        CandidateMetricEquivalenceTolerance)))
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

    private static CandidateEvaluation? SelectBestCandidate(
        IEnumerable<CandidateEvaluation> evaluations) =>
        OrderRankableCandidates(evaluations).FirstOrDefault();

    private static GpuAutoAffinityCandidateReport ToReport(
        string phase,
        CandidateEvaluation evaluation,
        int trialCount) =>
        new(
            phase,
            evaluation.Candidate.PhysicalCoreIndex,
            evaluation.Candidate.Processor,
            trialCount,
            evaluation.IsRankable ? "Ranked" : "Inconclusive",
            RelativeFrameP99Improvement: null,
            RegressedGuardrails: [],
            evaluation.IsRankable
                ? string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Median 1% low {evaluation.MedianLow1Fps:F1} FPS; AVG {evaluation.MedianAvgFps:F1} FPS; p99 {evaluation.MedianFrameP99Milliseconds:F2} ms; 0.1% low {evaluation.MedianLow01Fps:F1} FPS (rare-tail context).")
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
        var report = new GpuAutoAffinityReport(
            GpuAutoAffinityReport.SchemaId,
            request.SessionId,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            request.ShuffleSeed,
            candidateReports.AsReadOnly(),
            trialReports.AsReadOnly(),
            recommendation.ToString(),
            finalist?.Processor,
            finalStateVerified,
            originalStateRestored,
            reasons.AsReadOnly());
        return new GpuAutoAffinitySessionResult(recommendation, finalist, report);
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
        double PrimaryRelativeSpread,
        bool IsRankable,
        string? Reason)
    {
        internal static CandidateEvaluation Unrankable(GpuAffinityCandidate candidate, string reason) =>
            new(candidate, [], double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity, false, reason);
    }

    private sealed record OriginalEvaluation(
        double MedianLow1Fps,
        double MedianLow01Fps,
        double MedianAvgFps,
        double MedianFrameP99Milliseconds,
        double PrimaryRelativeSpread,
        bool IsRankable,
        string? Reason)
    {
        internal static OriginalEvaluation Unrankable(string reason) =>
            new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity, false, reason);
    }

    private enum PlacementEvidenceState
    {
        Unknown,
        Verified,
        Contradicted,
    }

    private sealed class SessionAbortException(string message) : Exception(message);
}

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
/// Bounded v1 GPU interrupt-affinity search. Every eligible physical core gets
/// one scored screen. The best up-to-three get two fresh scored re-tests, then
/// the winner is selected by median 1% low, 0.1% low, AVG FPS and finally p99.
/// Windows default remains the exact recovery/reference state; it is not a
/// minimum-improvement opponent. A winner is kept only after a final ETW-backed
/// runtime ISR placement capture verifies the requested processor.
/// </summary>
public sealed class GpuAutoAffinitySession
{
    private const double MaximumRunToRunPrimaryDrift = 0.20;
    private const int MaximumFinalistCandidates = 3;
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
            // One non-scored original-state block establishes frozen-workload,
            // process, driver and topology continuity. The default state is a
            // safety/reference anchor, not a candidate that must be beaten.
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

            reasons.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"CPU {finalist.Candidate.Processor.Number} ranked best by median 1% low ({finalist.MedianLow1Fps:F1} FPS), then 0.1% low ({finalist.MedianLow01Fps:F1} FPS) and AVG ({finalist.MedianAvgFps:F1} FPS). Frame-p99 ({finalist.MedianFrameP99Milliseconds:F2} ms) is diagnostic/tie context only."));

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
        var shortlist = OrderRankableCandidates(screeningEvaluations)
            .Take(MaximumFinalistCandidates)
            .ToArray();
        if (shortlist.Length == 0)
        {
            return [];
        }

        var finalists = new List<CandidateEvaluation>(shortlist.Length);
        foreach (var screened in shortlist)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var fresh = await MeasureCandidateAsync(
                    screened.Candidate,
                    FinalistPhaseName,
                    repetitions: 2,
                    request.ScreeningDuration,
                    reference,
                    nextRunNumber,
                    trialReports,
                    cancellationToken).ConfigureAwait(false);
                var combined = screened.Observations.Concat(fresh).ToArray();
                var evaluation = CreateEvaluation(screened.Candidate, combined);
                var report = ToReport(FinalistPhaseName, evaluation, fresh.Length);
                candidateReports.Add(report);
                await PublishCandidateReportAsync(report).ConfigureAwait(false);
                finalists.Add(evaluation);
            }
            catch (SessionAbortException abort)
            {
                var evaluation = CandidateEvaluation.Unrankable(screened.Candidate, abort.Message);
                var report = ToReport(FinalistPhaseName, evaluation, 0);
                candidateReports.Add(report);
                await PublishCandidateReportAsync(report).ConfigureAwait(false);
                finalists.Add(evaluation);
            }
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
            if (activeExperiment == Guid.Empty ||
                !await backend.VerifyCandidateStateAsync(activeExperiment.Value, finalist, cancellationToken).ConfigureAwait(false))
            {
                throw new SessionAbortException(
                    $"Final GPU winner CPU {finalist.Processor.Number} could not verify its stored affinity state before runtime placement verification.");
            }

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
        if (experimentId == Guid.Empty)
        {
            throw new SessionAbortException($"{phase}: candidate {candidate.Processor} returned an empty experiment identity.");
        }

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

            await RollbackAndVerifyOriginalAsync(experimentId, phase, candidate).ConfigureAwait(false);
            return observations;
        }
        catch (Exception failure)
        {
            try
            {
                await RollbackAndVerifyOriginalAsync(experimentId, phase, candidate).ConfigureAwait(false);
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

        if (candidate is not null &&
            (observation.Placement is null ||
             observation.Placement.TargetProcessor != candidate.Processor ||
             !observation.Placement.ConfirmsRequestedPlacement))
        {
            if (observation.Evidence.EtwIntegrityComplete)
            {
                reasons.Add("Candidate trial lacks resolved single-adapter ISR placement confined to the requested logical processor.");
            }
            else
            {
                context.Add("Runtime ISR placement is unverified for this screening trial (kernel ETW unavailable); ranking continues from benchmark frame periods under verified stored state.");
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
            IsRankable: true,
            Reason: null);
    }

    private static string? EvaluatePrimaryStability(GpuBenchmarkVideoStats[] stats)
    {
        if (stats.Length < 2)
        {
            return null;
        }

        var lows = stats.Select(static item => item.Low1PctFps).ToArray();
        var minimum = lows.Min();
        var maximum = lows.Max();
        var relativeSpread = (maximum - minimum) / minimum;
        return double.IsFinite(relativeSpread) && relativeSpread <= MaximumRunToRunPrimaryDrift
            ? null
            : $"Candidate repeated 1% low drift is {relativeSpread:P1}, exceeding the {MaximumRunToRunPrimaryDrift:P0} repeatability bound.";
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

    private static IEnumerable<CandidateEvaluation> OrderRankableCandidates(
        IEnumerable<CandidateEvaluation> evaluations) =>
        evaluations
            .Where(static evaluation => evaluation.IsRankable)
            .OrderByDescending(static evaluation => evaluation.MedianLow1Fps)
            .ThenByDescending(static evaluation => evaluation.MedianLow01Fps)
            .ThenByDescending(static evaluation => evaluation.MedianAvgFps)
            .ThenBy(static evaluation => evaluation.MedianFrameP99Milliseconds)
            .ThenBy(static evaluation => evaluation.Candidate.ObservedPressureScore)
            .ThenBy(static evaluation => evaluation.Candidate.PhysicalCoreIndex)
            .ThenBy(static evaluation => evaluation.Candidate.Processor.Number);

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
                    $"Median 1% low {evaluation.MedianLow1Fps:F1} FPS; 0.1% low {evaluation.MedianLow01Fps:F1} FPS; AVG {evaluation.MedianAvgFps:F1} FPS; p99 {evaluation.MedianFrameP99Milliseconds:F2} ms.")
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
        ArgumentNullException.ThrowIfNull(request.Policy);
        request.Policy.Validate();
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
        if (request.ConfirmationDuration < TimeSpan.FromSeconds(30) ||
            request.ConfirmationDuration > TimeSpan.FromSeconds(60))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Legacy confirmation duration must remain from 30 to 60 seconds while older callers migrate to the simplified v1 contract.");
        }
    }

    private sealed record CandidateEvaluation(
        GpuAffinityCandidate Candidate,
        GpuAutoAffinityTrialObservation[] Observations,
        double MedianLow1Fps,
        double MedianLow01Fps,
        double MedianAvgFps,
        double MedianFrameP99Milliseconds,
        bool IsRankable,
        string? Reason)
    {
        internal static CandidateEvaluation Unrankable(GpuAffinityCandidate candidate, string reason) =>
            new(candidate, [], double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, double.PositiveInfinity, false, reason);
    }

    private sealed class SessionAbortException(string message) : Exception(message);
}

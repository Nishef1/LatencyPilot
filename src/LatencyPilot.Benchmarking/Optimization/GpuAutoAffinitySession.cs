using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Metrics;
using LatencyPilot.Core.Results;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed record GpuAutoAffinitySessionRequest(
    Guid SessionId,
    ProcessorTopologySnapshot Topology,
    IEnumerable<ProcessorPressureEvidence> PressureEvidence,
    ProcessorCpuSetSnapshot? CpuSets,
    int ShuffleSeed,
    TimeSpan ScreeningDuration,
    TimeSpan ConfirmationDuration,
    ComparisonPolicy Policy);

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

public sealed class GpuAutoAffinitySession
{
    private const string FramePrimaryMetric = "CPU frame time (ms)";
    private const string DpcGuardrailMetric = "GPU-driver DPC duration (us)";
    private const string IsrGuardrailMetric = "GPU ISR duration (us)";
    private const double MaximumRunToRunFrameP99Drift = 0.20;
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
            var controls = new List<GpuAutoAffinityTrialObservation>(2);

            // Warm the controlled D3D12 workload before decision-grade controls.
            // The same five-second non-scored warmup is used after every affinity
            // transition so scored runs are not the transition itself.
            await CaptureAcceptedAsync(
                () => ++nextRunNumber,
                "screening-warmup",
                GpuConfirmationOrder.Original,
                null,
                TransitionWarmupDuration,
                null,
                null,
                trialReports,
                cancellationToken).ConfigureAwait(false);

            GpuBenchmarkEvidence? reference = null;
            for (var pass = 0; pass < 2; pass++)
            {
                var observation = await CaptureAcceptedAsync(
                    () => ++nextRunNumber,
                    "screening-control",
                    GpuConfirmationOrder.Original,
                    null,
                    request.ScreeningDuration,
                    reference,
                    null,
                    trialReports,
                    cancellationToken).ConfigureAwait(false);
                reference ??= observation.Evidence;
                controls.Add(observation);
            }

            var physicalEvaluations = new List<CandidateEvaluation>(physicalCandidates.Length);
            foreach (var candidate in physicalCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A single flaky screening trial (ETW loss, PresentMon gap,
                // post-restart transient) must mark that CPU unrankable, not
                // abort the remaining 7+ candidates and the whole Gate A run.
                CandidateEvaluation evaluation;
                try
                {
                    var observations = await MeasureCandidateAsync(
                        candidate,
                        "screening",
                        repetitions: 2,
                        request.ScreeningDuration,
                        reference!,
                        () => ++nextRunNumber,
                        trialReports,
                        cancellationToken).ConfigureAwait(false);
                    var comparison = Compare(controls, observations, request.Policy);
                    var report = ToReport("screening", candidate, observations.Length, comparison);
                    candidateReports.Add(report);
                    await PublishCandidateReportAsync(report).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    evaluation = CreateEvaluation(candidate, observations, comparison);
                }
                catch (SessionAbortException abort)
                {
                    var comparison = new ComparisonResult(
                        ExperimentVerdict.Inconclusive, null, null, null, [],
                        $"Screening CPU {candidate.Processor.Number} could not complete a decision-grade trial: {abort.Message}");
                    var report = ToReport("screening", candidate, 0, comparison);
                    candidateReports.Add(report);
                    await PublishCandidateReportAsync(report).ConfigureAwait(false);
                    evaluation = CreateEvaluation(candidate, [], comparison);
                }

                physicalEvaluations.Add(evaluation);
            }

            var physicalFinalist = await RescreenTopCandidatesAsync(
                request,
                physicalEvaluations,
                controls,
                reference!,
                candidateReports,
                trialReports,
                () => ++nextRunNumber,
                cancellationToken).ConfigureAwait(false);
            if (physicalFinalist is null)
            {
                reasons.Add(
                    "No physical-core candidate remained decision-grade and repeatable after the bounded finalist re-screen. The original state was restored rather than guessing.");
                reasons.AddRange(candidateReports.Select(static item =>
                    $"CPU {item.Processor.Number}: {item.Verdict}. {item.Reason}"));
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

            var siblings = GpuAffinityCandidatePlanner.CreateSiblingRefinement(
                request.Topology,
                pressure,
                physicalFinalist.Candidate,
                request.CpuSets).ToArray();
            var refinementEvaluations = new List<CandidateEvaluation>(siblings.Length);
            foreach (var sibling in siblings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CandidateEvaluation evaluation;
                try
                {
                    var observations = await MeasureCandidateAsync(
                        sibling,
                        "smt-refinement",
                        repetitions: 2,
                        request.ScreeningDuration,
                        reference!,
                        () => ++nextRunNumber,
                        trialReports,
                        cancellationToken).ConfigureAwait(false);
                    var comparison = Compare(controls, observations, request.Policy);
                    var report = ToReport("smt-refinement", sibling, observations.Length, comparison);
                    candidateReports.Add(report);
                    await PublishCandidateReportAsync(report).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    evaluation = CreateEvaluation(sibling, observations, comparison);
                }
                catch (SessionAbortException abort)
                {
                    var comparison = new ComparisonResult(
                        ExperimentVerdict.Inconclusive, null, null, null, [],
                        $"SMT refinement {sibling.Processor} could not complete a decision-grade trial: {abort.Message}");
                    var report = ToReport("smt-refinement", sibling, 0, comparison);
                    candidateReports.Add(report);
                    await PublishCandidateReportAsync(report).ConfigureAwait(false);
                    evaluation = CreateEvaluation(sibling, [], comparison);
                }

                refinementEvaluations.Add(evaluation);
            }

            var refinedFinalist = SelectBestCandidate(refinementEvaluations) ?? physicalFinalist;
            reasons.Add(
                $"CPU {refinedFinalist.Candidate.Processor.Number} ranked best among the repeatable GPU-affinity candidates by median frame-p99; the original/default state is reference context rather than a winner gate.");
            return await ConfirmFinalistAsync(
                request,
                startedAtUtc,
                refinedFinalist.Candidate,
                reference!,
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
                reasons.Add("The original GPU interrupt-affinity state could not be verified after the session aborted.");
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

    private async Task<CandidateEvaluation?> RescreenTopCandidatesAsync(
        GpuAutoAffinitySessionRequest request,
        List<CandidateEvaluation> physicalEvaluations,
        List<GpuAutoAffinityTrialObservation> controls,
        GpuBenchmarkEvidence reference,
        List<GpuAutoAffinityCandidateReport> candidateReports,
        List<GpuAutoAffinityTrialReport> trialReports,
        Func<int> nextRunNumber,
        CancellationToken cancellationToken)
    {
        var shortlist = OrderRankableCandidates(physicalEvaluations)
            .Take(MaximumFinalistCandidates)
            .ToArray();
        if (shortlist.Length == 0)
        {
            return null;
        }

        var finalistEvaluations = new List<CandidateEvaluation>(shortlist.Length);
        foreach (var shortlisted in shortlist)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CandidateEvaluation evaluation;
            try
            {
                var observations = await MeasureCandidateAsync(
                    shortlisted.Candidate,
                    FinalistPhaseName,
                    repetitions: 2,
                    request.ScreeningDuration,
                    reference,
                    nextRunNumber,
                    trialReports,
                    cancellationToken).ConfigureAwait(false);
                var comparison = Compare(controls, observations, request.Policy);
                var report = ToReport(FinalistPhaseName, shortlisted.Candidate, observations.Length, comparison);
                candidateReports.Add(report);
                await PublishCandidateReportAsync(report).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                evaluation = CreateEvaluation(shortlisted.Candidate, observations, comparison);
            }
            catch (SessionAbortException abort)
            {
                var comparison = new ComparisonResult(
                    ExperimentVerdict.Inconclusive, null, null, null, [],
                    $"Finalist re-screen CPU {shortlisted.Candidate.Processor.Number} could not complete a decision-grade trial: {abort.Message}");
                var report = ToReport(FinalistPhaseName, shortlisted.Candidate, 0, comparison);
                candidateReports.Add(report);
                await PublishCandidateReportAsync(report).ConfigureAwait(false);
                evaluation = CreateEvaluation(shortlisted.Candidate, [], comparison);
            }

            finalistEvaluations.Add(evaluation);
        }

        return SelectBestCandidate(finalistEvaluations);
    }

    private async Task<GpuAutoAffinitySessionResult> ConfirmFinalistAsync(
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
        var originalRuns = new List<GpuAutoAffinityTrialObservation>(4);
        var candidateRuns = new List<GpuAutoAffinityTrialObservation>(4);
        Guid? activeExperiment = null;

        try
        {
            var schedule = GpuOptimizationDecisionEngine.CreateBalancedConfirmationSchedule();
            for (var index = 0; index < schedule.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var role = schedule[index];
                if (role == GpuConfirmationOrder.Original)
                {
                    if (!await backend.VerifyOriginalStateAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new SessionAbortException(
                            $"Confirmation run {index + 1} could not verify the exact original GPU affinity state.");
                    }

                    await CaptureAcceptedAsync(
                        nextRunNumber,
                        "confirmation-warmup",
                        role,
                        null,
                        TransitionWarmupDuration,
                        null,
                        null,
                        trialReports,
                        cancellationToken).ConfigureAwait(false);

                    originalRuns.Add(await CaptureAcceptedAsync(
                        nextRunNumber,
                        "confirmation",
                        role,
                        null,
                        request.ConfirmationDuration,
                        reference,
                        null,
                        trialReports,
                        cancellationToken).ConfigureAwait(false));
                    continue;
                }

                activeExperiment = await backend.ApplyCandidateAsync(finalist, cancellationToken).ConfigureAwait(false);
                if (activeExperiment == Guid.Empty ||
                    !await backend.VerifyCandidateStateAsync(
                        activeExperiment.Value,
                        finalist,
                        cancellationToken).ConfigureAwait(false))
                {
                    throw new SessionAbortException(
                        $"Confirmation run {index + 1} could not verify the requested candidate state before measurement.");
                }

                await CaptureAcceptedAsync(
                    nextRunNumber,
                    "confirmation-warmup",
                    role,
                    finalist,
                    TransitionWarmupDuration,
                    null,
                    activeExperiment,
                    trialReports,
                    cancellationToken).ConfigureAwait(false);

                candidateRuns.Add(await CaptureAcceptedAsync(
                    nextRunNumber,
                    "confirmation",
                    role,
                    finalist,
                    request.ConfirmationDuration,
                    reference,
                    activeExperiment,
                    trialReports,
                    cancellationToken).ConfigureAwait(false));

                var isFinalRun = index == schedule.Count - 1;
                if (!isFinalRun)
                {
                    await backend.RollbackAsync(activeExperiment.Value, CancellationToken.None).ConfigureAwait(false);
                    activeExperiment = null;
                    if (!await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false))
                    {
                        throw new SessionAbortException(
                            $"Confirmation run {index + 1} did not restore the exact original state.");
                    }
                }
            }

            if (activeExperiment is null)
            {
                throw new SessionAbortException(
                    "Balanced confirmation ended without an owned finalist experiment for the final decision.");
            }

            var comparison = Compare(originalRuns, candidateRuns, request.Policy);
            var report = ToReport("confirmation", finalist, candidateRuns.Count, comparison);
            candidateReports.Add(report);
            await PublishCandidateReportAsync(report).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var finalistStability = EvaluateRunStability("Finalist", candidateRuns.ToArray());
            if (finalistStability is null &&
                comparison.Verdict != ExperimentVerdict.Inconclusive &&
                comparison.Verdict != ExperimentVerdict.Regressed)
            {
                var keptExperimentId = activeExperiment.Value;
                await backend.KeepAsync(keptExperimentId, CancellationToken.None).ConfigureAwait(false);
                activeExperiment = null;

                reasons.Add(
                    "Balanced ABBA + BAAB confirmation kept the ranked finalist because its repeated candidate measurements remained decision-grade and repeatable without regressing against the original state. The original comparison is retained as context, not as a minimum-improvement gate.");
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

            await backend.RollbackAsync(activeExperiment.Value, CancellationToken.None).ConfigureAwait(false);
            activeExperiment = null;
            var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
            reasons.Add(finalistStability is not null
                ? $"Ranked finalist confirmation was not repeatable: {finalistStability.Reason}"
                : $"Ranked finalist confirmation was not decision-grade: {comparison.Reason}");
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
                        "GPU auto-affinity confirmation rollback completed, but the exact original state could not be verified.");
                }
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(
                    "GPU auto-affinity confirmation failed and exact rollback also failed.",
                    failure,
                    rollbackFailure);
            }

            throw;
        }
    }

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

        GpuAutoAffinityTrialObservation[] observations;
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
                null,
                experimentId,
                trialReports,
                cancellationToken).ConfigureAwait(false);

            observations = new GpuAutoAffinityTrialObservation[repetitions];
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

        await RollbackAndVerifyOriginalAsync(experimentId, phase, candidate).ConfigureAwait(false);
        return observations;
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

            // Transient collector faults (ETW loss, PresentMon gap, overlap
            // miss after a restart) deserve the same single bounded retry as
            // control contamination. Persistent stored-state, placement, or
            // identity failures stay Inconclusive without a retry.
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

    private static bool IsTransientCollectorFailure(IReadOnlyList<string> reasons)
    {
        foreach (var reason in reasons)
        {
            if (reason.Contains("ETW", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("PresentMon", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("overlap", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("cover at least", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("frame-p99 evidence is incomplete", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("benchmark evidence is invalid", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private Task PublishCandidateReportAsync(GpuAutoAffinityCandidateReport report) =>
        observer is null
            ? Task.CompletedTask
            : observer.CandidateEvaluatedAsync(report);

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
                // ETW was healthy, so attribution was attempted and placement
                // genuinely failed: the trial cannot prove the candidate ran
                // under the requested affinity.
                reasons.Add("Candidate trial lacks resolved single-adapter ISR placement confined to the requested logical processor.");
            }
            else
            {
                // No ETW means no placement proof either way; ranking still
                // uses benchmark frame periods under verified stored state.
                context.Add("Runtime ISR placement is unverified for this trial (kernel ETW unavailable); stored affinity state was verified instead.");
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

    private static ComparisonResult Compare(
        IEnumerable<GpuAutoAffinityTrialObservation> originals,
        IEnumerable<GpuAutoAffinityTrialObservation> candidates,
        ComparisonPolicy policy)
    {
        var originalTrials = originals.ToArray();
        var candidateTrials = candidates.ToArray();
        var comparabilityNotes = new List<string>();
        var attributions = originalTrials.Concat(candidateTrials)
            .Select(static trial => trial.InterruptEvidence).ToArray();
        var distinctSources = attributions
            .Where(static item => item is not null)
            .Select(static item => (item!.IsrModuleName, item.IsrAttributionMode))
            .Distinct()
            .Count();
        if (distinctSources > 1)
        {
            // ETW was healthy on both sides but attributes ISR execution to
            // different modules: genuinely incomparable, not missing data.
            return new ComparisonResult(ExperimentVerdict.Inconclusive, null, null, null, [],
                "GPU ISR attribution changed between trials; different ISR sources cannot be compared.");
        }
        var compareDriverGuardrails = distinctSources == 1;
        if (distinctSources == 0)
        {
            // No ISR attribution anywhere (ETW unavailable): drop only the
            // driver-duration guardrails and still compare frame periods.
            comparabilityNotes.Add(
                "GPU ISR attribution is unavailable; driver DPC/ISR guardrails were excluded while frame periods were still compared.");
        }

        var originalStability = EvaluateRunStability("Original", originalTrials);
        if (originalStability is not null)
        {
            return originalStability;
        }

        var candidateStability = EvaluateRunStability("Candidate", candidateTrials);
        if (candidateStability is not null)
        {
            return candidateStability;
        }

        var originalSet = BuildMeasurementSet(originalTrials, compareDriverGuardrails);
        var candidateSet = BuildMeasurementSet(candidateTrials, compareDriverGuardrails);
        var guardrailPairs = originalSet.Guardrails
            .Where(pair => candidateSet.Guardrails.ContainsKey(pair.Key))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Value, candidateSet.Guardrails[pair.Key]))
            .ToArray();
        var comparison = BenchmarkComparer.Compare(
            originalSet.Primary,
            candidateSet.Primary,
            guardrailPairs,
            policy);
        return comparabilityNotes.Count == 0
            ? comparison
            : comparison with { Reason = $"{comparison.Reason} {string.Join(" ", comparabilityNotes)}" };
    }

    private static ComparisonResult? EvaluateRunStability(
        string label,
        GpuAutoAffinityTrialObservation[] trials)
    {
        if (trials.Length < 2)
        {
            return null;
        }

        var frameP99 = trials
            .Select(static trial => GpuBenchmarkEvidenceInterpreter.Interpret(trial.Evidence).FrameP99Milliseconds)
            .ToArray();
        if (frameP99.Any(static value => !double.IsFinite(value) || value <= 0))
        {
            return new ComparisonResult(
                ExperimentVerdict.Inconclusive,
                null,
                null,
                null,
                [],
                $"{label} run-to-run frame-p99 evidence is incomplete; repeated trials cannot be compared safely.");
        }

        var minimum = frameP99.Min();
        var maximum = frameP99.Max();
        var relativeSpread = (maximum - minimum) / minimum;
        if (!double.IsFinite(relativeSpread) || relativeSpread > MaximumRunToRunFrameP99Drift)
        {
            return new ComparisonResult(
                ExperimentVerdict.Inconclusive,
                null,
                null,
                null,
                [],
                $"{label} run-to-run frame-p99 drift is {relativeSpread:P1}, exceeding the {MaximumRunToRunFrameP99Drift:P0} repeatability bound.");
        }

        return null;
    }

    private static GpuOptimizationMeasurementSet BuildMeasurementSet(
        GpuAutoAffinityTrialObservation[] observations,
        bool includeDriverGuardrails)
    {
        if (observations.Length == 0)
        {
            throw new ArgumentException("At least one GPU benchmark observation is required.", nameof(observations));
        }

        var interpretations = observations
            .Select(static observation => GpuBenchmarkEvidenceInterpreter.Interpret(observation.Evidence))
            .ToArray();
        if (interpretations.Any(static interpretation => !interpretation.IsValid))
        {
            throw new InvalidOperationException("Only decision-grade GPU benchmark evidence can be compared.");
        }

        var primary = new MetricSeries(
            FramePrimaryMetric,
            MetricDirection.LowerIsBetter,
            interpretations.SelectMany(static item => item.PrimaryFrameTime.Samples));
        var guardrails = new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
        {
            [GpuBenchmarkEvidenceInterpreter.D3D12GpuWorkMetric] = new(
                GpuBenchmarkEvidenceInterpreter.D3D12GpuWorkMetric,
                MetricDirection.LowerIsBetter,
                interpretations.SelectMany(static item => item.D3D12GpuWork.Samples)),
        };

        // Driver-duration guardrails exist only when kernel ETW produced
        // samples for every compared trial; otherwise they are skipped rather
        // than failing the frame-period comparison.
        if (includeDriverGuardrails &&
            observations.All(static item => item.GpuDriverDpcDurationMicroseconds.Count > 0))
        {
            guardrails[DpcGuardrailMetric] = new(
                DpcGuardrailMetric,
                MetricDirection.LowerIsBetter,
                observations.SelectMany(static item => item.GpuDriverDpcDurationMicroseconds));
        }
        if (includeDriverGuardrails &&
            observations.All(static item => item.GpuDriverIsrDurationMicroseconds.Count > 0))
        {
            guardrails[IsrGuardrailMetric] = new(
                IsrGuardrailMetric,
                MetricDirection.LowerIsBetter,
                observations.SelectMany(static item => item.GpuDriverIsrDurationMicroseconds));
        }

        foreach (var name in interpretations[0].Guardrails.Keys.Order(StringComparer.Ordinal))
        {
            if (guardrails.ContainsKey(name) ||
                interpretations.Any(interpretation => !interpretation.Guardrails.ContainsKey(name)))
            {
                continue;
            }

            var template = interpretations[0].Guardrails[name];
            guardrails[name] = new MetricSeries(
                name,
                template.Direction,
                interpretations.SelectMany(interpretation => interpretation.Guardrails[name].Samples));
        }

        return new GpuOptimizationMeasurementSet(primary, guardrails);
    }

    private static CandidateEvaluation CreateEvaluation(
        GpuAffinityCandidate candidate,
        GpuAutoAffinityTrialObservation[] observations,
        ComparisonResult comparison)
    {
        var values = observations
            .Select(static observation => GpuBenchmarkEvidenceInterpreter.Interpret(observation.Evidence).FrameP99Milliseconds)
            .Order()
            .ToArray();
        var rankable = comparison.Verdict != ExperimentVerdict.Inconclusive &&
            values.Length > 0 &&
            values.All(static value => double.IsFinite(value) && value > 0) &&
            EvaluateRunStability("Candidate", observations) is null;
        var median = rankable
            ? values.Length % 2 == 0
                ? (values[(values.Length / 2) - 1] + values[values.Length / 2]) / 2d
                : values[values.Length / 2]
            : double.PositiveInfinity;
        return new CandidateEvaluation(candidate, comparison, median, rankable);
    }

    private static IEnumerable<CandidateEvaluation> OrderRankableCandidates(
        IEnumerable<CandidateEvaluation> evaluations) =>
        evaluations
            .Where(static evaluation => evaluation.IsRankable && double.IsFinite(evaluation.RankingFrameP99Milliseconds))
            .OrderBy(static evaluation => evaluation.RankingFrameP99Milliseconds)
            .ThenBy(static evaluation => evaluation.Candidate.ObservedPressureScore)
            .ThenBy(static evaluation => evaluation.Candidate.PhysicalCoreIndex)
            .ThenBy(static evaluation => evaluation.Candidate.Processor.Number);

    private static CandidateEvaluation? SelectBestCandidate(IEnumerable<CandidateEvaluation> evaluations) =>
        OrderRankableCandidates(evaluations).FirstOrDefault();

    private static GpuAutoAffinityCandidateReport ToReport(
        string phase,
        GpuAffinityCandidate candidate,
        int trialCount,
        ComparisonResult comparison) =>
        new(
            phase,
            candidate.PhysicalCoreIndex,
            candidate.Processor,
            trialCount,
            comparison.Verdict.ToString(),
            comparison.RelativeImprovement,
            comparison.RegressedGuardrails,
            comparison.Reason);

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
                "GPU finalist confirmation duration must be from 30 to 60 seconds.");
        }
    }

    private sealed record CandidateEvaluation(
        GpuAffinityCandidate Candidate,
        ComparisonResult Comparison,
        double RankingFrameP99Milliseconds,
        bool IsRankable);

    private sealed class SessionAbortException(string message) : Exception(message);
}

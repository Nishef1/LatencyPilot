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
    IReadOnlyList<double> GpuDriverIsrDurationMicroseconds);

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

public sealed record GpuAutoAffinitySessionResult(
    GpuOptimizationRecommendation Recommendation,
    GpuAffinityCandidate? Finalist,
    GpuAutoAffinityReport Report);

public sealed class GpuAutoAffinitySession
{
    private const string FramePrimaryMetric = "CPU frame time (ms)";
    private const string DpcGuardrailMetric = "GPU-driver DPC duration (us)";
    private const string IsrGuardrailMetric = "GPU-driver ISR duration (us)";
    private readonly IGpuAutoAffinitySessionBackend backend;

    public GpuAutoAffinitySession(IGpuAutoAffinitySessionBackend backend)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
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
                candidateReports.Add(ToReport("screening", candidate, observations.Length, comparison));
                physicalEvaluations.Add(new CandidateEvaluation(candidate, comparison));
            }

            var physicalFinalist = SelectFinalist(physicalEvaluations);
            if (physicalFinalist is null)
            {
                reasons.Add("No physical-core candidate measurably improved frame-tail performance without a guardrail regression.");
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
                candidateReports.Add(ToReport("smt-refinement", sibling, observations.Length, comparison));
                refinementEvaluations.Add(new CandidateEvaluation(sibling, comparison));
            }

            var refinedFinalist = SelectFinalist(refinementEvaluations) ?? physicalFinalist;
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
            candidateReports.Add(ToReport("confirmation", finalist, candidateRuns.Count, comparison));
            if (comparison.Verdict == ExperimentVerdict.Improved)
            {
                await backend.KeepAsync(activeExperiment.Value, CancellationToken.None).ConfigureAwait(false);
                activeExperiment = null;
                reasons.Add("Balanced ABBA + BAAB confirmation established a measurable improvement without a guardrail regression.");
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
            reasons.Add(
                $"Balanced confirmation did not establish a safe measurable improvement: {comparison.Reason}");
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
                _ = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
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

        try
        {
            if (!await backend.VerifyCandidateStateAsync(experimentId, candidate, cancellationToken).ConfigureAwait(false))
            {
                throw new SessionAbortException(
                    $"{phase}: candidate {candidate.Processor} was not verified before measurement.");
            }

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

            return observations;
        }
        finally
        {
            await backend.RollbackAsync(experimentId, CancellationToken.None).ConfigureAwait(false);
            if (!await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"{phase}: exact original GPU affinity state was not verified after rolling back {candidate.Processor}.");
            }
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

            throw new SessionAbortException(
                $"{phase} run {trialRequest.RunNumber} is not decision-grade: {string.Join(" ", readiness.Reasons)}");
        }

        throw new UnreachableException();
    }

    private static GpuBenchmarkReadinessResult EvaluateObservation(
        GpuBenchmarkEvidence? reference,
        GpuAutoAffinityTrialObservation observation,
        GpuAffinityCandidate? candidate,
        int retryAttempt)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var reasons = new List<string>();
        if (!observation.StoredStateVerifiedBefore || !observation.StoredStateVerifiedAfter)
        {
            reasons.Add("Expected stored GPU affinity state was not verified before and after the trial.");
        }

        if (candidate is not null &&
            (observation.Placement is null ||
             observation.Placement.TargetProcessor != candidate.Processor ||
             !observation.Placement.ConfirmsRequestedPlacement))
        {
            reasons.Add("Candidate trial lacks GPU-driver ISR placement confined to the requested logical processor.");
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
                []);
        }

        if (reference is null)
        {
            return new GpuBenchmarkReadinessResult(
                GpuBenchmarkEvidence.MethodIdValue,
                GpuBenchmarkReadinessState.Ready,
                [],
                []);
        }

        var contamination = observation.Contamination with { RetryAttempt = retryAttempt };
        return GpuBenchmarkReadiness.Evaluate(reference, observation.Evidence, contamination);
    }

    private static ComparisonResult Compare(
        IEnumerable<GpuAutoAffinityTrialObservation> originals,
        IEnumerable<GpuAutoAffinityTrialObservation> candidates,
        ComparisonPolicy policy)
    {
        var originalSet = BuildMeasurementSet(originals.ToArray());
        var candidateSet = BuildMeasurementSet(candidates.ToArray());
        var guardrailPairs = originalSet.Guardrails
            .Where(pair => candidateSet.Guardrails.ContainsKey(pair.Key))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Value, candidateSet.Guardrails[pair.Key]))
            .ToArray();
        return BenchmarkComparer.Compare(
            originalSet.Primary,
            candidateSet.Primary,
            guardrailPairs,
            policy);
    }

    private static GpuOptimizationMeasurementSet BuildMeasurementSet(
        GpuAutoAffinityTrialObservation[] observations)
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
            [DpcGuardrailMetric] = new(
                DpcGuardrailMetric,
                MetricDirection.LowerIsBetter,
                observations.SelectMany(static item => item.GpuDriverDpcDurationMicroseconds)),
            [IsrGuardrailMetric] = new(
                IsrGuardrailMetric,
                MetricDirection.LowerIsBetter,
                observations.SelectMany(static item => item.GpuDriverIsrDurationMicroseconds)),
        };

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

    private static CandidateEvaluation? SelectFinalist(List<CandidateEvaluation> evaluations) =>
        evaluations
            .Where(static evaluation => evaluation.Comparison.Verdict == ExperimentVerdict.Improved)
            .OrderByDescending(static evaluation => evaluation.Comparison.RelativeImprovement ?? double.NegativeInfinity)
            .ThenBy(static evaluation => evaluation.Candidate.ObservedPressureScore)
            .ThenBy(static evaluation => evaluation.Candidate.PhysicalCoreIndex)
            .ThenBy(static evaluation => evaluation.Candidate.Processor.Number)
            .FirstOrDefault();

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
            comparison.RegressedGuardrails);

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
            reasons);
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
        ComparisonResult Comparison);

    private sealed class SessionAbortException(string message) : Exception(message);
}

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
    TimeSpan ScreeningDuration,
    TimeSpan FinalistDuration = default,
    GpuAutoAffinitySearchScope SearchScope = GpuAutoAffinitySearchScope.Full,
    IReadOnlyList<LogicalProcessorId>? RequestedProcessors = null);

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
/// Measurement-first GPU interrupt-affinity search using direct local paired controls.
/// Every ranked treatment is backed by measured Original -> Candidate -> Original
/// evidence. Drift invalidates a pair instead of being transformed into candidate FPS.
/// Exact rollback remains mandatory between candidates and final Keep still requires
/// ETW-backed target-only runtime ISR placement proof.
/// </summary>
public sealed class GpuAutoAffinitySession
{
    public static readonly TimeSpan V2ScreeningDuration = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan V2FinalistDuration = TimeSpan.FromSeconds(30);

    private const double CandidateMetricEquivalenceTolerance = 0.01;
    private const double InterruptTailRegressionTolerance = 0.10;
    private const int MinimumInterruptTailSamples = 20;
    private const int MinimumInterruptTailRuns = 3;
    private const int MaximumRejectedOriginalRepeatabilityRuns =
        GpuRepeatabilityClusterSelector.MaximumOriginalAttemptCount -
        GpuRepeatabilityClusterSelector.RequiredRunCount;
    private const int MaximumPairAttempts = 2;
    private const int MaximumConsecutiveUnstableCandidates = 2;
    private const int MaximumPhysicalCoreHypotheses = 3;
    private const int MaximumFinalists = 3;
    private const int RequiredFinalistPairs = 3;
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
        var pairReports = new List<GpuAutoAffinityPairReport>();
        var finalistReports = new List<GpuAutoAffinityFinalistReport>();
        var reasons = new List<string>();
        var screenedProcessors = new HashSet<LogicalProcessorId>();
        var nextRunNumber = 0;
        var nextPairNumber = 0;
        var pressure = request.PressureEvidence.ToArray();
        var allEligibleCandidates = GpuAffinityCandidatePlanner
            .Create(request.Topology, pressure, request.CpuSets)
            .ToArray();
        var requestedProcessors = request.RequestedProcessors?.ToArray() ?? [];
        var candidates = SelectInitialCandidates(request, allEligibleCandidates, requestedProcessors);
        var finalistDuration = request.FinalistDuration == default
            ? V2FinalistDuration
            : request.FinalistDuration;

        if (candidates.Length == 0)
        {
            reasons.Add("No eligible GPU interrupt-affinity candidate is available for the requested search scope.");
            var originalVerified = await backend.VerifyOriginalStateAsync(cancellationToken).ConfigureAwait(false);
            return CreateResult(
                request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                originalVerified, originalVerified, candidateReports, trialReports, pairReports,
                finalistReports, reasons, null, screenedProcessors,
                fullTopologyCoverage: false, practicalTie: false);
        }

        GpuAutoAffinityDecisionBaselineReport? decisionBaseline = null;

        try
        {
            var referenceObservation = await CaptureAcceptedAsync(
                () => ++nextRunNumber,
                "screening-warmup",
                GpuConfirmationOrder.Original,
                null,
                TransitionWarmupDuration,
                null,
                null,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            var reference = referenceObservation.Evidence;

            var original = await QualifyOriginalAsync(
                request.ScreeningDuration,
                reference,
                () => ++nextRunNumber,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            if (!original.IsRankable)
            {
                reasons.Add($"Original-state benchmark is not repeatable enough for paired GPU comparison: {original.Reason}");
                var originalVerified = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    originalVerified, originalVerified, candidateReports, trialReports, pairReports,
                    finalistReports, reasons, null, screenedProcessors,
                    fullTopologyCoverage: false, practicalTie: false);
            }

            decisionBaseline = ToDecisionBaseline(original);
            if (original.TotalObservationCount > original.ValidObservationCount)
            {
                reasons.Add(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Original qualification recovered {original.ValidObservationCount} valid runs from {original.TotalObservationCount} attempts; {original.TotalObservationCount - original.ValidObservationCount} outlier sample(s) were excluded. Accepted 1%-low noise was {original.PrimaryRelativeNoise:P2}."));
            }

            var driftBudget = ComputePairDriftBudget(original.PrimaryRelativeNoise);
            GpuAutoAffinityTrialObservation? originalBefore = await CaptureOriginalControlAsync(
                "screening-original-control",
                request.ScreeningDuration,
                reference,
                () => ++nextRunNumber,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            var screeningMeasurements = new List<PairMeasurement>();
            var consecutiveUnstableCandidates = 0;

            ShuffleDeterministically(candidates, request.ShuffleSeed);
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (originalBefore is null)
                {
                    originalBefore = await CaptureOriginalControlAsync(
                        "screening-recovery-original-control",
                        request.ScreeningDuration,
                        reference,
                        () => ++nextRunNumber,
                        trialReports,
                        cancellationToken).ConfigureAwait(false);
                }

                screenedProcessors.Add(candidate.Processor);
                var outcome = await MeasureScreeningPairAsync(
                    candidate,
                    "screening-representative",
                    originalBefore,
                    driftBudget,
                    request.ScreeningDuration,
                    reference,
                    () => ++nextRunNumber,
                    () => ++nextPairNumber,
                    trialReports,
                    pairReports,
                    cancellationToken).ConfigureAwait(false);
                originalBefore = outcome.NextOriginal;

                if (outcome.ValidMeasurement is { } valid)
                {
                    screeningMeasurements.Add(valid);
                    consecutiveUnstableCandidates = 0;
                }
                else
                {
                    consecutiveUnstableCandidates++;
                }

                var candidateReport = ToScreeningCandidateReport(candidate, outcome.FinalReport, outcome.ValidMeasurement);
                candidateReports.Add(candidateReport);
                await PublishCandidateReportAsync(candidateReport).ConfigureAwait(false);

                if (consecutiveUnstableCandidates >= MaximumConsecutiveUnstableCandidates)
                {
                    reasons.Add(
                        $"Paired screening stopped early after {MaximumConsecutiveUnstableCandidates} consecutive candidates remained unstable after their bounded retry. The exact Original state is retained rather than ranking noise.");
                    ApplyPairDecisionRanks(candidateReports, screeningMeasurements);
                    var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                    return CreateResult(
                        request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                        restored, restored, candidateReports, trialReports, pairReports, finalistReports,
                        reasons, decisionBaseline, screenedProcessors,
                        fullTopologyCoverage: false, practicalTie: false);
                }
            }

            if (request.SearchScope == GpuAutoAffinitySearchScope.Custom)
            {
                ApplyPairDecisionRanks(candidateReports, screeningMeasurements);
                var best = OrderScreeningMeasurements(screeningMeasurements).FirstOrDefault();
                reasons.Add(best is null
                    ? "Custom diagnostic screening produced no valid local pair. Original was retained."
                    : string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Best within selected CPUs was CPU {best.Candidate.Processor.Number} with local 1%-low effect {best.Report.OnePercentLowEffect:P2}. Custom scope is diagnostic-only; Original was restored and no machine-wide winner was claimed."));
                var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    restored, restored, candidateReports, trialReports, pairReports, finalistReports,
                    reasons, decisionBaseline, screenedProcessors,
                    fullTopologyCoverage: false, practicalTie: false);
            }

            var stageAComplete = candidates
                .Select(static candidate => candidate.PhysicalCoreIndex)
                .Distinct()
                .Count() == allEligibleCandidates
                    .Select(static candidate => candidate.PhysicalCoreIndex)
                    .Distinct()
                    .Count();

            var selectedPhysicalCores = SelectPhysicalCoreHypotheses(screeningMeasurements);
            var tested = screenedProcessors.ToHashSet();
            var siblingCandidates = allEligibleCandidates
                .Where(candidate => selectedPhysicalCores.Contains(candidate.PhysicalCoreIndex))
                .Where(candidate => !tested.Contains(candidate.Processor))
                .ToArray();
            ShuffleDeterministically(siblingCandidates, unchecked(request.ShuffleSeed ^ 0x4F1BBCDC));

            foreach (var candidate in siblingCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (originalBefore is null)
                {
                    originalBefore = await CaptureOriginalControlAsync(
                        "screening-sibling-recovery-original-control",
                        request.ScreeningDuration,
                        reference,
                        () => ++nextRunNumber,
                        trialReports,
                        cancellationToken).ConfigureAwait(false);
                }

                screenedProcessors.Add(candidate.Processor);
                var outcome = await MeasureScreeningPairAsync(
                    candidate,
                    "screening-sibling",
                    originalBefore,
                    driftBudget,
                    request.ScreeningDuration,
                    reference,
                    () => ++nextRunNumber,
                    () => ++nextPairNumber,
                    trialReports,
                    pairReports,
                    cancellationToken).ConfigureAwait(false);
                originalBefore = outcome.NextOriginal;

                if (outcome.ValidMeasurement is { } valid)
                {
                    screeningMeasurements.Add(valid);
                    consecutiveUnstableCandidates = 0;
                }
                else
                {
                    consecutiveUnstableCandidates++;
                }

                var candidateReport = ToScreeningCandidateReport(candidate, outcome.FinalReport, outcome.ValidMeasurement);
                candidateReports.Add(candidateReport);
                await PublishCandidateReportAsync(candidateReport).ConfigureAwait(false);

                if (consecutiveUnstableCandidates >= MaximumConsecutiveUnstableCandidates)
                {
                    reasons.Add(
                        "Sibling refinement stopped because two consecutive candidates remained locally unstable after retry. Original was retained.");
                    ApplyPairDecisionRanks(candidateReports, screeningMeasurements);
                    var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                    return CreateResult(
                        request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                        restored, restored, candidateReports, trialReports, pairReports, finalistReports,
                        reasons, decisionBaseline, screenedProcessors,
                        fullTopologyCoverage: stageAComplete, practicalTie: false);
                }
            }

            ApplyPairDecisionRanks(candidateReports, screeningMeasurements);
            var finalistCandidates = SelectFinalists(screeningMeasurements);
            if (finalistCandidates.Length == 0)
            {
                reasons.Add("No valid paired screening candidate remained for finalist confirmation. Original was retained.");
                var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    restored, restored, candidateReports, trialReports, pairReports, finalistReports,
                    reasons, decisionBaseline, screenedProcessors,
                    fullTopologyCoverage: stageAComplete, practicalTie: false);
            }

            var finalistOriginal = await CaptureOriginalControlAsync(
                "finalist-original-control",
                finalistDuration,
                reference,
                () => ++nextRunNumber,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            var finalistMeasurements = finalistCandidates.ToDictionary(
                static candidate => candidate.Processor,
                static _ => new List<PairMeasurement>(RequiredFinalistPairs));
            var finalistInconclusive = new HashSet<LogicalProcessorId>();
            consecutiveUnstableCandidates = 0;

            for (var round = 0; round < RequiredFinalistPairs; round++)
            {
                var roundCandidates = finalistCandidates
                    .Where(candidate => !finalistInconclusive.Contains(candidate.Processor))
                    .ToArray();
                ShuffleDeterministically(
                    roundCandidates,
                    unchecked(request.ShuffleSeed ^ (int)(0x9E3779B9u * (uint)(round + 1))));

                foreach (var candidate in roundCandidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var outcome = await MeasureScreeningPairAsync(
                        candidate,
                        "finalist",
                        finalistOriginal,
                        driftBudget,
                        finalistDuration,
                        reference,
                        () => ++nextRunNumber,
                        () => ++nextPairNumber,
                        trialReports,
                        pairReports,
                        cancellationToken).ConfigureAwait(false);

                    if (outcome.NextOriginal is { } nextOriginal)
                    {
                        finalistOriginal = nextOriginal;
                    }
                    else if (round < RequiredFinalistPairs - 1 ||
                             roundCandidates[^1].Processor != candidate.Processor)
                    {
                        finalistOriginal = await CaptureOriginalControlAsync(
                            "finalist-recovery-original-control",
                            finalistDuration,
                            reference,
                            () => ++nextRunNumber,
                            trialReports,
                            cancellationToken).ConfigureAwait(false);
                    }

                    if (outcome.ValidMeasurement is { } valid)
                    {
                        finalistMeasurements[candidate.Processor].Add(valid);
                        consecutiveUnstableCandidates = 0;
                    }
                    else
                    {
                        finalistInconclusive.Add(candidate.Processor);
                        consecutiveUnstableCandidates++;
                    }

                    if (consecutiveUnstableCandidates >= MaximumConsecutiveUnstableCandidates)
                    {
                        reasons.Add(
                            "Finalist confirmation stopped because two consecutive candidates remained locally unstable after retry. Original was retained.");
                        var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                        BuildFinalistReports(
                            finalistCandidates,
                            finalistMeasurements,
                            finalistInconclusive,
                            finalistReports,
                            candidateReports);
                        return CreateResult(
                            request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                            restored, restored, candidateReports, trialReports, pairReports, finalistReports,
                            reasons, decisionBaseline, screenedProcessors,
                            fullTopologyCoverage: stageAComplete, practicalTie: false);
                    }
                }
            }

            var finalistDecisions = BuildFinalistReports(
                finalistCandidates,
                finalistMeasurements,
                finalistInconclusive,
                finalistReports,
                candidateReports);
            var improvementCapable = finalistDecisions
                .Where(static decision => decision.ImprovementCapable)
                .OrderByDescending(static decision => decision.Report.MedianOnePercentLowEffect)
                .ThenBy(static decision => decision.Candidate.ObservedPressureScore)
                .ThenBy(static decision => decision.Candidate.PhysicalCoreIndex)
                .ThenBy(static decision => decision.Candidate.Processor.Number)
                .ToArray();

            if (improvementCapable.Length == 0)
            {
                reasons.Add("No finalist produced three valid local pairs that cleared the paired improvement and guardrail decision floor. Original was retained.");
                var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    restored, restored, candidateReports, trialReports, pairReports, finalistReports,
                    reasons, decisionBaseline, screenedProcessors,
                    fullTopologyCoverage: stageAComplete, practicalTie: false);
            }

            var bestEffect = improvementCapable[0].Report.MedianOnePercentLowEffect!.Value;
            var tied = improvementCapable
                .Where(decision =>
                    Math.Abs(decision.Report.MedianOnePercentLowEffect!.Value - bestEffect) <=
                    CandidateMetricEquivalenceTolerance)
                .ToArray();
            var practicalTie = tied.Length > 1;
            var selected = tied
                .OrderBy(static decision => decision.Candidate.ObservedPressureScore)
                .ThenBy(static decision => decision.Candidate.PhysicalCoreIndex)
                .ThenBy(static decision => decision.Candidate.Processor.Number)
                .First();

            reasons.Add(practicalTie
                ? string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Finalists are within the 1 percentage-point practical-tie margin. CPU {selected.Candidate.Processor.Number} is the deterministic operational target; it is not claimed to be faster than tied peers. Median paired 1%-low effect {selected.Report.MedianOnePercentLowEffect:P2}.")
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"CPU {selected.Candidate.Processor.Number} is the strongest repeatable paired finalist. Median paired 1%-low effect {selected.Report.MedianOnePercentLowEffect:P2}."));

            return await VerifyAndKeepFinalistAsync(
                request,
                startedAtUtc,
                decisionBaseline,
                selected.Candidate,
                reference,
                () => ++nextRunNumber,
                candidateReports,
                trialReports,
                pairReports,
                finalistReports,
                reasons,
                screenedProcessors,
                stageAComplete,
                practicalTie,
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
                request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                restored, restored, candidateReports, trialReports, pairReports, finalistReports,
                reasons, decisionBaseline, screenedProcessors,
                fullTopologyCoverage: false, practicalTie: false);
        }
    }

    private async Task<OriginalEvaluation> QualifyOriginalAsync(
        TimeSpan duration,
        GpuBenchmarkEvidence reference,
        Func<int> nextRunNumber,
        List<GpuAutoAffinityTrialReport> trialReports,
        CancellationToken cancellationToken)
    {
        var observations = new List<GpuAutoAffinityTrialObservation>(
            GpuRepeatabilityClusterSelector.MaximumOriginalAttemptCount);
        var evaluation = OriginalEvaluation.Unrankable(
            "Original repeatability has not collected three scored observations yet.");

        while (observations.Count < GpuRepeatabilityClusterSelector.MaximumOriginalAttemptCount)
        {
            observations.Add(await CaptureAcceptedAsync(
                nextRunNumber,
                "screening-original",
                GpuConfirmationOrder.Original,
                null,
                duration,
                reference,
                null,
                trialReports,
                cancellationToken).ConfigureAwait(false));
            if (observations.Count < GpuRepeatabilityClusterSelector.RequiredRunCount)
            {
                continue;
            }

            evaluation = CreateOriginalEvaluation(observations.ToArray());
            if (evaluation.IsRankable)
            {
                break;
            }
        }

        return evaluation;
    }

    private static GpuAffinityCandidate[] SelectInitialCandidates(
        GpuAutoAffinitySessionRequest request,
        GpuAffinityCandidate[] allEligibleCandidates,
        LogicalProcessorId[] requestedProcessors)
    {
        if (request.SearchScope == GpuAutoAffinitySearchScope.Custom)
        {
            var requested = requestedProcessors.ToHashSet();
            var selected = allEligibleCandidates
                .Where(candidate => requested.Contains(candidate.Processor))
                .ToArray();
            if (selected.Length != requested.Count)
            {
                var found = selected.Select(static candidate => candidate.Processor).ToHashSet();
                var unavailable = requested.Where(processor => !found.Contains(processor)).ToArray();
                throw new ArgumentException(
                    $"Custom GPU scope contains processor(s) that are not currently eligible: {string.Join(", ", unavailable)}.",
                    nameof(request));
            }

            return selected;
        }

        return allEligibleCandidates
            .GroupBy(static candidate => candidate.PhysicalCoreIndex)
            .Select(static group => group.First())
            .ToArray();
    }

    private async Task<PairOutcome> MeasureScreeningPairAsync(
        GpuAffinityCandidate candidate,
        string stage,
        GpuAutoAffinityTrialObservation initialOriginalBefore,
        double driftBudget,
        TimeSpan duration,
        GpuBenchmarkEvidence reference,
        Func<int> nextRunNumber,
        Func<int> nextPairNumber,
        List<GpuAutoAffinityTrialReport> trialReports,
        List<GpuAutoAffinityPairReport> pairReports,
        CancellationToken cancellationToken)
    {
        var pairNumber = nextPairNumber();
        var originalBefore = initialOriginalBefore;
        GpuAutoAffinityPairReport? finalReport = null;

        for (var attempt = 1; attempt <= MaximumPairAttempts; attempt++)
        {
            var candidateObservation = await MeasureCandidateObservationAsync(
                candidate,
                stage,
                duration,
                reference,
                nextRunNumber,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            var originalAfter = await CaptureOriginalControlAsync(
                $"{stage}-original-after",
                duration,
                reference,
                nextRunNumber,
                trialReports,
                cancellationToken).ConfigureAwait(false);

            var pair = CreatePairReport(
                pairNumber,
                candidate,
                stage,
                attempt,
                originalBefore,
                candidateObservation,
                originalAfter,
                driftBudget);
            if (pair.Verdict == GpuAutoAffinityPairVerdict.Unstable && attempt == MaximumPairAttempts)
            {
                pair = pair with
                {
                    Verdict = GpuAutoAffinityPairVerdict.Inconclusive,
                    Reason = $"Local Original movement {pair.ControlMovement:P2} remained above the {pair.DriftBudget:P2} drift budget after the bounded retry.",
                };
            }

            pairReports.Add(pair);
            finalReport = pair;
            if (pair.Verdict == GpuAutoAffinityPairVerdict.Valid)
            {
                return new PairOutcome(
                    pair,
                    originalAfter,
                    new PairMeasurement(candidate, pair, originalBefore, candidateObservation, originalAfter));
            }

            // An unstable pair's OriginalAfter is evidence, but it is not a valid
            // chain anchor. Retry from a freshly acquired Original control instead.
            if (attempt < MaximumPairAttempts)
            {
                originalBefore = await CaptureOriginalControlAsync(
                    $"{stage}-retry-original-before",
                    duration,
                    reference,
                    nextRunNumber,
                    trialReports,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return new PairOutcome(
            finalReport ?? throw new InvalidOperationException("Pair evaluation produced no report."),
            NextOriginal: null,
            ValidMeasurement: null);
    }

    private async Task<GpuAutoAffinityTrialObservation> MeasureCandidateObservationAsync(
        GpuAffinityCandidate candidate,
        string phase,
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
                null,
                experimentId,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            var observation = await CaptureAcceptedAsync(
                nextRunNumber,
                phase,
                GpuConfirmationOrder.Candidate,
                candidate,
                duration,
                reference,
                experimentId,
                trialReports,
                cancellationToken).ConfigureAwait(false);

            if (ownsMutation)
            {
                await RollbackAndVerifyOriginalAsync(experimentId, phase, candidate).ConfigureAwait(false);
            }
            else if (!await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"{phase}: a no-write candidate no longer matches the captured original state.");
            }

            return observation;
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
                    throw new InvalidOperationException(
                        $"{phase}: a no-write candidate failed and the captured original state is no longer present.");
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

    private async Task<GpuAutoAffinityTrialObservation> CaptureOriginalControlAsync(
        string phase,
        TimeSpan duration,
        GpuBenchmarkEvidence reference,
        Func<int> nextRunNumber,
        List<GpuAutoAffinityTrialReport> trialReports,
        CancellationToken cancellationToken)
    {
        if (!await backend.VerifyOriginalStateAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new SessionAbortException($"{phase}: exact Original state was not verified before control capture.");
        }

        await CaptureAcceptedAsync(
            nextRunNumber,
            $"{phase}-warmup",
            GpuConfirmationOrder.Original,
            null,
            TransitionWarmupDuration,
            null,
            null,
            trialReports,
            cancellationToken).ConfigureAwait(false);
        return await CaptureAcceptedAsync(
            nextRunNumber,
            phase,
            GpuConfirmationOrder.Original,
            null,
            duration,
            reference,
            null,
            trialReports,
            cancellationToken).ConfigureAwait(false);
    }

    private static GpuAutoAffinityPairReport CreatePairReport(
        int pairNumber,
        GpuAffinityCandidate candidate,
        string stage,
        int attempt,
        GpuAutoAffinityTrialObservation originalBefore,
        GpuAutoAffinityTrialObservation candidateObservation,
        GpuAutoAffinityTrialObservation originalAfter,
        double driftBudget)
    {
        var before = RequireVideoStats(originalBefore, "OriginalBefore");
        var measured = RequireVideoStats(candidateObservation, "Candidate");
        var after = RequireVideoStats(originalAfter, "OriginalAfter");
        var controlMovement = RelativeMovement(before.Low1PctFps, after.Low1PctFps);
        var verdict = controlMovement <= driftBudget
            ? GpuAutoAffinityPairVerdict.Valid
            : GpuAutoAffinityPairVerdict.Unstable;
        var reason = verdict == GpuAutoAffinityPairVerdict.Valid
            ? string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Local controls moved {controlMovement:P2}, within the {driftBudget:P2} paired drift budget.")
            : string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Local controls moved {controlMovement:P2}, above the {driftBudget:P2} paired drift budget; this attempt cannot rank the candidate.");

        return new GpuAutoAffinityPairReport(
            pairNumber,
            candidate.Processor,
            candidate.PhysicalCoreIndex,
            stage,
            attempt,
            originalBefore.Evidence.EtwCaptureId,
            candidateObservation.Evidence.EtwCaptureId,
            originalAfter.Evidence.EtwCaptureId,
            before.Low1PctFps,
            measured.Low1PctFps,
            after.Low1PctFps,
            HigherIsBetterEffect(measured.Low1PctFps, before.Low1PctFps, after.Low1PctFps),
            HigherIsBetterEffect(measured.AvgFps, before.AvgFps, after.AvgFps),
            LowerIsBetterEffect(measured.P99Milliseconds, before.P99Milliseconds, after.P99Milliseconds),
            HigherIsBetterEffect(measured.Low01PctFps, before.Low01PctFps, after.Low01PctFps),
            controlMovement,
            driftBudget,
            verdict,
            reason);
    }

    private static double ComputePairDriftBudget(double initialNoise) =>
        Math.Clamp(Math.Max(0.06d, 2d * initialNoise), 0.06d, 0.10d);

    private static double GeometricMean(double left, double right)
    {
        RequireFinitePositive(left, nameof(left));
        RequireFinitePositive(right, nameof(right));
        return Math.Sqrt(left * right);
    }

    private static double HigherIsBetterEffect(double candidate, double before, double after)
    {
        RequireFinitePositive(candidate, nameof(candidate));
        return (candidate / GeometricMean(before, after)) - 1d;
    }

    private static double LowerIsBetterEffect(double candidate, double before, double after)
    {
        RequireFinitePositive(candidate, nameof(candidate));
        return (GeometricMean(before, after) / candidate) - 1d;
    }

    private static double RelativeMovement(double before, double after)
    {
        RequireFinitePositive(before, nameof(before));
        RequireFinitePositive(after, nameof(after));
        return Math.Abs(after - before) / Math.Max(before, after);
    }

    private static void RequireFinitePositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0d)
        {
            throw new SessionAbortException($"Paired GPU comparison requires finite positive {name} evidence.");
        }
    }

    private static GpuBenchmarkVideoStats RequireVideoStats(
        GpuAutoAffinityTrialObservation observation,
        string role)
    {
        var stats = GpuBenchmarkEvidenceInterpreter.Interpret(observation.Evidence).VideoStats;
        if (stats is null || !HasFiniteRankingStatistics(stats))
        {
            throw new SessionAbortException($"{role} lacks complete finite positive GPU benchmark statistics.");
        }

        return stats;
    }

    private static HashSet<int> SelectPhysicalCoreHypotheses(List<PairMeasurement> measurements)
    {
        var ordered = OrderScreeningMeasurements(measurements).ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var selected = ordered
            .Take(Math.Min(2, ordered.Length))
            .Select(static measurement => measurement.Candidate.PhysicalCoreIndex)
            .ToHashSet();
        if (ordered.Length > 2 &&
            Math.Abs(ordered[2].Report.OnePercentLowEffect - ordered[1].Report.OnePercentLowEffect) <=
            CandidateMetricEquivalenceTolerance)
        {
            selected.Add(ordered[2].Candidate.PhysicalCoreIndex);
        }

        return selected.Count <= MaximumPhysicalCoreHypotheses
            ? selected
            : selected.Take(MaximumPhysicalCoreHypotheses).ToHashSet();
    }

    private static GpuAffinityCandidate[] SelectFinalists(List<PairMeasurement> measurements)
    {
        var ordered = OrderScreeningMeasurements(measurements).ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var selected = ordered.Take(Math.Min(2, ordered.Length)).ToList();
        if (ordered.Length > 2 &&
            Math.Abs(ordered[2].Report.OnePercentLowEffect - ordered[1].Report.OnePercentLowEffect) <=
            CandidateMetricEquivalenceTolerance)
        {
            selected.Add(ordered[2]);
        }

        return selected
            .Take(MaximumFinalists)
            .Select(static measurement => measurement.Candidate)
            .ToArray();
    }

    private static IEnumerable<PairMeasurement> OrderScreeningMeasurements(List<PairMeasurement> measurements) =>
        measurements
            .Where(static measurement => measurement.Report.Verdict == GpuAutoAffinityPairVerdict.Valid)
            .OrderByDescending(static measurement => measurement.Report.OnePercentLowEffect)
            .ThenBy(static measurement => measurement.Candidate.ObservedPressureScore)
            .ThenBy(static measurement => measurement.Candidate.PhysicalCoreIndex)
            .ThenBy(static measurement => measurement.Candidate.Processor.Number);

    private static GpuAutoAffinityCandidateReport ToScreeningCandidateReport(
        GpuAffinityCandidate candidate,
        GpuAutoAffinityPairReport pair,
        PairMeasurement? measurement)
    {
        var stats = measurement is null ? null : RequireVideoStats(measurement.CandidateObservation, "Candidate");
        return new GpuAutoAffinityCandidateReport(
            "screening",
            candidate.PhysicalCoreIndex,
            candidate.Processor,
            1,
            pair.Verdict == GpuAutoAffinityPairVerdict.Valid ? "Ranked" : "Inconclusive",
            pair.Verdict == GpuAutoAffinityPairVerdict.Valid ? pair.FrameP99Effect : null,
            [],
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Paired {pair.Stage} attempt {pair.Attempt}: 1% low effect {pair.OnePercentLowEffect:P2}; AVG {pair.AvgEffect:P2}; frame-p99 {pair.FrameP99Effect:P2}; local control movement {pair.ControlMovement:P2}/{pair.DriftBudget:P2}. {pair.Reason}"),
            stats?.Low1PctFps,
            stats?.AvgFps,
            stats?.P99Milliseconds,
            stats?.Low01PctFps,
            pair.ControlMovement,
            UsesTimeLocalNormalization: false);
    }

    private static void ApplyPairDecisionRanks(
        List<GpuAutoAffinityCandidateReport> candidateReports,
        List<PairMeasurement> screeningMeasurements)
    {
        var ordered = OrderScreeningMeasurements(screeningMeasurements).ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            var processor = ordered[index].Candidate.Processor;
            var reportIndex = candidateReports.FindLastIndex(report =>
                string.Equals(report.Phase, "screening", StringComparison.Ordinal) &&
                report.Processor == processor);
            if (reportIndex >= 0)
            {
                candidateReports[reportIndex] = candidateReports[reportIndex] with
                {
                    DecisionRank = index + 1,
                };
            }
        }
    }

    private static FinalistDecision[] BuildFinalistReports(
        GpuAffinityCandidate[] finalists,
        Dictionary<LogicalProcessorId, List<PairMeasurement>> measurements,
        HashSet<LogicalProcessorId> inconclusive,
        List<GpuAutoAffinityFinalistReport> reports,
        List<GpuAutoAffinityCandidateReport> candidateReports)
    {
        var decisions = new List<FinalistDecision>(finalists.Length);
        foreach (var candidate in finalists)
        {
            var pairs = measurements[candidate.Processor];
            GpuAutoAffinityFinalistReport report;
            var improvementCapable = false;

            if (inconclusive.Contains(candidate.Processor) || pairs.Count != RequiredFinalistPairs)
            {
                report = new GpuAutoAffinityFinalistReport(
                    candidate.Processor,
                    candidate.PhysicalCoreIndex,
                    pairs.Select(static pair => pair.Report.PairNumber).ToArray(),
                    null,
                    null,
                    null,
                    null,
                    "Inconclusive",
                    $"Finalist produced {pairs.Count}/{RequiredFinalistPairs} valid paired observations within the bounded retry policy.");
            }
            else
            {
                var primary = pairs.Select(static pair => pair.Report.OnePercentLowEffect).ToArray();
                var avg = pairs.Select(static pair => pair.Report.AvgEffect).ToArray();
                var p99 = pairs.Select(static pair => pair.Report.FrameP99Effect).ToArray();
                var low01 = pairs
                    .Where(static pair => pair.Report.Low01PctEffect is not null)
                    .Select(static pair => pair.Report.Low01PctEffect!.Value)
                    .ToArray();
                var controlMovement = Median(pairs.Select(static pair => pair.Report.ControlMovement));
                var decisionFloor = Math.Max(CandidateMetricEquivalenceTolerance, controlMovement);
                var medianPrimary = Median(primary);
                var medianAvg = Median(avg);
                var medianP99 = Median(p99);
                var positivePrimaryCount = primary.Count(static effect => effect > 0d);
                var materialPrimaryRegression = primary.Any(effect => effect < -decisionFloor);
                var guardrailRegression = medianAvg < -decisionFloor || medianP99 < -decisionFloor;
                var interruptRegression = HasMaterialInterruptTailRegression(pairs, out var interruptReason);
                improvementCapable =
                    positivePrimaryCount >= 2 &&
                    medianPrimary > decisionFloor &&
                    !materialPrimaryRegression &&
                    !guardrailRegression &&
                    !interruptRegression;

                var reason = improvementCapable
                    ? string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Three valid paired observations; median 1%-low effect {medianPrimary:P2} clears the {decisionFloor:P2} local decision floor without material AVG/frame-p99/interrupt-tail regression.")
                    : string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"Finalist rejected: positive 1%-low pairs {positivePrimaryCount}/3; median effect {medianPrimary:P2}; decision floor {decisionFloor:P2}; material primary regression={materialPrimaryRegression}; AVG/p99 regression={guardrailRegression}. {interruptReason}");

                report = new GpuAutoAffinityFinalistReport(
                    candidate.Processor,
                    candidate.PhysicalCoreIndex,
                    pairs.Select(static pair => pair.Report.PairNumber).ToArray(),
                    medianPrimary,
                    medianAvg,
                    medianP99,
                    low01.Length == 0 ? null : Median(low01),
                    improvementCapable ? "ImprovementCapable" : "Rejected",
                    reason);
            }

            reports.Add(report);
            var lastMeasurement = pairs.LastOrDefault();
            var stats = lastMeasurement is null
                ? null
                : RequireVideoStats(lastMeasurement.CandidateObservation, "Finalist candidate");
            candidateReports.Add(new GpuAutoAffinityCandidateReport(
                FinalistPhaseName,
                candidate.PhysicalCoreIndex,
                candidate.Processor,
                pairs.Count,
                improvementCapable ? "Ranked" : "Inconclusive",
                report.MedianFrameP99Effect,
                [],
                report.Reason,
                stats?.Low1PctFps,
                stats?.AvgFps,
                stats?.P99Milliseconds,
                stats?.Low01PctFps,
                pairs.Count == 0 ? null : Median(pairs.Select(static pair => pair.Report.ControlMovement)),
                UsesTimeLocalNormalization: false));
            decisions.Add(new FinalistDecision(candidate, report, improvementCapable));
        }

        var ranked = decisions
            .Where(static decision => decision.Report.MedianOnePercentLowEffect is not null)
            .OrderByDescending(static decision => decision.Report.MedianOnePercentLowEffect)
            .ThenBy(static decision => decision.Candidate.ObservedPressureScore)
            .ThenBy(static decision => decision.Candidate.PhysicalCoreIndex)
            .ThenBy(static decision => decision.Candidate.Processor.Number)
            .ToArray();
        for (var index = 0; index < ranked.Length; index++)
        {
            var reportIndex = candidateReports.FindLastIndex(report =>
                string.Equals(report.Phase, FinalistPhaseName, StringComparison.Ordinal) &&
                report.Processor == ranked[index].Candidate.Processor);
            if (reportIndex >= 0)
            {
                candidateReports[reportIndex] = candidateReports[reportIndex] with { DecisionRank = index + 1 };
            }
        }

        return decisions.ToArray();
    }

    private static bool HasMaterialInterruptTailRegression(
        List<PairMeasurement> pairs,
        out string reason)
    {
        var original = pairs
            .SelectMany(static pair => new[] { pair.OriginalBefore, pair.OriginalAfter })
            .GroupBy(static observation => observation.Evidence.EtwCaptureId)
            .Select(static group => group.First())
            .ToArray();
        var candidate = pairs.Select(static pair => pair.CandidateObservation).ToArray();
        var regressions = new List<string>(2);
        CompareInterruptTail(
            "GPU-driver DPC p99",
            original,
            candidate,
            static observation => observation.GpuDriverDpcDurationMicroseconds,
            regressions);
        CompareInterruptTail(
            "GPU-driver ISR p99",
            original,
            candidate,
            static observation => observation.GpuDriverIsrDurationMicroseconds,
            regressions);
        if (regressions.Count == 0)
        {
            reason = string.Empty;
            return false;
        }

        reason = "Interrupt-tail guardrail regression: " + string.Join("; ", regressions) + ".";
        return true;
    }

    private static void CompareInterruptTail(
        string label,
        GpuAutoAffinityTrialObservation[] original,
        GpuAutoAffinityTrialObservation[] candidate,
        Func<GpuAutoAffinityTrialObservation, IReadOnlyList<double>> selector,
        List<string> regressions)
    {
        var originalRuns = BuildInterruptTailRunSeries(original, selector);
        var candidateRuns = BuildInterruptTailRunSeries(candidate, selector);
        if (originalRuns.Length < MinimumInterruptTailRuns || candidateRuns.Length < MinimumInterruptTailRuns)
        {
            return;
        }

        var originalMedian = Median(originalRuns);
        var candidateMedian = Median(candidateRuns);
        if (!double.IsFinite(originalMedian) || !double.IsFinite(candidateMedian) || originalMedian <= 0d)
        {
            return;
        }

        var allowedRegression = Math.Max(
            InterruptTailRegressionTolerance,
            Math.Max(
                MaximumRelativeDeviation(originalRuns, originalMedian),
                MaximumRelativeDeviation(candidateRuns, candidateMedian)));
        var regression = (candidateMedian - originalMedian) / originalMedian;
        if (regression > allowedRegression)
        {
            regressions.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{label} {originalMedian:F2} -> {candidateMedian:F2} us ({regression:P1}; limit {allowedRegression:P1})"));
        }
    }

    private static double[] BuildInterruptTailRunSeries(
        GpuAutoAffinityTrialObservation[] observations,
        Func<GpuAutoAffinityTrialObservation, IReadOnlyList<double>> selector) =>
        observations
            .Select(selector)
            .Where(static samples =>
                samples.Count >= MinimumInterruptTailSamples &&
                samples.All(static value => double.IsFinite(value) && value >= 0d))
            .Select(static samples => Percentiles.Calculate(samples, 0.99))
            .Where(static value => double.IsFinite(value) && value > 0d)
            .ToArray();

    private async Task<GpuAutoAffinitySessionResult> VerifyAndKeepFinalistAsync(
        GpuAutoAffinitySessionRequest request,
        DateTimeOffset startedAtUtc,
        GpuAutoAffinityDecisionBaselineReport decisionBaseline,
        GpuAffinityCandidate finalist,
        GpuBenchmarkEvidence reference,
        Func<int> nextRunNumber,
        List<GpuAutoAffinityCandidateReport> candidateReports,
        List<GpuAutoAffinityTrialReport> trialReports,
        List<GpuAutoAffinityPairReport> pairReports,
        List<GpuAutoAffinityFinalistReport> finalistReports,
        List<string> reasons,
        HashSet<LogicalProcessorId> screenedProcessors,
        bool fullTopologyCoverage,
        bool practicalTie,
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
                null,
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
                    "The paired GPU winner was not kept because final kernel ETW could not prove target-only runtime ISR placement on the selected CPU.");
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    restored, restored, candidateReports, trialReports, pairReports, finalistReports,
                    reasons, decisionBaseline, screenedProcessors, fullTopologyCoverage, practicalTie);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var keptId = activeExperiment.Value;
            if (keptId == Guid.Empty)
            {
                activeExperiment = null;
                var originalVerified = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                reasons.Add(
                    "The selected processor already represented the exact Original stored policy, so no write or Keep terminalization was necessary.");
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    originalVerified, originalVerified, candidateReports, trialReports, pairReports,
                    finalistReports, reasons, decisionBaseline, screenedProcessors,
                    fullTopologyCoverage, practicalTie);
            }

            await backend.KeepAsync(keptId, CancellationToken.None).ConfigureAwait(false);
            activeExperiment = null;
            reasons.Add(
                $"CPU {finalist.Processor.Number} was kept after repeated paired improvement and final kernel-ETW target-only ISR placement proof.");
            return CreateResult(
                request, startedAtUtc, GpuOptimizationRecommendation.KeepCandidate, finalist,
                finalStateVerified: true, originalStateRestored: false,
                candidateReports, trialReports, pairReports, finalistReports, reasons,
                decisionBaseline, screenedProcessors, fullTopologyCoverage, practicalTie);
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
                        "GPU winner final verification failed and rollback completed, but the exact Original state could not be verified.");
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

    private async Task RollbackAndVerifyOriginalAsync(
        Guid experimentId,
        string phase,
        GpuAffinityCandidate candidate)
    {
        await backend.RollbackAsync(experimentId, CancellationToken.None).ConfigureAwait(false);
        if (!await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"{phase}: exact Original GPU affinity state was not verified after rolling back {candidate.Processor}.");
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
                nextRunNumber(), phase, role, candidate, duration, retryAttempt);
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
                    context.Add("Runtime ISR placement is Unknown for this screening trial because resolved single-adapter ISR placement evidence is unavailable; absence of attributable ISR samples is not proof of off-target placement.");
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
            return OriginalEvaluation.Unrankable(
                "Original benchmark lacks complete frame-period statistics.", observations.Length);
        }
        var values = stats.Select(static item => item!).ToArray();
        if (values.Any(static value => !HasFiniteRankingStatistics(value)))
        {
            return OriginalEvaluation.Unrankable(
                "Original benchmark ranking statistics are missing or non-finite.", observations.Length);
        }

        var cluster = GpuRepeatabilityClusterSelector.Select(
            values.Select(static item => item.Low1PctFps).ToArray());
        if (cluster is null)
        {
            if (observations.Length < GpuRepeatabilityClusterSelector.MaximumOriginalAttemptCount)
            {
                return OriginalEvaluation.Unrankable(
                    $"No stable {GpuRepeatabilityClusterSelector.RequiredRunCount}-run Original 1% low cluster exists after {observations.Length} scored observation(s).",
                    observations.Length);
            }

            return OriginalEvaluation.Unrankable(
                $"No stable {GpuRepeatabilityClusterSelector.RequiredRunCount}-run Original 1% low cluster exists after the bounded {GpuRepeatabilityClusterSelector.MaximumOriginalAttemptCount} scored observations; candidate mutation is not allowed.",
                observations.Length);
        }
        if (observations.Length - cluster.Indexes.Length > MaximumRejectedOriginalRepeatabilityRuns)
        {
            return OriginalEvaluation.Unrankable(
                "Original repeatability excluded more observations than the bounded acquisition policy permits.",
                observations.Length);
        }

        var selected = cluster.Indexes.Select(index => values[index]).ToArray();
        var selectedObservations = cluster.Indexes.Select(index => observations[index]).ToArray();
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
            null);
    }

    private static GpuAutoAffinityDecisionBaselineReport ToDecisionBaseline(OriginalEvaluation original) =>
        new(
            original.MedianLow1Fps,
            original.MedianAvgFps,
            original.MedianFrameP99Milliseconds,
            original.MedianLow01Fps,
            original.PrimaryRelativeNoise,
            original.AvgRelativeNoise,
            original.FrameP99RelativeNoise,
            original.Low01RelativeNoise,
            original.ValidObservationCount,
            original.TotalObservationCount,
            UsedNoiseAwareFallback: false);

    private static bool HasFiniteRankingStatistics(GpuBenchmarkVideoStats item) =>
        double.IsFinite(item.Low1PctFps) && item.Low1PctFps > 0d &&
        double.IsFinite(item.Low01PctFps) && item.Low01PctFps > 0d &&
        double.IsFinite(item.AvgFps) && item.AvgFps > 0d &&
        double.IsFinite(item.P99Milliseconds) && item.P99Milliseconds > 0d;

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
        List<GpuAutoAffinityPairReport> pairReports,
        List<GpuAutoAffinityFinalistReport> finalistReports,
        List<string> reasons,
        GpuAutoAffinityDecisionBaselineReport? decisionBaseline,
        HashSet<LogicalProcessorId> screenedProcessors,
        bool fullTopologyCoverage,
        bool practicalTie)
    {
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
            reasons.AsReadOnly(),
            DecisionBaseline: decisionBaseline)
        {
            SearchScope = request.SearchScope,
            RequestedProcessors = request.RequestedProcessors?.ToArray() ?? [],
            ValidatedProcessors = screenedProcessors
                .OrderBy(static processor => processor.Group)
                .ThenBy(static processor => processor.Number)
                .ToArray(),
            FullTopologyCoverage = request.SearchScope == GpuAutoAffinitySearchScope.Full && fullTopologyCoverage,
            Pairs = pairReports.AsReadOnly(),
            Finalists = finalistReports.AsReadOnly(),
            PracticalTie = practicalTie,
        };
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
        if (request.ScreeningDuration != V2ScreeningDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"GPU paired v2 screening duration must be exactly {V2ScreeningDuration.TotalSeconds:F0} seconds.");
        }
        if (request.FinalistDuration != default && request.FinalistDuration != V2FinalistDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"GPU paired v2 finalist duration must be exactly {V2FinalistDuration.TotalSeconds:F0} seconds.");
        }

        var requested = request.RequestedProcessors?.ToArray() ?? [];
        if (request.SearchScope == GpuAutoAffinitySearchScope.Custom)
        {
            if (requested.Length == 0)
            {
                throw new ArgumentException("Custom GPU search scope requires at least one logical processor.", nameof(request));
            }
            if (requested.Distinct().Count() != requested.Length)
            {
                throw new ArgumentException("Custom GPU search scope contains duplicate logical processors.", nameof(request));
            }
            if (requested.Any(static processor => processor.Group != 0))
            {
                throw new NotSupportedException("Custom GPU search currently supports processor group 0 only.");
            }
        }
        else if (requested.Length != 0)
        {
            throw new ArgumentException("Full GPU search scope must not supply a custom processor subset.", nameof(request));
        }
    }

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

    private sealed record PairMeasurement(
        GpuAffinityCandidate Candidate,
        GpuAutoAffinityPairReport Report,
        GpuAutoAffinityTrialObservation OriginalBefore,
        GpuAutoAffinityTrialObservation CandidateObservation,
        GpuAutoAffinityTrialObservation OriginalAfter);

    private sealed record PairOutcome(
        GpuAutoAffinityPairReport FinalReport,
        GpuAutoAffinityTrialObservation? NextOriginal,
        PairMeasurement? ValidMeasurement);

    private sealed record FinalistDecision(
        GpuAffinityCandidate Candidate,
        GpuAutoAffinityFinalistReport Report,
        bool ImprovementCapable);

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
        string? Reason)
    {
        internal static OriginalEvaluation Unrankable(string reason, int totalObservationCount = 0) =>
            new([], double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity,
                double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity,
                double.PositiveInfinity, 0, totalObservationCount, false, reason);
    }

    private enum PlacementEvidenceState
    {
        Unknown,
        Verified,
        Contradicted,
    }

    private sealed class SessionAbortException(string message) : Exception(message);
}

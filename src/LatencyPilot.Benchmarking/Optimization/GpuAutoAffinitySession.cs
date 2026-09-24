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
/// evidence. Ordinary drift lowers confidence instead of erasing a structurally valid
/// winner. Screening is adaptive: uncertain near-leaders get one short recheck before
/// only the top two receive 15-second confirmation. Exact rollback remains mandatory
/// and final Keep still requires ETW-backed target-only runtime ISR placement proof.
/// </summary>
public sealed class GpuAutoAffinitySession
{
    public static readonly TimeSpan ScreeningDuration = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FinalistDuration = TimeSpan.FromSeconds(15);

    // Compatibility aliases for historical internal callers.
    public static readonly TimeSpan V2ScreeningDuration = ScreeningDuration;
    public static readonly TimeSpan V2FinalistDuration = FinalistDuration;

    private const double CandidateMetricEquivalenceTolerance = 0.01;
    private const double KeepGuardrailRegressionTolerance = 0.03;
    private const double MaximumKeepGuardrailRegressionTolerance = 0.10;
    private const double InterruptTailRegressionTolerance = 0.10;
    private const int MinimumInterruptTailSamples = 20;
    private const int MinimumInterruptTailRuns = 2;
    private const int MaximumPairAttempts = 2;
    internal const int MaximumPhysicalCoreHypotheses = 4;
    internal const int MaximumAdaptiveShortlistCandidates = 5;
    internal const int MaximumFinalists = 2;
    private const int MinimumFinalistPairs = 2;
    private const int MaximumFinalistPairs = 3;
    private const string ShortlistPhaseName = "screening-shortlist";
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
            ? FinalistDuration
            : request.FinalistDuration;

        if (candidates.Length == 0 && request.SearchScope != GpuAutoAffinitySearchScope.OriginalDiagnostics)
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

            if (request.SearchScope == GpuAutoAffinitySearchScope.OriginalDiagnostics)
            {
                var observations = new List<GpuAutoAffinityTrialObservation>();
                for (var index = 0; index < GpuOriginalBaselinePolicy.MaximumScoredObservationCount; index++)
                {
                    observations.Add(await CaptureAcceptedAsync(
                        () => ++nextRunNumber, "diagnostic-original", GpuConfirmationOrder.Original,
                        null, request.ScreeningDuration, reference, null, trialReports,
                        cancellationToken).ConfigureAwait(false));
                }

                // Diagnose all five observations, without selecting a quiet subset.
                var stats = observations.Select(item => RequireVideoStats(item, "Original diagnostic")).ToArray();
                var low1Noise = RelativeNoise(stats.Select(static item => item.Low1PctFps), Median(stats.Select(static item => item.Low1PctFps)));
                var avgNoise = RelativeNoise(stats.Select(static item => item.AvgFps), Median(stats.Select(static item => item.AvgFps)));
                var p99Noise = RelativeNoise(stats.Select(static item => item.P99Milliseconds), Median(stats.Select(static item => item.P99Milliseconds)));
                var repeatable = Math.Max(low1Noise, Math.Max(avgNoise, p99Noise)) <= 0.06d;
                var diagnostic = new GpuOriginalDiagnosticReport(
                    observations.Count, low1Noise, avgNoise, p99Noise, repeatable,
                    $"All {observations.Count} Original observations retained: 1%-low variation {low1Noise:P1}, AVG {avgNoise:P1}, p99 {p99Noise:P1}. " +
                    (repeatable ? "Repeatable within the 6% diagnostic budget. " : "Measurement is unstable before any affinity change. ") +
                    "No device restart or affinity change was performed; restart sensitivity and candidate benefit remain untested.");
                reasons.Add(diagnostic.Reason);
                var verified = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                var diagnosticResult = CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    verified, verified, candidateReports, trialReports, pairReports, finalistReports,
                    reasons, null, screenedProcessors, fullTopologyCoverage: false, practicalTie: false);
                return diagnosticResult with { Report = diagnosticResult.Report with { OriginalDiagnostic = diagnostic } };
            }

            var original = await QualifyOriginalAsync(
                request.ScreeningDuration,
                reference,
                () => ++nextRunNumber,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            if (!original.IsRankable)
            {
                throw new SessionAbortException(
                    $"Original benchmark evidence is structurally unusable: {original.Reason}");
            }

            decisionBaseline = ToDecisionBaseline(original);
            if (original.TotalObservationCount > GpuRepeatabilityClusterSelector.RequiredRunCount)
            {
                reasons.Add(string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"Original variability was elevated, so {original.TotalObservationCount} scored observations were retained instead of rejecting the search. Robust 1%-low variability is {original.PrimaryRelativeNoise:P2}; noise lowers selection confidence but does not erase the best observed CPU."));
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
                }

                var candidateReport = ToScreeningCandidateReport(candidate, outcome.FinalReport, outcome.ValidMeasurement);
                candidateReports.Add(candidateReport);
                await PublishCandidateReportAsync(candidateReport).ConfigureAwait(false);
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
                }

                var candidateReport = ToScreeningCandidateReport(candidate, outcome.FinalReport, outcome.ValidMeasurement);
                candidateReports.Add(candidateReport);
                await PublishCandidateReportAsync(candidateReport).ConfigureAwait(false);
            }

            ApplyPairDecisionRanks(candidateReports, screeningMeasurements);
            var shortlistCandidates = SelectAdaptiveShortlist(screeningMeasurements);
            if (shortlistCandidates.Length == 0)
            {
                reasons.Add("No structurally valid paired screening candidate remained for adaptive recheck. Original was retained.");
                var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    restored, restored, candidateReports, trialReports, pairReports, finalistReports,
                    reasons, decisionBaseline, screenedProcessors,
                    fullTopologyCoverage: stageAComplete, practicalTie: false);
            }

            reasons.Add(
                $"Adaptive shortlist retained {shortlistCandidates.Length} CPU(s) whose bounded screening uncertainty still overlapped the leader; clear losers were not re-tested.");
            var shortlistOrder = shortlistCandidates.ToArray();
            ShuffleDeterministically(shortlistOrder, unchecked(request.ShuffleSeed ^ 0x62A9D9ED));
            foreach (var candidate in shortlistOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (originalBefore is null)
                {
                    originalBefore = await CaptureOriginalControlAsync(
                        "screening-shortlist-recovery-original-control",
                        request.ScreeningDuration,
                        reference,
                        () => ++nextRunNumber,
                        trialReports,
                        cancellationToken).ConfigureAwait(false);
                }

                var outcome = await MeasureScreeningPairAsync(
                    candidate,
                    ShortlistPhaseName,
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
                }

                var candidateReport = ToScreeningCandidateReport(
                    candidate,
                    outcome.FinalReport,
                    outcome.ValidMeasurement,
                    ShortlistPhaseName);
                candidateReports.Add(candidateReport);
                await PublishCandidateReportAsync(candidateReport).ConfigureAwait(false);
            }

            var finalistCandidates = SelectFinalists(screeningMeasurements, shortlistCandidates);
            if (finalistCandidates.Length == 0)
            {
                reasons.Add("No adaptive-shortlist candidate produced structurally valid confirmation evidence. Original was retained.");
                var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    restored, restored, candidateReports, trialReports, pairReports, finalistReports,
                    reasons, decisionBaseline, screenedProcessors,
                    fullTopologyCoverage: stageAComplete, practicalTie: false);
            }

            reasons.Add(
                $"Adaptive recheck advanced {finalistCandidates.Length} CPU(s) to 15-second finalist confirmation.");
            var finalistOriginal = await CaptureOriginalControlAsync(
                "finalist-original-control",
                finalistDuration,
                reference,
                () => ++nextRunNumber,
                trialReports,
                cancellationToken).ConfigureAwait(false);
            var finalistMeasurements = finalistCandidates.ToDictionary(
                static candidate => candidate.Processor,
                static _ => new List<PairMeasurement>(MaximumFinalistPairs));

            for (var round = 0; round < MaximumFinalistPairs; round++)
            {
                var roundCandidates = finalistCandidates.ToArray();
                ShuffleDeterministically(
                    roundCandidates,
                    unchecked(request.ShuffleSeed ^ (int)(0x9E3779B9u * (uint)(round + 1))));

                foreach (var candidate in roundCandidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var outcome = await MeasureScreeningPairAsync(
                        candidate,
                        FinalistPhaseName,
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
                    else if (round < MaximumFinalistPairs - 1 ||
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
                    }
                }

                if (round + 1 >= MinimumFinalistPairs)
                {
                    if (!FinalistsNeedMoreEvidence(finalistCandidates, finalistMeasurements))
                    {
                        reasons.Add(
                            $"Finalist confirmation stopped after {round + 1} round(s) because the top-two separation exceeded measured uncertainty.");
                        break;
                    }

                    if (round + 1 < MaximumFinalistPairs)
                    {
                        reasons.Add(
                            "The two finalists remain close relative to measured uncertainty; one final 15-second round is being added instead of extending every candidate.");
                    }
                }
            }

            var finalistDecisions = BuildFinalistReports(
                finalistCandidates,
                finalistMeasurements,
                finalistReports,
                candidateReports);
            var rankedFinalists = finalistDecisions
                .Where(static decision => decision.Report.MedianOnePercentLowEffect is not null)
                .OrderByDescending(static decision => decision.Report.MedianOnePercentLowEffect)
                .ThenBy(static decision => decision.Candidate.ObservedPressureScore)
                .ThenBy(static decision => decision.Candidate.PhysicalCoreIndex)
                .ThenBy(static decision => decision.Candidate.Processor.Number)
                .ToArray();

            if (rankedFinalists.Length == 0)
            {
                reasons.Add("No finalist produced structurally valid benchmark evidence. Original was retained.");
                var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    restored, restored, candidateReports, trialReports, pairReports, finalistReports,
                    reasons, decisionBaseline, screenedProcessors,
                    fullTopologyCoverage: stageAComplete, practicalTie: false);
            }

            var selected = rankedFinalists[0];
            var bestEffect = selected.Report.MedianOnePercentLowEffect!.Value;
            var practicalTie = rankedFinalists
                .Skip(1)
                .Any(decision =>
                    Math.Abs(decision.Report.MedianOnePercentLowEffect!.Value - bestEffect) <=
                    CandidateMetricEquivalenceTolerance);

            reasons.Add(practicalTie
                ? string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"CPU {selected.Candidate.Processor.Number} is the best observed finalist inside a practical tie. Median paired 1%-low effect {selected.Report.MedianOnePercentLowEffect:P2}; confidence is reduced rather than deleting the winner.")
                : string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"CPU {selected.Candidate.Processor.Number} is the best observed finalist. Median paired 1%-low effect {selected.Report.MedianOnePercentLowEffect:P2}."));

            if (!selected.RecommendedForKeep)
            {
                reasons.Add(
                    $"CPU {selected.Candidate.Processor.Number} remains the best observed CPU, but its median benefit is non-positive or a material guardrail regressed. The exact Original state is retained.");
                var restored = await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false);
                return CreateResult(
                    request, startedAtUtc, GpuOptimizationRecommendation.RestoreOriginal, null,
                    restored, restored, candidateReports, trialReports, pairReports, finalistReports,
                    reasons, decisionBaseline, screenedProcessors,
                    fullTopologyCoverage: stageAComplete, practicalTie);
            }

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
            "Original variability estimation has not collected three scored observations yet.");

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
            if (!evaluation.IsRankable)
            {
                break;
            }

            if (evaluation.PrimaryRelativeNoise <= GpuRepeatabilityClusterSelector.RelativeTolerance ||
                observations.Count == GpuRepeatabilityClusterSelector.MaximumOriginalAttemptCount)
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
                    Verdict = GpuAutoAffinityPairVerdict.Valid,
                    Reason = $"Local Original movement {pair.ControlMovement:P2} remained above the {pair.DriftBudget:P2} noise guide after one retry. The measurement remains rankable; elevated drift reduces confidence instead of erasing this CPU from the comparison.",
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

    private static HashSet<int> SelectPhysicalCoreHypotheses(List<PairMeasurement> measurements) =>
        SelectPlausibleScreeningAggregates(measurements, MaximumPhysicalCoreHypotheses)
            .Select(static aggregate => aggregate.Candidate.PhysicalCoreIndex)
            .ToHashSet();

    private static GpuAffinityCandidate[] SelectAdaptiveShortlist(List<PairMeasurement> measurements) =>
        SelectPlausibleScreeningAggregates(measurements, MaximumAdaptiveShortlistCandidates)
            .Select(static aggregate => aggregate.Candidate)
            .ToArray();

    private static GpuAffinityCandidate[] SelectFinalists(
        List<PairMeasurement> measurements,
        GpuAffinityCandidate[] shortlist)
    {
        var allowed = shortlist.Select(static candidate => candidate.Processor).ToHashSet();
        return BuildScreeningAggregates(measurements)
            .Where(aggregate => allowed.Contains(aggregate.Candidate.Processor))
            .OrderByDescending(static aggregate => aggregate.MedianOnePercentLowEffect)
            .ThenBy(static aggregate => aggregate.Candidate.ObservedPressureScore)
            .ThenBy(static aggregate => aggregate.Candidate.PhysicalCoreIndex)
            .ThenBy(static aggregate => aggregate.Candidate.Processor.Number)
            .Take(MaximumFinalists)
            .Select(static aggregate => aggregate.Candidate)
            .ToArray();
    }

    private static ScreeningAggregate[] SelectPlausibleScreeningAggregates(
        List<PairMeasurement> measurements,
        int maximumCount)
    {
        var ordered = BuildScreeningAggregates(measurements)
            .OrderByDescending(static aggregate => aggregate.MedianOnePercentLowEffect)
            .ThenBy(static aggregate => aggregate.Candidate.ObservedPressureScore)
            .ThenBy(static aggregate => aggregate.Candidate.PhysicalCoreIndex)
            .ThenBy(static aggregate => aggregate.Candidate.Processor.Number)
            .ToArray();
        if (ordered.Length == 0)
        {
            return [];
        }

        var boundedMaximum = Math.Min(maximumCount, ordered.Length);
        var guaranteedCount = Math.Min(2, boundedMaximum);
        var guaranteed = ordered.Take(guaranteedCount);
        if (guaranteedCount == boundedMaximum)
        {
            return guaranteed.ToArray();
        }

        // Never let one noisy point estimate collapse a multi-candidate search to
        // one CPU. The observed top two stay alive whenever two structurally valid
        // candidates exist; uncertainty is used only to admit additional plausible
        // challengers up to the bounded budget.
        var plausibilityFloor = ordered[0].MedianOnePercentLowEffect - CandidateMetricEquivalenceTolerance;
        var challengers = ordered
            .Skip(guaranteedCount)
            .Where(aggregate =>
                aggregate.MedianOnePercentLowEffect + aggregate.UncertaintyFraction >= plausibilityFloor)
            .Take(boundedMaximum - guaranteedCount);

        return guaranteed.Concat(challengers).ToArray();
    }

    private static ScreeningAggregate[] BuildScreeningAggregates(IEnumerable<PairMeasurement> measurements) =>
        measurements
            .Where(static measurement => measurement.Report.Verdict == GpuAutoAffinityPairVerdict.Valid)
            .GroupBy(static measurement => measurement.Candidate.Processor)
            .Select(static group =>
            {
                var pairs = group.ToArray();
                var candidate = pairs[0].Candidate;
                var effects = pairs.Select(static pair => pair.Report.OnePercentLowEffect).ToArray();
                var medianEffect = Median(effects);
                var effectMad = MedianAbsoluteDeviation(effects, medianEffect);
                var boundedControlNoise = Median(pairs.Select(static pair =>
                    Math.Min(pair.Report.ControlMovement, pair.Report.DriftBudget)));
                return new ScreeningAggregate(
                    candidate,
                    medianEffect,
                    Math.Max(
                        CandidateMetricEquivalenceTolerance,
                        Math.Max(effectMad, boundedControlNoise)),
                    pairs.Length);
            })
            .ToArray();

    private static bool FinalistsNeedMoreEvidence(
        GpuAffinityCandidate[] finalists,
        Dictionary<LogicalProcessorId, List<PairMeasurement>> measurements)
    {
        if (finalists.Length < 2)
        {
            return false;
        }

        var aggregates = finalists
            .Select(candidate =>
            {
                var pairs = measurements[candidate.Processor];
                if (pairs.Count < MinimumFinalistPairs)
                {
                    return new ScreeningAggregate(candidate, double.NegativeInfinity, double.PositiveInfinity, pairs.Count);
                }

                var effects = pairs.Select(static pair => pair.Report.OnePercentLowEffect).ToArray();
                var median = Median(effects);
                var mad = MedianAbsoluteDeviation(effects, median);
                var boundedControlNoise = Median(pairs.Select(static pair =>
                    Math.Min(pair.Report.ControlMovement, pair.Report.DriftBudget)));
                return new ScreeningAggregate(
                    candidate,
                    median,
                    Math.Max(CandidateMetricEquivalenceTolerance, Math.Max(mad, boundedControlNoise)),
                    pairs.Count);
            })
            .OrderByDescending(static aggregate => aggregate.MedianOnePercentLowEffect)
            .ToArray();

        if (aggregates.Any(static aggregate => aggregate.PairCount < MinimumFinalistPairs))
        {
            return true;
        }

        var lead = aggregates[0].MedianOnePercentLowEffect - aggregates[1].MedianOnePercentLowEffect;
        var uncertainty = Math.Max(aggregates[0].UncertaintyFraction, aggregates[1].UncertaintyFraction);
        return lead <= Math.Max(CandidateMetricEquivalenceTolerance, uncertainty);
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
        PairMeasurement? measurement,
        string reportPhase = "screening")
    {
        var stats = measurement is null ? null : RequireVideoStats(measurement.CandidateObservation, "Candidate");
        return new GpuAutoAffinityCandidateReport(
            reportPhase,
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
            UsesTimeLocalNormalization: false)
        {
            DecisionOnePercentLowEffect = measurement is null ? null : pair.OnePercentLowEffect,
            DecisionAvgEffect = measurement is null ? null : pair.AvgEffect,
            DecisionFrameP99Effect = measurement is null ? null : pair.FrameP99Effect,
            DecisionLow01PctEffect = measurement is null ? null : pair.Low01PctEffect,
        };
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
        List<GpuAutoAffinityFinalistReport> reports,
        List<GpuAutoAffinityCandidateReport> candidateReports)
    {
        var decisions = new List<FinalistDecision>(finalists.Length);
        foreach (var candidate in finalists)
        {
            var pairs = measurements[candidate.Processor];
            GpuAutoAffinityFinalistReport report;
            var recommendedForKeep = false;

            if (pairs.Count == 0)
            {
                report = new GpuAutoAffinityFinalistReport(
                    candidate.Processor,
                    candidate.PhysicalCoreIndex,
                    [],
                    null,
                    null,
                    null,
                    null,
                    "Inconclusive",
                    "Finalist produced no structurally valid paired benchmark observations.");
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
                var beforeStats = pairs
                    .Select(static pair => RequireVideoStats(pair.OriginalBefore, "Finalist OriginalBefore"))
                    .ToArray();
                var candidateStats = pairs
                    .Select(static pair => RequireVideoStats(pair.CandidateObservation, "Finalist candidate"))
                    .ToArray();
                var afterStats = pairs
                    .Select(static pair => RequireVideoStats(pair.OriginalAfter, "Finalist OriginalAfter"))
                    .ToArray();

                var medianPrimary = Median(primary);
                var medianAvg = Median(avg);
                var medianP99 = Median(p99);
                var effectMad = MedianAbsoluteDeviation(primary, medianPrimary);
                var controlMovement = Median(pairs.Select(static pair => pair.Report.ControlMovement));
                var noiseGuide = Math.Max(CandidateMetricEquivalenceTolerance, Math.Max(effectMad, controlMovement));
                var positivePrimaryCount = primary.Count(static effect => effect > 0d);
                var keepGuardrailTolerance = Math.Clamp(
                    Math.Max(KeepGuardrailRegressionTolerance, effectMad),
                    KeepGuardrailRegressionTolerance,
                    MaximumKeepGuardrailRegressionTolerance);
                var guardrailRegression =
                    medianAvg < -keepGuardrailTolerance ||
                    medianP99 < -keepGuardrailTolerance;
                var interruptRegression = HasMaterialInterruptTailRegression(pairs, out var interruptReason);
                var requiredPositivePairs = RequiredPositivePairCount(pairs.Count);
                recommendedForKeep =
                    medianPrimary > 0d &&
                    positivePrimaryCount >= requiredPositivePairs &&
                    !guardrailRegression &&
                    !interruptRegression;

                var reason = FormattableString.Invariant(
                    $"Ranked from {pairs.Count} paired observation(s): median 1%-low effect {medianPrimary:P2}; effect MAD {effectMad:P2}; positive pairs {positivePrimaryCount}/{pairs.Count} (Keep requires {requiredPositivePairs}); local-noise guide {noiseGuide:P2}; Keep guardrail tolerance {keepGuardrailTolerance:P2}; AVG/p99 regression={guardrailRegression}; interrupt-tail regression={interruptRegression}. ") +
                    (recommendedForKeep
                        ? "This candidate may be kept if final runtime placement verifies."
                        : "It remains rankable as the best-observed candidate, but automatic Keep is not recommended by the measured benefit/guardrails.") +
                    (string.IsNullOrWhiteSpace(interruptReason) ? string.Empty : $" {interruptReason}");

                var medianOriginalLow1 = Median(beforeStats.Select((item, index) =>
                    GeometricMean(item.Low1PctFps, afterStats[index].Low1PctFps)));
                var medianOriginalAvg = Median(beforeStats.Select((item, index) =>
                    GeometricMean(item.AvgFps, afterStats[index].AvgFps)));
                var medianOriginalP99 = Median(beforeStats.Select((item, index) =>
                    GeometricMean(item.P99Milliseconds, afterStats[index].P99Milliseconds)));
                var medianOriginalLow01 = Median(beforeStats.Select((item, index) =>
                    GeometricMean(item.Low01PctFps, afterStats[index].Low01PctFps)));

                report = new GpuAutoAffinityFinalistReport(
                    candidate.Processor,
                    candidate.PhysicalCoreIndex,
                    pairs.Select(static pair => pair.Report.PairNumber).ToArray(),
                    medianPrimary,
                    medianAvg,
                    medianP99,
                    low01.Length == 0 ? null : Median(low01),
                    "Ranked",
                    reason)
                {
                    DecisionFloor = noiseGuide,
                    NoiseFraction = noiseGuide,
                    OnePercentLowEffectMedianAbsoluteDeviation = effectMad,
                    PositiveOnePercentLowPairCount = positivePrimaryCount,
                    MedianOriginalOnePercentLowFps = medianOriginalLow1,
                    MedianCandidateOnePercentLowFps = Median(candidateStats.Select(static item => item.Low1PctFps)),
                    MedianOriginalAvgFps = medianOriginalAvg,
                    MedianCandidateAvgFps = Median(candidateStats.Select(static item => item.AvgFps)),
                    MedianOriginalFrameP99Milliseconds = medianOriginalP99,
                    MedianCandidateFrameP99Milliseconds = Median(candidateStats.Select(static item => item.P99Milliseconds)),
                    MedianOriginalLow01PctFps = medianOriginalLow01,
                    MedianCandidateLow01PctFps = Median(candidateStats.Select(static item => item.Low01PctFps)),
                    RecommendedForKeep = recommendedForKeep,
                };
            }

            reports.Add(report);
            candidateReports.Add(new GpuAutoAffinityCandidateReport(
                FinalistPhaseName,
                candidate.PhysicalCoreIndex,
                candidate.Processor,
                pairs.Count,
                report.MedianOnePercentLowEffect is null ? "Inconclusive" : "Ranked",
                report.MedianFrameP99Effect,
                [],
                report.Reason,
                report.MedianCandidateOnePercentLowFps,
                report.MedianCandidateAvgFps,
                report.MedianCandidateFrameP99Milliseconds,
                report.MedianCandidateLow01PctFps,
                report.NoiseFraction,
                UsesTimeLocalNormalization: false)
            {
                DecisionOnePercentLowEffect = report.MedianOnePercentLowEffect,
                DecisionAvgEffect = report.MedianAvgEffect,
                DecisionFrameP99Effect = report.MedianFrameP99Effect,
                DecisionLow01PctEffect = report.MedianLow01PctEffect,
            });
            decisions.Add(new FinalistDecision(candidate, report, recommendedForKeep));
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

    private static int RequiredPositivePairCount(int pairCount) =>
        pairCount >= MaximumFinalistPairs ? 2 : Math.Min(2, pairCount);

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
                $"Best observed CPU {finalist.Processor.Number} was kept after final kernel-ETW target-only ISR placement proof.");
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

        if (observations.Length < GpuRepeatabilityClusterSelector.RequiredRunCount)
        {
            return OriginalEvaluation.Unrankable(
                $"At least {GpuRepeatabilityClusterSelector.RequiredRunCount} scored Original observations are required.",
                observations.Length);
        }

        var medianLow1 = Median(values.Select(static item => item.Low1PctFps));
        var medianLow01 = Median(values.Select(static item => item.Low01PctFps));
        var medianAvg = Median(values.Select(static item => item.AvgFps));
        var medianP99 = Median(values.Select(static item => item.P99Milliseconds));
        return new OriginalEvaluation(
            observations,
            medianLow1,
            medianLow01,
            medianAvg,
            medianP99,
            RelativeMedianAbsoluteDeviation(values.Select(static item => item.Low1PctFps), medianLow1),
            RelativeMedianAbsoluteDeviation(values.Select(static item => item.Low01PctFps), medianLow01),
            RelativeMedianAbsoluteDeviation(values.Select(static item => item.AvgFps), medianAvg),
            RelativeMedianAbsoluteDeviation(values.Select(static item => item.P99Milliseconds), medianP99),
            observations.Length,
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
            UsedNoiseAwareFallback: original.TotalObservationCount > GpuRepeatabilityClusterSelector.RequiredRunCount);

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
            observation.Evidence.PresentMonCapture is { IsAvailable: true } presentMon &&
            double.IsFinite(presentMon.ActualWindowMilliseconds) &&
            presentMon.ActualWindowMilliseconds > 0
                ? presentMon.ActualWindowMilliseconds
                : request.Duration.TotalMilliseconds,
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
        var bestObserved = candidateReports
            .Where(static candidate =>
                candidate.DecisionRank == 1 &&
                candidate.DecisionOnePercentLowEffect is { } effect &&
                double.IsFinite(effect))
            .OrderByDescending(static candidate =>
                string.Equals(candidate.Phase, FinalistPhaseName, StringComparison.Ordinal))
            .FirstOrDefault();
        var selectionConfidence = DetermineSelectionConfidence(
            bestObserved?.Processor,
            finalistReports,
            decisionBaseline,
            practicalTie);

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
            BestObservedProcessor = bestObserved?.Processor,
            BestObservedOnePercentLowEffect = bestObserved?.DecisionOnePercentLowEffect,
            SelectionConfidence = selectionConfidence,
        };
        return new GpuAutoAffinitySessionResult(recommendation, terminalFinalist, report);
    }

    private static string DetermineSelectionConfidence(
        LogicalProcessorId? bestProcessor,
        IReadOnlyList<GpuAutoAffinityFinalistReport> finalistReports,
        GpuAutoAffinityDecisionBaselineReport? decisionBaseline,
        bool practicalTie)
    {
        if (bestProcessor is null)
        {
            return "Unavailable";
        }

        var ranked = finalistReports
            .Where(static report =>
                report.MedianOnePercentLowEffect is { } effect &&
                double.IsFinite(effect))
            .OrderByDescending(static report => report.MedianOnePercentLowEffect)
            .ThenBy(static report => report.PhysicalCoreIndex)
            .ThenBy(static report => report.Processor.Number)
            .ToArray();
        var best = ranked.FirstOrDefault(report => report.Processor == bestProcessor);
        if (best is null || best.MedianOnePercentLowEffect is not { } bestEffect || bestEffect <= 0d)
        {
            return "Low";
        }
        if (practicalTie)
        {
            return "Low";
        }

        var runnerUp = ranked.FirstOrDefault(report => report.Processor != bestProcessor);
        if (runnerUp is null || runnerUp.MedianOnePercentLowEffect is not { } runnerEffect)
        {
            return best.PositiveOnePercentLowPairCount >= RequiredPositivePairCount(best.PairNumbers.Count)
                ? "Medium"
                : "Low";
        }

        var lead = bestEffect - runnerEffect;
        var variability = Math.Max(
            best.OnePercentLowEffectMedianAbsoluteDeviation ?? 0d,
            runnerUp.OnePercentLowEffectMedianAbsoluteDeviation ?? 0d);
        if (decisionBaseline is not null)
        {
            variability = Math.Max(variability, decisionBaseline.OnePercentLowRelativeNoise);
        }

        if (best.PairNumbers.Count >= MinimumFinalistPairs &&
            best.PositiveOnePercentLowPairCount == best.PairNumbers.Count &&
            lead > Math.Max(CandidateMetricEquivalenceTolerance, variability))
        {
            return "High";
        }

        var mediumLeadFloor = Math.Max(0.005d, variability * 0.5d);
        return best.PositiveOnePercentLowPairCount >= RequiredPositivePairCount(best.PairNumbers.Count) &&
               lead > mediumLeadFloor
            ? "Medium"
            : "Low";
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
        if (!Enum.IsDefined(request.SearchScope))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Unknown GPU search scope.");
        }
        if (request.SessionId == Guid.Empty)
        {
            throw new ArgumentException("GPU auto-affinity session identity is required.", nameof(request));
        }
        if (request.ScreeningDuration != ScreeningDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"GPU adaptive screening duration must be exactly {ScreeningDuration.TotalSeconds:F0} seconds.");
        }
        if (request.FinalistDuration != default && request.FinalistDuration != FinalistDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"GPU adaptive finalist duration must be exactly {FinalistDuration.TotalSeconds:F0} seconds.");
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

    private static double RelativeMedianAbsoluteDeviation(IEnumerable<double> values, double median)
    {
        if (!double.IsFinite(median) || median <= 0d)
        {
            return double.PositiveInfinity;
        }

        return MedianAbsoluteDeviation(values, median) / Math.Abs(median);
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

    private sealed record ScreeningAggregate(
        GpuAffinityCandidate Candidate,
        double MedianOnePercentLowEffect,
        double UncertaintyFraction,
        int PairCount);

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

    private static double MedianAbsoluteDeviation(IEnumerable<double> source, double median) =>
        Median(source.Select(value => Math.Abs(value - median)));

    private sealed record FinalistDecision(
        GpuAffinityCandidate Candidate,
        GpuAutoAffinityFinalistReport Report,
        bool RecommendedForKeep);

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

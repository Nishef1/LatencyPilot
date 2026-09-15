using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;

namespace LatencyPilot.Service;

internal sealed record GpuOptimizationOrchestrationRequest(
    string DeviceInstanceId,
    uint WorkloadProcessId,
    GpuOptimizationBaselineEvidence Baseline,
    IReadOnlyList<GpuAffinityCandidate> Candidates,
    TimeSpan ScreeningDuration,
    TimeSpan ConfirmationDuration,
    ComparisonPolicy Policy,
    string? PresentMonApiPath = null,
    string? PresentMonControlPipeName = null);

internal sealed record GpuOptimizationCandidateRunRecord(
    GpuAffinityCandidate Candidate,
    bool EvidenceUsable,
    string? Reason);

internal sealed record GpuOptimizationOrchestrationResult(
    GpuOptimizationRecommendation Recommendation,
    GpuOptimizationScreeningResult? Screening,
    GpuOptimizationConfirmationResult? Confirmation,
    IReadOnlyList<GpuOptimizationCandidateRunRecord> CandidateRuns,
    IReadOnlyList<string> Reasons);

internal interface IGpuOptimizationExecutionBackend
{
    GpuInterruptAffinitySnapshot CaptureOriginal(string deviceInstanceId);

    Guid ApplyCandidate(string deviceInstanceId, GpuAffinityCandidate candidate);

    void BeginMeasurement(Guid experimentId);

    Task<GpuOptimizationEvidenceCollectionResult> CaptureAsync(
        GpuOptimizationEvidenceRequest request,
        GpuInterruptAffinitySnapshot originalState,
        string? presentMonApiPath,
        string? presentMonControlPipeName,
        CancellationToken cancellationToken);

    void Rollback(Guid experimentId);
}

internal sealed class GpuOptimizationOrchestrator
{
    private readonly IGpuOptimizationExecutionBackend backend;

    internal GpuOptimizationOrchestrator(IGpuOptimizationExecutionBackend backend)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    internal GpuOptimizationOrchestrator(MutationJournal journal)
        : this(new GpuOptimizationExecutionBackend(journal))
    {
    }

    internal async Task<GpuOptimizationOrchestrationResult> RunAsync(
        GpuOptimizationOrchestrationRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = request.Candidates.ToArray();
        if (candidates.Length == 0)
        {
            return new GpuOptimizationOrchestrationResult(
                GpuOptimizationRecommendation.RestoreOriginal,
                null,
                null,
                [],
                ["No bounded GPU affinity candidate is available for measurement."]);
        }

        var originalState = backend.CaptureOriginal(request.DeviceInstanceId);
        var originalRequest = CreateEvidenceRequest(
            request,
            runNumber: 1,
            GpuConfirmationOrder.Original,
            candidates[0],
            request.ScreeningDuration);
        var originalEvidence = await backend.CaptureAsync(
            originalRequest,
            originalState,
            request.PresentMonApiPath,
            request.PresentMonControlPipeName,
            cancellationToken).ConfigureAwait(false);
        if (!originalEvidence.IsUsable || originalEvidence.Run is null)
        {
            return new GpuOptimizationOrchestrationResult(
                GpuOptimizationRecommendation.RestoreOriginal,
                null,
                null,
                [],
                [originalEvidence.Reason ?? "Original-state screening evidence is unusable."]);
        }

        var measuredCandidates = new List<GpuOptimizationCandidateMeasurement>(candidates.Length);
        var candidateRuns = new List<GpuOptimizationCandidateRunRecord>(candidates.Length);
        for (var index = 0; index < candidates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            var experimentId = backend.ApplyCandidate(request.DeviceInstanceId, candidate);
            backend.BeginMeasurement(experimentId);

            GpuOptimizationEvidenceCollectionResult candidateEvidence;
            try
            {
                candidateEvidence = await backend.CaptureAsync(
                    CreateEvidenceRequest(
                        request,
                        runNumber: index + 2,
                        GpuConfirmationOrder.Candidate,
                        candidate,
                        request.ScreeningDuration),
                    originalState,
                    request.PresentMonApiPath,
                    request.PresentMonControlPipeName,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                backend.Rollback(experimentId);
            }

            if (candidateEvidence.IsUsable && candidateEvidence.Run is not null)
            {
                measuredCandidates.Add(new GpuOptimizationCandidateMeasurement(
                    candidate,
                    candidateEvidence.Run.Measurement));
                candidateRuns.Add(new GpuOptimizationCandidateRunRecord(candidate, true, null));
            }
            else
            {
                candidateRuns.Add(new GpuOptimizationCandidateRunRecord(
                    candidate,
                    false,
                    candidateEvidence.Reason ?? "Candidate screening evidence is unusable."));
            }
        }

        var screening = GpuOptimizationDecisionEngine.Screen(
            originalEvidence.Run.Measurement,
            measuredCandidates,
            request.Policy);
        if (screening.Recommendation != GpuOptimizationRecommendation.ConfirmFinalist ||
            screening.Finalist is null)
        {
            return new GpuOptimizationOrchestrationResult(
                GpuOptimizationRecommendation.RestoreOriginal,
                screening,
                null,
                candidateRuns.AsReadOnly(),
                [screening.Reason]);
        }

        throw new InvalidOperationException(
            "A screening finalist exists, but balanced confirmation has not been executed yet.");
    }

    private static GpuOptimizationEvidenceRequest CreateEvidenceRequest(
        GpuOptimizationOrchestrationRequest request,
        int runNumber,
        GpuConfirmationOrder role,
        GpuAffinityCandidate candidate,
        TimeSpan duration) =>
        new(
            runNumber,
            role,
            request.Baseline.SessionId,
            request.WorkloadProcessId,
            request.Baseline.WorkloadIdentity,
            request.Baseline.EnvironmentIdentity,
            request.Baseline.SourceRevisionId,
            candidate,
            duration);

    private static void Validate(GpuOptimizationOrchestrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DeviceInstanceId);
        ArgumentNullException.ThrowIfNull(request.Baseline);
        ArgumentNullException.ThrowIfNull(request.Candidates);
        ArgumentNullException.ThrowIfNull(request.Policy);
        request.Policy.Validate();

        if (request.WorkloadProcessId == 0 || request.Baseline.SessionId == Guid.Empty)
        {
            throw new ArgumentException("Workload process and baseline session identities are required.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.Baseline.WorkloadIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Baseline.EnvironmentIdentity);
        if (request.Baseline.SourceRevisionId is not { Length: 40 } revision ||
            !revision.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("Baseline source revision must be an exact 40-character hexadecimal commit SHA.", nameof(request));
        }

        ValidateDuration(request.ScreeningDuration, nameof(request.ScreeningDuration));
        ValidateDuration(request.ConfirmationDuration, nameof(request.ConfirmationDuration));

        var candidates = request.Candidates.ToArray();
        if (candidates.Length > GpuAffinityCandidatePlanner.MaximumCandidates ||
            candidates.Any(static candidate => candidate is null ||
                candidate.PhysicalCoreIndex < 0 ||
                candidate.Processor.Group != 0 ||
                candidate.Processor.Number >= 64) ||
            candidates.Select(static candidate => candidate.Processor).Distinct().Count() != candidates.Length ||
            candidates.Select(static candidate => candidate.PhysicalCoreIndex).Distinct().Count() != candidates.Length)
        {
            throw new ArgumentException(
                "GPU orchestration requires a bounded set of distinct group-0 physical-core candidates.",
                nameof(request));
        }
    }

    private static void ValidateDuration(TimeSpan duration, string parameterName)
    {
        if (duration.TotalMilliseconds < GpuOptimizationConfirmation.MinimumRunDurationMilliseconds ||
            duration.TotalMilliseconds > 60_000 ||
            duration.TotalMilliseconds != Math.Truncate(duration.TotalMilliseconds))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "GPU experiment duration must be an integral millisecond value from 30 to 60 seconds.");
        }
    }
}

internal sealed class GpuOptimizationExecutionBackend : IGpuOptimizationExecutionBackend
{
    private readonly MutationJournal journal;
    private readonly GpuInterruptAffinityMutationTransaction transaction;

    internal GpuOptimizationExecutionBackend(MutationJournal journal)
    {
        this.journal = journal ?? throw new ArgumentNullException(nameof(journal));
        transaction = new GpuInterruptAffinityMutationTransaction(journal);
    }

    public GpuInterruptAffinitySnapshot CaptureOriginal(string deviceInstanceId) =>
        GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);

    public Guid ApplyCandidate(string deviceInstanceId, GpuAffinityCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        ArgumentNullException.ThrowIfNull(candidate);
        var processor = candidate.Processor;
        if (processor.Group != 0 || processor.Number >= 64)
        {
            throw new NotSupportedException(
                "GPU optimization v1 supports only a group-0 x64 affinity candidate.");
        }

        var mutationCandidate = new GpuInterruptAffinityCandidate(
            processor.Group,
            processor.Number,
            1UL << processor.Number);
        var prepared = transaction.Prepare(deviceInstanceId, mutationCandidate);
        var applied = transaction.ApplyAndActivate(prepared.ExperimentId);
        if (applied.JournalEntry.State != MutationJournalState.Applied)
        {
            throw new InvalidOperationException(
                applied.JournalEntry.FailureReason ??
                "GPU affinity candidate did not reach the verified Applied state.");
        }

        return prepared.ExperimentId;
    }

    public void BeginMeasurement(Guid experimentId)
    {
        var entry = journal.TryGet(experimentId)
            ?? throw new InvalidOperationException($"GPU affinity experiment {experimentId:D} was not found.");
        if (!string.Equals(entry.Kind, GpuInterruptAffinityMutationContract.Kind, StringComparison.Ordinal) ||
            entry.State != MutationJournalState.Applied)
        {
            throw new InvalidOperationException(
                $"GPU affinity experiment {experimentId:D} must be Applied before measurement begins.");
        }

        _ = journal.Transition(
            entry.ExperimentId,
            entry.Revision,
            MutationJournalState.Applied,
            MutationJournalState.Measuring);
    }

    public Task<GpuOptimizationEvidenceCollectionResult> CaptureAsync(
        GpuOptimizationEvidenceRequest request,
        GpuInterruptAffinitySnapshot originalState,
        string? presentMonApiPath,
        string? presentMonControlPipeName,
        CancellationToken cancellationToken) =>
        GpuOptimizationEvidenceCollector.CaptureAsync(
            request,
            originalState,
            presentMonApiPath,
            presentMonControlPipeName,
            cancellationToken);

    public void Rollback(Guid experimentId)
    {
        var rollback = transaction.RollbackAndActivate(experimentId);
        if (rollback.JournalEntry.State != MutationJournalState.Reverted ||
            !rollback.OriginalStateRestored)
        {
            throw new InvalidOperationException(
                rollback.JournalEntry.FailureReason ??
                "GPU affinity rollback did not reach a verified exact-original state.");
        }
    }
}

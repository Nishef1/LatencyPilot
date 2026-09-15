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
    GpuGraphicsTargetIdentityResolution ResolveGraphicsTarget(
        string deviceInstanceId,
        string? presentMonApiPath,
        string? presentMonControlPipeName) =>
        GpuGraphicsTargetIdentityResolver.Capture(
            deviceInstanceId,
            presentMonApiPath,
            presentMonControlPipeName);

    GpuInterruptAffinitySnapshot CaptureOriginal(string deviceInstanceId);

    Guid ApplyCandidate(string deviceInstanceId, GpuAffinityCandidate candidate);

    void BeginMeasurement(Guid experimentId);

    Task<GpuOptimizationEvidenceCollectionResult> CaptureAsync(
        GpuOptimizationEvidenceRequest request,
        GpuInterruptAffinitySnapshot originalState,
        string? presentMonApiPath,
        string? presentMonControlPipeName,
        CancellationToken cancellationToken);

    void AwaitDecision(Guid experimentId);

    void KeepCandidate(Guid experimentId);

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

        var graphicsTarget = backend.ResolveGraphicsTarget(
            request.DeviceInstanceId,
            request.PresentMonApiPath,
            request.PresentMonControlPipeName);
        if (!TryAcceptInitialTargetPreflight(
                request.DeviceInstanceId,
                graphicsTarget,
                out var targetIdentity,
                out var preflightReason))
        {
            return new GpuOptimizationOrchestrationResult(
                GpuOptimizationRecommendation.RestoreOriginal,
                null,
                null,
                [],
                [preflightReason!]);
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
            if (!TryVerifyTargetPreflight(request, targetIdentity!, out var mutationPreflightReason))
            {
                candidateRuns.Add(new GpuOptimizationCandidateRunRecord(
                    candidate,
                    false,
                    mutationPreflightReason));
                return new GpuOptimizationOrchestrationResult(
                    GpuOptimizationRecommendation.RestoreOriginal,
                    null,
                    null,
                    candidateRuns.AsReadOnly(),
                    [mutationPreflightReason!]);
            }

            var experimentId = backend.ApplyCandidate(request.DeviceInstanceId, candidate);

            GpuOptimizationEvidenceCollectionResult candidateEvidence;
            try
            {
                backend.BeginMeasurement(experimentId);
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

        return await ConfirmFinalistAsync(
            request,
            originalState,
            targetIdentity!,
            screening,
            screening.Finalist.Candidate,
            candidateRuns.AsReadOnly(),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<GpuOptimizationOrchestrationResult> ConfirmFinalistAsync(
        GpuOptimizationOrchestrationRequest request,
        GpuInterruptAffinitySnapshot originalState,
        GpuGraphicsTargetIdentitySnapshot targetIdentity,
        GpuOptimizationScreeningResult screening,
        GpuAffinityCandidate finalist,
        IReadOnlyList<GpuOptimizationCandidateRunRecord> candidateRuns,
        CancellationToken cancellationToken)
    {
        var schedule = GpuOptimizationDecisionEngine.CreateBalancedConfirmationSchedule();
        var runs = new List<GpuOptimizationConfirmationRun>(schedule.Count);
        Guid? activeExperimentId = null;

        try
        {
            for (var index = 0; index < schedule.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var role = schedule[index];

                if (role == GpuConfirmationOrder.Original && activeExperimentId is { } activeOriginalTransition)
                {
                    backend.Rollback(activeOriginalTransition);
                    activeExperimentId = null;
                }
                else if (role == GpuConfirmationOrder.Candidate && activeExperimentId is null)
                {
                    if (!TryVerifyTargetPreflight(request, targetIdentity, out var mutationPreflightReason))
                    {
                        var partial = GpuOptimizationConfirmation.Confirm(
                            finalist,
                            request.Baseline,
                            runs,
                            request.Policy);
                        var reasons = new List<string> { mutationPreflightReason! };
                        reasons.AddRange(partial.Reasons);
                        return new GpuOptimizationOrchestrationResult(
                            GpuOptimizationRecommendation.RestoreOriginal,
                            screening,
                            partial,
                            candidateRuns,
                            reasons.AsReadOnly());
                    }

                    activeExperimentId = backend.ApplyCandidate(request.DeviceInstanceId, finalist);
                    backend.BeginMeasurement(activeExperimentId.Value);
                }

                var evidence = await backend.CaptureAsync(
                    CreateEvidenceRequest(
                        request,
                        index + 1,
                        role,
                        finalist,
                        request.ConfirmationDuration),
                    originalState,
                    request.PresentMonApiPath,
                    request.PresentMonControlPipeName,
                    cancellationToken).ConfigureAwait(false);
                if (!evidence.IsUsable || evidence.Run is null)
                {
                    if (activeExperimentId is { } unusableActive)
                    {
                        backend.Rollback(unusableActive);
                        activeExperimentId = null;
                    }

                    var partial = GpuOptimizationConfirmation.Confirm(
                        finalist,
                        request.Baseline,
                        runs,
                        request.Policy);
                    var reasons = new List<string>
                    {
                        evidence.Reason ?? $"Confirmation run {index + 1} evidence is unusable.",
                    };
                    reasons.AddRange(partial.Reasons);
                    return new GpuOptimizationOrchestrationResult(
                        GpuOptimizationRecommendation.RestoreOriginal,
                        screening,
                        partial,
                        candidateRuns,
                        reasons.AsReadOnly());
                }

                runs.Add(evidence.Run);

                if (role != GpuConfirmationOrder.Candidate || activeExperimentId is null)
                {
                    continue;
                }

                var isFinalRun = index == schedule.Count - 1;
                var nextReturnsToOriginal = !isFinalRun &&
                    schedule[index + 1] == GpuConfirmationOrder.Original;
                if (nextReturnsToOriginal)
                {
                    backend.Rollback(activeExperimentId.Value);
                    activeExperimentId = null;
                }
                else if (isFinalRun)
                {
                    backend.AwaitDecision(activeExperimentId.Value);
                }
            }

            var confirmation = GpuOptimizationConfirmation.Confirm(
                finalist,
                request.Baseline,
                runs,
                request.Policy);
            if (confirmation.Recommendation == GpuOptimizationRecommendation.KeepCandidate)
            {
                if (activeExperimentId is null)
                {
                    throw new InvalidOperationException(
                        "Balanced confirmation recommended Keep, but no verified candidate experiment remains active.");
                }

                if (!TryVerifyTargetPreflight(request, targetIdentity, out var keepPreflightReason))
                {
                    backend.Rollback(activeExperimentId.Value);
                    activeExperimentId = null;
                    var reasons = new List<string> { keepPreflightReason! };
                    reasons.AddRange(confirmation.Reasons);
                    return new GpuOptimizationOrchestrationResult(
                        GpuOptimizationRecommendation.RestoreOriginal,
                        screening,
                        confirmation,
                        candidateRuns,
                        reasons.AsReadOnly());
                }

                backend.KeepCandidate(activeExperimentId.Value);
                activeExperimentId = null;
                return new GpuOptimizationOrchestrationResult(
                    GpuOptimizationRecommendation.KeepCandidate,
                    screening,
                    confirmation,
                    candidateRuns,
                    confirmation.Reasons);
            }

            if (activeExperimentId is { } activeRestore)
            {
                backend.Rollback(activeRestore);
                activeExperimentId = null;
            }

            return new GpuOptimizationOrchestrationResult(
                GpuOptimizationRecommendation.RestoreOriginal,
                screening,
                confirmation,
                candidateRuns,
                confirmation.Reasons.Count == 0
                    ? ["Balanced confirmation did not establish a safe measurable improvement."]
                    : confirmation.Reasons);
        }
        catch (Exception failure) when (activeExperimentId is not null)
        {
            var active = activeExperimentId.Value;
            try
            {
                backend.Rollback(active);
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException(
                    "GPU optimization confirmation failed and the active candidate did not roll back cleanly.",
                    failure,
                    rollbackFailure);
            }

            throw;
        }
    }

    private bool TryVerifyTargetPreflight(
        GpuOptimizationOrchestrationRequest request,
        GpuGraphicsTargetIdentitySnapshot expected,
        out string? reason)
    {
        var current = backend.ResolveGraphicsTarget(
            request.DeviceInstanceId,
            request.PresentMonApiPath,
            request.PresentMonControlPipeName);
        if (!current.IsUsable || current.Identity is null)
        {
            reason = $"GPU target preflight rejected mutation: {current.Reason ?? "target identity is unavailable."}";
            return false;
        }

        if (!TargetIdentityMatches(expected, current.Identity))
        {
            reason = "GPU target preflight rejected mutation because the PnP/DXGI/PresentMon identity changed since the original-state preflight.";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool TryAcceptInitialTargetPreflight(
        string requestedDeviceInstanceId,
        GpuGraphicsTargetIdentityResolution resolution,
        out GpuGraphicsTargetIdentitySnapshot? identity,
        out string? reason)
    {
        identity = resolution.Identity;
        if (!resolution.IsUsable || identity is null)
        {
            reason = $"GPU target preflight rejected the experiment before any mutation: {resolution.Reason ?? "target identity is unavailable."}";
            return false;
        }

        if (identity.HardwareAdapterCount != 1 ||
            !string.Equals(identity.DeviceInstanceId, requestedDeviceInstanceId, StringComparison.OrdinalIgnoreCase))
        {
            identity = null;
            reason = "GPU target preflight rejected the experiment before any mutation because a unique single-adapter target identity was not proven.";
            return false;
        }

        reason = null;
        return true;
    }

    private static bool TargetIdentityMatches(
        GpuGraphicsTargetIdentitySnapshot expected,
        GpuGraphicsTargetIdentitySnapshot current) =>
        expected.HardwareAdapterCount == 1 &&
        current.HardwareAdapterCount == 1 &&
        string.Equals(expected.DeviceInstanceId, current.DeviceInstanceId, StringComparison.OrdinalIgnoreCase) &&
        expected.Luid == current.Luid &&
        expected.PresentMonDeviceId == current.PresentMonDeviceId;

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

        if (!request.Baseline.IsEligibleForExperiment)
        {
            throw new ArgumentException(
                "GPU orchestration requires both a valid baseline-quality-v2 decision baseline and a stable workload-stability-v1 activity assessment.",
                nameof(request));
        }

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
        var entry = GetRequiredEntry(experimentId, MutationJournalState.Applied);
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

    public void AwaitDecision(Guid experimentId)
    {
        var entry = GetRequiredEntry(experimentId, MutationJournalState.Measuring);
        _ = journal.Transition(
            entry.ExperimentId,
            entry.Revision,
            MutationJournalState.Measuring,
            MutationJournalState.AwaitingDecision);
    }

    public void KeepCandidate(Guid experimentId)
    {
        var entry = GetRequiredEntry(experimentId, MutationJournalState.AwaitingDecision);
        var original = GpuInterruptAffinityJournalCodec.DeserializeOriginal(entry.OriginalStateJson);
        var candidate = GpuInterruptAffinityJournalCodec.DeserializeCandidate(entry.CandidateStateJson);
        var current = GpuInterruptAffinityPolicyStore.Capture(original.DeviceInstanceId);
        var driverMatches = string.Equals(
            current.DriverVersion,
            original.DriverVersion,
            StringComparison.OrdinalIgnoreCase);
        if (!driverMatches || !GpuInterruptAffinityStateComparer.MatchesCandidate(current, candidate))
        {
            const string reason =
                "GPU affinity candidate changed before Keep could be finalized; recovery is required.";
            _ = journal.Transition(
                entry.ExperimentId,
                entry.Revision,
                MutationJournalState.AwaitingDecision,
                MutationJournalState.RecoveryRequired,
                reason);
            throw new InvalidOperationException(reason);
        }

        _ = journal.Transition(
            entry.ExperimentId,
            entry.Revision,
            MutationJournalState.AwaitingDecision,
            MutationJournalState.Kept);
    }

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

    private MutationJournalEntry GetRequiredEntry(
        Guid experimentId,
        MutationJournalState requiredState)
    {
        var entry = journal.TryGet(experimentId)
            ?? throw new InvalidOperationException($"GPU affinity experiment {experimentId:D} was not found.");
        if (!string.Equals(entry.Kind, GpuInterruptAffinityMutationContract.Kind, StringComparison.Ordinal) ||
            entry.State != requiredState)
        {
            throw new InvalidOperationException(
                $"GPU affinity experiment {experimentId:D} must be {requiredState}; actual state is {entry.State}.");
        }

        return entry;
    }
}

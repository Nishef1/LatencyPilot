using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Benchmarking.Statistics;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.Etw;
using LatencyPilot.Service;

namespace LatencyPilot.GateAValidation;

internal sealed class GpuAutoAffinityGateABackend : IGpuAutoAffinitySessionBackend
{
    private static readonly Guid DisplayDeviceClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private const int KernelMaximumEvents = 2_000_000;
    private const double MinimumOverlapRatio = 0.95;
    private const double MinimumCpuBusyAbsoluteDriftPercent = 10d;
    private const double MaximumCpuBusyRelativeDrift = 0.25d;
    private const int MinimumOriginalCpuBusySamples = 3;
    private static readonly TimeSpan CollectorTailSlack = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string deviceInstanceId;
    private readonly string sourceRevisionId;
    private readonly Guid sessionId;
    private readonly uint benchmarkProcessId;
    private readonly GpuBenchmarkControlClient benchmark;
    private readonly GpuAffinityMutationBackend mutation;
    private readonly GpuInterruptAffinitySnapshot originalState;
    private readonly string topologyIdentity;
    private readonly string driverServiceName;
    private readonly HashSet<Guid> measuringExperiments = [];
    private readonly Dictionary<Guid, GpuAffinityCandidate> ownedCandidates = [];
    private readonly List<GpuAutoAffinityMutationAuditEntry> mutationAudit = [];
    private readonly List<double> originalCpuBusyPercent = [];
    private GpuAutoAffinityReportProvenance? reportProvenance;
    private string? referenceIsrModuleName;

    internal GpuAutoAffinityGateABackend(
        string deviceInstanceId,
        string sourceRevisionId,
        Guid sessionId,
        uint benchmarkProcessId,
        ProcessorTopologySnapshot topology,
        GpuBenchmarkControlClient benchmark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRevisionId);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(benchmark);
        if (sourceRevisionId.Length != 40 || !sourceRevisionId.All(Uri.IsHexDigit) ||
            sessionId == Guid.Empty || benchmarkProcessId == 0)
        {
            throw new ArgumentException("Gate A benchmark provenance is incomplete or invalid.");
        }

        this.deviceInstanceId = deviceInstanceId;
        this.sourceRevisionId = sourceRevisionId.ToLowerInvariant();
        this.sessionId = sessionId;
        this.benchmarkProcessId = benchmarkProcessId;
        this.benchmark = benchmark;
        topologyIdentity = ComputeTopologyIdentity(topology);
        driverServiceName = ResolveDriverServiceName(deviceInstanceId);

        var journal = new MutationJournal(MutationJournal.GetDefaultDatabasePath());
        journal.Initialize();
        mutation = new GpuAffinityMutationBackend(journal);
        originalState = GpuAffinityMutationBackend.CaptureOriginal(deviceInstanceId);
        mutationAudit.Add(new GpuAutoAffinityMutationAuditEntry(
            DateTimeOffset.UtcNow,
            "CaptureOriginalState",
            null,
            null,
            StoredStateVerified: true,
            ToStoredStateReport(originalState)));
    }

    internal GpuAutoAffinityReportProvenance? ReportProvenance => reportProvenance;

    internal GpuAutoAffinityReport CompleteReport(GpuAutoAffinityReport report, int unresolvedCount)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentOutOfRangeException.ThrowIfNegative(unresolvedCount);

        var current = GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);
        var driverStable = string.Equals(
            current.DriverVersion,
            originalState.DriverVersion,
            StringComparison.OrdinalIgnoreCase);
        var matchesOriginal = driverStable &&
            GpuInterruptAffinityStateComparer.MatchesOriginal(current, originalState);

        var expectsCandidate =
            string.Equals(
                report.FinalRecommendation,
                GpuOptimizationRecommendation.KeepCandidate.ToString(),
                StringComparison.Ordinal) &&
            report.FinalProcessor is not null;
        var matchesExpected = expectsCandidate && report.FinalProcessor is { } finalProcessor
            ? driverStable && GpuInterruptAffinityStateComparer.MatchesCandidate(
                current,
                new GpuInterruptAffinityCandidate(
                    finalProcessor.Group,
                    finalProcessor.Number,
                    1UL << finalProcessor.Number))
            : matchesOriginal;

        var finalStateVerified = report.FinalStateVerified && unresolvedCount == 0 && matchesExpected;
        var originalStateRestored = report.OriginalStateRestored && unresolvedCount == 0 && matchesOriginal;
        var recoveryStatus = unresolvedCount != 0
            ? $"unresolved-journal:{unresolvedCount.ToString(CultureInfo.InvariantCulture)}"
            : finalStateVerified
                ? "clean-zero-unresolved"
                : "final-state-unverified";

        return report with
        {
            FinalStateVerified = finalStateVerified,
            OriginalStateRestored = originalStateRestored,
            Provenance = reportProvenance,
            OriginalStoredState = ToStoredStateReport(originalState),
            FinalStoredState = ToStoredStateReport(current),
            MutationAudit = mutationAudit.ToArray(),
            RecoveryStatus = recoveryStatus,
        };
    }

    public Task<GpuAutoAffinityTrialObservation> CaptureOriginalAsync(
        GpuAutoAffinityTrialRequest request,
        CancellationToken cancellationToken) =>
        CaptureAsync(request, experimentId: null, candidate: null, cancellationToken);

    public async Task<Guid> ApplyCandidateAsync(
        GpuAffinityCandidate candidate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(candidate);
        var currentBefore = GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);
        var expected = ToMutationCandidate(candidate);
        var driverMatchesOriginal = string.Equals(
            currentBefore.DriverVersion,
            originalState.DriverVersion,
            StringComparison.OrdinalIgnoreCase);
        if (!driverMatchesOriginal ||
            !GpuInterruptAffinityStateComparer.MatchesOriginal(currentBefore, originalState))
        {
            mutationAudit.Add(new GpuAutoAffinityMutationAuditEntry(
                DateTimeOffset.UtcNow,
                "ApplyCandidateRefusedExternalDrift",
                Guid.Empty,
                candidate.Processor,
                StoredStateVerified: false,
                ToStoredStateReport(currentBefore),
                "Stored GPU affinity or display-driver version changed outside the session before candidate apply."));
            throw new InvalidOperationException(
                "GPU affinity state changed outside the active optimization session; candidate apply was refused before any write.");
        }

        if (GpuInterruptAffinityStateComparer.MatchesCandidate(currentBefore, expected))
        {
            mutationAudit.Add(new GpuAutoAffinityMutationAuditEntry(
                DateTimeOffset.UtcNow,
                "MeasureExistingCandidateNoWrite",
                Guid.Empty,
                candidate.Processor,
                StoredStateVerified: true,
                ToStoredStateReport(currentBefore)));
            return Guid.Empty;
        }

        var experimentId = mutation.ApplyCandidate(deviceInstanceId, candidate, originalState);
        ownedCandidates[experimentId] = candidate;

        try
        {
            var current = GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);
            var verified = string.Equals(
                    current.DriverVersion,
                    originalState.DriverVersion,
                    StringComparison.OrdinalIgnoreCase) &&
                GpuInterruptAffinityStateComparer.MatchesCandidate(current, ToMutationCandidate(candidate));
            mutationAudit.Add(new GpuAutoAffinityMutationAuditEntry(
                DateTimeOffset.UtcNow,
                "ApplyCandidate",
                experimentId,
                candidate.Processor,
                verified,
                ToStoredStateReport(current)));
            if (!verified)
            {
                throw new InvalidOperationException(
                    "GPU candidate post-apply verification failed before ownership could be transferred to the session.");
            }

            await benchmark.RecreateRendererAsync(cancellationToken).ConfigureAwait(false);
            mutationAudit.Add(new GpuAutoAffinityMutationAuditEntry(
                DateTimeOffset.UtcNow,
                "RecreateBenchmarkRenderer",
                experimentId,
                candidate.Processor,
                StoredStateVerified: true,
                ToStoredStateReport(current)));
            return experimentId;
        }
        catch (Exception postApplyFailure)
        {
            RollbackCandidateAfterPostApplyFailure(experimentId, candidate, postApplyFailure);
            throw;
        }
    }

    private void RollbackCandidateAfterPostApplyFailure(
        Guid experimentId,
        GpuAffinityCandidate candidate,
        Exception postApplyFailure)
    {
        try
        {
            mutation.Rollback(experimentId);
            var current = GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);
            var verified = string.Equals(
                    current.DriverVersion,
                    originalState.DriverVersion,
                    StringComparison.OrdinalIgnoreCase) &&
                GpuInterruptAffinityStateComparer.MatchesOriginal(current, originalState);
            mutationAudit.Add(new GpuAutoAffinityMutationAuditEntry(
                DateTimeOffset.UtcNow,
                "RollbackAfterPostApplyFailure",
                experimentId,
                candidate.Processor,
                verified,
                ToStoredStateReport(current),
                postApplyFailure.Message));
            if (!verified)
            {
                throw new InvalidOperationException(
                    "GPU candidate post-apply verification failed and rollback completed without an exact-original verification.");
            }
        }
        catch (Exception rollbackFailure)
        {
            throw new AggregateException(
                "GPU candidate post-apply verification failed and exact rollback also failed.",
                postApplyFailure,
                rollbackFailure);
        }
        finally
        {
            measuringExperiments.Remove(experimentId);
            ownedCandidates.Remove(experimentId);
        }
    }

    public Task<GpuAutoAffinityTrialObservation> CaptureCandidateAsync(
        Guid experimentId,
        GpuAutoAffinityTrialRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Candidate is null)
        {
            throw new ArgumentException("Candidate trial request is missing its processor candidate.", nameof(request));
        }

        return CaptureAsync(request, experimentId, request.Candidate, cancellationToken);
    }

    public async Task RollbackAsync(Guid experimentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (experimentId == Guid.Empty)
        {
            var currentNoWrite = GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);
            var verifiedNoWrite = string.Equals(currentNoWrite.DriverVersion, originalState.DriverVersion, StringComparison.OrdinalIgnoreCase) &&
                GpuInterruptAffinityStateComparer.MatchesOriginal(currentNoWrite, originalState);
            mutationAudit.Add(new GpuAutoAffinityMutationAuditEntry(
                DateTimeOffset.UtcNow, "RollbackNoWrite", Guid.Empty, null, verifiedNoWrite, ToStoredStateReport(currentNoWrite)));
            if (!verifiedNoWrite)
            {
                throw new InvalidOperationException("No-write candidate no longer matches the exact original stored state.");
            }
            return;
        }

        ownedCandidates.TryGetValue(experimentId, out var candidate);
        try
        {
            // RollbackAndActivate restarts the display adapter. Any D3D12 device,
            // swap chain, fence, queue or resource created before that restart is
            // no longer a valid renderer for the next Original/candidate block.
            mutation.Rollback(experimentId);
            var current = GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);
            var verified = string.Equals(
                    current.DriverVersion,
                    originalState.DriverVersion,
                    StringComparison.OrdinalIgnoreCase) &&
                GpuInterruptAffinityStateComparer.MatchesOriginal(current, originalState);
            mutationAudit.Add(new GpuAutoAffinityMutationAuditEntry(
                DateTimeOffset.UtcNow,
                "Rollback",
                experimentId,
                candidate?.Processor,
                verified,
                ToStoredStateReport(current)));
            if (!verified)
            {
                throw new InvalidOperationException(
                    "GPU candidate rollback completed, but the exact original stored state could not be verified.");
            }

            await benchmark.RecreateRendererAsync(cancellationToken).ConfigureAwait(false);
            mutationAudit.Add(new GpuAutoAffinityMutationAuditEntry(
                DateTimeOffset.UtcNow,
                "RecreateBenchmarkRendererAfterRollback",
                experimentId,
                candidate?.Processor,
                StoredStateVerified: true,
                ToStoredStateReport(current)));
        }
        finally
        {
            measuringExperiments.Remove(experimentId);
            ownedCandidates.Remove(experimentId);
        }
    }

    public Task KeepAsync(Guid experimentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!measuringExperiments.Contains(experimentId))
        {
            throw new InvalidOperationException(
                "GPU candidate cannot be kept before a measurement-owned experiment reaches Measuring state.");
        }
        if (!ownedCandidates.TryGetValue(experimentId, out var candidate))
        {
            throw new InvalidOperationException(
                "GPU candidate cannot be kept because its owned candidate identity is unavailable.");
        }

        mutation.AwaitDecision(experimentId);
        var current = GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);
        var verified = string.Equals(
                current.DriverVersion,
                originalState.DriverVersion,
                StringComparison.OrdinalIgnoreCase) &&
            GpuInterruptAffinityStateComparer.MatchesCandidate(current, ToMutationCandidate(candidate));
        mutationAudit.Add(new GpuAutoAffinityMutationAuditEntry(
            DateTimeOffset.UtcNow,
            "KeepCandidatePreflight",
            experimentId,
            candidate.Processor,
            verified,
            ToStoredStateReport(current)));
        if (!verified)
        {
            throw new InvalidOperationException(
                "GPU candidate pre-keep verification failed; the keep decision was not terminalized.");
        }

        mutation.KeepCandidate(experimentId);
        mutationAudit.Add(new GpuAutoAffinityMutationAuditEntry(
            DateTimeOffset.UtcNow,
            "KeepCandidate",
            experimentId,
            candidate.Processor,
            StoredStateVerified: true,
            ToStoredStateReport(current)));
        measuringExperiments.Remove(experimentId);
        ownedCandidates.Remove(experimentId);
        return Task.CompletedTask;
    }

    public Task<bool> VerifyOriginalStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);
        return Task.FromResult(
            string.Equals(current.DriverVersion, originalState.DriverVersion, StringComparison.OrdinalIgnoreCase) &&
            GpuInterruptAffinityStateComparer.MatchesOriginal(current, originalState));
    }

    public Task<bool> VerifyCandidateStateAsync(
        Guid experimentId,
        GpuAffinityCandidate candidate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(candidate);
        var current = GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);
        var expected = ToMutationCandidate(candidate);
        var ownershipValid = experimentId == Guid.Empty ||
            (ownedCandidates.TryGetValue(experimentId, out var owned) && owned == candidate);
        return Task.FromResult(
            ownershipValid &&
            string.Equals(current.DriverVersion, originalState.DriverVersion, StringComparison.OrdinalIgnoreCase) &&
            GpuInterruptAffinityStateComparer.MatchesCandidate(current, expected));
    }

    private async Task<GpuAutoAffinityTrialObservation> CaptureAsync(
        GpuAutoAffinityTrialRequest request,
        Guid? experimentId,
        GpuAffinityCandidate? candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if ((candidate is null) != (request.Role == GpuConfirmationOrder.Original))
        {
            throw new ArgumentException("Benchmark role and candidate identity disagree.", nameof(request));
        }

        if (!GpuOptimizationCaptureContinuity.TryCapture(
                benchmarkProcessId,
                deviceInstanceId,
                presentMonApiPath: null,
                presentMonControlPipeName: null,
                out var continuityBefore,
                out var continuityBeforeReason) || continuityBefore is null)
        {
            throw new InvalidOperationException(
                $"GPU benchmark continuity could not be established before trial {request.RunNumber}: {continuityBeforeReason}");
        }

        var storedBefore = candidate is null
            ? await VerifyOriginalStateAsync(cancellationToken).ConfigureAwait(false)
            : await VerifyCandidateStateAsync(experimentId!.Value, candidate, cancellationToken).ConfigureAwait(false);
        if (candidate is not null && experimentId is { } ownedExperiment && ownedExperiment != Guid.Empty &&
            measuringExperiments.Add(ownedExperiment))
        {
            mutation.BeginMeasurement(ownedExperiment);
        }

        var isWarmup = request.Phase.EndsWith("-warmup", StringComparison.Ordinal);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Duration + TimeSpan.FromSeconds(60));

        GpuBenchmarkTrialArtifact artifact;
        KernelLatencyCaptureResult kernel;
        PresentMonFrameCaptureSnapshot presentMon;
        if (isWarmup)
        {
            // Warm-up exists only to stabilize the post-restart graphics/workload
            // state. Starting two external ETW collectors here adds observer cost
            // without contributing any scored or Keep evidence.
            var artifactPath = await benchmark.RunTrialAsync(
                request.RunNumber,
                request.Duration,
                deadline.Token).ConfigureAwait(false);
            artifact = await ReadArtifactAsync(artifactPath, deadline.Token).ConfigureAwait(false);
            var actualDuration = artifact.EndedAtUtc > artifact.StartedAtUtc
                ? artifact.EndedAtUtc - artifact.StartedAtUtc
                : request.Duration;
            kernel = new KernelLatencyCaptureResult(
                artifact.StartedAtUtc,
                request.Duration,
                actualDuration,
                [],
                0,
                0,
                0,
                false);
            presentMon = new PresentMonFrameCaptureSnapshot(
                PresentMonWorkloadCaptureStatus.TrackingFailed,
                benchmarkProcessId,
                request.Duration.TotalMilliseconds,
                0d,
                null,
                [],
                [],
                null,
                null,
                "External PresentMon/ETW collectors intentionally skipped for non-scored warm-up.",
                artifact.StartedAtUtc,
                artifact.EndedAtUtc);
        }
        else
        {
            // Provision/start the pinned standalone collector only for scored
            // or final-verification evidence. PresentMon emits CPUStartQPC and is
            // cropped against the benchmark's exact scored QPC interval.
            var presentMonWindow = request.Duration;
            var presentMonStart = await TryStartOptionalPresentMonAsync(
                benchmarkProcessId,
                presentMonWindow,
                cancellationToken).ConfigureAwait(false);
            // Own the collector before starting either capture. A benchmark/ETW
            // exception or cancellation must dispose it as well as the happy path.
            await using var presentMonSession = presentMonStart.Session;

            var kernelDuration = request.Duration + CollectorTailSlack;
            var kernelTask = Task.Run(
                () => KernelLatencyCapture.Capture(
                    new KernelLatencyCaptureOptions(kernelDuration, KernelMaximumEvents),
                    deadline.Token),
                deadline.Token);

            await Task.Delay(TimeSpan.FromMilliseconds(150), deadline.Token).ConfigureAwait(false);
            var artifactPathTask = benchmark.RunTrialAsync(
                request.RunNumber,
                request.Duration,
                deadline.Token);

            await Task.WhenAll(kernelTask, artifactPathTask).ConfigureAwait(false);
            kernel = await kernelTask.ConfigureAwait(false);
            var artifactPath = await artifactPathTask.ConfigureAwait(false);
            artifact = await ReadArtifactAsync(artifactPath, deadline.Token).ConfigureAwait(false);

            if (presentMonSession is null)
            {
                presentMon = CreateUnavailablePresentMon(
                    benchmarkProcessId,
                    presentMonWindow,
                    artifact.StartedAtUtc,
                    artifact.EndedAtUtc,
                    presentMonStart.Error ?? "PresentMon startup unavailable.");
            }
            else
            {
                presentMon = await presentMonSession.CompleteAsync(
                    artifact.StartedAtQpc,
                    artifact.EndedAtQpc,
                    artifact.QpcFrequency,
                    artifact.StartedAtUtc,
                    artifact.EndedAtUtc,
                    deadline.Token).ConfigureAwait(false);
            }
        }

        if (!GpuOptimizationCaptureContinuity.TryCapture(
                benchmarkProcessId,
                deviceInstanceId,
                presentMonApiPath: null,
                presentMonControlPipeName: null,
                out var continuityAfter,
                out var continuityAfterReason) || continuityAfter is null)
        {
            throw new InvalidOperationException(
                $"GPU benchmark continuity could not be established after trial {request.RunNumber}: {continuityAfterReason}");
        }

        var storedAfter = candidate is null
            ? await VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false)
            : await VerifyCandidateStateAsync(experimentId!.Value, candidate, CancellationToken.None).ConfigureAwait(false);
        var continuity = GpuOptimizationCaptureContinuity.Evaluate(continuityBefore, continuityAfter);
        // Hard reasons invalidate the trial. Soft notes degrade guardrails to
        // best-effort context: ranking uses the benchmark's own frame periods
        // (video-style AVG / 1% low / 0.1% low) and never depends on an
        // external collector being healthy.
        var (reasons, softNotes) = ValidateTrial(
            request,
            artifact,
            kernel,
            presentMon,
            continuity,
            storedBefore,
            storedAfter);

        var systemCpuBusyDrifted = false;
        if (!isWarmup &&
            continuity.SystemCpuBusyPercent is { } cpuBusy &&
            double.IsFinite(cpuBusy))
        {
            if (request.Role == GpuConfirmationOrder.Original &&
                string.Equals(request.Phase, "screening-original", StringComparison.Ordinal))
            {
                originalCpuBusyPercent.Add(cpuBusy);
            }
            else if (originalCpuBusyPercent.Count >= MinimumOriginalCpuBusySamples)
            {
                var originalMedian = Percentiles.Calculate(originalCpuBusyPercent, 0.50);
                var allowedAbsoluteDrift = Math.Max(
                    MinimumCpuBusyAbsoluteDriftPercent,
                    originalMedian * MaximumCpuBusyRelativeDrift);
                var absoluteDrift = Math.Abs(cpuBusy - originalMedian);
                systemCpuBusyDrifted = absoluteDrift > allowedAbsoluteDrift;
                if (systemCpuBusyDrifted)
                {
                    softNotes.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"System CPU busy changed from an Original median of {originalMedian:F1}% to {cpuBusy:F1}% during this trial; allowed drift is {allowedAbsoluteDrift:F1} percentage points."));
                }
            }
        }

        GpuInterruptRuntimePlacementEvidence? runtimePlacement = null;
        GpuAutoAffinityPlacementProof? placement = null;
        GpuInterruptIsrAttribution? isrAttribution = null;
        // Crop the extended kernel window to the scored benchmark artifact
        // interval so D3D12 device recreation + pre-roll idle cannot pollute
        // placement proof or driver guardrails.
        var scopedKernel = CropKernelToArtifact(kernel, artifact.StartedAtUtc, artifact.EndedAtUtc);
        var attributionAttempted = !isWarmup && kernel.IsValid && storedBefore && storedAfter;
        if (attributionAttempted)
        {
            try
            {
                isrAttribution = GpuInterruptRuntimePlacementVerifier.CaptureIsrAttribution(scopedKernel, deviceInstanceId);
                if (isrAttribution.Events.Count > 0)
                {
                    referenceIsrModuleName ??= isrAttribution.ModuleName;
                    if (!string.Equals(referenceIsrModuleName, isrAttribution.ModuleName, StringComparison.OrdinalIgnoreCase))
                    {
                        reasons.Add($"GPU ISR attribution changed from {referenceIsrModuleName} to {isrAttribution.ModuleName}; timings from different modules cannot be compared.");
                    }
                }

                if (candidate is not null)
                {
                    runtimePlacement = GpuInterruptRuntimePlacementVerifier.Analyze(isrAttribution, ToMutationCandidate(candidate));
                    placement = new GpuAutoAffinityPlacementProof(
                        candidate.Processor,
                        runtimePlacement.TargetProcessorIsrEventCount,
                        runtimePlacement.OffTargetIsrEventCount);
                    if (runtimePlacement.OffTargetIsrEventCount > 0 ||
                        runtimePlacement.TargetProcessorNumber != candidate.Processor.Number)
                    {
                        reasons.Add("Resolved single-adapter ISR evidence contradicts the requested logical processor.");
                    }
                    else if (runtimePlacement.TargetProcessorIsrEventCount == 0)
                    {
                        softNotes.Add("No attributable GPU ISR sample was observed in this screening window; placement remains Unknown rather than contradicted.");
                    }
                }
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or
                NotSupportedException or
                System.ComponentModel.Win32Exception)
            {
                softNotes.Add(
                    $"GPU ISR attribution is unavailable for this trial: {exception.GetType().Name}: {exception.Message}. Placement remains Unknown; final Keep still requires positive target-only proof.");
            }
        }
        else if (candidate is not null)
        {
            softNotes.Add(
                "Runtime ISR placement is unverified for this trial because kernel ETW is unavailable; ranking uses benchmark frame periods with verified stored affinity state.");
        }

        var workload = GpuBenchmarkFrozenWorkload.Create(
            artifact.FrozenWorkload.Width,
            artifact.FrozenWorkload.Height,
            artifact.FrozenWorkload.WorkerMap,
            artifact.FrozenWorkload.SimulationIterationsPerWorker,
            artifact.FrozenWorkload.CommandBatchesPerWorker,
            artifact.FrozenWorkload.Seed);
        var gpuIdentity = string.Create(
            CultureInfo.InvariantCulture,
            $"{continuityBefore.GraphicsTarget.DeviceInstanceId}|{continuityBefore.GraphicsTarget.AdapterName}");
        var evidence = new GpuBenchmarkEvidence(
            GpuBenchmarkEvidence.SchemaId,
            sourceRevisionId,
            GpuBenchmarkEvidence.MethodIdValue,
            $"{RuntimeInformation.OSDescription.Trim()}|{RuntimeInformation.OSArchitecture}",
            gpuIdentity,
            originalState.DriverVersion ?? "unavailable",
            topologyIdentity,
            benchmarkProcessId,
            request.Role.ToString(),
            request.RunNumber,
            candidate?.Processor,
            workload.WorkloadIdentity,
            artifact.FrozenWorkload.WorkerMap.ToArray(),
            artifact.FrozenWorkload.Seed,
            artifact.GpuTimestampFrequency,
            artifact.Frames.Select(static frame => frame.GpuWorkMilliseconds).ToArray(),
            presentMon,
            isWarmup ? null : ReadPresentMonBinaryVersion(presentMon.ApiPath),
            isWarmup ? Guid.Empty : Guid.NewGuid(),
            !isWarmup && kernel.IsValid,
            isWarmup ? 0 : kernel.EventsLost,
            reasons.AsReadOnly(),
            artifact.FrozenWorkload,
            artifact.Frames.Select(static frame => frame.FramePeriodMilliseconds).ToArray());
        if (!isWarmup)
        {
            reportProvenance ??= GpuAutoAffinityReportProvenance.FromEvidence(evidence);
        }

        var driverDpc = FilterDriverDurations(scopedKernel, KernelLatencyEventKind.Dpc);
        var driverIsr = isrAttribution?.Events.Select(static item => item.DurationMicroseconds).ToArray() ?? [];
        if (!kernel.IsValid)
        {
            softNotes.Add("Kernel ETW capture integrity is not clean; driver DPC/ISR guardrails are best-effort for this trial.");
        }
        if (!presentMon.IsAvailable || presentMon.ProcessId != benchmarkProcessId || presentMon.Frames.Count == 0)
        {
            softNotes.Add(
                $"Standalone PresentMon evidence is unavailable for this trial [{presentMon.Status}]; ranking uses benchmark frame periods.");
        }
        var contamination = new GpuBenchmarkContaminationContext(
            SystemCpuBusyDrifted: systemCpuBusyDrifted,
            ControlTrialDrifted: false,
            SleepOrResumeDetected: continuity.Reasons.Any(static reason =>
                reason.Contains("sleep", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("awake", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("suspend", StringComparison.OrdinalIgnoreCase)),
            DeviceResetDetected: false,
            RetryAttempt: request.RetryAttempt,
            SoftNotes: softNotes.AsReadOnly());

        return new GpuAutoAffinityTrialObservation(
            evidence,
            contamination,
            storedBefore,
            storedAfter,
            placement,
            driverDpc,
            driverIsr,
            isrAttribution is null ? null : new GpuAutoAffinityInterruptEvidence(
                isrAttribution.ModuleName, isrAttribution.Mode,
                driverDpc.Length, driverIsr.Length, isrAttribution.UnresolvedIsrEventCount));
    }

    private static async Task<(PresentMonConsoleFrameMetricsReader.PresentMonConsoleCaptureSession? Session, string? Error)> TryStartOptionalPresentMonAsync(
        uint processId,
        TimeSpan requestedWindow,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await PresentMonConsoleFrameMetricsReader.StartAsync(
                processId,
                requestedWindow,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return (session, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "PresentMon startup unavailable: provisioning/startup exceeded its own bounded deadline.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            // A pinned-binary integrity failure is not optional evidence loss.
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (System.Security.SecurityException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or
            DirectoryNotFoundException or
            System.Net.Http.HttpRequestException or
            InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException)
        {
            return (null, $"PresentMon startup unavailable: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static PresentMonFrameCaptureSnapshot CreateUnavailablePresentMon(
        uint processId,
        TimeSpan requestedWindow,
        DateTimeOffset benchmarkStartedAtUtc,
        DateTimeOffset benchmarkEndedAtUtc,
        string error) =>
        new(
            PresentMonWorkloadCaptureStatus.TrackingFailed,
            processId,
            requestedWindow.TotalMilliseconds,
            Math.Max(0d, (benchmarkEndedAtUtc - benchmarkStartedAtUtc).TotalMilliseconds),
            ApiVersion: null,
            Frames: [],
            UnavailableOptionalMetrics: [],
            ApiPath: null,
            NativeStatusCode: null,
            Error: error,
            StartedAtUtc: benchmarkStartedAtUtc,
            EndedAtUtc: benchmarkEndedAtUtc);

    private static async Task<GpuBenchmarkTrialArtifact> ReadArtifactAsync(
        string artifactPath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(artifactPath);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<GpuBenchmarkTrialArtifact>(
                   stream,
                   JsonOptions,
                   cancellationToken).ConfigureAwait(false)
            is { } artifact
            ? EnsureArtifactShape(artifact)
            : throw new InvalidDataException("GPU benchmark trial artifact is empty.");
    }

    private static GpuBenchmarkTrialArtifact EnsureArtifactShape(GpuBenchmarkTrialArtifact artifact)
    {
        if (artifact.FrozenWorkload is null)
        {
            throw new InvalidDataException(
                "GPU benchmark trial artifact is missing its frozen workload identity.");
        }

        if (artifact.FrozenWorkload.WorkerMap is null || artifact.FrozenWorkload.WorkerMap.Count == 0)
        {
            throw new InvalidDataException(
                "GPU benchmark trial artifact is missing its frozen worker map.");
        }

        if (artifact.Frames is null || artifact.Frames.Count == 0)
        {
            throw new InvalidDataException(
                "GPU benchmark trial artifact is missing frame evidence.");
        }

        if (artifact.WorkerChecksums is null ||
            artifact.WorkerChecksums.Count != artifact.FrozenWorkload.WorkerMap.Count)
        {
            throw new InvalidDataException(
                "GPU benchmark trial artifact has incomplete worker checksum evidence.");
        }

        if (artifact.StartedAtQpc <= 0 ||
            artifact.EndedAtQpc <= artifact.StartedAtQpc ||
            artifact.QpcFrequency <= 0)
        {
            throw new InvalidDataException(
                "GPU benchmark trial artifact is missing valid scored QPC window provenance.");
        }

        return artifact;
    }

    private (List<string> Hard, List<string> Soft) ValidateTrial(
        GpuAutoAffinityTrialRequest request,
        GpuBenchmarkTrialArtifact artifact,
        KernelLatencyCaptureResult kernel,
        PresentMonFrameCaptureSnapshot presentMon,
        GpuOptimizationCaptureContinuityResult continuity,
        bool storedBefore,
        bool storedAfter)
    {
        var hard = new List<string>();
        var soft = new List<string>();
        if (!string.Equals(artifact.Schema, GpuBenchmarkTrialArtifact.SchemaId, StringComparison.Ordinal) ||
            artifact.SessionId != sessionId || artifact.Frames.Count == 0)
        {
            hard.Add("Benchmark raw artifact schema/session/frame evidence is invalid.");
        }
        if (artifact.FrozenWorkload.WorkerMap.Count == 0 ||
            artifact.WorkerChecksums.Count != artifact.FrozenWorkload.WorkerMap.Count)
        {
            hard.Add("Benchmark frozen worker map/checksum evidence is incomplete.");
        }
        if (!storedBefore || !storedAfter)
        {
            hard.Add("Exact stored GPU affinity state was not stable before and after the trial.");
        }
        if (!continuity.IsStable)
        {
            hard.AddRange(continuity.Reasons);
        }

        if (!kernel.IsValid)
        {
            soft.Add("Kernel ETW capture integrity is not clean; external timing cross-checks are best-effort.");
        }
        if (!presentMon.IsAvailable || presentMon.ProcessId != benchmarkProcessId || presentMon.Frames.Count == 0)
        {
            soft.Add("Raw PresentMon evidence is unavailable, empty, or belongs to a different process; ranking uses benchmark frame periods.");
            if (!string.IsNullOrWhiteSpace(presentMon.Error))
            {
                soft.Add($"PresentMon detail [{presentMon.Status}]: {presentMon.Error}");
            }
        }

        var kernelEnd = kernel.StartedAtUtc + kernel.ActualDuration;
        // The kernel window intentionally starts before the scored benchmark
        // (it covers D3D12 device recreation after a GPU restart) and runs
        // longer than the trial. A coverage miss only degrades external
        // cross-checks; it never invalidates benchmark-period ranking.
        var kernelCoveredMs = (Min(artifact.EndedAtUtc, kernelEnd) -
            Max(artifact.StartedAtUtc, kernel.StartedAtUtc)).TotalMilliseconds;
        if (!double.IsFinite(kernelCoveredMs) ||
            kernelCoveredMs < request.Duration.TotalMilliseconds * MinimumOverlapRatio)
        {
            soft.Add("Kernel ETW does not cover at least 95% of the scored benchmark trial.");
        }

        // PresentMon now crops CPUStartQPC against the exact benchmark QPC window.
        // Keep it best-effort, but validate the resulting observed duration without
        // crossing back through wall-clock overlap arithmetic.
        if (presentMon.IsAvailable &&
            (!double.IsFinite(presentMon.ActualWindowMilliseconds) ||
             presentMon.ActualWindowMilliseconds < request.Duration.TotalMilliseconds * MinimumOverlapRatio))
        {
            soft.Add("Standalone PresentMon does not cover at least 95% of the scored benchmark QPC window.");
        }

        return (hard, soft);
    }

    private static KernelLatencyCaptureResult CropKernelToArtifact(
        KernelLatencyCaptureResult kernel,
        DateTimeOffset artifactStart,
        DateTimeOffset artifactEnd)
    {
        var offsetStartMs = (artifactStart - kernel.StartedAtUtc).TotalMilliseconds - 1000d;
        var offsetEndMs = (artifactEnd - kernel.StartedAtUtc).TotalMilliseconds + 1000d;
        if (!double.IsFinite(offsetStartMs) || !double.IsFinite(offsetEndMs) || offsetEndMs <= 0)
        {
            return kernel;
        }

        var scoped = kernel.Events
            .Where(item => item.TimeStampRelativeMilliseconds >= offsetStartMs &&
                item.TimeStampRelativeMilliseconds <= offsetEndMs)
            .ToArray();
        return kernel with { Events = scoped };
    }

    private double[] FilterDriverDurations(
        KernelLatencyCaptureResult capture,
        KernelLatencyEventKind kind) =>
        capture.Events
            .Where(item => item.Kind == kind && ModuleMatchesDriver(item.ModulePath))
            .Select(static item => item.DurationMicroseconds)
            .Where(static value => double.IsFinite(value) && value >= 0)
            .ToArray();

    private bool ModuleMatchesDriver(string? modulePath)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
        {
            return false;
        }

        return string.Equals(
            Path.GetFileNameWithoutExtension(Path.GetFileName(modulePath.Trim())),
            driverServiceName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static GpuInterruptAffinityCandidate ToMutationCandidate(GpuAffinityCandidate candidate) =>
        new(
            candidate.Processor.Group,
            candidate.Processor.Number,
            1UL << candidate.Processor.Number);

    private static GpuAutoAffinityStoredStateReport ToStoredStateReport(GpuInterruptAffinitySnapshot state) =>
        new(
            state.DeviceInstanceId,
            state.DisplayName,
            state.DriverVersion,
            state.AffinityPolicyKeyExisted,
            ToStoredValueReport(state.DevicePolicy),
            ToStoredValueReport(state.AssignmentSetOverride));

    private static GpuAutoAffinityStoredValueReport ToStoredValueReport(RegistryValueSnapshot value) =>
        new(
            value.Exists,
            value.Kind?.ToString(),
            Convert.ToHexString(value.Data));

    private static string ResolveDriverServiceName(string deviceInstanceId)
    {
        var target = DeviceInventoryReader.CapturePresentDevices().Devices.SingleOrDefault(device =>
            device.ClassGuid == DisplayDeviceClass &&
            string.Equals(device.InstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Gate A GPU target is not a present display adapter.");
        if (string.IsNullOrWhiteSpace(target.ServiceName))
        {
            throw new InvalidOperationException(
                "Gate A GPU target does not expose a driver service name for direct ETW attribution.");
        }

        return Path.GetFileNameWithoutExtension(Path.GetFileName(target.ServiceName.Trim()));
    }

    private static string ComputeTopologyIdentity(ProcessorTopologySnapshot topology)
    {
        var canonical = string.Join(
            '|',
            topology.ProcessorGroupCount.ToString(CultureInfo.InvariantCulture),
            topology.PhysicalCoreCount.ToString(CultureInfo.InvariantCulture),
            string.Join(';', topology.Cores.Select(core => string.Create(
                CultureInfo.InvariantCulture,
                $"{core.Index}:{core.EfficiencyClass}:{string.Join(',', core.LogicalProcessors.Select(static processor => $"{processor.Group}:{processor.Number}"))}"))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string? ReadPresentMonBinaryVersion(string? apiPath)
    {
        if (string.IsNullOrWhiteSpace(apiPath) || !File.Exists(apiPath))
        {
            return null;
        }

        var version = FileVersionInfo.GetVersionInfo(apiPath).FileVersion;
        if (!string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        // The standalone collector path is already SHA-256 pinned to 2.5.1 by
        // the locator before it is ever used. A missing version resource must
        // not fail an otherwise decision-grade trial.
        if (string.Equals(
                Path.GetFileName(apiPath),
                PresentMonConsoleLocator.PinnedFileName,
                StringComparison.OrdinalIgnoreCase))
        {
            return PresentMonConsoleLocator.PinnedVersion;
        }

        return null;
    }

    private static DateTimeOffset Max(DateTimeOffset first, DateTimeOffset second) =>
        first >= second ? first : second;

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;

    private static DateTimeOffset Max(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third) =>
        Max(Max(first, second), third);

    private static DateTimeOffset Min(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third) =>
        Min(Min(first, second), third);
}

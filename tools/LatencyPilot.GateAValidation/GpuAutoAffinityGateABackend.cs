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
    private const double ControlDriftThreshold = 0.20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string deviceInstanceId;
    private readonly string sourceRevisionId;
    private readonly Guid sessionId;
    private readonly uint benchmarkProcessId;
    private readonly GpuBenchmarkControlClient benchmark;
    private readonly GpuOptimizationExecutionBackend mutation;
    private readonly GpuInterruptAffinitySnapshot originalState;
    private readonly string topologyIdentity;
    private readonly string driverServiceName;
    private readonly HashSet<Guid> measuringExperiments = [];
    private readonly Dictionary<Guid, GpuAffinityCandidate> ownedCandidates = [];
    private readonly List<GpuAutoAffinityMutationAuditEntry> mutationAudit = [];
    private double? referenceControlP99Milliseconds;
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
        mutation = new GpuOptimizationExecutionBackend(journal);
        originalState = mutation.CaptureOriginal(deviceInstanceId);
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

    public Task<Guid> ApplyCandidateAsync(
        GpuAffinityCandidate candidate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(candidate);
        var experimentId = mutation.ApplyCandidate(deviceInstanceId, candidate);
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

            return Task.FromResult(experimentId);
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

    public Task RollbackAsync(Guid experimentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ownedCandidates.TryGetValue(experimentId, out var candidate);
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
        }
        finally
        {
            measuringExperiments.Remove(experimentId);
            ownedCandidates.Remove(experimentId);
        }

        return Task.CompletedTask;
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
        return Task.FromResult(
            experimentId != Guid.Empty &&
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
        if (candidate is not null && measuringExperiments.Add(experimentId!.Value))
        {
            mutation.BeginMeasurement(experimentId.Value);
        }

        // Provision and start the pinned standalone PresentMon collector before
        // the decision-grade workload begins. Provisioning is outside the trial
        // deadline so a first-run download cannot shorten the benchmark window.
        // The collector window covers typical D3D12 device recreation after
        // a GPU restart plus the full scored trial; the CSV is cropped to the
        // benchmark artifact interval during parsing. Kept to +12 s (not the
        // full 30 s recreation budget) so a fast trial does not idle 30 s
        // waiting for an over-long PresentMon timed exit on every run.
        var presentMonWindow = request.Duration + TimeSpan.FromSeconds(12);
        await using var presentMonSession = await PresentMonConsoleFrameMetricsReader.StartAsync(
            benchmarkProcessId,
            presentMonWindow,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Duration + TimeSpan.FromSeconds(60));
        // The controlled benchmark recreates its D3D12 device/window inside
        // RunTrial (required after a GPU apply/restart removes the device).
        // That recreation costs 1-3 s after the trial command is sent, so the
        // kernel window must cover recreation + the full scored trial.
        // Without this headroom the kernel/benchmark overlap gate fails
        // systematically even when every stream is healthy.
        var kernelDuration = request.Duration + TimeSpan.FromSeconds(8);
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
        var kernel = await kernelTask.ConfigureAwait(false);
        var artifactPath = await artifactPathTask.ConfigureAwait(false);
        var artifact = await ReadArtifactAsync(artifactPath, deadline.Token).ConfigureAwait(false);
        var presentMon = await presentMonSession.CompleteAsync(
            artifact.StartedAtUtc,
            artifact.EndedAtUtc,
            deadline.Token).ConfigureAwait(false);

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
        var reasons = ValidateTrial(
            request,
            artifact,
            kernel,
            presentMon,
            continuity,
            storedBefore,
            storedAfter);

        GpuInterruptRuntimePlacementEvidence? runtimePlacement = null;
        GpuAutoAffinityPlacementProof? placement = null;
        GpuInterruptIsrAttribution? isrAttribution = null;
        // Crop the extended kernel window to the scored benchmark artifact
        // interval so D3D12 device recreation + pre-roll idle cannot pollute
        // placement proof or driver guardrails.
        var scopedKernel = CropKernelToArtifact(kernel, artifact.StartedAtUtc, artifact.EndedAtUtc);
        if (kernel.IsValid && storedBefore && storedAfter)
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
                    if (!runtimePlacement.ConfirmsRequestedPlacement)
                    {
                        reasons.Add("Resolved single-adapter ISR placement was not confined to the requested logical processor.");
                    }
                }
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or
                NotSupportedException or
                System.ComponentModel.Win32Exception)
            {
                reasons.Add(
                    $"GPU ISR attribution failed: {exception.GetType().Name}: {exception.Message}");
            }
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
            ReadPresentMonBinaryVersion(presentMon.ApiPath),
            Guid.NewGuid(),
            kernel.IsValid,
            kernel.EventsLost,
            reasons.AsReadOnly(),
            artifact.FrozenWorkload);
        reportProvenance ??= GpuAutoAffinityReportProvenance.FromEvidence(evidence);

        var interpretation = GpuBenchmarkEvidenceInterpreter.Interpret(evidence);
        var controlDrifted = false;
        if (candidate is null &&
            string.Equals(request.Phase, "screening-control", StringComparison.Ordinal) &&
            interpretation.IsValid &&
            double.IsFinite(interpretation.FrameP99Milliseconds) &&
            interpretation.FrameP99Milliseconds > 0)
        {
            if (referenceControlP99Milliseconds is { } reference && reference > 0)
            {
                controlDrifted = Math.Abs(interpretation.FrameP99Milliseconds - reference) / reference >
                    ControlDriftThreshold;
            }
            else
            {
                referenceControlP99Milliseconds = interpretation.FrameP99Milliseconds;
            }
        }

        var driverDpc = FilterDriverDurations(scopedKernel, KernelLatencyEventKind.Dpc);
        var driverIsr = isrAttribution?.Events.Select(static item => item.DurationMicroseconds).ToArray() ?? [];
        var contamination = new GpuBenchmarkContaminationContext(
            SystemCpuBusyDrifted: false,
            ControlTrialDrifted: controlDrifted,
            SleepOrResumeDetected: continuity.Reasons.Any(static reason =>
                reason.Contains("sleep", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("awake", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("suspend", StringComparison.OrdinalIgnoreCase)),
            DeviceResetDetected: false,
            RetryAttempt: request.RetryAttempt);

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

        return artifact;
    }

    private List<string> ValidateTrial(
        GpuAutoAffinityTrialRequest request,
        GpuBenchmarkTrialArtifact artifact,
        KernelLatencyCaptureResult kernel,
        PresentMonFrameCaptureSnapshot presentMon,
        GpuOptimizationCaptureContinuityResult continuity,
        bool storedBefore,
        bool storedAfter)
    {
        var reasons = new List<string>();
        if (!string.Equals(artifact.Schema, GpuBenchmarkTrialArtifact.SchemaId, StringComparison.Ordinal) ||
            artifact.SessionId != sessionId || artifact.Frames.Count == 0)
        {
            reasons.Add("Benchmark raw artifact schema/session/frame evidence is invalid.");
        }
        if (artifact.FrozenWorkload.WorkerMap.Count == 0 ||
            artifact.WorkerChecksums.Count != artifact.FrozenWorkload.WorkerMap.Count)
        {
            reasons.Add("Benchmark frozen worker map/checksum evidence is incomplete.");
        }
        if (!kernel.IsValid)
        {
            reasons.Add("Kernel ETW capture integrity is not clean.");
        }
        if (!presentMon.IsAvailable || presentMon.ProcessId != benchmarkProcessId || presentMon.Frames.Count == 0)
        {
            reasons.Add("Raw PresentMon evidence is unavailable, empty, or belongs to a different process.");
        }
        if (!storedBefore || !storedAfter)
        {
            reasons.Add("Exact stored GPU affinity state was not stable before and after the trial.");
        }
        if (!continuity.IsStable)
        {
            reasons.AddRange(continuity.Reasons);
        }

        var kernelEnd = kernel.StartedAtUtc + kernel.ActualDuration;
        // The kernel window intentionally starts before the scored benchmark
        // (it covers D3D12 device recreation after a GPU restart) and runs
        // longer than the trial. Require the scored artifact to be contained
        // in the kernel window with 95% coverage, not a vacuous mutual
        // overlap that fails on healthy recreation offsets.
        var kernelCoveredMs = (Min(artifact.EndedAtUtc, kernelEnd) -
            Max(artifact.StartedAtUtc, kernel.StartedAtUtc)).TotalMilliseconds;
        if (!double.IsFinite(kernelCoveredMs) ||
            kernelCoveredMs < request.Duration.TotalMilliseconds * MinimumOverlapRatio)
        {
            reasons.Add("Kernel ETW does not cover at least 95% of the scored benchmark trial.");
        }

        // PresentMon now reports its honest CSV-observed window. Require the
        // scored artifact to be covered by that window rather than comparing
        // an echoed interval against itself.
        var presentMonCoveredMs = (Min(artifact.EndedAtUtc, presentMon.EndedAtUtc) -
            Max(artifact.StartedAtUtc, presentMon.StartedAtUtc)).TotalMilliseconds;
        if (!double.IsFinite(presentMonCoveredMs) ||
            presentMonCoveredMs < request.Duration.TotalMilliseconds * MinimumOverlapRatio)
        {
            reasons.Add("Standalone PresentMon does not cover at least 95% of the scored benchmark trial.");
        }

        return reasons;
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

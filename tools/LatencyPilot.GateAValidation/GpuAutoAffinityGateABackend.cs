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
        return Task.FromResult(experimentId);
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
        mutation.KeepCandidate(experimentId);
        var current = GpuInterruptAffinityPolicyStore.Capture(deviceInstanceId);
        var verified = string.Equals(
                current.DriverVersion,
                originalState.DriverVersion,
                StringComparison.OrdinalIgnoreCase) &&
            GpuInterruptAffinityStateComparer.MatchesCandidate(current, ToMutationCandidate(candidate));
        mutationAudit.Add(new GpuAutoAffinityMutationAuditEntry(
            DateTimeOffset.UtcNow,
            "KeepCandidate",
            experimentId,
            candidate.Processor,
            verified,
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

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Duration + TimeSpan.FromSeconds(20));
        var kernelTask = Task.Run(
            () => KernelLatencyCapture.Capture(
                new KernelLatencyCaptureOptions(request.Duration, KernelMaximumEvents),
                deadline.Token),
            deadline.Token);
        var presentMonTask = PresentMonFrameMetricsReader.CaptureAsync(
            benchmarkProcessId,
            request.Duration,
            cancellationToken: deadline.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(150), deadline.Token).ConfigureAwait(false);
        var artifactPathTask = benchmark.RunTrialAsync(
            request.RunNumber,
            request.Duration,
            deadline.Token);

        await Task.WhenAll(kernelTask, presentMonTask, artifactPathTask).ConfigureAwait(false);
        var kernel = await kernelTask.ConfigureAwait(false);
        var presentMon = await presentMonTask.ConfigureAwait(false);
        var artifactPath = await artifactPathTask.ConfigureAwait(false);
        var artifact = await ReadArtifactAsync(artifactPath, deadline.Token).ConfigureAwait(false);

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
        if (candidate is not null && kernel.IsValid && storedBefore && storedAfter)
        {
            try
            {
                runtimePlacement = GpuInterruptRuntimePlacementVerifier.Analyze(
                    kernel,
                    deviceInstanceId,
                    ToMutationCandidate(candidate));
                placement = new GpuAutoAffinityPlacementProof(
                    candidate.Processor,
                    runtimePlacement.TargetProcessorIsrEventCount,
                    runtimePlacement.OffTargetIsrEventCount);
                if (!runtimePlacement.ConfirmsRequestedPlacement)
                {
                    reasons.Add(
                        "Resolved GPU-driver ISR placement was not confined to the requested logical processor.");
                }
            }
            catch (Exception exception) when (exception is
                InvalidOperationException or
                NotSupportedException or
                System.ComponentModel.Win32Exception)
            {
                reasons.Add(
                    $"GPU-driver ISR placement proof failed: {exception.GetType().Name}: {exception.Message}");
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
            $"{continuityBefore.GraphicsTarget.DeviceInstanceId}|{continuityBefore.GraphicsTarget.Luid}|pm:{continuityBefore.GraphicsTarget.PresentMonDeviceId}");
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
        if (candidate is null && interpretation.IsValid &&
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

        var driverDpc = FilterDriverDurations(kernel, KernelLatencyEventKind.Dpc);
        var driverIsr = FilterDriverDurations(kernel, KernelLatencyEventKind.Isr);
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
            driverIsr);
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
            ?? throw new InvalidDataException("GPU benchmark trial artifact is empty.");
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

        var overlapStart = Max(
            artifact.StartedAtUtc,
            kernel.StartedAtUtc,
            presentMon.StartedAtUtc);
        var overlapEnd = Min(
            artifact.EndedAtUtc,
            kernel.StartedAtUtc + kernel.ActualDuration,
            presentMon.EndedAtUtc);
        var overlapMilliseconds = (overlapEnd - overlapStart).TotalMilliseconds;
        if (!double.IsFinite(overlapMilliseconds) ||
            overlapMilliseconds < request.Duration.TotalMilliseconds * MinimumOverlapRatio)
        {
            reasons.Add("Benchmark, ETW, and PresentMon do not overlap for at least 95% of the requested trial.");
        }

        return reasons;
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

        return FileVersionInfo.GetVersionInfo(apiPath).FileVersion;
    }

    private static DateTimeOffset Max(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third) =>
        first >= second
            ? first >= third ? first : third
            : second >= third ? second : third;

    private static DateTimeOffset Min(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third) =>
        first <= second
            ? first <= third ? first : third
            : second <= third ? second : third;
}

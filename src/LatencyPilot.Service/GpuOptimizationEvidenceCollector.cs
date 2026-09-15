using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Metrics;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;

namespace LatencyPilot.Service;

internal sealed record GpuOptimizationEvidenceRequest(
    int RunNumber,
    GpuConfirmationOrder Role,
    Guid SessionId,
    uint WorkloadProcessId,
    string WorkloadIdentity,
    string EnvironmentIdentity,
    string SourceRevisionId,
    GpuAffinityCandidate Finalist,
    TimeSpan RequestedDuration);

internal sealed record GpuOptimizationStateVerification(
    string TargetDeviceInstanceId,
    GpuConfirmationOrder Role,
    LogicalProcessorId? AppliedProcessor,
    bool IsVerified,
    DateTimeOffset VerifiedAtUtc);

internal sealed record GpuOptimizationEvidenceCollectionResult(
    bool IsUsable,
    GpuOptimizationConfirmationRun? Run,
    string? Reason,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    GpuInterruptRuntimePlacementEvidence? RuntimePlacement)
{
    internal static GpuOptimizationEvidenceCollectionResult Unusable(
        string reason,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        GpuInterruptRuntimePlacementEvidence? runtimePlacement = null) =>
        new(false, null, reason, startedAtUtc, endedAtUtc, runtimePlacement);
}

internal sealed class GpuOptimizationEvidenceCollector
{
    private const double MinimumDurationRatio = 0.95;
    private const int CaptureDeadlineSlackSeconds = 15;
    private const int KernelMaximumEvents = 2_000_000;

    internal static async Task<GpuOptimizationEvidenceCollectionResult> CaptureAsync(
        GpuOptimizationEvidenceRequest request,
        GpuInterruptAffinitySnapshot originalState,
        string? presentMonApiPath = null,
        string? presentMonControlPipeName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(originalState);
        ValidateRequestShape(request);

        var continuityStartedAt = DateTimeOffset.UtcNow;
        if (!GpuOptimizationCaptureContinuity.TryCapture(
                request.WorkloadProcessId,
                originalState.DeviceInstanceId,
                presentMonApiPath,
                presentMonControlPipeName,
                out var continuityBefore,
                out var continuityBeforeReason) || continuityBefore is null)
        {
            return GpuOptimizationEvidenceCollectionResult.Unusable(
                $"Workload continuity could not be established before capture: {continuityBeforeReason}",
                continuityStartedAt,
                DateTimeOffset.UtcNow);
        }

        var before = VerifyExpectedStoredState(request, originalState);
        if (!before.IsVerified)
        {
            return GpuOptimizationEvidenceCollectionResult.Unusable(
                "Expected GPU affinity state was not verified immediately before capture.",
                before.VerifiedAtUtc,
                before.VerifiedAtUtc);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.RequestedDuration + TimeSpan.FromSeconds(CaptureDeadlineSlackSeconds));

        var kernelTask = Task.Run(
            () => LatencyPilot.Platform.Windows.Etw.KernelLatencyCapture.Capture(
                new KernelLatencyCaptureOptions(request.RequestedDuration, KernelMaximumEvents),
                deadline.Token),
            deadline.Token);
        var presentMonTask = PresentMonFrameMetricsReader.CaptureAsync(
            request.WorkloadProcessId,
            request.RequestedDuration,
            presentMonApiPath,
            presentMonControlPipeName,
            deadline.Token);

        await Task.WhenAll(kernelTask, presentMonTask).ConfigureAwait(false);
        var kernelCapture = await kernelTask.ConfigureAwait(false);
        var presentMonCapture = await presentMonTask.ConfigureAwait(false);
        var after = VerifyExpectedStoredState(request, originalState);
        var combinedVerification = after with
        {
            IsVerified = before.IsVerified && after.IsVerified,
        };

        var captureStartedAt = Max(kernelCapture.StartedAtUtc, presentMonCapture.StartedAtUtc);
        var captureEndedAt = Min(
            kernelCapture.StartedAtUtc + kernelCapture.ActualDuration,
            presentMonCapture.EndedAtUtc);
        if (!GpuOptimizationCaptureContinuity.TryCapture(
                request.WorkloadProcessId,
                originalState.DeviceInstanceId,
                presentMonApiPath,
                presentMonControlPipeName,
                out var continuityAfter,
                out var continuityAfterReason) || continuityAfter is null)
        {
            return GpuOptimizationEvidenceCollectionResult.Unusable(
                $"Workload continuity could not be established after capture: {continuityAfterReason}",
                captureStartedAt,
                captureEndedAt < captureStartedAt ? captureStartedAt : captureEndedAt);
        }

        var continuity = GpuOptimizationCaptureContinuity.Evaluate(continuityBefore, continuityAfter);
        if (!continuity.IsStable)
        {
            return GpuOptimizationEvidenceCollectionResult.Unusable(
                $"Workload or environment changed during capture: {string.Join(" ", continuity.Reasons)}",
                captureStartedAt,
                captureEndedAt < captureStartedAt ? captureStartedAt : captureEndedAt);
        }

        GpuInterruptRuntimePlacementEvidence? runtimePlacement = null;
        if (request.Role == GpuConfirmationOrder.Candidate && combinedVerification.IsVerified)
        {
            try
            {
                runtimePlacement = GpuInterruptRuntimePlacementVerifier.Analyze(
                    kernelCapture,
                    originalState.DeviceInstanceId,
                    new GpuInterruptAffinityCandidate(
                        request.Finalist.Processor.Group,
                        request.Finalist.Processor.Number,
                        1UL << request.Finalist.Processor.Number));
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                NotSupportedException or
                System.ComponentModel.Win32Exception)
            {
                return GpuOptimizationEvidenceCollectionResult.Unusable(
                    $"Effective GPU ISR placement evidence could not be established: {exception.Message}",
                    captureStartedAt,
                    captureEndedAt < captureStartedAt ? captureStartedAt : captureEndedAt);
            }
        }

        return TryCreateRun(
            request,
            combinedVerification,
            kernelCapture,
            presentMonCapture,
            runtimePlacement,
            Guid.NewGuid());
    }

    internal static GpuOptimizationEvidenceCollectionResult TryCreateRun(
        GpuOptimizationEvidenceRequest request,
        GpuOptimizationStateVerification verification,
        KernelLatencyCaptureResult kernelCapture,
        PresentMonFrameCaptureSnapshot presentMonCapture,
        GpuInterruptRuntimePlacementEvidence? runtimePlacement,
        Guid captureId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(kernelCapture);
        ArgumentNullException.ThrowIfNull(presentMonCapture);

        var startedAt = Max(kernelCapture.StartedAtUtc, presentMonCapture.StartedAtUtc);
        var kernelEndedAt = kernelCapture.StartedAtUtc + kernelCapture.ActualDuration;
        var endedAt = Min(kernelEndedAt, presentMonCapture.EndedAtUtc);

        string? invalidReason = null;
        try
        {
            ValidateRequestShape(request);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            invalidReason = $"Invalid evidence request: {exception.Message}";
        }

        if (invalidReason is null && captureId == Guid.Empty)
        {
            invalidReason = "Capture identity is missing.";
        }

        if (invalidReason is null &&
            (!verification.IsVerified ||
             string.IsNullOrWhiteSpace(verification.TargetDeviceInstanceId) ||
             verification.Role != request.Role ||
             (request.Role == GpuConfirmationOrder.Candidate
                 ? verification.AppliedProcessor != request.Finalist.Processor
                 : verification.AppliedProcessor is not null)))
        {
            invalidReason = "Expected GPU affinity state was not verified for this run role.";
        }

        if (invalidReason is null && request.Role == GpuConfirmationOrder.Candidate &&
            (runtimePlacement is null ||
             !string.Equals(
                 runtimePlacement.DeviceInstanceId,
                 verification.TargetDeviceInstanceId,
                 StringComparison.OrdinalIgnoreCase) ||
             runtimePlacement.TargetProcessorNumber != request.Finalist.Processor.Number ||
             !runtimePlacement.ConfirmsRequestedPlacement))
        {
            invalidReason =
                "Candidate run lacks direct GPU-driver ISR evidence confined to the requested target processor.";
        }

        if (invalidReason is null && request.Role == GpuConfirmationOrder.Original && runtimePlacement is not null)
        {
            invalidReason = "Original-state runs must not claim candidate runtime-placement evidence.";
        }

        if (invalidReason is null &&
            (!kernelCapture.IsValid ||
             kernelCapture.RequestedDuration != request.RequestedDuration ||
             kernelCapture.ActualDuration <= TimeSpan.Zero))
        {
            invalidReason = "Kernel ETW capture integrity or requested-duration identity is invalid.";
        }

        if (invalidReason is null &&
            (!presentMonCapture.IsAvailable ||
             presentMonCapture.ProcessId != request.WorkloadProcessId ||
             presentMonCapture.ApiVersion is null ||
             !NearlyEqual(presentMonCapture.RequestedWindowMilliseconds, request.RequestedDuration.TotalMilliseconds) ||
             presentMonCapture.Frames.Count == 0))
        {
            invalidReason = "PresentMon raw-frame evidence is unavailable or belongs to a different workload/window.";
        }

        var overlapMilliseconds = (endedAt - startedAt).TotalMilliseconds;
        if (invalidReason is null &&
            (!double.IsFinite(overlapMilliseconds) ||
             overlapMilliseconds < request.RequestedDuration.TotalMilliseconds * MinimumDurationRatio))
        {
            invalidReason = "ETW and PresentMon do not share at least 95% of the requested evidence interval.";
        }

        var dpcSamples = kernelCapture.Events
            .Where(static item => item.Kind == KernelLatencyEventKind.Dpc)
            .Select(static item => item.DurationMicroseconds)
            .ToArray();
        if (invalidReason is null &&
            (dpcSamples.Length < GpuOptimizationConfirmation.MinimumSamplesPerMetricRun ||
             dpcSamples.Any(static value => !double.IsFinite(value) || value < 0)))
        {
            invalidReason = "Kernel capture does not contain enough valid DPC duration observations for confirmation.";
        }

        var guardrails = PresentMonGuardrailSeriesBuilder.Create(presentMonCapture);
        if (invalidReason is null &&
            (guardrails.Count == 0 || guardrails.Values.Any(static series =>
                series.Samples.Count < GpuOptimizationConfirmation.MinimumSamplesPerMetricRun)))
        {
            invalidReason = "PresentMon capture does not contain enough complete raw-frame guardrail observations.";
        }

        if (invalidReason is not null)
        {
            return GpuOptimizationEvidenceCollectionResult.Unusable(
                invalidReason,
                startedAt,
                endedAt < startedAt ? startedAt : endedAt,
                runtimePlacement);
        }

        var primary = new MetricSeries(
            "DPC duration (us)",
            MetricDirection.LowerIsBetter,
            dpcSamples);
        var measurement = new GpuOptimizationMeasurementSet(primary, guardrails);
        var run = new GpuOptimizationConfirmationRun(
            request.RunNumber,
            request.Role,
            request.SessionId,
            captureId,
            request.WorkloadIdentity,
            request.EnvironmentIdentity,
            request.SourceRevisionId,
            request.Role == GpuConfirmationOrder.Candidate ? request.Finalist.Processor : null,
            true,
            true,
            checked((int)request.RequestedDuration.TotalMilliseconds),
            overlapMilliseconds,
            measurement);

        return new GpuOptimizationEvidenceCollectionResult(
            true,
            run,
            null,
            startedAt,
            endedAt,
            runtimePlacement);
    }

    private static GpuOptimizationStateVerification VerifyExpectedStoredState(
        GpuOptimizationEvidenceRequest request,
        GpuInterruptAffinitySnapshot originalState)
    {
        var verifiedAt = DateTimeOffset.UtcNow;
        var current = GpuInterruptAffinityPolicyStore.Capture(originalState.DeviceInstanceId);
        var driverMatches = string.Equals(
            current.DriverVersion,
            originalState.DriverVersion,
            StringComparison.OrdinalIgnoreCase);

        var expectedProcessor = request.Role == GpuConfirmationOrder.Candidate
            ? request.Finalist.Processor
            : (LogicalProcessorId?)null;
        var stateMatches = request.Role switch
        {
            GpuConfirmationOrder.Original =>
                GpuInterruptAffinityStateComparer.MatchesOriginal(current, originalState),
            GpuConfirmationOrder.Candidate =>
                GpuInterruptAffinityStateComparer.MatchesCandidate(
                    current,
                    new GpuInterruptAffinityCandidate(
                        request.Finalist.Processor.Group,
                        request.Finalist.Processor.Number,
                        1UL << request.Finalist.Processor.Number)),
            _ => false,
        };

        return new GpuOptimizationStateVerification(
            originalState.DeviceInstanceId,
            request.Role,
            expectedProcessor,
            driverMatches && stateMatches,
            verifiedAt);
    }

    private static void ValidateRequestShape(GpuOptimizationEvidenceRequest request)
    {
        if (request.RunNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Run number must be positive.");
        }

        if (request.SessionId == Guid.Empty || request.WorkloadProcessId == 0)
        {
            throw new ArgumentException("Session and workload process identities are required.", nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkloadIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.EnvironmentIdentity);
        if (request.SourceRevisionId is not { Length: 40 } revision || !revision.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("Source revision must be an exact 40-character hexadecimal commit SHA.", nameof(request));
        }

        ArgumentNullException.ThrowIfNull(request.Finalist);
        if (request.Finalist.Processor.Group != 0 || request.Finalist.Processor.Number >= 64)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "GPU confirmation v1 supports only a group-0 x64 affinity finalist.");
        }

        if (request.RequestedDuration.TotalMilliseconds < GpuOptimizationConfirmation.MinimumRunDurationMilliseconds ||
            request.RequestedDuration.TotalMilliseconds > 60_000 ||
            request.RequestedDuration.TotalMilliseconds != Math.Truncate(request.RequestedDuration.TotalMilliseconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "GPU evidence duration must be an integral millisecond value from 30 to 60 seconds.");
        }
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static bool NearlyEqual(double left, double right) =>
        double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) <= 0.5;
}

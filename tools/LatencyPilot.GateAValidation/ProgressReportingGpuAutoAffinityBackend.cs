using System.Text.Json;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;

namespace LatencyPilot.GateAValidation;

internal sealed class ProgressReportingGpuAutoAffinityBackend(
    IGpuAutoAffinitySessionBackend inner,
    GpuGateAProgressFile progress) : IGpuAutoAffinitySessionBackend, IGpuAutoAffinitySessionObserver
{
    private readonly Dictionary<Guid, GpuAffinityCandidate> activeCandidates = [];
    private readonly List<double> screeningOriginalLow1Fps = [];
    private int pendingOriginalRetryIndex = -1;

    public async Task<GpuAutoAffinityTrialObservation> CaptureOriginalAsync(
        GpuAutoAffinityTrialRequest request,
        CancellationToken cancellationToken)
    {
        await progress.ReportTrialStartingAsync(request).ConfigureAwait(false);
        var observation = await inner.CaptureOriginalAsync(request, cancellationToken).ConfigureAwait(false);
        observation = ApplyBoundedOriginalBaselineRecovery(request, observation);
        await progress.ReportTrialCompletedAsync(request, observation).ConfigureAwait(false);
        return observation;
    }

    public async Task<Guid> ApplyCandidateAsync(
        GpuAffinityCandidate candidate,
        CancellationToken cancellationToken)
    {
        var experimentId = await inner.ApplyCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
        activeCandidates[experimentId] = candidate;
        return experimentId;
    }

    public async Task<GpuAutoAffinityTrialObservation> CaptureCandidateAsync(
        Guid experimentId,
        GpuAutoAffinityTrialRequest request,
        CancellationToken cancellationToken)
    {
        await progress.ReportTrialStartingAsync(request).ConfigureAwait(false);
        var observation = await inner.CaptureCandidateAsync(
            experimentId,
            request,
            cancellationToken).ConfigureAwait(false);
        await progress.ReportTrialCompletedAsync(request, observation).ConfigureAwait(false);
        return observation;
    }

    public async Task RollbackAsync(Guid experimentId, CancellationToken cancellationToken)
    {
        activeCandidates.TryGetValue(experimentId, out var candidate);
        Exception? progressFailure = null;
        try
        {
            await progress.ReportRestoringAsync(
                candidate,
                candidate is null
                    ? "Restoring exact original GPU affinity state."
                    : $"Restoring exact original state after CPU {candidate.Processor}.").ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            progressFailure = exception;
        }

        await inner.RollbackAsync(experimentId, cancellationToken).ConfigureAwait(false);
        activeCandidates.Remove(experimentId);
        if (progressFailure is not null)
        {
            throw new IOException(
                "GPU affinity rollback completed, but progress reporting failed before rollback.",
                progressFailure);
        }
    }

    public async Task KeepAsync(Guid experimentId, CancellationToken cancellationToken)
    {
        await inner.KeepAsync(experimentId, cancellationToken).ConfigureAwait(false);
        activeCandidates.Remove(experimentId);
    }

    public Task<bool> VerifyOriginalStateAsync(CancellationToken cancellationToken) =>
        inner.VerifyOriginalStateAsync(cancellationToken);

    public Task<bool> VerifyCandidateStateAsync(
        Guid experimentId,
        GpuAffinityCandidate candidate,
        CancellationToken cancellationToken) =>
        inner.VerifyCandidateStateAsync(experimentId, candidate, cancellationToken);

    public Task CandidateEvaluatedAsync(GpuAutoAffinityCandidateReport report) =>
        progress.ReportCandidateEvaluatedAsync(report);

    private GpuAutoAffinityTrialObservation ApplyBoundedOriginalBaselineRecovery(
        GpuAutoAffinityTrialRequest request,
        GpuAutoAffinityTrialObservation observation)
    {
        if (!string.Equals(request.Phase, "screening-original", StringComparison.Ordinal))
        {
            return observation;
        }

        var videoStats = GpuBenchmarkEvidenceInterpreter.Interpret(observation.Evidence).VideoStats;
        if (videoStats is null ||
            !double.IsFinite(videoStats.Low1PctFps) ||
            videoStats.Low1PctFps <= 0d)
        {
            return observation;
        }

        if (request.RetryAttempt == 0)
        {
            screeningOriginalLow1Fps.Add(videoStats.Low1PctFps);
            pendingOriginalRetryIndex = screeningOriginalLow1Fps.Count - 1;

            if (screeningOriginalLow1Fps.Count == GpuOriginalBaselinePolicy.ReplacementTriggerAttemptCount &&
                !GpuOriginalBaselinePolicy.HasRepeatableCluster(screeningOriginalLow1Fps))
            {
                // CaptureAcceptedAsync owns exactly one retry. Reclassify only this
                // fourth Original as retryable contamination so the replacement run
                // becomes the bounded fifth physical baseline sample. The discarded
                // fourth sample remains present in TrialReports/audit evidence.
                return observation with
                {
                    Contamination = observation.Contamination with
                    {
                        ControlTrialDrifted = true,
                    },
                };
            }

            return observation;
        }

        if (pendingOriginalRetryIndex >= 0 &&
            pendingOriginalRetryIndex < screeningOriginalLow1Fps.Count)
        {
            screeningOriginalLow1Fps[pendingOriginalRetryIndex] = videoStats.Low1PctFps;
        }
        else
        {
            screeningOriginalLow1Fps.Add(videoStats.Low1PctFps);
        }
        pendingOriginalRetryIndex = -1;

        if (screeningOriginalLow1Fps.Count == GpuOriginalBaselinePolicy.ReplacementTriggerAttemptCount &&
            !GpuOriginalBaselinePolicy.HasRepeatableCluster(screeningOriginalLow1Fps))
        {
            // This is the single bounded replacement. Returning retryable
            // contamination on retry attempt 1 makes readiness terminally
            // Inconclusive, so the session exits before its first candidate write.
            return observation with
            {
                Contamination = observation.Contamination with
                {
                    ControlTrialDrifted = true,
                },
            };
        }

        return observation;
    }
}

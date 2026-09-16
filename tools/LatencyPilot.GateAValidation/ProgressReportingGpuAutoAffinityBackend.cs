using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;

namespace LatencyPilot.GateAValidation;

internal sealed class ProgressReportingGpuAutoAffinityBackend(
    IGpuAutoAffinitySessionBackend inner,
    GpuGateAProgressFile progress) : IGpuAutoAffinitySessionBackend, IGpuAutoAffinitySessionObserver
{
    private readonly Dictionary<Guid, GpuAffinityCandidate> activeCandidates = [];

    public async Task<GpuAutoAffinityTrialObservation> CaptureOriginalAsync(
        GpuAutoAffinityTrialRequest request,
        CancellationToken cancellationToken)
    {
        await progress.ReportTrialStartingAsync(request).ConfigureAwait(false);
        var observation = await inner.CaptureOriginalAsync(request, cancellationToken).ConfigureAwait(false);
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
        await progress.ReportRestoringAsync(
            candidate,
            candidate is null
                ? "Restoring exact original GPU affinity state."
                : $"Restoring exact original state after CPU {candidate.Processor}.").ConfigureAwait(false);
        await inner.RollbackAsync(experimentId, cancellationToken).ConfigureAwait(false);
        activeCandidates.Remove(experimentId);
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
}

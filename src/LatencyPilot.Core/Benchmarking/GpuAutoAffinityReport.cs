using LatencyPilot.Core.System;

namespace LatencyPilot.Core.Benchmarking;

public sealed record GpuAutoAffinityPlacementProof(
    LogicalProcessorId TargetProcessor,
    int TargetIsrEventCount,
    int OffTargetIsrEventCount)
{
    public bool ConfirmsRequestedPlacement =>
        TargetIsrEventCount > 0 && OffTargetIsrEventCount == 0;
}

public sealed record GpuAutoAffinityTrialReport(
    int RunNumber,
    string Phase,
    string Role,
    LogicalProcessorId? Processor,
    Guid CaptureId,
    string ReadinessState,
    bool StoredStateVerifiedBefore,
    bool StoredStateVerifiedAfter,
    GpuAutoAffinityPlacementProof? Placement,
    double? FrameP99Milliseconds,
    double? OnePercentLowFps,
    double RequestedDurationMilliseconds,
    double ActualDurationMilliseconds,
    IReadOnlyList<string> Reasons);

public sealed record GpuAutoAffinityCandidateReport(
    string Phase,
    int PhysicalCoreIndex,
    LogicalProcessorId Processor,
    int TrialCount,
    string Verdict,
    double? RelativeFrameP99Improvement,
    IReadOnlyList<string> RegressedGuardrails);

public sealed record GpuAutoAffinityReport(
    string Schema,
    Guid SessionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    int ShuffleSeed,
    IReadOnlyList<GpuAutoAffinityCandidateReport> Candidates,
    IReadOnlyList<GpuAutoAffinityTrialReport> Trials,
    string FinalRecommendation,
    LogicalProcessorId? FinalProcessor,
    bool FinalStateVerified,
    bool OriginalStateRestored,
    IReadOnlyList<string> Reasons)
{
    public const string SchemaId = "latencypilot-gpu-auto-affinity-report-v1";
}

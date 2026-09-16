using LatencyPilot.Core.System;

namespace LatencyPilot.Core.Benchmarking;

public sealed record GpuOptimizationProgressSnapshot(
    string Schema,
    Guid SessionId,
    string Phase,
    int CompletedUnits,
    int TotalUnits,
    LogicalProcessorId? Processor,
    int? PhysicalCore,
    int? CandidateIndex,
    int? CandidateCount,
    string Message,
    double ElapsedMilliseconds,
    double? EstimatedRemainingMilliseconds,
    double? FrameP99Milliseconds,
    double? OnePercentLowFps,
    string IsrPlacementState,
    string LastCompletedCandidateVerdict,
    bool IsRestoring,
    bool IsTerminal)
{
    public const string SchemaId = "latencypilot-gpu-optimizer-progress-v1";

    public double PercentComplete => TotalUnits <= 0
        ? 0d
        : Math.Clamp(CompletedUnits * 100d / TotalUnits, 0d, 100d);
}

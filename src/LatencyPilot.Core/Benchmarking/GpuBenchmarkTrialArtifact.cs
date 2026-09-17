using LatencyPilot.Core.System;

namespace LatencyPilot.Core.Benchmarking;

public sealed record GpuBenchmarkArtifactWorkload(
    int CommandBatchesPerWorker,
    int SimulationIterationsPerWorker,
    IReadOnlyList<LogicalProcessorId> WorkerMap,
    int Seed,
    int Width,
    int Height);

public readonly record struct GpuBenchmarkArtifactFrame(
    long FrameIndex,
    double CpuRecordingMilliseconds,
    double GpuWorkMilliseconds,
    double FramePeriodMilliseconds);

public sealed record GpuBenchmarkTrialArtifact(
    string Schema,
    Guid SessionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    string Adapter,
    string PresentMode,
    ulong GpuTimestampFrequency,
    GpuBenchmarkArtifactWorkload FrozenWorkload,
    IReadOnlyList<GpuBenchmarkArtifactFrame> Frames,
    IReadOnlyList<ulong> WorkerChecksums)
{
    public const string SchemaId = "latencypilot-gpu-benchmark-v1";
}

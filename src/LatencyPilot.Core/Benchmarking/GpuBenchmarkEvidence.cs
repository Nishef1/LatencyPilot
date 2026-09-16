using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;

namespace LatencyPilot.Core.Benchmarking;

public sealed record GpuBenchmarkEvidence(
    string Schema,
    string SourceRevisionId,
    string MethodId,
    string WindowsIdentity,
    string GpuIdentity,
    string DriverIdentity,
    string TopologyIdentity,
    uint BenchmarkProcessId,
    string TrialRole,
    int TrialIndex,
    LogicalProcessorId? CandidateProcessor,
    string FrozenWorkloadIdentity,
    IReadOnlyList<LogicalProcessorId> WorkerMap,
    int Seed,
    ulong D3D12TimestampFrequency,
    IReadOnlyList<double> D3D12GpuWorkMilliseconds,
    PresentMonFrameCaptureSnapshot PresentMonCapture,
    string? PresentMonBinaryVersion,
    Guid EtwCaptureId,
    bool EtwIntegrityComplete,
    int EtwLostEventCount,
    IReadOnlyList<string> ValidityReasons,
    GpuBenchmarkArtifactWorkload? FrozenWorkload = null)
{
    public const string SchemaId = "latencypilot-gpu-benchmark-v1";
    public const string MethodIdValue = "gpu-affinity-benchmark-v1";
}

using LatencyPilot.Core.Devices;
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

public sealed record GpuAutoAffinityReportProvenance(
    string SourceRevisionId,
    string MethodId,
    string WindowsIdentity,
    string GpuIdentity,
    string DriverIdentity,
    string TopologyIdentity,
    uint BenchmarkProcessId,
    string FrozenWorkloadIdentity,
    GpuBenchmarkArtifactWorkload FrozenWorkload,
    string? PresentMonBinaryVersion,
    PresentMonApiVersionSnapshot? PresentMonApiVersion,
    ulong D3D12TimestampFrequency)
{
    public static GpuAutoAffinityReportProvenance FromEvidence(GpuBenchmarkEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var workload = evidence.FrozenWorkload
            ?? throw new InvalidOperationException(
                "GPU auto-affinity report provenance requires the frozen calibrated benchmark workload.");
        if (workload.Seed != evidence.Seed ||
            workload.WorkerMap.Count != evidence.WorkerMap.Count ||
            !workload.WorkerMap.SequenceEqual(evidence.WorkerMap))
        {
            throw new InvalidOperationException(
                "GPU benchmark evidence and frozen-workload provenance disagree on seed or worker placement.");
        }

        return new GpuAutoAffinityReportProvenance(
            evidence.SourceRevisionId,
            evidence.MethodId,
            evidence.WindowsIdentity,
            evidence.GpuIdentity,
            evidence.DriverIdentity,
            evidence.TopologyIdentity,
            evidence.BenchmarkProcessId,
            evidence.FrozenWorkloadIdentity,
            workload,
            evidence.PresentMonBinaryVersion,
            evidence.PresentMonCapture.ApiVersion,
            evidence.D3D12TimestampFrequency);
    }

    public static GpuAutoAffinityReportProvenance? TryFromEvidence(GpuBenchmarkEvidence evidence) =>
        evidence.FrozenWorkload is null ? null : FromEvidence(evidence);
}

public sealed record GpuAutoAffinityStoredValueReport(
    bool Exists,
    string? Kind,
    string DataHex);

public sealed record GpuAutoAffinityStoredStateReport(
    string DeviceInstanceId,
    string DisplayName,
    string? DriverVersion,
    bool AffinityPolicyKeyExisted,
    GpuAutoAffinityStoredValueReport DevicePolicy,
    GpuAutoAffinityStoredValueReport AssignmentSetOverride);

public sealed record GpuAutoAffinityMutationAuditEntry(
    DateTimeOffset TimestampUtc,
    string Action,
    Guid? ExperimentId,
    LogicalProcessorId? Processor,
    bool StoredStateVerified,
    GpuAutoAffinityStoredStateReport StoredState,
    string? Error = null);

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
    IReadOnlyList<string> Reasons,
    GpuAutoAffinityReportProvenance? Provenance = null,
    GpuAutoAffinityInterruptEvidence? InterruptEvidence = null,
    double? AvgFps = null,
    double? Low01PctFps = null);

public sealed record GpuAutoAffinityInterruptEvidence(
    string IsrModuleName,
    string IsrAttributionMode,
    int DpcSampleCount,
    int IsrSampleCount,
    int UnresolvedIsrEventCount);

public sealed record GpuAutoAffinityCandidateReport(
    string Phase,
    int PhysicalCoreIndex,
    LogicalProcessorId Processor,
    int TrialCount,
    string Verdict,
    double? RelativeFrameP99Improvement,
    IReadOnlyList<string> RegressedGuardrails,
    string? Reason = null,
    double? DecisionOnePercentLowFps = null,
    double? DecisionAvgFps = null,
    double? DecisionFrameP99Milliseconds = null,
    double? DecisionLow01PctFps = null,
    double? LocalControlUncertainty = null,
    bool UsesTimeLocalNormalization = false);

public sealed record UsbAffinityRecommendationReport(
    string Status,
    string? ControllerInstanceId,
    LogicalProcessorId? Processor,
    IReadOnlyList<string> InputDeviceInstanceIds,
    double? TotalInterruptDurationMicroseconds,
    double? InterruptTailP99Microseconds,
    int? DpcCount,
    int? IsrCount,
    string Reason);

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
    IReadOnlyList<string> Reasons,
    GpuAutoAffinityReportProvenance? Provenance = null,
    GpuAutoAffinityStoredStateReport? OriginalStoredState = null,
    GpuAutoAffinityStoredStateReport? FinalStoredState = null,
    IReadOnlyList<GpuAutoAffinityMutationAuditEntry>? MutationAudit = null,
    string? RecoveryStatus = null,
    UsbAffinityRecommendationReport? UsbRecommendation = null)
{
    public const string SchemaId = "latencypilot-gpu-auto-affinity-report-v1";

    public string SourceState { get; init; } = "unknown";

    public bool GateAClosureEligible { get; init; }
}

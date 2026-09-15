namespace LatencyPilot.Core.Devices;

public enum PresentMonWorkloadCaptureStatus
{
    Available,
    ApiUnavailable,
    ServiceUnavailable,
    VersionIncompatible,
    InvalidProcess,
    TrackingFailed,
    QueryUnavailable,
    PollFailed,
    NoSwapChains,
    InvalidData,
}

public sealed record PresentMonApiVersionSnapshot(
    ushort Major,
    ushort Minor,
    ushort Patch)
{
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

public sealed record PresentMonSwapChainMetricsSnapshot(
    ulong SwapChainAddress,
    double? PresentedFps,
    double? DisplayedFps,
    double? CpuFrameTimeMilliseconds,
    double? CpuBusyMilliseconds,
    double? CpuWaitMilliseconds,
    double? GpuTimeMilliseconds,
    double? GpuBusyMilliseconds,
    double? GpuWaitMilliseconds,
    double? DroppedFrameRatio,
    double? GpuLatencyMilliseconds,
    double? DisplayLatencyMilliseconds);

public sealed record PresentMonWorkloadMetricsSnapshot(
    PresentMonWorkloadCaptureStatus Status,
    uint ProcessId,
    double RequestedWindowMilliseconds,
    PresentMonApiVersionSnapshot? ApiVersion,
    IReadOnlyList<PresentMonSwapChainMetricsSnapshot> SwapChains,
    IReadOnlyList<string> UnavailableOptionalMetrics,
    string? ApiPath,
    int? NativeStatusCode,
    string? Error,
    DateTimeOffset CapturedAtUtc)
{
    public bool IsAvailable => Status == PresentMonWorkloadCaptureStatus.Available;

    public bool HasFrameDeliveryEvidence => SwapChains.Any(static chain =>
        chain.PresentedFps is not null || chain.DisplayedFps is not null);

    public bool HasGpuGuardrailEvidence => SwapChains.Any(static chain =>
        chain.GpuBusyMilliseconds is not null ||
        chain.GpuLatencyMilliseconds is not null ||
        chain.DisplayLatencyMilliseconds is not null);
}

public sealed record PresentMonFrameMetricsSnapshot(
    ulong SwapChainAddress,
    double? CpuFrameTimeMilliseconds,
    double? CpuBusyMilliseconds,
    double? CpuWaitMilliseconds,
    double? GpuTimeMilliseconds,
    double? GpuBusyMilliseconds,
    double? GpuWaitMilliseconds,
    bool? DroppedFrame,
    double? GpuLatencyMilliseconds,
    double? DisplayLatencyMilliseconds);

public sealed record PresentMonFrameCaptureSnapshot(
    PresentMonWorkloadCaptureStatus Status,
    uint ProcessId,
    double RequestedWindowMilliseconds,
    double ActualWindowMilliseconds,
    PresentMonApiVersionSnapshot? ApiVersion,
    IReadOnlyList<PresentMonFrameMetricsSnapshot> Frames,
    IReadOnlyList<string> UnavailableOptionalMetrics,
    string? ApiPath,
    int? NativeStatusCode,
    string? Error,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc)
{
    public bool IsAvailable => Status == PresentMonWorkloadCaptureStatus.Available;
}

namespace LatencyPilot.Protocol;

public static class ObservationProtocol
{
    public const string PipeName = "LatencyPilot.Observation.v1";
    public const int MaximumRequestBytes = 64 * 1024;
    public const int MaximumResponseBytes = 1024 * 1024;
    public const int MaximumCaptureDurationMilliseconds = 30_000;
    public const int MaximumCaptureEvents = 500_000;
}

public enum ObservationCommand
{
    GetStatus = 1,
    CaptureKernelLatency = 2,
}

public enum ObservationResponseStatus
{
    Ok = 1,
    Error = 2,
}

public enum ObservationErrorCode
{
    None = 0,
    ProtocolVersionMismatch = 1,
    InvalidRequest = 2,
    CaptureUnavailable = 3,
}

public sealed record ObservationRequest(
    int ProtocolVersion,
    Guid RequestId,
    ObservationCommand Command,
    KernelLatencyCaptureRequest? KernelLatencyCapture);

public sealed record KernelLatencyCaptureRequest(
    int DurationMilliseconds,
    int MaximumEvents);

public sealed record ObservationResponse(
    int ProtocolVersion,
    Guid RequestId,
    ObservationResponseStatus Status,
    ObservationErrorCode ErrorCode,
    string? ErrorMessage,
    ObservationServiceStatus? ServiceStatus,
    KernelLatencyCaptureResponse? KernelLatencyCapture);

public sealed record ObservationServiceStatus(
    bool PrivilegedObservationHostImplemented,
    bool MutationAvailable);

public sealed record KernelLatencyCaptureResponse(
    DateTimeOffset StartedAtUtc,
    int RequestedDurationMilliseconds,
    double ActualDurationMilliseconds,
    int EventsLost,
    int InvalidEventCount,
    bool EventLimitReached,
    LatencyDistribution Dpc,
    LatencyDistribution Isr,
    IReadOnlyList<ProcessorLatencyDistribution> Processors);

public sealed record LatencyDistribution(
    int Count,
    double? P50Microseconds,
    double? P95Microseconds,
    double? P99Microseconds,
    double? MaximumMicroseconds);

public sealed record ProcessorLatencyDistribution(
    int ProcessorNumber,
    LatencyDistribution Dpc,
    LatencyDistribution Isr);

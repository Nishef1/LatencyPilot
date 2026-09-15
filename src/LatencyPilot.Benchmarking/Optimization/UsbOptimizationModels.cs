using LatencyPilot.Core.Metrics;

namespace LatencyPilot.Benchmarking.Optimization;

public enum UsbOptimizationReadinessStatus
{
    Ready = 0,
    NotReady = 1,
    Inconclusive = 2,
}

public static class UsbOptimizationMetricNames
{
    public const string XhciDpcDuration = "xHCI DPC duration (us)";
    public const string XhciIsrDuration = "xHCI ISR duration (us)";
    public const string InputReportInterval = "Input report interval (ms)";
}

public static class UsbOptimizationGuardrailNames
{
    public const string NetworkLatencyJitter = "Network latency/jitter";
    public const string AudioStability = "Audio stability";
    public const string GraphicsFrameTime = "Graphics frame time";
    public const string SystemStability = "System/device stability";
}

public sealed record UsbOptimizationReadinessResult(
    UsbOptimizationReadinessStatus Status,
    string? ControllerInstanceId,
    IReadOnlyDictionary<string, MetricSeries> TargetMetrics,
    IReadOnlyList<string> RequiredGuardrails,
    IReadOnlyList<string> Reasons)
{
    public bool IsReady => Status == UsbOptimizationReadinessStatus.Ready;
}

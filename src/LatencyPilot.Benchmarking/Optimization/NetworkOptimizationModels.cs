using LatencyPilot.Core.Metrics;

namespace LatencyPilot.Benchmarking.Optimization;

public enum NetworkBenchmarkScope
{
    LocalAuthoritative = 0,
    InternetSupplemental = 1,
}

public enum NetworkBenchmarkStatus
{
    Available = 0,
    InsufficientSamples = 1,
    SupplementalOnly = 2,
}

public static class NetworkBenchmarkMetricNames
{
    public const string RoundTripTime = "Local network RTT (ms)";
    public const string Jitter = "Local network jitter (ms)";
    public const string LossIndicator = "Local network packet loss";
    public const string Throughput = "Local network throughput (Mbps)";
    public const string CpuUtilization = "Local network CPU utilization (%)";
}

public sealed record NetworkProbeObservation(
    double TimestampMilliseconds,
    bool Success,
    double? RoundTripMilliseconds,
    double? ThroughputMbps = null,
    double? CpuUtilizationPercent = null);

public sealed record NetworkBenchmarkResult(
    NetworkBenchmarkStatus Status,
    NetworkBenchmarkScope Scope,
    IReadOnlyDictionary<string, MetricSeries> Metrics,
    double LossRatio,
    IReadOnlyList<string> Reasons)
{
    public bool IsAuthoritative =>
        Status == NetworkBenchmarkStatus.Available && Scope == NetworkBenchmarkScope.LocalAuthoritative;
}

public enum NetworkOptimizationReadinessStatus
{
    Ready = 0,
    NotReady = 1,
    Inconclusive = 2,
}

public sealed record NetworkOptimizationReadinessResult(
    NetworkOptimizationReadinessStatus Status,
    string? AdapterInstanceId,
    IReadOnlyDictionary<string, MetricSeries> TargetMetrics,
    IReadOnlyList<string> RequiredGuardrails,
    IReadOnlyList<string> Reasons);

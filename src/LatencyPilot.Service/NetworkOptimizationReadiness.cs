using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Metrics;
using LatencyPilot.Platform.Windows.Devices;

namespace LatencyPilot.Service;

internal static class NetworkOptimizationReadiness
{
    private static readonly string[] RequiredGuardrails =
    [
        NetworkBenchmarkMetricNames.LossIndicator,
        NetworkBenchmarkMetricNames.Throughput,
        NetworkBenchmarkMetricNames.CpuUtilization,
    ];

    internal static NetworkOptimizationReadinessResult Evaluate(
        NetworkRssAdapterSnapshot rss,
        PnPDeviceSnapshot adapter,
        NetworkBenchmarkResult benchmark,
        NetworkInterruptAttributionEvidence attribution)
    {
        ArgumentNullException.ThrowIfNull(rss);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(benchmark);
        ArgumentNullException.ThrowIfNull(attribution);

        if (!rss.PnpCorrelation.IsAvailable ||
            !string.Equals(rss.PnpCorrelation.PnpInstanceId, adapter.InstanceId, StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                NetworkOptimizationReadinessStatus.NotReady,
                adapter.InstanceId,
                "RSS provider evidence does not map authoritatively to the selected PnP network adapter.");
        }

        if (rss.Enabled is false)
        {
            return Result(
                NetworkOptimizationReadinessStatus.NotReady,
                adapter.InstanceId,
                "RSS is disabled for the selected adapter.");
        }

        if (rss.Enabled is null)
        {
            return Result(
                NetworkOptimizationReadinessStatus.Inconclusive,
                adapter.InstanceId,
                "RSS enabled state is unavailable from the provider.");
        }

        if (!string.Equals(attribution.AdapterInstanceId, adapter.InstanceId, StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                NetworkOptimizationReadinessStatus.NotReady,
                adapter.InstanceId,
                "Kernel interrupt attribution belongs to a different network adapter.");
        }

        if (!benchmark.IsAuthoritative)
        {
            return Result(
                NetworkOptimizationReadinessStatus.Inconclusive,
                adapter.InstanceId,
                "A complete local-authoritative network benchmark is required; Internet evidence remains supplemental.");
        }

        if (!attribution.CaptureIntegrityValid || !attribution.HasTargetEvidence)
        {
            return Result(
                NetworkOptimizationReadinessStatus.Inconclusive,
                adapter.InstanceId,
                "Clean exact miniport DPC and ISR attribution is required before a network experiment is ready.");
        }

        if (!benchmark.Metrics.TryGetValue(NetworkBenchmarkMetricNames.RoundTripTime, out var rtt) ||
            !benchmark.Metrics.TryGetValue(NetworkBenchmarkMetricNames.Jitter, out var jitter) ||
            rtt.Samples.Count < LocalNetworkBenchmark.MinimumObservations ||
            jitter.Samples.Count < LocalNetworkBenchmark.MinimumObservations)
        {
            return Result(
                NetworkOptimizationReadinessStatus.Inconclusive,
                adapter.InstanceId,
                "Local RTT/jitter evidence is below the comparison sample requirement.");
        }

        if (RequiredGuardrails.Any(name =>
            !benchmark.Metrics.TryGetValue(name, out var metric) ||
            metric.Samples.Count < LocalNetworkBenchmark.MinimumObservations))
        {
            return Result(
                NetworkOptimizationReadinessStatus.Inconclusive,
                adapter.InstanceId,
                "Loss, throughput and CPU guardrail evidence must all be complete for the local benchmark.");
        }

        var targetMetrics = new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
        {
            [NetworkBenchmarkMetricNames.RoundTripTime] = rtt,
            [NetworkBenchmarkMetricNames.Jitter] = jitter,
        };

        return new NetworkOptimizationReadinessResult(
            NetworkOptimizationReadinessStatus.Ready,
            adapter.InstanceId,
            targetMetrics,
            RequiredGuardrails,
            []);
    }

    private static NetworkOptimizationReadinessResult Result(
        NetworkOptimizationReadinessStatus status,
        string? adapterInstanceId,
        string reason) =>
        new(
            status,
            adapterInstanceId,
            new Dictionary<string, MetricSeries>(StringComparer.Ordinal),
            RequiredGuardrails,
            [reason]);
}

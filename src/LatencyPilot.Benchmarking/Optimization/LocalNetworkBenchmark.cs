using LatencyPilot.Core.Metrics;

namespace LatencyPilot.Benchmarking.Optimization;

public static class LocalNetworkBenchmark
{
    public const int MinimumObservations = 20;
    public const int MaximumObservations = 10_000;

    public static NetworkBenchmarkResult Analyze(
        NetworkBenchmarkScope scope,
        IReadOnlyList<NetworkProbeObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }

        if (observations.Count > MaximumObservations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observations),
                $"Network benchmark accepts at most {MaximumObservations} observations.");
        }

        ValidateObservations(observations);

        var successfulRtt = observations
            .Where(static observation => observation.Success)
            .Select(static observation => observation.RoundTripMilliseconds!.Value)
            .ToArray();
        var jitter = successfulRtt
            .Zip(successfulRtt.Skip(1), static (previous, current) => Math.Abs(current - previous))
            .ToArray();
        var lossSamples = observations
            .Select(static observation => observation.Success ? 0d : 1d)
            .ToArray();
        var lossRatio = observations.Count == 0
            ? 0d
            : lossSamples.Sum() / observations.Count;

        var metrics = new Dictionary<string, MetricSeries>(StringComparer.Ordinal);
        if (successfulRtt.Length > 0)
        {
            metrics[NetworkBenchmarkMetricNames.RoundTripTime] = new MetricSeries(
                NetworkBenchmarkMetricNames.RoundTripTime,
                MetricDirection.LowerIsBetter,
                successfulRtt);
        }

        if (jitter.Length > 0)
        {
            metrics[NetworkBenchmarkMetricNames.Jitter] = new MetricSeries(
                NetworkBenchmarkMetricNames.Jitter,
                MetricDirection.LowerIsBetter,
                jitter);
        }

        if (lossSamples.Length > 0)
        {
            metrics[NetworkBenchmarkMetricNames.LossIndicator] = new MetricSeries(
                NetworkBenchmarkMetricNames.LossIndicator,
                MetricDirection.LowerIsBetter,
                lossSamples);
        }

        AddCompleteOptionalSeries(
            observations,
            static observation => observation.ThroughputMbps,
            NetworkBenchmarkMetricNames.Throughput,
            MetricDirection.HigherIsBetter,
            metrics);
        AddCompleteOptionalSeries(
            observations,
            static observation => observation.CpuUtilizationPercent,
            NetworkBenchmarkMetricNames.CpuUtilization,
            MetricDirection.LowerIsBetter,
            metrics);

        if (observations.Count < MinimumObservations || successfulRtt.Length < MinimumObservations)
        {
            return new NetworkBenchmarkResult(
                NetworkBenchmarkStatus.InsufficientSamples,
                scope,
                metrics,
                lossRatio,
                [$"At least {MinimumObservations} observations and successful RTT samples are required."]);
        }

        if (scope == NetworkBenchmarkScope.InternetSupplemental)
        {
            return new NetworkBenchmarkResult(
                NetworkBenchmarkStatus.SupplementalOnly,
                scope,
                metrics,
                lossRatio,
                ["Internet probe evidence is supplemental and cannot satisfy the authoritative local-network benchmark requirement."]);
        }

        return new NetworkBenchmarkResult(
            NetworkBenchmarkStatus.Available,
            scope,
            metrics,
            lossRatio,
            []);
    }

    private static void ValidateObservations(IReadOnlyList<NetworkProbeObservation> observations)
    {
        double? previousTimestamp = null;
        foreach (var observation in observations)
        {
            if (!double.IsFinite(observation.TimestampMilliseconds) || observation.TimestampMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(observations),
                    "Network probe timestamps must be finite non-negative host-observed values.");
            }

            if (previousTimestamp.HasValue && observation.TimestampMilliseconds <= previousTimestamp.Value)
            {
                throw new ArgumentException(
                    "Network probe timestamps must be strictly increasing.",
                    nameof(observations));
            }
            previousTimestamp = observation.TimestampMilliseconds;

            if (observation.Success)
            {
                if (!observation.RoundTripMilliseconds.HasValue ||
                    !double.IsFinite(observation.RoundTripMilliseconds.Value) ||
                    observation.RoundTripMilliseconds.Value < 0)
                {
                    throw new ArgumentException(
                        "Successful network probes require one finite non-negative RTT measurement.",
                        nameof(observations));
                }
            }
            else if (observation.RoundTripMilliseconds.HasValue)
            {
                throw new ArgumentException(
                    "Failed network probes contribute to packet loss only and must not carry an RTT value.",
                    nameof(observations));
            }

            ValidateOptionalNonNegative(observation.ThroughputMbps, nameof(observation.ThroughputMbps));
            ValidateOptionalRange(observation.CpuUtilizationPercent, 0, 100, nameof(observation.CpuUtilizationPercent));
        }
    }

    private static void ValidateOptionalNonNegative(double? value, string name)
    {
        if (value.HasValue && (!double.IsFinite(value.Value) || value.Value < 0))
        {
            throw new ArgumentOutOfRangeException(name, "Optional network metrics must be finite non-negative values.");
        }
    }

    private static void ValidateOptionalRange(double? value, double minimum, double maximum, string name)
    {
        if (value.HasValue &&
            (!double.IsFinite(value.Value) || value.Value < minimum || value.Value > maximum))
        {
            throw new ArgumentOutOfRangeException(name, $"Optional network metric must be between {minimum} and {maximum}.");
        }
    }

    private static void AddCompleteOptionalSeries(
        IReadOnlyList<NetworkProbeObservation> observations,
        Func<NetworkProbeObservation, double?> selector,
        string metricName,
        MetricDirection direction,
        Dictionary<string, MetricSeries> metrics)
    {
        if (observations.Count == 0 || observations.Any(observation => !selector(observation).HasValue))
        {
            return;
        }

        metrics[metricName] = new MetricSeries(
            metricName,
            direction,
            observations.Select(observation => selector(observation)!.Value));
    }
}

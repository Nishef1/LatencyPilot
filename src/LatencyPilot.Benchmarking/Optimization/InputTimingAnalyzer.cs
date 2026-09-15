using LatencyPilot.Benchmarking.Statistics;
using LatencyPilot.Core.Devices;

namespace LatencyPilot.Benchmarking.Optimization;

public static class InputTimingAnalyzer
{
    public const int MinimumIntervals = 20;
    public const int MaximumIntervals = 100_000;
    public const double LongGapMultiplier = 2.5;
    public const double BurstMultiplier = 0.5;

    public static InputTimingSnapshot Analyze(InputReportTimestampSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentException.ThrowIfNullOrWhiteSpace(series.DeviceIdentity);

        if (series.TimestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(series), "Timestamp frequency must be positive.");
        }

        ArgumentNullException.ThrowIfNull(series.TimestampTicks);
        if (series.TimestampTicks.Count > MaximumIntervals + 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(series),
                $"Input timing is bounded to {MaximumIntervals + 1} report timestamps.");
        }

        var intervals = new double[Math.Max(0, series.TimestampTicks.Count - 1)];
        for (var index = 1; index < series.TimestampTicks.Count; index++)
        {
            var previous = series.TimestampTicks[index - 1];
            var current = series.TimestampTicks[index];
            if (current <= previous)
            {
                throw new ArgumentException(
                    "Input report timestamps must be strictly increasing in capture order.",
                    nameof(series));
            }

            var deltaTicks = current - previous;
            var milliseconds = deltaTicks * 1_000d / series.TimestampFrequency;
            if (!double.IsFinite(milliseconds) || milliseconds <= 0)
            {
                throw new ArgumentException(
                    "Input report timestamp deltas must convert to finite positive intervals.",
                    nameof(series));
            }

            intervals[index - 1] = milliseconds;
        }

        if (intervals.Length < MinimumIntervals)
        {
            return new InputTimingSnapshot(
                InputTimingAnalysisStatus.InsufficientSamples,
                series.DeviceIdentity,
                series.ReportCount,
                intervals.Length,
                intervals,
                null,
                null,
                null,
                null,
                null,
                0,
                0,
                false,
                InputTimingSnapshot.HostObservableScope,
                $"At least {MinimumIntervals} report intervals are required; observed {intervals.Length}.");
        }

        var sortedIntervals = intervals.Order().ToArray();
        var median = Percentiles.CalculateSorted(sortedIntervals, 0.50);
        var p95 = Percentiles.CalculateSorted(sortedIntervals, 0.95);
        var p99 = Percentiles.CalculateSorted(sortedIntervals, 0.99);
        var longGapThreshold = median * LongGapMultiplier;
        var burstThreshold = median * BurstMultiplier;
        var longGapCount = intervals.Count(interval => interval > longGapThreshold);
        var burstCount = intervals.Count(interval => interval < burstThreshold);

        return new InputTimingSnapshot(
            InputTimingAnalysisStatus.Available,
            series.DeviceIdentity,
            series.ReportCount,
            intervals.Length,
            intervals,
            median,
            p95,
            p99,
            1_000d / median,
            Math.Max(0d, p95 - median),
            longGapCount,
            burstCount,
            burstCount > 0,
            InputTimingSnapshot.HostObservableScope,
            null);
    }
}

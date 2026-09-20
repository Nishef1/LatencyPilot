namespace LatencyPilot.Benchmarking.Optimization;

internal sealed record GpuRepeatabilityClusterSelection(
    int[] Indexes,
    double Median,
    double MaximumRelativeDeviation,
    double TotalRelativeDeviation);

internal static class GpuRepeatabilityClusterSelector
{
    internal const int RequiredRunCount = 3;
    internal const int MaximumAttemptCount = 4;
    internal const int MaximumOriginalAttemptCount = 5;
    internal const double RelativeTolerance = 0.03d;
    internal const double RecoveryRelativeTolerance = 0.06d;

    internal static GpuRepeatabilityClusterSelection? Select(double[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length < RequiredRunCount ||
            values.Length > MaximumOriginalAttemptCount ||
            values.Any(static value => !double.IsFinite(value) || value <= 0d))
        {
            return null;
        }

        var preferred = SelectWithinTolerance(values, RelativeTolerance);
        if (preferred is not null || values.Length < MaximumAttemptCount)
        {
            return preferred;
        }

        // Attempts four and five are bounded replacement opportunities. If a
        // coherent three-run regime appears after one or two cold/background
        // outliers, keep that regime while preserving every excluded observation
        // in the audit trail. Recovery never widens beyond ±6% and never accepts
        // more than five total observations.
        return SelectWithinTolerance(values, RecoveryRelativeTolerance);
    }

    private static GpuRepeatabilityClusterSelection? SelectWithinTolerance(
        double[] values,
        double tolerance)
    {
        GpuRepeatabilityClusterSelection? best = null;
        for (var first = 0; first < values.Length - 2; first++)
        {
            for (var second = first + 1; second < values.Length - 1; second++)
            {
                for (var third = second + 1; third < values.Length; third++)
                {
                    var indexes = new[] { first, second, third };
                    var ordered = indexes.Select(index => values[index]).Order().ToArray();
                    var median = ordered[1];
                    var deviations = indexes.Select(index => Math.Abs(values[index] - median) / median).ToArray();
                    var maximum = deviations.Max();
                    if (maximum > tolerance)
                    {
                        continue;
                    }

                    var total = deviations.Sum();
                    if (best is null || maximum < best.MaximumRelativeDeviation - 1e-12 ||
                        (Math.Abs(maximum - best.MaximumRelativeDeviation) <= 1e-12 && total < best.TotalRelativeDeviation - 1e-12))
                    {
                        best = new GpuRepeatabilityClusterSelection(indexes, median, maximum, total);
                    }
                }
            }
        }

        return best;
    }
}

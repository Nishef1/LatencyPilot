namespace LatencyPilot.Benchmarking.Statistics;

public static class Percentiles
{
    public static double Calculate(IReadOnlyList<double> samples, double percentile)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        }

        if (percentile is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        var sorted = samples.Order().ToArray();
        var position = (sorted.Length - 1) * percentile;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return sorted[lowerIndex];
        }

        var weight = position - lowerIndex;
        return sorted[lowerIndex] + ((sorted[upperIndex] - sorted[lowerIndex]) * weight);
    }
}

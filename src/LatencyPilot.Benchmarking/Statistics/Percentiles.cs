namespace LatencyPilot.Benchmarking.Statistics;

public static class Percentiles
{
    public static double Calculate(IReadOnlyList<double> samples, double percentile)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ValidateInputs(samples.Count, percentile);

        var sorted = samples.Order().ToArray();
        return CalculateSorted(sorted, percentile);
    }

    public static double CalculateSorted(IReadOnlyList<double> sortedSamples, double percentile)
    {
        ArgumentNullException.ThrowIfNull(sortedSamples);
        ValidateInputs(sortedSamples.Count, percentile);

        var position = (sortedSamples.Count - 1) * percentile;
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return sortedSamples[lowerIndex];
        }

        var weight = position - lowerIndex;
        return sortedSamples[lowerIndex] +
            ((sortedSamples[upperIndex] - sortedSamples[lowerIndex]) * weight);
    }

    private static void ValidateInputs(int sampleCount, double percentile)
    {
        if (sampleCount == 0)
        {
            throw new ArgumentException("At least one sample is required.");
        }

        if (percentile is < 0 or > 1 || !double.IsFinite(percentile))
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }
    }
}

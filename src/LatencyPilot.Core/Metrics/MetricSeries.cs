namespace LatencyPilot.Core.Metrics;

public sealed class MetricSeries
{
    public MetricSeries(string name, MetricDirection direction, IEnumerable<double> samples)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(samples);

        var materialized = samples.ToArray();
        if (materialized.Any(static value => !double.IsFinite(value)))
        {
            throw new ArgumentOutOfRangeException(nameof(samples), "Metric samples must be finite numbers.");
        }

        Name = name;
        Direction = direction;
        Samples = Array.AsReadOnly(materialized);
    }

    public string Name { get; }

    public MetricDirection Direction { get; }

    public IReadOnlyList<double> Samples { get; }
}

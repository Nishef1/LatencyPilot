namespace LatencyPilot.Benchmarking.Optimization;

public static class GpuOriginalBaselinePolicy
{
    public const int PreferredRunCount = GpuRepeatabilityClusterSelector.RequiredRunCount;
    public const int MaximumPhysicalAttemptCount = GpuRepeatabilityClusterSelector.MaximumOriginalAttemptCount;

    public static bool HasRepeatableCluster(IEnumerable<double> onePercentLowFps)
    {
        ArgumentNullException.ThrowIfNull(onePercentLowFps);
        var values = onePercentLowFps.ToArray();
        return GpuRepeatabilityClusterSelector.Select(values) is not null;
    }
}

using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Metrics;

namespace LatencyPilot.Benchmarking.Optimization;

public static class PresentMonGuardrailSeriesBuilder
{
    public const string CpuFrameTimeMetric = "CPU frame time (ms)";
    public const string PresentedFpsMetric = "Presented FPS";
    public const string DisplayedFpsMetric = "Displayed FPS";
    public const string DroppedFrameRatioMetric = "Dropped frame ratio";
    public const string GpuLatencyMetric = "GPU latency (ms)";
    public const string DisplayLatencyMetric = "Display latency (ms)";

    public static IReadOnlyDictionary<string, MetricSeries> Create(
        IEnumerable<PresentMonWorkloadMetricsSnapshot> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var available = windows
            .Where(static window => window is not null && window.IsAvailable)
            .ToArray();

        var result = new Dictionary<string, MetricSeries>(StringComparer.Ordinal);
        AddLowerIsBetter(
            result,
            CpuFrameTimeMetric,
            available,
            static chain => chain.CpuFrameTimeMilliseconds);
        AddHigherIsBetter(
            result,
            PresentedFpsMetric,
            available,
            static chain => chain.PresentedFps);
        AddHigherIsBetter(
            result,
            DisplayedFpsMetric,
            available,
            static chain => chain.DisplayedFps);
        AddLowerIsBetter(
            result,
            DroppedFrameRatioMetric,
            available,
            static chain => chain.DroppedFrameRatio);
        AddLowerIsBetter(
            result,
            GpuLatencyMetric,
            available,
            static chain => chain.GpuLatencyMilliseconds);
        AddLowerIsBetter(
            result,
            DisplayLatencyMetric,
            available,
            static chain => chain.DisplayLatencyMilliseconds);

        return result;
    }

    private static void AddLowerIsBetter(
        Dictionary<string, MetricSeries> result,
        string name,
        IEnumerable<PresentMonWorkloadMetricsSnapshot> windows,
        Func<PresentMonSwapChainMetricsSnapshot, double?> selector)
    {
        var samples = windows
            .Select(window => SelectWindowExtreme(window, selector, takeMaximum: true))
            .Where(static value => value is not null)
            .Select(static value => value!.Value)
            .ToArray();

        if (samples.Length > 0)
        {
            result[name] = new MetricSeries(name, MetricDirection.LowerIsBetter, samples);
        }
    }

    private static void AddHigherIsBetter(
        Dictionary<string, MetricSeries> result,
        string name,
        IEnumerable<PresentMonWorkloadMetricsSnapshot> windows,
        Func<PresentMonSwapChainMetricsSnapshot, double?> selector)
    {
        var samples = windows
            .Select(window => SelectWindowExtreme(window, selector, takeMaximum: false))
            .Where(static value => value is not null)
            .Select(static value => value!.Value)
            .ToArray();

        if (samples.Length > 0)
        {
            result[name] = new MetricSeries(name, MetricDirection.HigherIsBetter, samples);
        }
    }

    private static double? SelectWindowExtreme(
        PresentMonWorkloadMetricsSnapshot window,
        Func<PresentMonSwapChainMetricsSnapshot, double?> selector,
        bool takeMaximum)
    {
        var values = window.SwapChains
            .Select(selector)
            .Where(static value => value is { } number && double.IsFinite(number) && number >= 0)
            .Select(static value => value!.Value)
            .ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        return takeMaximum ? values.Max() : values.Min();
    }
}

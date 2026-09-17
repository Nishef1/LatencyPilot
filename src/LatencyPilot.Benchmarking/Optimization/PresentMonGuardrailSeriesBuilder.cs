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
    public const string CpuBusyMetric = "CPU busy (ms)";
    public const string CpuWaitMetric = "CPU wait (ms)";
    public const string GpuTimeMetric = "GPU time (ms)";
    public const string GpuBusyMetric = "GPU busy (ms)";
    public const string GpuWaitMetric = "GPU wait (ms)";

    public static IReadOnlyDictionary<string, MetricSeries> Create(
        IEnumerable<PresentMonWorkloadMetricsSnapshot> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var available = windows.ToArray();
        var result = new Dictionary<string, MetricSeries>(StringComparer.Ordinal);
        if (available.Length == 0 || available.Any(static window =>
                window is null || !window.IsAvailable || window.ProcessId == 0 ||
                !double.IsFinite(window.RequestedWindowMilliseconds) || window.RequestedWindowMilliseconds <= 0 ||
                window.SwapChains.Count == 0 ||
                window.SwapChains.Any(static chain => chain is null || chain.SwapChainAddress == 0)))
        {
            return result.AsReadOnly();
        }

        var first = available[0];
        var addresses = first.SwapChains.Select(static chain => chain.SwapChainAddress).Order().ToArray();
        if (addresses.Distinct().Count() != addresses.Length || available.Any(window =>
                window.ProcessId != first.ProcessId || window.ApiVersion != first.ApiVersion ||
                window.RequestedWindowMilliseconds != first.RequestedWindowMilliseconds ||
                !window.SwapChains.Select(static chain => chain.SwapChainAddress).Order().SequenceEqual(addresses)))
        {
            return result.AsReadOnly();
        }

        AddLowerIsBetter(result, CpuFrameTimeMetric, available, static chain => chain.CpuFrameTimeMilliseconds);
        AddHigherIsBetter(result, PresentedFpsMetric, available, static chain => chain.PresentedFps);
        AddHigherIsBetter(result, DisplayedFpsMetric, available, static chain => chain.DisplayedFps);
        AddLowerIsBetter(result, DroppedFrameRatioMetric, available, static chain => chain.DroppedFrameRatio);
        AddLowerIsBetter(result, GpuLatencyMetric, available, static chain => chain.GpuLatencyMilliseconds);
        AddLowerIsBetter(result, DisplayLatencyMetric, available, static chain => chain.DisplayLatencyMilliseconds);
        AddLowerIsBetter(result, CpuBusyMetric, available, static chain => chain.CpuBusyMilliseconds);
        AddLowerIsBetter(result, CpuWaitMetric, available, static chain => chain.CpuWaitMilliseconds);
        AddLowerIsBetter(result, GpuTimeMetric, available, static chain => chain.GpuTimeMilliseconds);
        AddLowerIsBetter(result, GpuBusyMetric, available, static chain => chain.GpuBusyMilliseconds);
        AddLowerIsBetter(result, GpuWaitMetric, available, static chain => chain.GpuWaitMilliseconds);

        return result.AsReadOnly();
    }

    public static IReadOnlyDictionary<string, MetricSeries> Create(PresentMonFrameCaptureSnapshot capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        var result = new Dictionary<string, MetricSeries>(StringComparer.Ordinal);
        var supportedSource = capture.ApiVersion is not null ||
            GpuBenchmarkEvidenceInterpreter.IsStandaloneConsoleCapture(capture);
        if (!capture.IsAvailable ||
            capture.ProcessId == 0 ||
            !supportedSource ||
            !double.IsFinite(capture.RequestedWindowMilliseconds) || capture.RequestedWindowMilliseconds <= 0 ||
            !double.IsFinite(capture.ActualWindowMilliseconds) || capture.ActualWindowMilliseconds <= 0 ||
            capture.EndedAtUtc < capture.StartedAtUtc ||
            capture.Frames.Count == 0 ||
            capture.Frames.Any(static frame => frame is null || frame.SwapChainAddress == 0))
        {
            return result.AsReadOnly();
        }

        AddRawLowerIsBetter(result, CpuFrameTimeMetric, capture.Frames, static frame => frame.CpuFrameTimeMilliseconds);
        AddRawLowerIsBetter(result, CpuBusyMetric, capture.Frames, static frame => frame.CpuBusyMilliseconds);
        AddRawLowerIsBetter(result, CpuWaitMetric, capture.Frames, static frame => frame.CpuWaitMilliseconds);
        AddRawLowerIsBetter(result, GpuTimeMetric, capture.Frames, static frame => frame.GpuTimeMilliseconds);
        AddRawLowerIsBetter(result, GpuBusyMetric, capture.Frames, static frame => frame.GpuBusyMilliseconds);
        AddRawLowerIsBetter(result, GpuWaitMetric, capture.Frames, static frame => frame.GpuWaitMilliseconds);
        AddRawLowerIsBetter(result, GpuLatencyMetric, capture.Frames, static frame => frame.GpuLatencyMilliseconds);
        AddRawLowerIsBetter(result, DisplayLatencyMetric, capture.Frames, static frame => frame.DisplayLatencyMilliseconds);

        var dropped = capture.Frames.Select(static frame => frame.DroppedFrame).ToArray();
        if (dropped.All(static value => value is not null))
        {
            result[DroppedFrameRatioMetric] = new MetricSeries(
                DroppedFrameRatioMetric,
                MetricDirection.LowerIsBetter,
                dropped.Select(static value => value!.Value ? 1d : 0d));
        }

        return result.AsReadOnly();
    }

    private static void AddRawLowerIsBetter(
        Dictionary<string, MetricSeries> result,
        string name,
        IReadOnlyList<PresentMonFrameMetricsSnapshot> frames,
        Func<PresentMonFrameMetricsSnapshot, double?> selector)
    {
        var samples = frames.Select(selector).ToArray();
        if (samples.All(static value => value is { } number && double.IsFinite(number) && number >= 0))
        {
            result[name] = new MetricSeries(
                name,
                MetricDirection.LowerIsBetter,
                samples.Select(static value => value!.Value));
        }
    }

    private static void AddLowerIsBetter(
        Dictionary<string, MetricSeries> result,
        string name,
        PresentMonWorkloadMetricsSnapshot[] windows,
        Func<PresentMonSwapChainMetricsSnapshot, double?> selector)
    {
        var samples = windows
            .Select(window => SelectWindowExtreme(window, selector, takeMaximum: true, ratio: name == DroppedFrameRatioMetric))
            .Where(static value => value is not null)
            .Select(static value => value!.Value)
            .ToArray();

        if (samples.Length == windows.Length)
        {
            result[name] = new MetricSeries(name, MetricDirection.LowerIsBetter, samples);
        }
    }

    private static void AddHigherIsBetter(
        Dictionary<string, MetricSeries> result,
        string name,
        PresentMonWorkloadMetricsSnapshot[] windows,
        Func<PresentMonSwapChainMetricsSnapshot, double?> selector)
    {
        var samples = windows
            .Select(window => SelectWindowExtreme(window, selector, takeMaximum: false, ratio: false))
            .Where(static value => value is not null)
            .Select(static value => value!.Value)
            .ToArray();

        if (samples.Length == windows.Length)
        {
            result[name] = new MetricSeries(name, MetricDirection.HigherIsBetter, samples);
        }
    }

    private static double? SelectWindowExtreme(
        PresentMonWorkloadMetricsSnapshot window,
        Func<PresentMonSwapChainMetricsSnapshot, double?> selector,
        bool takeMaximum,
        bool ratio)
    {
        var values = window.SwapChains
            .Select(selector)
            .ToArray();

        if (values.Any(value => value is not { } number || !double.IsFinite(number) ||
                number < 0 || (ratio && number > 1) || (!takeMaximum && number == 0)))
        {
            return null;
        }

        return takeMaximum ? values.Max() : values.Min();
    }
}

using LatencyPilot.Benchmarking.Statistics;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Metrics;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed record GpuBenchmarkEvidenceInterpretation(
    bool IsValid,
    IReadOnlyList<string> ValidityReasons,
    MetricSeries PrimaryFrameTime,
    MetricSeries D3D12GpuWork,
    IReadOnlyDictionary<string, MetricSeries> Guardrails,
    IReadOnlyDictionary<string, MetricSeries> Context,
    double FrameP99Milliseconds,
    double OnePercentLowFps,
    GpuBenchmarkVideoStats? VideoStats = null)
{
    public bool HasVideoPrimary => VideoStats is not null;
}

public static class GpuBenchmarkEvidenceInterpreter
{
    public const string D3D12GpuWorkMetric = "D3D12 GPU work (ms)";
    private static readonly Version MinimumPresentMonBinaryVersion = new(2, 5, 1);

    public static GpuBenchmarkEvidenceInterpretation Interpret(GpuBenchmarkEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var reasons = evidence.ValidityReasons
            .Where(static reason => !string.IsNullOrWhiteSpace(reason))
            .Select(static reason => reason.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Require(string.Equals(evidence.Schema, GpuBenchmarkEvidence.SchemaId, StringComparison.Ordinal),
            "GPU benchmark evidence schema is not supported.", reasons);
        Require(string.Equals(evidence.MethodId, GpuBenchmarkEvidence.MethodIdValue, StringComparison.Ordinal),
            "GPU benchmark method identity is not supported.", reasons);
        Require(IsExactRevision(evidence.SourceRevisionId),
            "GPU benchmark evidence requires an exact clean 40-character source revision.", reasons);
        Require(evidence.BenchmarkProcessId != 0 && evidence.PresentMonCapture.ProcessId == evidence.BenchmarkProcessId,
            "Benchmark process identity changed or is invalid.", reasons);
        Require(evidence.TrialIndex > 0, "Benchmark trial index must be positive.", reasons);
        Require(!string.IsNullOrWhiteSpace(evidence.FrozenWorkloadIdentity) && evidence.WorkerMap.Count > 0,
            "Frozen benchmark workload identity or worker map is missing.", reasons);
        Require(evidence.D3D12TimestampFrequency > 0,
            "D3D12 timestamp frequency is missing or zero.", reasons);

        var d3d12Samples = evidence.D3D12GpuWorkMilliseconds.ToArray();
        var d3d12SamplesValid = d3d12Samples.Length > 0 &&
            d3d12Samples.All(static sample => double.IsFinite(sample) && sample > 0d);
        Require(d3d12SamplesValid, "D3D12 GPU timestamp evidence is missing or invalid.", reasons);

        // Video-primary path: the benchmark's own wall-clock frame periods
        // carry AVG / 1% low / 0.1% low with no external collector. When they
        // exist, PresentMon and kernel ETW degrade to best-effort guardrails
        // instead of hard validity gates. Without them, the legacy
        // external-evidence contract applies unchanged.
        var videoStats = evidence.FramePeriodMilliseconds is { } periods
            ? GpuBenchmarkVideoStats.TryCreate(periods)
            : null;
        var hasVideoPrimary = videoStats is not null;

        var capture = evidence.PresentMonCapture;
        if (!hasVideoPrimary)
        {
            Require(capture.IsAvailable && capture.Frames.Count > 0,
                "Raw PresentMon frame capture is unavailable or empty.", reasons);
            Require(IsSupportedPresentMonSource(capture),
                "PresentMon API 3.3+ or the pinned standalone PresentMon console collector is required for authoritative GPU benchmark evidence.", reasons);
            Require(IsSupportedPresentMonBinary(evidence.PresentMonBinaryVersion),
                "PresentMon 2.5.1 or later is required for authoritative GPU benchmark evidence.", reasons);
            Require(capture.ActualWindowMilliseconds > 0 && capture.EndedAtUtc >= capture.StartedAtUtc,
                "PresentMon capture timing is invalid.", reasons);
            Require(evidence.EtwCaptureId != Guid.Empty && evidence.EtwIntegrityComplete && evidence.EtwLostEventCount == 0,
                "Kernel ETW capture is missing, incomplete, or reports event loss.", reasons);
        }

        var rawSeries = PresentMonGuardrailSeriesBuilder.Create(capture);
        var hasPresentMonFrameTimes = rawSeries.TryGetValue(PresentMonGuardrailSeriesBuilder.CpuFrameTimeMetric, out var frameSeries) &&
            frameSeries.Samples.Count > 0 &&
            frameSeries.Samples.All(static value => value > 0d);
        if (!hasVideoPrimary)
        {
            Require(hasPresentMonFrameTimes, "Complete positive raw CPU frame intervals are required.", reasons);
        }

        var primaryFrameTime = hasVideoPrimary && videoStats is not null
            ? new MetricSeries(
                PresentMonGuardrailSeriesBuilder.CpuFrameTimeMetric,
                MetricDirection.LowerIsBetter,
                evidence.FramePeriodMilliseconds!.Where(static value => double.IsFinite(value) && value > 0d))
            : hasPresentMonFrameTimes
                ? frameSeries!
                : new MetricSeries(PresentMonGuardrailSeriesBuilder.CpuFrameTimeMetric, MetricDirection.LowerIsBetter, []);
        var d3d12GpuWork = new MetricSeries(
            D3D12GpuWorkMetric,
            MetricDirection.LowerIsBetter,
            d3d12SamplesValid ? d3d12Samples : []);

        var guardrails = new Dictionary<string, MetricSeries>(StringComparer.Ordinal);
        var context = new Dictionary<string, MetricSeries>(StringComparer.Ordinal);
        foreach (var (name, series) in rawSeries)
        {
            if (name == PresentMonGuardrailSeriesBuilder.CpuFrameTimeMetric)
            {
                continue;
            }

            if (name == PresentMonGuardrailSeriesBuilder.GpuBusyMetric)
            {
                context[name] = series;
                continue;
            }

            guardrails[name] = series;
        }

        var frameP99 = hasVideoPrimary && videoStats is not null
            ? videoStats.P99Milliseconds
            : primaryFrameTime.Samples.Count > 0
                ? Percentiles.Calculate(primaryFrameTime.Samples, 0.99)
                : double.NaN;
        var onePercentLowFps = hasVideoPrimary && videoStats is not null
            ? videoStats.Low1PctFps
            : double.IsFinite(frameP99) && frameP99 > 0d
                ? 1000d / frameP99
                : double.NaN;

        return new GpuBenchmarkEvidenceInterpretation(
            reasons.Count == 0,
            reasons.AsReadOnly(),
            primaryFrameTime,
            d3d12GpuWork,
            guardrails.AsReadOnly(),
            context.AsReadOnly(),
            frameP99,
            onePercentLowFps,
            videoStats);
    }

    internal static bool IsStandaloneConsoleCapture(LatencyPilot.Core.Devices.PresentMonFrameCaptureSnapshot capture) =>
        capture.ApiVersion is null &&
        capture.ApiPath is { Length: > 0 } path &&
        string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsExactRevision(string? revision) =>
        revision is { Length: 40 } && revision.All(Uri.IsHexDigit);

    private static bool IsSupportedPresentMonSource(LatencyPilot.Core.Devices.PresentMonFrameCaptureSnapshot capture) =>
        IsStandaloneConsoleCapture(capture) || IsSupportedPresentMonApi(capture.ApiVersion);

    private static bool IsSupportedPresentMonApi(LatencyPilot.Core.Devices.PresentMonApiVersionSnapshot? version) =>
        version is not null && (version.Major > 3 || (version.Major == 3 && version.Minor >= 3));

    private static bool IsSupportedPresentMonBinary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().Split(['-', '+'], 2)[0];
        return Version.TryParse(normalized, out var version) && version >= MinimumPresentMonBinaryVersion;
    }

    private static void Require(bool condition, string reason, List<string> reasons)
    {
        if (!condition && !reasons.Contains(reason, StringComparer.Ordinal))
        {
            reasons.Add(reason);
        }
    }
}

using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;

namespace LatencyPilot.Core.Benchmarking;

public sealed record GpuBenchmarkEvidence(
    string Schema,
    string SourceRevisionId,
    string MethodId,
    string WindowsIdentity,
    string GpuIdentity,
    string DriverIdentity,
    string TopologyIdentity,
    uint BenchmarkProcessId,
    string TrialRole,
    int TrialIndex,
    LogicalProcessorId? CandidateProcessor,
    string FrozenWorkloadIdentity,
    IReadOnlyList<LogicalProcessorId> WorkerMap,
    int Seed,
    ulong D3D12TimestampFrequency,
    IReadOnlyList<double> D3D12GpuWorkMilliseconds,
    PresentMonFrameCaptureSnapshot PresentMonCapture,
    string? PresentMonBinaryVersion,
    Guid EtwCaptureId,
    bool EtwIntegrityComplete,
    int EtwLostEventCount,
    IReadOnlyList<string> ValidityReasons,
    GpuBenchmarkArtifactWorkload? FrozenWorkload = null,
    IReadOnlyList<double>? FramePeriodMilliseconds = null)
{
    public const string SchemaId = "latencypilot-gpu-benchmark-v1";
    public const string MethodIdValue = "gpu-affinity-benchmark-v1";
}

/// <summary>
/// Video-style benchmark statistics computed from the benchmark's own
/// wall-clock frame periods (AVG FPS, 1% low, 0.1% low). This is the same
/// decision signal a manual per-core affinity test uses: no external frame
/// collector is required for ranking.
/// </summary>
public sealed record GpuBenchmarkVideoStats(
    int SampleCount,
    double AvgFps,
    double P99Milliseconds,
    double Low1PctFps,
    double Low01PctFps)
{
    public static GpuBenchmarkVideoStats? TryCreate(IEnumerable<double> framePeriodMilliseconds)
    {
        var ordered = framePeriodMilliseconds
            .Where(static value => double.IsFinite(value) && value > 0d)
            .Order()
            .ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }

        static double LowFps(double[] sortedAscending, double fraction)
        {
            var take = Math.Max(1, (int)Math.Ceiling(sortedAscending.Length * fraction));
            var worst = sortedAscending.Skip(sortedAscending.Length - take).ToArray();
            return 1000d / worst.Average();
        }

        // Same linear-interpolation percentile rule as the canonical estimator.
        static double Percentile99(double[] sortedAscending)
        {
            var position = (sortedAscending.Length - 1) * 0.99;
            var lower = (int)Math.Floor(position);
            var upper = (int)Math.Ceiling(position);
            return lower == upper
                ? sortedAscending[lower]
                : sortedAscending[lower] + ((sortedAscending[upper] - sortedAscending[lower]) * (position - lower));
        }

        return new GpuBenchmarkVideoStats(
            ordered.Length,
            1000d / ordered.Average(),
            Percentile99(ordered),
            LowFps(ordered, 0.01),
            LowFps(ordered, 0.001));
    }
}

using System.Diagnostics;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.System;

namespace LatencyPilot.GpuBenchmark;

internal sealed record FrozenBenchmarkWorkload(
    int CommandBatchesPerWorker,
    int SimulationIterationsPerWorker,
    IReadOnlyList<LogicalProcessorId> WorkerMap,
    int Seed,
    int Width,
    int Height);

internal sealed class BenchmarkWorkload
{
    private const int MinimumCommandBatches = 1;
    private const int MaximumCommandBatches = 4096;
    private const int MinimumSimulationIterations = 1_000;
    private static readonly TimeSpan WarmupDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ObserverSettleDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CalibrationProgressInterval = TimeSpan.FromMilliseconds(250);
    private readonly BenchmarkOptions options;
    private readonly TextWriter output;
    private readonly IReadOnlyList<LogicalProcessorId> workerMap;

    internal BenchmarkWorkload(
        BenchmarkOptions options,
        TextWriter output,
        IReadOnlyList<LogicalProcessorId> workerMap)
    {
        this.options = options;
        this.output = output;
        this.workerMap = workerMap;
    }

    internal Task<FrozenBenchmarkWorkload> CalibrateAsync(
        D3D12BenchmarkRenderer renderer,
        CancellationToken cancellationToken = default)
    {
        var commandBatches = 8;
        var simulationIterations = MinimumSimulationIterations;
        var started = Stopwatch.StartNew();
        var intervalStarted = Stopwatch.StartNew();
        var gpuSum = 0d;
        var intervalFrames = 0;
        var lastProgress = TimeSpan.Zero;

        renderer.BeginMeasurementWindow();
        while (started.Elapsed < WarmupDuration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (renderer.RenderFrame(simulationIterations, commandBatches) is { } frame)
            {
                ValidateFrame(frame);
                gpuSum += frame.GpuWorkMilliseconds;
                intervalFrames++;
            }

            if (intervalStarted.Elapsed >= TimeSpan.FromSeconds(1) && intervalFrames > 0)
            {
                commandBatches = TuneCommandBatches(
                    commandBatches,
                    gpuSum / intervalFrames);
                gpuSum = 0;
                intervalFrames = 0;
                intervalStarted.Restart();
            }

            if (started.Elapsed - lastProgress >= CalibrationProgressInterval)
            {
                lastProgress = started.Elapsed;
                BenchmarkProtocol.WriteProgress(
                    output,
                    options.SessionId,
                    "calibrating",
                    Math.Clamp(
                        started.Elapsed.TotalMilliseconds /
                        WarmupDuration.TotalMilliseconds,
                        0d,
                        1d),
                    $"Calibrating GPU-dominant workload: {commandBatches} command batches, {simulationIterations} fixed simulation iterations.");
            }
        }

        foreach (var frame in renderer.DrainFrames())
        {
            ValidateFrame(frame);
        }

        return Task.FromResult(new FrozenBenchmarkWorkload(
            commandBatches,
            simulationIterations,
            workerMap.ToArray(),
            options.Seed,
            options.Width,
            options.Height));
    }

    internal Task<GpuBenchmarkTrialArtifact> RunTrialAsync(
        D3D12BenchmarkRenderer renderer,
        FrozenBenchmarkWorkload workload,
        CancellationToken cancellationToken = default) =>
        RunTrialAsync(renderer, workload, options.Duration, cancellationToken);

    internal Task<GpuBenchmarkTrialArtifact> RunTrialAsync(
        D3D12BenchmarkRenderer renderer,
        FrozenBenchmarkWorkload workload,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workload);
        if (duration.TotalMilliseconds is
            < GpuBenchmarkControlProtocol.MinimumTrialDurationMilliseconds or
            > GpuBenchmarkControlProtocol.MaximumTrialDurationMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var expectedFrames = checked((int)Math.Clamp(
            Math.Ceiling(duration.TotalSeconds * 240d),
            1d,
            150_000d));
        var frames = new List<BenchmarkFrameTelemetry>(expectedFrames);

        // Scored Gate A trials start external observers shortly before invoking the
        // benchmark. Run one bounded unscored workload second so collector/JIT/page-in
        // startup cannot become the first scored Original's tail. This is repeated for
        // every trial to keep treatment symmetric; its frames are drained and discarded.
        BenchmarkProtocol.WriteProgress(
            output,
            options.SessionId,
            "observer-settle",
            0d,
            "Settling benchmark and observer startup before the scored QPC window.");
        renderer.BeginMeasurementWindow();
        var observerSettle = Stopwatch.StartNew();
        while (observerSettle.Elapsed < ObserverSettleDuration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (renderer.RenderFrame(
                    workload.SimulationIterationsPerWorker,
                    workload.CommandBatchesPerWorker) is { } settleFrame)
            {
                ValidateFrame(settleFrame);
            }
        }
        foreach (var settleFrame in renderer.DrainFrames())
        {
            ValidateFrame(settleFrame);
        }

        // Keep observer work outside the scored interval. Frame periods are measured
        // between Present calls, so serialization/console flushing between frames
        // would become artificial tail latency in the following frame.
        BenchmarkProtocol.WriteProgress(
            output,
            options.SessionId,
            "measuring",
            0d,
            "Starting scored measurement window; in-window progress output is suspended to avoid perturbing frame periods.");

        renderer.BeginMeasurementWindow();
        var startedAtUtc = DateTimeOffset.UtcNow;
        var startedAtQpc = Stopwatch.GetTimestamp();
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (renderer.RenderFrame(
                    workload.SimulationIterationsPerWorker,
                    workload.CommandBatchesPerWorker) is { } frame)
            {
                ValidateFrame(frame);
                frames.Add(frame);
            }
        }

        // Every Present belonging to the scored interval has occurred by this point.
        // Capture the QPC boundary before draining pending GPU completions so external
        // frame collectors can crop in the exact same monotonic clock domain.
        var completedAtQpc = Stopwatch.GetTimestamp();
        foreach (var frame in renderer.DrainFrames())
        {
            ValidateFrame(frame);
            frames.Add(frame);
        }

        var completedAtUtc = DateTimeOffset.UtcNow;

        if (frames.Count == 0)
        {
            throw new InvalidDataException(
                "Benchmark trial completed without any frame evidence.");
        }
        if (startedAtQpc <= 0 || completedAtQpc <= startedAtQpc || Stopwatch.Frequency <= 0)
        {
            throw new InvalidDataException(
                "Benchmark trial completed without a valid monotonic QPC measurement window.");
        }

        frames.Sort(static (left, right) => left.FrameIndex.CompareTo(right.FrameIndex));

        BenchmarkProtocol.WriteProgress(
            output,
            options.SessionId,
            "measuring",
            1d,
            $"Measured {frames.Count} completed frames with frozen workload.");

        return Task.FromResult(new GpuBenchmarkTrialArtifact(
            GpuBenchmarkTrialArtifact.SchemaId,
            options.SessionId,
            startedAtUtc,
            completedAtUtc,
            renderer.AdapterName,
            renderer.PresentMode,
            // Preserve the existing trial-level field for compatibility, but source
            // it from actual scored evidence rather than renderer construction time.
            frames[0].GpuTimestampFrequency,
            new GpuBenchmarkArtifactWorkload(
                workload.CommandBatchesPerWorker,
                workload.SimulationIterationsPerWorker,
                workload.WorkerMap.ToArray(),
                workload.Seed,
                workload.Width,
                workload.Height),
            frames.Select(static frame => new GpuBenchmarkArtifactFrame(
                frame.FrameIndex,
                frame.CpuRecordingMilliseconds,
                frame.GpuWorkMilliseconds,
                frame.FramePeriodMilliseconds,
                frame.GpuTimestampFrequency)).ToArray(),
            renderer.CaptureWorkerChecksums(),
            startedAtQpc,
            completedAtQpc,
            Stopwatch.Frequency));
    }

    private static void ValidateFrame(BenchmarkFrameTelemetry frame)
    {
        if (!double.IsFinite(frame.CpuRecordingMilliseconds) ||
            frame.CpuRecordingMilliseconds <= 0 ||
            !double.IsFinite(frame.GpuWorkMilliseconds) ||
            frame.GpuWorkMilliseconds < 0 ||
            !double.IsFinite(frame.FramePeriodMilliseconds) ||
            frame.FramePeriodMilliseconds <= 0 ||
            frame.GpuTimestampFrequency == 0)
        {
            throw new InvalidDataException(
                "Benchmark trial produced invalid frame timing or timestamp provenance.");
        }
    }

    private static int TuneCommandBatches(
        int current,
        double averageGpuMilliseconds)
    {
        if (averageGpuMilliseconds < 2d)
        {
            return Math.Min(
                MaximumCommandBatches,
                checked(current * 2));
        }

        if (averageGpuMilliseconds > 14d)
        {
            return Math.Max(MinimumCommandBatches, current / 2);
        }

        return current;
    }
}

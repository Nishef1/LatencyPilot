using System.Diagnostics;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.System;

namespace LatencyPilot.GpuBenchmark;

internal sealed record FrozenBenchmarkWorkload(int CommandBatchesPerWorker, int SimulationIterationsPerWorker, IReadOnlyList<LogicalProcessorId> WorkerMap, int Seed, int Width, int Height);
internal sealed record BenchmarkTrialArtifact(string Schema, Guid SessionId, DateTimeOffset StartedAtUtc, DateTimeOffset EndedAtUtc, string Adapter, string PresentMode, ulong GpuTimestampFrequency, FrozenBenchmarkWorkload FrozenWorkload, IReadOnlyList<BenchmarkFrameTelemetry> Frames, IReadOnlyList<ulong> WorkerChecksums);

internal sealed class BenchmarkWorkload
{
    private const string ArtifactSchema = "latencypilot-gpu-benchmark-v1";
    private const int MinimumCommandBatches = 1;
    private const int MaximumCommandBatches = 4096;
    private const int MinimumSimulationIterations = 1_000;
    private const int MaximumSimulationIterations = 4_000_000;
    private static readonly TimeSpan WarmupDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);
    private readonly BenchmarkOptions options;
    private readonly TextWriter output;
    private readonly IReadOnlyList<LogicalProcessorId> workerMap;

    internal BenchmarkWorkload(BenchmarkOptions options, TextWriter output, IReadOnlyList<LogicalProcessorId> workerMap)
    {
        this.options = options;
        this.output = output;
        this.workerMap = workerMap;
    }

    internal Task<FrozenBenchmarkWorkload> CalibrateAsync(D3D12BenchmarkRenderer renderer, CancellationToken cancellationToken = default)
    {
        var commandBatches = 8;
        var simulationIterations = 20_000;
        var started = Stopwatch.StartNew();
        var intervalStarted = Stopwatch.StartNew();
        var cpuSum = 0d;
        var gpuSum = 0d;
        var intervalFrames = 0;
        var lastProgress = TimeSpan.Zero;

        while (started.Elapsed < WarmupDuration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = renderer.RenderFrame(simulationIterations, commandBatches);
            if (!double.IsFinite(frame.CpuRecordingMilliseconds) || frame.CpuRecordingMilliseconds <= 0 ||
                !double.IsFinite(frame.GpuWorkMilliseconds) || frame.GpuWorkMilliseconds < 0)
            {
                throw new InvalidDataException("Benchmark calibration produced invalid frame timing.");
            }

            cpuSum += frame.CpuRecordingMilliseconds;
            gpuSum += frame.GpuWorkMilliseconds;
            intervalFrames++;
            if (intervalStarted.Elapsed >= TimeSpan.FromSeconds(1) && intervalFrames > 0)
            {
                simulationIterations = TuneSimulationIterations(simulationIterations, cpuSum / intervalFrames);
                commandBatches = TuneCommandBatches(commandBatches, gpuSum / intervalFrames);
                cpuSum = 0;
                gpuSum = 0;
                intervalFrames = 0;
                intervalStarted.Restart();
            }

            if (started.Elapsed - lastProgress >= ProgressInterval)
            {
                lastProgress = started.Elapsed;
                BenchmarkProtocol.WriteProgress(
                    output,
                    options.SessionId,
                    "calibrating",
                    Math.Clamp(started.Elapsed.TotalMilliseconds / WarmupDuration.TotalMilliseconds, 0d, 1d),
                    $"Calibrating fixed workload: {commandBatches} command batches, {simulationIterations} simulation iterations.");
            }
        }

        return Task.FromResult(new FrozenBenchmarkWorkload(
            commandBatches,
            simulationIterations,
            workerMap.ToArray(),
            options.Seed,
            options.Width,
            options.Height));
    }

    internal Task<BenchmarkTrialArtifact> RunTrialAsync(
        D3D12BenchmarkRenderer renderer,
        FrozenBenchmarkWorkload workload,
        CancellationToken cancellationToken = default) =>
        RunTrialAsync(renderer, workload, options.Duration, cancellationToken);

    internal Task<BenchmarkTrialArtifact> RunTrialAsync(
        D3D12BenchmarkRenderer renderer,
        FrozenBenchmarkWorkload workload,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workload);
        if (duration.TotalMilliseconds is < GpuBenchmarkControlProtocol.MinimumTrialDurationMilliseconds or
            > GpuBenchmarkControlProtocol.MaximumTrialDurationMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var expectedFrames = checked((int)Math.Clamp(Math.Ceiling(duration.TotalSeconds * 240d), 1d, 150_000d));
        var frames = new List<BenchmarkFrameTelemetry>(expectedFrames);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var lastProgress = TimeSpan.Zero;

        while (stopwatch.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = renderer.RenderFrame(workload.SimulationIterationsPerWorker, workload.CommandBatchesPerWorker);
            if (!double.IsFinite(frame.CpuRecordingMilliseconds) || frame.CpuRecordingMilliseconds <= 0 ||
                !double.IsFinite(frame.GpuWorkMilliseconds) || frame.GpuWorkMilliseconds < 0)
            {
                throw new InvalidDataException("Benchmark trial produced invalid frame timing.");
            }

            frames.Add(frame);
            if (stopwatch.Elapsed - lastProgress >= ProgressInterval)
            {
                lastProgress = stopwatch.Elapsed;
                BenchmarkProtocol.WriteProgress(
                    output,
                    options.SessionId,
                    "measuring",
                    Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0d, 1d),
                    $"Measured {frames.Count} frames with frozen workload.");
            }
        }

        if (frames.Count == 0)
        {
            throw new InvalidDataException("Benchmark trial completed without any frame evidence.");
        }

        return Task.FromResult(new BenchmarkTrialArtifact(
            ArtifactSchema,
            options.SessionId,
            startedAtUtc,
            DateTimeOffset.UtcNow,
            renderer.AdapterName,
            renderer.PresentMode,
            renderer.TimestampFrequency,
            workload,
            frames,
            renderer.CaptureWorkerChecksums()));
    }

    private static int TuneSimulationIterations(int current, double averageCpuMilliseconds)
    {
        if (averageCpuMilliseconds < 2d) return Math.Min(MaximumSimulationIterations, checked(current * 2));
        if (averageCpuMilliseconds > 12d) return Math.Max(MinimumSimulationIterations, current / 2);
        return current;
    }

    private static int TuneCommandBatches(int current, double averageGpuMilliseconds)
    {
        if (averageGpuMilliseconds < 2d) return Math.Min(MaximumCommandBatches, checked(current * 2));
        if (averageGpuMilliseconds > 14d) return Math.Max(MinimumCommandBatches, current / 2);
        return current;
    }
}
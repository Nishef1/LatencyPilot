using System.ComponentModel;
using System.Diagnostics;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.System;

namespace LatencyPilot.GpuBenchmark;

internal sealed record FrozenBenchmarkWorkload(
    int CommandBatchesPerWorker,
    int SimulationIterationsPerWorker,
    IReadOnlyList<LogicalProcessorId> WorkerMap,
    IReadOnlyList<ulong> WorkerAffinityMasks,
    int Seed,
    int Width,
    int Height);

internal sealed class BenchmarkWorkload
{
    private const int MinimumCommandBatches = 1;
    private const int MaximumCommandBatches = 4096;
    private const int MinimumSimulationIterations = 1_000;
    private static readonly TimeSpan WarmupDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinimumObserverSettleDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaximumObserverSettleDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ObserverQuietTailDuration = TimeSpan.FromMilliseconds(500);
    private const double MinimumObserverTransientFramePeriodMilliseconds = 10d;
    private const double ObserverTransientMedianMultiplier = 2d;
    private static readonly TimeSpan CalibrationProgressInterval = TimeSpan.FromMilliseconds(250);
    private readonly BenchmarkOptions options;
    private readonly TextWriter output;
    private readonly IReadOnlyList<LogicalProcessorId> workerMap;
    private readonly IReadOnlyList<ulong> workerAffinityMasks;

    internal BenchmarkWorkload(
        BenchmarkOptions options,
        TextWriter output,
        IReadOnlyList<LogicalProcessorId> workerMap,
        IReadOnlyList<ulong> workerAffinityMasks)
    {
        this.options = options;
        this.output = output;
        this.workerMap = workerMap;
        this.workerAffinityMasks = workerAffinityMasks;
        if (workerAffinityMasks.Count != workerMap.Count)
        {
            throw new ArgumentException(
                "Benchmark worker affinity-mask count must match the worker map.",
                nameof(workerAffinityMasks));
        }
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
            workerAffinityMasks.ToArray(),
            options.Seed,
            options.Width,
            options.Height));
    }

    internal Task<GpuBenchmarkTrialArtifact> RunTrialAsync(
        D3D12BenchmarkRenderer renderer,
        FrozenBenchmarkWorkload workload,
        CancellationToken cancellationToken = default) =>
        RunTrialAsync(
            renderer,
            workload,
            options.Duration,
            observerActive: false,
            cancellationToken: cancellationToken);

    internal Task<GpuBenchmarkTrialArtifact> RunTrialAsync(
        D3D12BenchmarkRenderer renderer,
        FrozenBenchmarkWorkload workload,
        TimeSpan duration,
        bool observerActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workload);
        if (duration.TotalMilliseconds is
            < GpuBenchmarkControlProtocol.MinimumTrialDurationMilliseconds or
            > GpuBenchmarkControlProtocol.MaximumTrialDurationMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        // Inspect the actual subject after calibration/renderer recreation, outside
        // the scored window. A running overlay process alone is not injection proof.
        var measurementWarnings = CaptureMeasurementWarnings();
        var expectedFrames = checked((int)Math.Clamp(
            Math.Ceiling(duration.TotalSeconds * 240d),
            1d,
            150_000d));
        var frames = new List<BenchmarkFrameTelemetry>(expectedFrames);

        if (observerActive)
        {
            // Scored Gate A trials start external observers shortly before invoking the
            // benchmark. The 2026-09-24 physical v4 evidence showed a phase-locked observer
            // startup transient roughly two seconds after collector startup, so a fixed
            // one-second settle could move that observer cost into the 1%-low tail. Require
            // both a minimum settle and a quiet tail after the last large transient, with a
            // hard upper bound so startup can never hang indefinitely.
            BenchmarkProtocol.WriteProgress(
                output,
                options.SessionId,
                "observer-settle",
                0d,
                "Settling benchmark and observer startup before the scored QPC window.");
            renderer.BeginMeasurementWindow();
            var observerSettle = Stopwatch.StartNew();
            var settleFrames = new List<(TimeSpan ObservedAt, double PeriodMilliseconds)>();

            void ObserveSettleFrame(BenchmarkFrameTelemetry frame)
            {
                ValidateFrame(frame);
                settleFrames.Add((observerSettle.Elapsed, frame.FramePeriodMilliseconds));
            }

            while (observerSettle.Elapsed < MinimumObserverSettleDuration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (renderer.RenderFrame(
                        workload.SimulationIterationsPerWorker,
                        workload.CommandBatchesPerWorker) is { } settleFrame)
                {
                    ObserveSettleFrame(settleFrame);
                }
            }

            foreach (var pendingSettleFrame in renderer.DrainFrames())
            {
                ObserveSettleFrame(pendingSettleFrame);
            }

            if (settleFrames.Count == 0)
            {
                throw new InvalidDataException(
                    "Benchmark observer settle completed without frame-period evidence.");
            }

            var settleMedian = Median(settleFrames.Select(static sample => sample.PeriodMilliseconds));
            var transientThreshold = Math.Max(
                MinimumObserverTransientFramePeriodMilliseconds,
                settleMedian * ObserverTransientMedianMultiplier);
            TimeSpan? lastObserverTransient = settleFrames
                .Where(sample => sample.PeriodMilliseconds >= transientThreshold)
                .Select(static sample => (TimeSpan?)sample.ObservedAt)
                .LastOrDefault();

            bool QuietTailSatisfied() =>
                lastObserverTransient is null ||
                observerSettle.Elapsed - lastObserverTransient.Value >= ObserverQuietTailDuration;

            var observerSettled = QuietTailSatisfied();
            while (!observerSettled && observerSettle.Elapsed < MaximumObserverSettleDuration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (renderer.RenderFrame(
                        workload.SimulationIterationsPerWorker,
                        workload.CommandBatchesPerWorker) is { } settleFrame)
                {
                    ObserveSettleFrame(settleFrame);
                    if (settleFrame.FramePeriodMilliseconds >= transientThreshold)
                    {
                        lastObserverTransient = observerSettle.Elapsed;
                    }
                }

                if (!QuietTailSatisfied())
                {
                    continue;
                }

                // Complete both in-flight contexts before accepting the quiet
                // boundary. A delayed transient is still observer startup.
                foreach (var pendingSettleFrame in renderer.DrainFrames())
                {
                    ObserveSettleFrame(pendingSettleFrame);
                    if (pendingSettleFrame.FramePeriodMilliseconds >= transientThreshold)
                    {
                        lastObserverTransient = observerSettle.Elapsed;
                    }
                }

                observerSettled = QuietTailSatisfied();
            }

            if (!observerSettled)
            {
                foreach (var pendingSettleFrame in renderer.DrainFrames())
                {
                    ObserveSettleFrame(pendingSettleFrame);
                    if (pendingSettleFrame.FramePeriodMilliseconds >= transientThreshold)
                    {
                        lastObserverTransient = observerSettle.Elapsed;
                    }
                }
                observerSettled = QuietTailSatisfied();
            }

            if (!observerSettled)
            {
                throw new InvalidDataException(
                    "Benchmark observers did not reach a quiet pre-score interval before the bounded settle deadline.");
            }
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
                workload.Height,
                workload.WorkerAffinityMasks.ToArray()),
            frames.Select(static frame => new GpuBenchmarkArtifactFrame(
                frame.FrameIndex,
                frame.CpuRecordingMilliseconds,
                frame.GpuWorkMilliseconds,
                frame.FramePeriodMilliseconds,
                frame.GpuTimestampFrequency)).ToArray(),
            renderer.CaptureWorkerChecksums(),
            startedAtQpc,
            completedAtQpc,
            Stopwatch.Frequency,
            measurementWarnings));
    }

    private static string[] CaptureMeasurementWarnings()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var hooks = process.Modules.Cast<ProcessModule>()
                .Select(static module => module.ModuleName)
                .Where(static name =>
                    string.Equals(name, "RTSSHooks64.dll", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "nvspcap64.dll", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return hooks.Length == 0 ? [] :
                [$"Graphics interception detected inside the benchmark: {string.Join(", ", hooks)}. " +
                 "RTSS/NVIDIA overlay or capture hooks can perturb frame timing. If Original or local pairs are unstable, " +
                 "disable the corresponding overlay/monitoring tool and start a new benchmark session. " +
                 "Loaded modules are interference context, not proof of causation; no application was closed automatically."];
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return ["Benchmark graphics-hook inspection was unavailable; overlay interference is unknown."];
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("Median requires at least one value.", nameof(values));
        }

        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2d
            : ordered[middle];
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

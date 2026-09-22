using System.Diagnostics;
using System.Globalization;
using LatencyPilot.Core.Devices;
using Microsoft.Diagnostics.Tracing.Session;
using Sylvan.Data.Csv;

namespace LatencyPilot.Platform.Windows.Devices;

public static class PresentMonConsoleFrameMetricsReader
{
    private const int MaximumRetainedFailureCaptures = 5;
    private static readonly TimeSpan StartupReadinessTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartupReadinessPollInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan CompletionSlack = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ActiveCaptureGrace = TimeSpan.FromMinutes(5);

    public static async Task<PresentMonConsoleCaptureSession> StartAsync(
        uint processId,
        TimeSpan requestedWindow,
        string? executablePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(processId);
        if (requestedWindow <= TimeSpan.Zero || requestedWindow > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(requestedWindow));
        }

        var presentMonPath = await PresentMonConsoleLocator.ResolveAsync(
            executablePath,
            cancellationToken).ConfigureAwait(false);
        var captureId = Guid.NewGuid().ToString("N");
        var sessionName = $"LatencyPilot-{captureId}";
        var tempRoot = Path.Combine(Path.GetTempPath(), "LatencyPilot", "PresentMon");
        var tempDirectory = Path.Combine(tempRoot, captureId);
        Directory.CreateDirectory(tempDirectory);
        var csvPath = Path.Combine(tempDirectory, "frames.csv");

        var captureSeconds = checked((uint)Math.Ceiling(requestedWindow.TotalSeconds + 3d));
        var startInfo = new ProcessStartInfo
        {
            FileName = presentMonPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--process_id");
        startInfo.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--output_file");
        startInfo.ArgumentList.Add(csvPath);
        startInfo.ArgumentList.Add("--v2_metrics");
        // PresentMon and Stopwatch/QPC now share the same monotonic time domain.
        // This avoids local-time conversion, wall-clock adjustment, and tolerance
        // padding when cropping external frame evidence to the scored benchmark.
        startInfo.ArgumentList.Add("--qpc_time");
        startInfo.ArgumentList.Add("--no_console_stats");
        startInfo.ArgumentList.Add("--session_name");
        startInfo.ArgumentList.Add(sessionName);
        startInfo.ArgumentList.Add("--timed");
        startInfo.ArgumentList.Add(captureSeconds.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--terminate_after_timed");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("PresentMon console process could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            using var startupDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupDeadline.CancelAfter(StartupReadinessTimeout);

            bool traceSessionReady;
            try
            {
                traceSessionReady = await WaitForTraceSessionReadyAsync(
                    process,
                    sessionName,
                    startupDeadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    $"PresentMon ETW session did not become queryable within {StartupReadinessTimeout.TotalSeconds:F0} seconds.");
            }

            if (!traceSessionReady || process.HasExited)
            {
                var output = await stdout.ConfigureAwait(false);
                var error = await stderr.ConfigureAwait(false);
                var exit = process.HasExited
                    ? $"exit {process.ExitCode}"
                    : "process exited before ETW session readiness";
                throw new InvalidOperationException(
                    $"PresentMon was not ready before the benchmark started ({exit}). {Truncate(error)} {Truncate(output)}".Trim());
            }

            return new PresentMonConsoleCaptureSession(
                processId,
                requestedWindow,
                presentMonPath,
                tempRoot,
                tempDirectory,
                csvPath,
                process,
                stdout,
                stderr);
        }
        catch
        {
            TryTerminate(process);
            try
            {
                await Task.WhenAll(stdout, stderr)
                    .WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or IOException or InvalidOperationException)
            {
            }
            process.Dispose();
            TryDeleteDirectory(tempDirectory);
            throw;
        }
    }

    private static async Task<bool> WaitForTraceSessionReadyAsync(
        Process process,
        string sessionName,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                return false;
            }

            try
            {
                if (TraceEventSession.GetActiveSessionNames().Any(name =>
                        string.Equals(name, sessionName, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new InvalidOperationException(
                    "PresentMon ETW readiness could not be queried with the current process permissions.",
                    exception);
            }
            catch (global::System.Security.SecurityException exception)
            {
                throw new InvalidOperationException(
                    "PresentMon ETW readiness could not be queried with the current process permissions.",
                    exception);
            }

            await Task.Delay(StartupReadinessPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static async Task<PresentMonFrameCaptureSnapshot> ParseAsync(
        string csvPath,
        uint processId,
        TimeSpan requestedWindow,
        long benchmarkStartedAtQpc,
        long benchmarkEndedAtQpc,
        long qpcFrequency,
        DateTimeOffset benchmarkStartedAtUtc,
        DateTimeOffset benchmarkEndedAtUtc,
        string presentMonPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(csvPath))
        {
            return Failure(
                PresentMonWorkloadCaptureStatus.TrackingFailed,
                processId,
                requestedWindow,
                presentMonPath,
                "PresentMon completed without creating its CSV output.",
                benchmarkStartedAtUtc,
                benchmarkEndedAtUtc);
        }
        if (benchmarkStartedAtQpc <= 0 ||
            benchmarkEndedAtQpc <= benchmarkStartedAtQpc ||
            qpcFrequency <= 0)
        {
            return Failure(
                PresentMonWorkloadCaptureStatus.InvalidData,
                processId,
                requestedWindow,
                presentMonPath,
                "Benchmark QPC provenance is missing or invalid; PresentMon rows cannot be correlated safely.",
                benchmarkStartedAtUtc,
                benchmarkEndedAtUtc);
        }

        try
        {
            using var csv = CsvDataReader.Create(csvPath, new CsvDataReaderOptions
            {
                Culture = CultureInfo.InvariantCulture,
                HasHeaders = true,
            });

            var processIdOrdinal = RequireColumn(csv, "ProcessID");
            var swapChainOrdinal = RequireColumn(csv, "SwapChainAddress");
            var startQpcOrdinal = RequireColumn(csv, "CPUStartQPC");
            // PresentMon documents MsBetweenPresents as the interval between
            // Present() calls. MsBetweenAppStart measures a different CPU-frame
            // boundary and must not be substituted as the same frame interval.
            var frameTimeOrdinal = RequireAnyColumn(csv, "FrameTime", "MsBetweenPresents");
            var cpuBusyOrdinal = RequireAnyColumn(csv, "CPUBusy", "MsCPUBusy");
            var cpuWaitOrdinal = RequireAnyColumn(csv, "CPUWait", "MsCPUWait");
            var gpuLatencyOrdinal = OptionalAnyColumn(csv, "GPULatency", "MsGPULatency");
            var gpuTimeOrdinal = OptionalAnyColumn(csv, "GPUTime", "MsGPUTime", "MsUntilRenderComplete");
            var gpuBusyOrdinal = OptionalAnyColumn(csv, "GPUBusy", "MsGPUBusy");
            var gpuWaitOrdinal = OptionalAnyColumn(csv, "GPUWait", "MsGPUWait");
            var displayLatencyOrdinal = OptionalAnyColumn(csv, "DisplayLatency", "MsUntilDisplayed");

            var unavailable = new List<string> { "Dropped frame ratio" };
            AddUnavailable(unavailable, gpuLatencyOrdinal, "GPU latency (ms)");
            AddUnavailable(unavailable, gpuTimeOrdinal, "GPU time (ms)");
            AddUnavailable(unavailable, gpuBusyOrdinal, "GPU busy (ms)");
            AddUnavailable(unavailable, gpuWaitOrdinal, "GPU wait (ms)");
            AddUnavailable(unavailable, displayLatencyOrdinal, "Display latency (ms)");

            var frames = new List<PresentMonFrameMetricsSnapshot>(8_192);
            var minimumAcceptedQpc = long.MaxValue;
            var maximumAcceptedQpc = long.MinValue;
            var rowsForProcess = 0;
            var rowsInWindow = 0;
            while (await csv.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!uint.TryParse(csv.GetString(processIdOrdinal), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowProcessId) ||
                    rowProcessId != processId)
                {
                    continue;
                }

                rowsForProcess++;
                if (!long.TryParse(
                        csv.GetString(startQpcOrdinal),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var rowStartedAtQpc) ||
                    rowStartedAtQpc < benchmarkStartedAtQpc ||
                    rowStartedAtQpc > benchmarkEndedAtQpc)
                {
                    continue;
                }

                rowsInWindow++;
                minimumAcceptedQpc = Math.Min(minimumAcceptedQpc, rowStartedAtQpc);
                maximumAcceptedQpc = Math.Max(maximumAcceptedQpc, rowStartedAtQpc);

                if (!TryParseSwapChain(csv.GetString(swapChainOrdinal), out var swapChain) || swapChain == 0 ||
                    !TryParseRequiredDouble(csv.GetString(frameTimeOrdinal), out var frameTime) || frameTime <= 0 ||
                    !TryParseRequiredDouble(csv.GetString(cpuBusyOrdinal), out var cpuBusy) || cpuBusy < 0 ||
                    !TryParseRequiredDouble(csv.GetString(cpuWaitOrdinal), out var cpuWait) || cpuWait < 0)
                {
                    continue;
                }

                frames.Add(new PresentMonFrameMetricsSnapshot(
                    swapChain,
                    frameTime,
                    cpuBusy,
                    cpuWait,
                    ParseOptionalDouble(csv, gpuTimeOrdinal),
                    ParseOptionalDouble(csv, gpuBusyOrdinal),
                    ParseOptionalDouble(csv, gpuWaitOrdinal),
                    DroppedFrame: null,
                    ParseOptionalDouble(csv, gpuLatencyOrdinal),
                    ParseOptionalDouble(csv, displayLatencyOrdinal)));
            }

            if (frames.Count == 0)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.NoSwapChains,
                    processId,
                    requestedWindow,
                    presentMonPath,
                    $"PresentMon produced no valid frame rows inside the benchmark QPC window (target PID {processId}; rows for target={rowsForProcess}, rows in window={rowsInWindow}).",
                    benchmarkStartedAtUtc,
                    benchmarkEndedAtUtc);
            }

            var observedStart = AddQpcOffset(
                benchmarkStartedAtUtc,
                minimumAcceptedQpc - benchmarkStartedAtQpc,
                qpcFrequency);
            var observedEnd = AddQpcOffset(
                benchmarkStartedAtUtc,
                maximumAcceptedQpc - benchmarkStartedAtQpc,
                qpcFrequency);
            var actualWindowMs = Math.Max(
                1d,
                (maximumAcceptedQpc - minimumAcceptedQpc) * 1000d / qpcFrequency);
            return new PresentMonFrameCaptureSnapshot(
                PresentMonWorkloadCaptureStatus.Available,
                processId,
                requestedWindow.TotalMilliseconds,
                actualWindowMs,
                ApiVersion: null,
                frames.AsReadOnly(),
                unavailable.AsReadOnly(),
                presentMonPath,
                NativeStatusCode: 0,
                Error: null,
                observedStart,
                observedEnd);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            CsvFormatException or
            InvalidDataException or
            IOException or
            FormatException or
            OverflowException or
            ArgumentOutOfRangeException)
        {
            return Failure(
                PresentMonWorkloadCaptureStatus.InvalidData,
                processId,
                requestedWindow,
                presentMonPath,
                $"PresentMon CSV could not be decoded: {exception.GetType().Name}: {exception.Message}",
                benchmarkStartedAtUtc,
                benchmarkEndedAtUtc);
        }
    }

    private static DateTimeOffset AddQpcOffset(
        DateTimeOffset benchmarkStartedAtUtc,
        long qpcDelta,
        long qpcFrequency)
    {
        var seconds = qpcDelta / (double)qpcFrequency;
        if (!double.IsFinite(seconds) || seconds < 0d)
        {
            throw new InvalidDataException("PresentMon QPC row produced an invalid benchmark-relative offset.");
        }

        return benchmarkStartedAtUtc.AddSeconds(seconds);
    }

    private static PresentMonFrameCaptureSnapshot Failure(
        PresentMonWorkloadCaptureStatus status,
        uint processId,
        TimeSpan requestedWindow,
        string path,
        string error,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc) =>
        new(
            status,
            processId,
            requestedWindow.TotalMilliseconds,
            Math.Max(0d, (endedAtUtc - startedAtUtc).TotalMilliseconds),
            ApiVersion: null,
            [],
            [],
            path,
            NativeStatusCode: null,
            error,
            startedAtUtc,
            endedAtUtc);

    private static int RequireColumn(CsvDataReader csv, string name)
    {
        var ordinal = OptionalColumn(csv, name);
        return ordinal >= 0
            ? ordinal
            : throw new InvalidDataException($"PresentMon CSV is missing required column '{name}'.");
    }

    private static int RequireAnyColumn(CsvDataReader csv, params string[] names)
    {
        foreach (var name in names)
        {
            var ordinal = OptionalColumn(csv, name);
            if (ordinal >= 0) return ordinal;
        }

        throw new InvalidDataException(
            $"PresentMon CSV is missing required column '{names[0]}' (tried {string.Join('/', names)}; have {string.Join(',', Enumerable.Range(0, csv.FieldCount).Select(csv.GetName))}).");
    }

    private static int OptionalAnyColumn(CsvDataReader csv, params string[] names)
    {
        foreach (var name in names)
        {
            var ordinal = OptionalColumn(csv, name);
            if (ordinal >= 0) return ordinal;
        }
        return -1;
    }

    private static int OptionalColumn(CsvDataReader csv, string name)
    {
        for (var ordinal = 0; ordinal < csv.FieldCount; ordinal++)
        {
            if (string.Equals(csv.GetName(ordinal), name, StringComparison.OrdinalIgnoreCase)) return ordinal;
        }
        return -1;
    }

    private static string Truncate(string? value, int maximumLength = 500)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }

    private static void AddUnavailable(List<string> unavailable, int ordinal, string name)
    {
        if (ordinal < 0) unavailable.Add(name);
    }

    private static bool TryParseSwapChain(string value, out ulong swapChain)
    {
        var text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out swapChain);
    }

    private static bool TryParseRequiredDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);

    private static double? ParseOptionalDouble(CsvDataReader csv, int ordinal)
    {
        if (ordinal < 0) return null;
        var value = csv.GetString(ordinal).Trim();
        return value.Length == 0 || string.Equals(value, "NA", StringComparison.OrdinalIgnoreCase)
            ? null
            : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) &&
              double.IsFinite(result) && result >= 0
                ? result
                : null;
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void PruneRetainedFailures(string rootDirectory, string currentDirectory)
    {
        try
        {
            if (!Directory.Exists(rootDirectory)) return;
            var cutoff = DateTime.UtcNow - ActiveCaptureGrace;
            var oldFailures = new DirectoryInfo(rootDirectory)
                .EnumerateDirectories()
                .Where(directory =>
                    !string.Equals(directory.FullName, currentDirectory, StringComparison.OrdinalIgnoreCase) &&
                    directory.LastWriteTimeUtc < cutoff)
                .OrderByDescending(static directory => directory.LastWriteTimeUtc)
                .Skip(Math.Max(0, MaximumRetainedFailureCaptures - 1))
                .ToArray();
            foreach (var directory in oldFailures)
            {
                TryDeleteDirectory(directory.FullName);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public sealed class PresentMonConsoleCaptureSession : IAsyncDisposable
    {
        private readonly uint processId;
        private readonly TimeSpan requestedWindow;
        private readonly string presentMonPath;
        private readonly string tempRoot;
        private readonly string tempDirectory;
        private readonly string csvPath;
        private readonly Process process;
        private readonly Task<string> stdout;
        private readonly Task<string> stderr;
        private bool completed;

        internal PresentMonConsoleCaptureSession(
            uint processId,
            TimeSpan requestedWindow,
            string presentMonPath,
            string tempRoot,
            string tempDirectory,
            string csvPath,
            Process process,
            Task<string> stdout,
            Task<string> stderr)
        {
            this.processId = processId;
            this.requestedWindow = requestedWindow;
            this.presentMonPath = presentMonPath;
            this.tempRoot = tempRoot;
            this.tempDirectory = tempDirectory;
            this.csvPath = csvPath;
            this.process = process;
            this.stdout = stdout;
            this.stderr = stderr;
        }

        public async Task<PresentMonFrameCaptureSnapshot> CompleteAsync(
            long benchmarkStartedAtQpc,
            long benchmarkEndedAtQpc,
            long qpcFrequency,
            DateTimeOffset benchmarkStartedAtUtc,
            DateTimeOffset benchmarkEndedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (completed) throw new InvalidOperationException("PresentMon capture was already completed.");
            completed = true;

            try
            {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(requestedWindow + CompletionSlack);
            PresentMonFrameCaptureSnapshot snapshot;
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                var output = await stdout.ConfigureAwait(false);
                var error = await stderr.ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    snapshot = Failure(
                        PresentMonWorkloadCaptureStatus.TrackingFailed,
                        processId,
                        requestedWindow,
                        presentMonPath,
                        $"PresentMon exited with code {process.ExitCode}. {Truncate(error)} {Truncate(output)}".Trim(),
                        benchmarkStartedAtUtc,
                        benchmarkEndedAtUtc);
                }
                else
                {
                    snapshot = await ParseAsync(
                        csvPath,
                        processId,
                        requestedWindow,
                        benchmarkStartedAtQpc,
                        benchmarkEndedAtQpc,
                        qpcFrequency,
                        benchmarkStartedAtUtc,
                        benchmarkEndedAtUtc,
                        presentMonPath,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                TryTerminate(process);
                process.Dispose();
            }

            if (snapshot.IsAvailable)
            {
                TryDeleteDirectory(tempDirectory);
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.Error))
            {
                snapshot = snapshot with
                {
                    Error = $"{snapshot.Error} Raw CSV retained at: {csvPath}",
                };
                PruneRetainedFailures(tempRoot, tempDirectory);
            }

            return snapshot;
            }
            catch
            {
                // completed is already true, so DisposeAsync will not clean up;
                // remove the capture directory on cancellation/failure so temp is not leaked.
                TryDeleteDirectory(tempDirectory);
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (!completed)
            {
                TryTerminate(process);
                process.Dispose();
                TryDeleteDirectory(tempDirectory);
                completed = true;
            }
            return ValueTask.CompletedTask;
        }
    }
}

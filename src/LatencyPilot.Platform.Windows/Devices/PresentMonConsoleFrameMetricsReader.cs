using System.Diagnostics;
using System.Globalization;
using LatencyPilot.Core.Devices;
using Sylvan.Data.Csv;

namespace LatencyPilot.Platform.Windows.Devices;

public static class PresentMonConsoleFrameMetricsReader
{
    private const int MaximumRetainedFailureCaptures = 5;
    private static readonly TimeSpan StartupProbeDelay = TimeSpan.FromMilliseconds(500);
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
        startInfo.ArgumentList.Add("--date_time");
        startInfo.ArgumentList.Add("--no_console_stats");
        startInfo.ArgumentList.Add("--session_name");
        startInfo.ArgumentList.Add($"LatencyPilot-{captureId}");
        startInfo.ArgumentList.Add("--timed");
        startInfo.ArgumentList.Add(captureSeconds.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--terminate_after_timed");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("PresentMon console process could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await Task.Delay(StartupProbeDelay, cancellationToken).ConfigureAwait(false);
            if (process.HasExited)
            {
                var output = await stdout.ConfigureAwait(false);
                var error = await stderr.ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"PresentMon exited before the benchmark started (exit {process.ExitCode}). {error} {output}".Trim());
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
            process.Dispose();
            TryDeleteDirectory(tempDirectory);
            throw;
        }
    }

    internal static async Task<PresentMonFrameCaptureSnapshot> ParseAsync(
        string csvPath,
        uint processId,
        TimeSpan requestedWindow,
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

        try
        {
            using var csv = CsvDataReader.Create(csvPath, new CsvDataReaderOptions
            {
                Culture = CultureInfo.InvariantCulture,
                HasHeaders = true,
            });

            var processIdOrdinal = RequireColumn(csv, "ProcessID");
            var swapChainOrdinal = RequireColumn(csv, "SwapChainAddress");
            var startTimeOrdinal = RequireAnyColumn(csv, "CPUStartDateTime", "CPUStartTime", "CPUStartQPC", "CPUStartQPCTime");
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
            var startColumnName = csv.GetName(startTimeOrdinal);
            var useDateCrop = startColumnName.Contains("DateTime", StringComparison.OrdinalIgnoreCase);
            var minRowUtc = DateTimeOffset.MaxValue;
            var maxRowUtc = DateTimeOffset.MinValue;
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
                if (useDateCrop)
                {
                    if (!TryParseLocalTimestamp(csv.GetString(startTimeOrdinal), out var rowStartedAtUtc) ||
                        rowStartedAtUtc < benchmarkStartedAtUtc - TimeSpan.FromSeconds(5) ||
                        rowStartedAtUtc > benchmarkEndedAtUtc + TimeSpan.FromSeconds(5))
                    {
                        continue;
                    }

                    rowsInWindow++;
                    if (rowStartedAtUtc < minRowUtc) minRowUtc = rowStartedAtUtc;
                    if (rowStartedAtUtc > maxRowUtc) maxRowUtc = rowStartedAtUtc;
                }
                else
                {
                    rowsInWindow++;
                }

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
                    $"PresentMon produced no valid frame rows inside the benchmark artifact window (rows for PID={rowsForProcess}, in window={rowsInWindow}).",
                    benchmarkStartedAtUtc,
                    benchmarkEndedAtUtc);
            }

            var observedStart = useDateCrop && maxRowUtc >= minRowUtc ? minRowUtc : benchmarkStartedAtUtc;
            var observedEnd = useDateCrop && maxRowUtc >= minRowUtc ? maxRowUtc : benchmarkEndedAtUtc;
            var actualWindowMs = Math.Max(1d, (observedEnd - observedStart).TotalMilliseconds);
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
            OverflowException)
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

    private static bool TryParseLocalTimestamp(string value, out DateTimeOffset utc)
    {
        utc = default;
        var text = value.Trim();
        var dot = text.LastIndexOf('.');
        if (dot >= 0)
        {
            var fractionLength = text.Length - dot - 1;
            if (fractionLength > 7) text = text[..(dot + 1 + 7)];
        }

        if (!DateTime.TryParseExact(
                text,
                "yyyy-M-d H:mm:ss.FFFFFFF",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var local))
        {
            return false;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Local);
        utc = new DateTimeOffset(local).ToUniversalTime();
        return true;
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
            DateTimeOffset benchmarkStartedAtUtc,
            DateTimeOffset benchmarkEndedAtUtc,
            CancellationToken cancellationToken = default)
        {
            if (completed) throw new InvalidOperationException("PresentMon capture was already completed.");
            completed = true;

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

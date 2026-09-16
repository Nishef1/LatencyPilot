using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Comparisons;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.System;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;

namespace LatencyPilot.GateAValidation;

internal static class GpuAutoAffinityGateARunner
{
    internal const string ModeFlag = "--auto-affinity";
    private const string MutationConfirmationFlag = "--confirm-physical-mutation";
    private static readonly Guid DisplayDeviceClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    internal static async Task<int> RunAsync(string[] args)
    {
        AutoOptions? options = null;
        GpuBenchmarkControlClient? benchmark = null;
        GpuAutoAffinityGateABackend? rawBackend = null;
        GpuInterruptAffinitySnapshot? preMutationOriginalState = null;
        GpuGateAProgressFile? progress = null;
        CancellationTokenSource? sessionCancellation = null;
        CancellationTokenSource? watcherShutdown = null;
        Task? cancelWatcher = null;
        try
        {
            options = AutoOptions.Parse(args);
            EnsureAdministrator();
            await VerifyCleanExactSourceAsync(options).ConfigureAwait(false);

            var journal = new MutationJournal(MutationJournal.GetDefaultDatabasePath());
            journal.Initialize();
            var unresolvedBefore = MutationJournalReadOnlyInspector.GetUnresolved(
                MutationJournal.GetDefaultDatabasePath());
            if (unresolvedBefore.Count != 0)
            {
                throw new InvalidOperationException(
                    $"GPU auto-affinity Gate A requires zero unresolved journal entries; found {unresolvedBefore.Count}.");
            }

            var target = DeviceInventoryReader.CapturePresentDevices().Devices
                .Where(static device => device.ClassGuid == DisplayDeviceClass)
                .ToArray();
            if (target.Length != 1)
            {
                throw new InvalidOperationException(
                    $"GPU auto-affinity Gate A requires exactly one present display adapter; found {target.Length}.");
            }

            preMutationOriginalState = GpuInterruptAffinityPolicyStore.Capture(target[0].InstanceId);

            var topology = ProcessorTopologyReader.Capture();
            if (topology.ProcessorGroupCount != 1)
            {
                throw new NotSupportedException(
                    "GPU auto-affinity Gate A v1 requires exactly one Windows processor group.");
            }

            ProcessorCpuSetSnapshot? cpuSets = null;
            try
            {
                cpuSets = ProcessorCpuSetReader.Capture();
            }
            catch (Exception exception) when (exception is
                Win32Exception or
                InvalidDataException or
                PlatformNotSupportedException)
            {
                Console.Error.WriteLine(
                    $"CPU-set metadata is unavailable; Gate A will preserve topology evidence without inventing CPU-set exclusions: {exception.Message}");
            }

            var pressure = topology.Cores
                .SelectMany(static core => core.LogicalProcessors)
                .Select(static processor => new ProcessorPressureEvidence(processor, 0d))
                .ToArray();
            var progressPlan = GpuAutoAffinityProgressPlan.Create(topology, pressure, cpuSets);
            if (progressPlan.PhysicalCandidateCount == 0)
            {
                throw new InvalidOperationException(
                    "GPU auto-affinity Gate A has no eligible physical-core candidates after topology/CPU-set exclusions.");
            }

            progress = new GpuGateAProgressFile(
                options.SessionId,
                options.ProgressPath,
                progressPlan);
            await progress.ReportInitializingAsync(
                "Preparing the benchmark-backed GPU affinity session.").ConfigureAwait(false);

            sessionCancellation = new CancellationTokenSource();
            watcherShutdown = new CancellationTokenSource();
            cancelWatcher = WatchCancellationAsync(
                options.CancelPath,
                progress,
                sessionCancellation,
                watcherShutdown.Token);

            benchmark = await GpuBenchmarkControlClient.ConnectAsync(
                options.BenchmarkPipeName,
                options.SessionId,
                options.BenchmarkToken,
                sessionCancellation.Token).ConfigureAwait(false);
            rawBackend = new GpuAutoAffinityGateABackend(
                target[0].InstanceId,
                options.ExpectedCommit,
                options.SessionId,
                benchmark.ProcessId,
                topology,
                benchmark);
            if (!MatchesExactOriginalState(preMutationOriginalState))
            {
                throw new InvalidOperationException(
                    "GPU affinity state changed during Gate A startup before any owned mutation began.");
            }

            var reportingBackend = new ProgressReportingGpuAutoAffinityBackend(rawBackend, progress);
            var shuffleSeed = RandomNumberGenerator.GetInt32(int.MaxValue);
            var request = new GpuAutoAffinitySessionRequest(
                options.SessionId,
                topology,
                pressure,
                cpuSets,
                shuffleSeed,
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30),
                new ComparisonPolicy());

            Console.WriteLine($"session={options.SessionId:D}");
            Console.WriteLine($"source-revision={options.ExpectedCommit}");
            Console.WriteLine($"benchmark-pid={benchmark.ProcessId.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine($"device={target[0].InstanceId}");
            Console.WriteLine($"physical-cores={topology.PhysicalCoreCount.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine($"candidate-count={progressPlan.PhysicalCandidateCount.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine($"shuffle-seed={shuffleSeed.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine("gpu-auto-affinity=running");

            var session = new GpuAutoAffinitySession(reportingBackend, reportingBackend);
            var result = await session.RunAsync(request, sessionCancellation.Token).ConfigureAwait(false);
            await TryStopBenchmarkAsync(benchmark).ConfigureAwait(false);

            var unresolvedAfter = MutationJournalReadOnlyInspector.GetUnresolved(
                MutationJournal.GetDefaultDatabasePath());
            var finalReport = rawBackend.CompleteReport(result.Report, unresolvedAfter.Count);
            if (unresolvedAfter.Count != 0)
            {
                throw new InvalidOperationException(
                    $"GPU auto-affinity Gate A ended with {unresolvedAfter.Count} unresolved journal entries.");
            }
            if (finalReport.Provenance is null)
            {
                throw new InvalidOperationException(
                    "GPU auto-affinity Gate A completed without benchmark provenance for the saved report.");
            }
            if (!finalReport.FinalStateVerified)
            {
                throw new InvalidOperationException(
                    "GPU auto-affinity Gate A completed without verified final machine state.");
            }

            await WriteReportAsync(options.OutputPath, finalReport).ConfigureAwait(false);
            await progress.ReportTerminalAsync(
                result.Recommendation.ToString(),
                result.Finalist,
                finalReport.FinalStateVerified,
                result.Recommendation == GpuOptimizationRecommendation.KeepCandidate
                    ? "GPU auto-affinity finished with a verified finalist candidate."
                    : "GPU auto-affinity finished with the verified original state.").ConfigureAwait(false);
            Console.WriteLine($"recommendation={result.Recommendation}");
            Console.WriteLine($"final-processor={result.Finalist?.Processor.ToString() ?? "original"}");
            Console.WriteLine("unresolved=0");
            Console.WriteLine($"report={options.OutputPath}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            await TryStopBenchmarkAsync(benchmark).ConfigureAwait(false);
            var safe = await VerifyStoppedStateAsync(rawBackend, preMutationOriginalState).ConfigureAwait(false);
            if (progress is not null)
            {
                await progress.ReportTerminalAsync(
                    "Stopped safely",
                    null,
                    safe,
                    safe
                        ? "Stopped safely. The exact original GPU affinity state is verified and no unresolved mutation remains."
                        : "Stop completed, but final machine state could not be verified automatically. Inspect recovery evidence before continuing.").ConfigureAwait(false);
            }

            if (options is not null)
            {
                var stoppedReport = new GpuAutoAffinityReport(
                    GpuAutoAffinityReport.SchemaId,
                    options.SessionId,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    0,
                    [],
                    [],
                    GpuOptimizationRecommendation.RestoreOriginal.ToString(),
                    null,
                    FinalStateVerified: safe,
                    OriginalStateRestored: safe,
                    [safe
                        ? "Stop safely was requested; future trials were cancelled and the exact original state was verified."
                        : "Stop safely was requested, but exact rollback/recovery could not be verified automatically."],
                    Provenance: rawBackend?.ReportProvenance);
                stoppedReport = TryCompleteReport(rawBackend, stoppedReport);
                await WriteReportAsync(options.OutputPath, stoppedReport).ConfigureAwait(false);
            }

            Console.WriteLine(safe ? "gpu-auto-affinity=stopped-safely" : "gpu-auto-affinity=manual-recovery-required");
            return safe ? 3 : 4;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{exception.GetType().Name}: {exception.Message}");
            await TryStopBenchmarkAsync(benchmark).ConfigureAwait(false);
            var safe = await VerifyStoppedStateAsync(rawBackend, preMutationOriginalState).ConfigureAwait(false);
            if (options is not null)
            {
                var fallback = new GpuAutoAffinityReport(
                    GpuAutoAffinityReport.SchemaId,
                    options.SessionId,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    0,
                    [],
                    [],
                    GpuOptimizationRecommendation.RestoreOriginal.ToString(),
                    null,
                    FinalStateVerified: safe,
                    OriginalStateRestored: safe,
                    [$"{exception.GetType().Name}: {exception.Message}"],
                    Provenance: rawBackend?.ReportProvenance);
                fallback = TryCompleteReport(rawBackend, fallback);
                try
                {
                    await WriteReportAsync(options.OutputPath, fallback).ConfigureAwait(false);
                    if (progress is not null)
                    {
                        await progress.ReportTerminalAsync(
                            "Failed safely",
                            null,
                            fallback.FinalStateVerified,
                            fallback.FinalStateVerified
                                ? "The session failed, but the exact original state is verified and no unresolved mutation remains."
                                : "The session failed and automatic recovery is not fully verified.").ConfigureAwait(false);
                    }
                }
                catch (Exception writeFailure) when (writeFailure is
                    IOException or
                    UnauthorizedAccessException or
                    JsonException)
                {
                    Console.Error.WriteLine($"Unable to write Gate A failure report: {writeFailure.Message}");
                }
            }

            return safe ? 1 : 4;
        }
        finally
        {
            if (watcherShutdown is not null)
            {
                watcherShutdown.Cancel();
            }
            if (cancelWatcher is not null)
            {
                try
                {
                    await cancelWatcher.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when the helper terminalizes before a stop request arrives.
                }
            }
            watcherShutdown?.Dispose();
            sessionCancellation?.Dispose();

            if (benchmark is not null)
            {
                try
                {
                    await benchmark.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is
                    IOException or
                    InvalidDataException or
                    InvalidOperationException or
                    OperationCanceledException or
                    ObjectDisposedException)
                {
                    Console.Error.WriteLine($"Unable to dispose the Gate A benchmark control channel cleanly: {exception.Message}");
                }
            }
        }
    }

    private static GpuAutoAffinityReport TryCompleteReport(
        GpuAutoAffinityGateABackend? backend,
        GpuAutoAffinityReport report)
    {
        if (backend is null)
        {
            return report;
        }

        try
        {
            var unresolved = MutationJournalReadOnlyInspector.GetUnresolved(
                MutationJournal.GetDefaultDatabasePath());
            return backend.CompleteReport(report, unresolved.Count);
        }
        catch (Exception exception) when (exception is
            IOException or
            InvalidDataException or
            InvalidOperationException or
            UnauthorizedAccessException or
            Win32Exception)
        {
            Console.Error.WriteLine($"Unable to enrich Gate A terminal report with final machine state: {exception.Message}");
            return report with { RecoveryStatus = "final-state-capture-failed" };
        }
    }

    private static async Task WatchCancellationAsync(
        string cancelPath,
        GpuGateAProgressFile progress,
        CancellationTokenSource sessionCancellation,
        CancellationToken shutdownToken)
    {
        var fullPath = Path.GetFullPath(cancelPath);
        while (true)
        {
            shutdownToken.ThrowIfCancellationRequested();
            if (File.Exists(fullPath))
            {
                await progress.ReportStopRequestedAsync().ConfigureAwait(false);
                sessionCancellation.Cancel();
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), shutdownToken).ConfigureAwait(false);
        }
    }

    private static async Task TryStopBenchmarkAsync(GpuBenchmarkControlClient? benchmark)
    {
        if (benchmark is null)
        {
            return;
        }

        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await benchmark.StopAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            IOException or
            InvalidDataException or
            InvalidOperationException or
            OperationCanceledException or
            ObjectDisposedException)
        {
            Console.Error.WriteLine(
                $"Benchmark control Stop did not complete cleanly; closing the authenticated pipe instead: {exception.Message}");
        }
    }

    private static async Task<bool> VerifyStoppedStateAsync(
        GpuAutoAffinityGateABackend? backend,
        GpuInterruptAffinitySnapshot? preMutationOriginalState)
    {
        try
        {
            var unresolved = MutationJournalReadOnlyInspector.GetUnresolved(
                MutationJournal.GetDefaultDatabasePath());
            if (unresolved.Count != 0 || preMutationOriginalState is null)
            {
                return false;
            }

            if (backend is not null &&
                !await backend.VerifyOriginalStateAsync(CancellationToken.None).ConfigureAwait(false))
            {
                return false;
            }

            return MatchesExactOriginalState(preMutationOriginalState);
        }
        catch (Exception exception) when (exception is
            IOException or
            InvalidDataException or
            InvalidOperationException or
            UnauthorizedAccessException or
            Win32Exception)
        {
            Console.Error.WriteLine($"Final safe-state verification failed: {exception.Message}");
            return false;
        }
    }

    private static bool MatchesExactOriginalState(GpuInterruptAffinitySnapshot original)
    {
        var current = GpuInterruptAffinityPolicyStore.Capture(original.DeviceInstanceId);
        return string.Equals(
                   current.DriverVersion,
                   original.DriverVersion,
                   StringComparison.OrdinalIgnoreCase) &&
               GpuInterruptAffinityStateComparer.MatchesOriginal(current, original);
    }

    private static async Task VerifyCleanExactSourceAsync(AutoOptions options)
    {
        var head = await RunGitAsync(options.RepositoryRoot, "rev-parse", "HEAD").ConfigureAwait(false);
        var branch = await RunGitAsync(options.RepositoryRoot, "branch", "--show-current").ConfigureAwait(false);
        var status = await RunGitAsync(options.RepositoryRoot, "status", "--porcelain").ConfigureAwait(false);
        if (!string.Equals(head.Trim(), options.ExpectedCommit, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(branch.Trim(), "main", StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(status))
        {
            throw new InvalidOperationException(
                "GPU auto-affinity Gate A requires a clean main checkout at the exact expected source revision.");
        }
    }

    private static async Task<string> RunGitAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start git for Gate A source verification.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {stderr.Trim()}");
        }

        return stdout;
    }

    private static async Task WriteReportAsync(string outputPath, GpuAutoAffinityReport report)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("GPU auto-affinity report path has no parent directory."));
        var temporaryPath = outputPath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(report, JsonOptions)).ConfigureAwait(false);
        File.Move(temporaryPath, outputPath, overwrite: true);
    }

    private static void EnsureAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("GPU auto-affinity Gate A is supported only on Windows.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new UnauthorizedAccessException(
                "GPU auto-affinity Gate A must run in the explicitly elevated helper process.");
        }
    }

    private sealed record AutoOptions(
        string RepositoryRoot,
        string ExpectedCommit,
        string OutputPath,
        string ProgressPath,
        string CancelPath,
        Guid SessionId,
        string BenchmarkPipeName,
        string BenchmarkToken)
    {
        internal static AutoOptions Parse(string[] args)
        {
            ArgumentNullException.ThrowIfNull(args);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var mode = false;
            var confirmation = false;
            for (var index = 0; index < args.Length; index++)
            {
                var token = args[index];
                if (string.Equals(token, ModeFlag, StringComparison.Ordinal))
                {
                    mode = true;
                    continue;
                }
                if (string.Equals(token, MutationConfirmationFlag, StringComparison.Ordinal))
                {
                    confirmation = true;
                    continue;
                }

                if (token is not ("--repo-root" or "--expected-commit" or "--output" or
                    "--progress" or "--cancel" or "--session-id" or "--benchmark-pipe" or "--benchmark-token"))
                {
                    throw new ArgumentException($"Unknown GPU auto-affinity Gate A option '{token}'.");
                }
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new ArgumentException($"GPU auto-affinity Gate A option '{token}' requires a value.");
                }

                values[token] = args[++index];
            }

            if (!mode || !confirmation)
            {
                throw new InvalidOperationException(
                    $"GPU auto-affinity Gate A requires {ModeFlag} and explicit {MutationConfirmationFlag} acknowledgement.");
            }

            string Required(string key) => values.TryGetValue(key, out var value)
                ? value.Trim()
                : throw new ArgumentException($"Required GPU auto-affinity Gate A option '{key}' is missing.");

            var expectedCommit = Required("--expected-commit").ToLowerInvariant();
            if (expectedCommit.Length != 40 || !expectedCommit.All(Uri.IsHexDigit))
            {
                throw new ArgumentException("--expected-commit must be an exact 40-character hexadecimal revision.");
            }
            if (!Guid.TryParseExact(Required("--session-id"), "D", out var sessionId) || sessionId == Guid.Empty)
            {
                throw new ArgumentException("--session-id must be a non-empty D-format GUID.");
            }
            var benchmarkToken = Required("--benchmark-token");
            if (!GpuBenchmarkControlProtocol.IsValidToken(benchmarkToken))
            {
                throw new ArgumentException("--benchmark-token is not a valid 256-bit hexadecimal control token.");
            }

            var repoRoot = Path.GetFullPath(Required("--repo-root"));
            if (!Directory.Exists(repoRoot) || !File.Exists(Path.Combine(repoRoot, "LatencyPilot.slnx")))
            {
                throw new DirectoryNotFoundException("--repo-root is not a LatencyPilot source checkout.");
            }

            return new AutoOptions(
                repoRoot,
                expectedCommit,
                Path.GetFullPath(Required("--output")),
                Path.GetFullPath(Required("--progress")),
                Path.GetFullPath(Required("--cancel")),
                sessionId,
                Required("--benchmark-pipe"),
                benchmarkToken);
        }
    }
}

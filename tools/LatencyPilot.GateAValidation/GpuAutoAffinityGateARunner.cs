using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.Etw;
using LatencyPilot.Platform.Windows.System;

namespace LatencyPilot.GateAValidation;

internal static class GpuAutoAffinityGateARunner
{
    internal const string ModeFlag = "--auto-affinity";
    private const string MutationConfirmationFlag = "--confirm-physical-mutation";
    private const string DirtyDevelopmentSourceFlag = "--allow-dirty-development-source";
    private static readonly Guid DisplayDeviceClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    internal static async Task<int> RunAsync(string[] args)
    {
        AutoOptions? options = null;
        GpuOptimizationSourceAssessment? sourceAssessment = null;
        GpuBenchmarkControlClient? benchmark = null;
        GpuAutoAffinityGateABackend? rawBackend = null;
        GpuInterruptAffinitySnapshot? preMutationOriginalState = null;
        GpuGateAProgressFile? progress = null;
        CancellationTokenSource? sessionCancellation = null;
        CancellationTokenSource? watcherShutdown = null;
        Task? cancelWatcher = null;
        var stage = "argument parsing";
        try
        {
            options = AutoOptions.Parse(args);
            stage = "administrator and source preflight";
            EnsureAdministrator();
            sourceAssessment = await VerifySourceAsync(options).ConfigureAwait(false);

            stage = "mutation-journal and GPU-target preflight";
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
            stage = "Gate A progress initialization";
            await progress.ReportInitializingAsync(
                sourceAssessment.State == GpuOptimizationSourceState.DevelopmentOnly
                    ? "Preparing development-only GPU affinity validation. This run cannot close physical Gate A."
                    : "Preparing the benchmark-backed GPU affinity session.").ConfigureAwait(false);

            sessionCancellation = new CancellationTokenSource();
            watcherShutdown = new CancellationTokenSource();
            stage = "benchmark control handshake";
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
            stage = "Gate A backend initialization";
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

            stage = "GPU candidate search and final verification";
            var reportingBackend = new ProgressReportingGpuAutoAffinityBackend(rawBackend, progress);
            var shuffleSeed = RandomNumberGenerator.GetInt32(int.MaxValue);
            var request = new GpuAutoAffinitySessionRequest(
                options.SessionId,
                topology,
                pressure,
                cpuSets,
                shuffleSeed,
                TimeSpan.FromSeconds(30));

            Console.WriteLine($"session={options.SessionId:D}");
            Console.WriteLine($"source-revision={options.ExpectedCommit}");
            Console.WriteLine($"source-state={sourceAssessment.State}");
            Console.WriteLine($"gate-a-closure-eligible={sourceAssessment.IsClosureEligible}");
            Console.WriteLine($"benchmark-pid={benchmark.ProcessId.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine($"device={target[0].InstanceId}");
            Console.WriteLine($"physical-cores={topology.PhysicalCoreCount.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine($"candidate-count={progressPlan.PhysicalCandidateCount.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine($"shuffle-seed={shuffleSeed.ToString(CultureInfo.InvariantCulture)}");
            Console.WriteLine("gpu-auto-affinity=running");

            var session = new GpuAutoAffinitySession(reportingBackend, reportingBackend);
            var result = await session.RunAsync(request, sessionCancellation.Token).ConfigureAwait(false);
            stage = "final stop and report verification";
            await TryStopBenchmarkAsync(benchmark).ConfigureAwait(false);

            UsbAffinityRecommendationReport? usbRecommendation = null;
            if (result.Recommendation == GpuOptimizationRecommendation.KeepCandidate &&
                result.Finalist is not null)
            {
                stage = "post-GPU USB recommendation";
                usbRecommendation = CaptureUsbRecommendation(
                    topology,
                    result.Finalist.Processor,
                    sessionCancellation.Token);
                Console.WriteLine(
                    $"usb-recommendation={usbRecommendation.Status};" +
                    $"controller={usbRecommendation.ControllerInstanceId ?? "unavailable"};" +
                    $"processor={usbRecommendation.Processor?.ToString() ?? "unavailable"}");
                stage = "final stop and report verification";
            }

            var unresolvedAfter = MutationJournalReadOnlyInspector.GetUnresolved(
                MutationJournal.GetDefaultDatabasePath());
            var finalSourceAssessment = await ReadFinalSourceAssessmentAsync(options).ConfigureAwait(false);
            var closureEligible = sourceAssessment.IsClosureEligible && finalSourceAssessment.IsClosureEligible;
            var effectiveSourceState = DetermineEffectiveSourceState(sourceAssessment, finalSourceAssessment);
            var completedReport = rawBackend.CompleteReport(result.Report, unresolvedAfter.Count);
            IReadOnlyList<string> reportReasons = closureEligible
                ? completedReport.Reasons
                : [
                    .. completedReport.Reasons,
                    BuildSourceEligibilityReason(sourceAssessment, finalSourceAssessment),
                ];
            var finalReport = completedReport with
            {
                UsbRecommendation = usbRecommendation,
                SourceState = effectiveSourceState.ToString(),
                GateAClosureEligible = closureEligible,
                Reasons = reportReasons,
            };
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
                closureEligible
                    ? result.Recommendation == GpuOptimizationRecommendation.KeepCandidate
                        ? "GPU auto-affinity finished with a verified finalist candidate and evidence-ready source state."
                        : $"GPU auto-affinity finished with the verified original state. {string.Join(" ", finalReport.Reasons.Take(2))}"
                    : "GPU auto-affinity finished safely as non-closure evidence. It cannot close physical Gate A.").ConfigureAwait(false);
            Console.WriteLine($"recommendation={result.Recommendation}");
            Console.WriteLine($"final-processor={result.Finalist?.Processor.ToString() ?? "original"}");
            Console.WriteLine($"source-state={finalReport.SourceState}");
            Console.WriteLine($"gate-a-closure-eligible={finalReport.GateAClosureEligible}");
            Console.WriteLine("unresolved=0");
            Console.WriteLine($"report={options.OutputPath}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            await TryStopBenchmarkAsync(benchmark).ConfigureAwait(false);
            var safe = await VerifyStoppedStateAsync(rawBackend, preMutationOriginalState).ConfigureAwait(false);

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
                    Provenance: rawBackend?.ReportProvenance)
                {
                    SourceState = sourceAssessment?.State.ToString() ?? GpuOptimizationSourceState.Blocked.ToString(),
                    GateAClosureEligible = false,
                };
                stoppedReport = TryCompleteReport(rawBackend, stoppedReport);
                safe = IsVerifiedOriginalTerminalState(stoppedReport);
                await TryWriteTerminalReportAsync(options.OutputPath, stoppedReport).ConfigureAwait(false);
            }

            if (progress is not null)
            {
                await TryReportTerminalAsync(
                    progress,
                    "Stopped safely",
                    null,
                    safe,
                    safe
                        ? "Stopped safely. The exact original GPU affinity state is verified and no unresolved mutation remains."
                        : "Stop completed, but final machine state could not be verified automatically. Inspect recovery evidence before continuing.").ConfigureAwait(false);
            }

            Console.WriteLine(safe ? "gpu-auto-affinity=stopped-safely" : "gpu-auto-affinity=manual-recovery-required");
            return safe ? 3 : 4;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Gate A failed during {stage}: {exception}");
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
                    [$"Gate A failed during {stage}: {exception}"],
                    Provenance: rawBackend?.ReportProvenance)
                {
                    SourceState = sourceAssessment?.State.ToString() ?? GpuOptimizationSourceState.Blocked.ToString(),
                    GateAClosureEligible = false,
                };
                fallback = TryCompleteReport(rawBackend, fallback);
                safe = IsVerifiedOriginalTerminalState(fallback);
                await TryWriteTerminalReportAsync(options.OutputPath, fallback).ConfigureAwait(false);
            }

            if (progress is not null)
            {
                await TryReportTerminalAsync(
                    progress,
                    "Failed safely",
                    null,
                    safe,
                    safe
                        ? "The session failed, but the exact original state is verified and no unresolved mutation remains."
                        : "The session failed and automatic recovery is not fully verified.").ConfigureAwait(false);
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

    private static UsbAffinityRecommendationReport CaptureUsbRecommendation(
        ProcessorTopologySnapshot topology,
        LogicalProcessorId gpuWinner,
        CancellationToken cancellationToken)
    {
        try
        {
            var capture = KernelLatencyCapture.Capture(
                new KernelLatencyCaptureOptions(TimeSpan.FromSeconds(10), 500_000),
                cancellationToken);
            var routes = InputDeviceRouteReader.Capture();
            var recommendation = UsbAffinityRecommendationPlanner.Create(
                topology,
                capture,
                routes,
                gpuWinner);
            var evidence = recommendation.CpuEvidence;
            return new UsbAffinityRecommendationReport(
                recommendation.Status.ToString(),
                recommendation.ControllerInstanceId,
                recommendation.Processor,
                recommendation.InputDeviceInstanceIds,
                evidence?.TotalInterruptDurationMicroseconds,
                evidence?.InterruptTailP99Microseconds,
                evidence?.DpcCount,
                evidence?.IsrCount,
                recommendation.Reason);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            Win32Exception or
            IOException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            UnauthorizedAccessException or
            System.Security.SecurityException)
        {
            return new UsbAffinityRecommendationReport(
                UsbAffinityRecommendationStatus.NotReady.ToString(),
                null,
                null,
                [],
                null,
                null,
                null,
                null,
                $"Post-GPU USB/xHCI recommendation is unavailable: {exception.GetType().Name}: {exception.Message}");
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

    private static bool IsVerifiedOriginalTerminalState(GpuAutoAffinityReport report) =>
        report.FinalStateVerified &&
        report.OriginalStateRestored &&
        string.Equals(
            report.FinalRecommendation,
            GpuOptimizationRecommendation.RestoreOriginal.ToString(),
            StringComparison.Ordinal) &&
        report.FinalProcessor is null &&
        !string.Equals(report.RecoveryStatus, "final-state-capture-failed", StringComparison.Ordinal);

    private static async Task TryWriteTerminalReportAsync(
        string outputPath,
        GpuAutoAffinityReport report)
    {
        try
        {
            await WriteReportAsync(outputPath, report).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException)
        {
            Console.Error.WriteLine($"Unable to write Gate A terminal report: {exception.Message}");
        }
    }

    private static async Task TryReportTerminalAsync(
        GpuGateAProgressFile progress,
        string recommendation,
        GpuAffinityCandidate? candidate,
        bool finalStateVerified,
        string message)
    {
        try
        {
            await progress.ReportTerminalAsync(
                recommendation,
                candidate,
                finalStateVerified,
                message).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException)
        {
            Console.Error.WriteLine($"Unable to write Gate A terminal progress: {exception.Message}");
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

    private static async Task<GpuOptimizationSourceAssessment> VerifySourceAsync(AutoOptions options)
    {
        var assessment = await ReadSourceAssessmentAsync(options).ConfigureAwait(false);
        if (!assessment.CanRun)
        {
            throw new InvalidOperationException(assessment.Reason);
        }
        if (assessment.State == GpuOptimizationSourceState.DevelopmentOnly && !options.AllowDirtyDevelopmentSource)
        {
            throw new InvalidOperationException(
                $"{assessment.Reason} Pass {DirtyDevelopmentSourceFlag} only for an explicitly development-only owner run.");
        }

        return assessment;
    }

    private static async Task<GpuOptimizationSourceAssessment> ReadSourceAssessmentAsync(AutoOptions options)
    {
        var head = (await RunGitAsync(options.RepositoryRoot, "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
        var branch = (await RunGitAsync(options.RepositoryRoot, "branch", "--show-current").ConfigureAwait(false)).Trim();
        var status = await RunGitAsync(options.RepositoryRoot, "status", "--porcelain").ConfigureAwait(false);
        return GpuOptimizationSourceRevisionPolicy.Assess(
            head,
            branch,
            !string.IsNullOrWhiteSpace(status),
            options.ExpectedCommit);
    }

    private static async Task<GpuOptimizationSourceAssessment> ReadFinalSourceAssessmentAsync(AutoOptions options)
    {
        try
        {
            return await ReadSourceAssessmentAsync(options).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            Win32Exception)
        {
            return new GpuOptimizationSourceAssessment(
                GpuOptimizationSourceState.Blocked,
                string.Empty,
                string.Empty,
                false,
                $"Final source state could not be verified: {exception.Message}");
        }
    }

    private static GpuOptimizationSourceState DetermineEffectiveSourceState(
        GpuOptimizationSourceAssessment initial,
        GpuOptimizationSourceAssessment final)
    {
        if (initial.IsClosureEligible && final.IsClosureEligible)
        {
            return GpuOptimizationSourceState.EvidenceReady;
        }
        if (initial.State == GpuOptimizationSourceState.Blocked || final.State == GpuOptimizationSourceState.Blocked)
        {
            return GpuOptimizationSourceState.Blocked;
        }
        return GpuOptimizationSourceState.DevelopmentOnly;
    }

    private static string BuildSourceEligibilityReason(
        GpuOptimizationSourceAssessment initial,
        GpuOptimizationSourceAssessment final)
    {
        if (initial.State == GpuOptimizationSourceState.DevelopmentOnly)
        {
            return "This report was produced from a dirty development checkout. Hardware apply/rollback evidence is preserved, but the report cannot close physical Gate A or arm product mutation.";
        }
        if (!final.IsClosureEligible)
        {
            return $"The source checkout changed or became unverifiable during Gate A. The run remains auditable, but it cannot close physical Gate A. Final source state: {final.State}. {final.Reason}";
        }
        return "This report is not eligible to close physical Gate A.";
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
        string BenchmarkToken,
        bool AllowDirtyDevelopmentSource)
    {
        internal static AutoOptions Parse(string[] args)
        {
            ArgumentNullException.ThrowIfNull(args);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var mode = false;
            var confirmation = false;
            var allowDirtyDevelopmentSource = false;
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
                if (string.Equals(token, DirtyDevelopmentSourceFlag, StringComparison.Ordinal))
                {
                    allowDirtyDevelopmentSource = true;
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
            if (!GpuOptimizationSourceRevisionPolicy.IsValidFullRevision(expectedCommit))
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
                benchmarkToken,
                allowDirtyDevelopmentSource);
        }
    }
}

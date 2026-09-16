using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Platform.Windows.System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private static readonly JsonSerializerOptions GateAJsonOptions = new(JsonSerializerDefaults.Web);
    private Button? _gateAValidationButton;
    private string? _gateARepositoryRoot;

    internal void InitializeGateAValidationExperience()
    {
        if (_gateAValidationButton is not null)
        {
            return;
        }

        _gateARepositoryRoot = TryFindRepositoryRoot();
        if (!IsDevelopmentGateAAvailable(_gateARepositoryRoot))
        {
            return;
        }

        _gateAValidationButton = new Button
        {
            Content = "Run GPU Gate A",
            MinHeight = 34,
            Padding = new Thickness(12, 6, 12, 6),
            Style = (Style)Application.Current.Resources["QuietButtonStyle"],
        };
        AutomationProperties.SetName(_gateAValidationButton, "Run GPU Gate A development validation");
        AutomationProperties.SetHelpText(
            _gateAValidationButton,
            "Development-only owner validation. Launches the deterministic Direct3D 12 benchmark as a normal-user process, asks for administrator consent once, screens eligible physical cores, refines SMT siblings, confirms the finalist with direct ISR placement evidence, and preserves journal-owned rollback/recovery.");
        ToolTipService.SetToolTip(
            _gateAValidationButton,
            "Development-only physical GPU affinity validation.");
        _gateAValidationButton.Click += GateAValidationButton_Click;
        DeveloperValidationCard.Visibility = Visibility.Visible;
        DeveloperValidationHost.Children.Add(_gateAValidationButton);
    }

    internal static bool IsDevelopmentGateAAvailable(string? repositoryRoot) =>
        !string.IsNullOrWhiteSpace(repositoryRoot);

    private async void GateAValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_gateAValidationButton is null || _gateARepositoryRoot is null || _measurementBusy)
        {
            return;
        }

        Process? benchmarkProcess = null;
        Task<string>? benchmarkStdoutTask = null;
        Task<string>? benchmarkStderrTask = null;
        GpuOptimizationProgressWindow? progressWindow = null;
        var mainMinimized = false;
        _gateAValidationButton.IsEnabled = false;

        try
        {
            var sourceRevision = await ReadCleanSourceRevisionAsync(_gateARepositoryRoot);
            var topology = ProcessorTopologyReader.Capture();
            if (topology.ProcessorGroupCount != 1 || topology.PhysicalCoreCount <= 0)
            {
                throw new NotSupportedException(
                    "GPU Gate A v1 requires one Windows processor group with at least one physical core.");
            }

            var workerCount = Math.Min(
                topology.PhysicalCoreCount,
                GpuAffinityCandidatePlanner.MaximumCandidates);
            var sessionId = Guid.NewGuid();
            var benchmarkToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var benchmarkPipe = $"LatencyPilot.GpuBenchmark.{sessionId:N}";
            var benchmarkSeed = RandomNumberGenerator.GetInt32(int.MaxValue);
            var validationDirectory = GetValidationDirectory();
            var stamp = DateTimeOffset.UtcNow.ToString(
                "yyyyMMddTHHmmssfffZ",
                CultureInfo.InvariantCulture);
            var sessionDirectory = Path.Combine(
                validationDirectory,
                $"gpu-auto-affinity-{stamp}-{sessionId:N}");
            var benchmarkOutputDirectory = Path.Combine(sessionDirectory, "benchmark");
            var reportPath = Path.Combine(sessionDirectory, "gpu-auto-affinity-report.json");
            var progressPath = Path.Combine(sessionDirectory, "progress.json");
            var cancelPath = Path.Combine(sessionDirectory, "stop.request");
            Directory.CreateDirectory(benchmarkOutputDirectory);

            var dotnetExecutable = ResolveDotnetExecutable();
            var benchmarkProject = Path.Combine(
                _gateARepositoryRoot,
                "src",
                "LatencyPilot.GpuBenchmark",
                "LatencyPilot.GpuBenchmark.csproj");
            var helperProject = Path.Combine(
                _gateARepositoryRoot,
                "tools",
                "LatencyPilot.GateAValidation",
                "LatencyPilot.GateAValidation.csproj");
            if (!File.Exists(benchmarkProject) || !File.Exists(helperProject))
            {
                throw new FileNotFoundException(
                    "The GPU benchmark or Gate A validation project was not found in this development checkout.");
            }

            EvidenceExportStatusText.Text =
                "GPU Gate A is starting the deterministic normal-user benchmark. A compact progress window will remain available while the main window is minimized.";

            var benchmarkStartInfo = new ProcessStartInfo
            {
                FileName = dotnetExecutable,
                WorkingDirectory = _gateARepositoryRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var argument in new[]
                     {
                         "run",
                         "--project", benchmarkProject,
                         "--configuration", "Release",
                         "--",
                         "--session-id", sessionId.ToString("D"),
                         "--width", "1280",
                         "--height", "720",
                         "--worker-count", workerCount.ToString(CultureInfo.InvariantCulture),
                         "--seed", benchmarkSeed.ToString(CultureInfo.InvariantCulture),
                         "--control-pipe", benchmarkPipe,
                         "--control-token", benchmarkToken,
                         "--output-directory", benchmarkOutputDirectory,
                     })
            {
                benchmarkStartInfo.ArgumentList.Add(argument);
            }

            benchmarkProcess = Process.Start(benchmarkStartInfo)
                ?? throw new InvalidOperationException("The normal-user GPU benchmark process could not be started.");
            benchmarkStdoutTask = benchmarkProcess.StandardOutput.ReadToEndAsync();
            benchmarkStderrTask = benchmarkProcess.StandardError.ReadToEndAsync();

            progressWindow = new GpuOptimizationProgressWindow(
                sessionId,
                progressPath,
                cancelPath,
                RootGrid.ActualTheme);
            progressWindow.StartMonitoring();
            progressWindow.Activate();

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Minimize();
                mainMinimized = true;
            }

            var helperStartInfo = new ProcessStartInfo
            {
                FileName = dotnetExecutable,
                WorkingDirectory = _gateARepositoryRoot,
                UseShellExecute = true,
                Verb = "runas",
            };
            foreach (var argument in new[]
                     {
                         "run",
                         "--project", helperProject,
                         "--configuration", "Release",
                         "--",
                         "--auto-affinity",
                         "--repo-root", _gateARepositoryRoot,
                         "--expected-commit", sourceRevision,
                         "--output", reportPath,
                         "--progress", progressPath,
                         "--cancel", cancelPath,
                         "--session-id", sessionId.ToString("D"),
                         "--benchmark-pipe", benchmarkPipe,
                         "--benchmark-token", benchmarkToken,
                         "--confirm-physical-mutation",
                     })
            {
                helperStartInfo.ArgumentList.Add(argument);
            }

            using var helper = Process.Start(helperStartInfo)
                ?? throw new InvalidOperationException("The elevated GPU Gate A helper could not be started.");
            await helper.WaitForExitAsync();
            await progressWindow.StopMonitoringAsync();

            await EnsureBenchmarkExitedAsync(benchmarkProcess);
            var benchmarkStandardError = benchmarkStderrTask is null
                ? string.Empty
                : await benchmarkStderrTask;
            _ = benchmarkStdoutTask is null ? string.Empty : await benchmarkStdoutTask;

            if (!File.Exists(reportPath))
            {
                var benchmarkDetails = string.IsNullOrWhiteSpace(benchmarkStandardError)
                    ? string.Empty
                    : $" Benchmark: {benchmarkStandardError.Trim()}";
                throw new InvalidOperationException(
                    $"GPU Gate A helper exited with code {helper.ExitCode}, but no report was produced.{benchmarkDetails}");
            }

            var report = JsonSerializer.Deserialize<GpuAutoAffinityReport>(
                    await File.ReadAllTextAsync(reportPath),
                    GateAJsonOptions)
                ?? throw new InvalidDataException("GPU Gate A report is empty or invalid.");
            if (!string.Equals(report.Schema, GpuAutoAffinityReport.SchemaId, StringComparison.Ordinal) ||
                report.SessionId != sessionId)
            {
                throw new InvalidDataException("GPU Gate A report schema or session identity does not match the owner run.");
            }

            var terminalSummary = BuildGateATerminalSummary(helper.ExitCode, report);
            var terminalStateVerified = IsGateATerminalStateVerified(helper.ExitCode, report);
            progressWindow.ShowFinalOutcome(terminalSummary, reportPath, terminalStateVerified);
            EvidenceExportStatusText.Text = $"{terminalSummary} Report: {reportPath}";
            TryRevealReport(reportPath);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            if (progressWindow is not null)
            {
                await progressWindow.StopMonitoringAsync();
                progressWindow.ShowStartupFailure(
                    "GPU Gate A was cancelled at the UAC prompt. The normal-user benchmark will be stopped; no privileged mutation helper was started.");
            }
            EvidenceExportStatusText.Text =
                "GPU Gate A was cancelled at the UAC prompt. No privileged mutation helper was started.";
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            JsonException or
            Win32Exception)
        {
            Logger.Error(exception, "Development GPU Gate A validation failed before a complete verified result could be presented.");
            if (progressWindow is not null)
            {
                await progressWindow.StopMonitoringAsync();
                progressWindow.ShowStartupFailure(
                    $"GPU Gate A could not complete: {exception.Message}");
            }
            EvidenceExportStatusText.Text = $"GPU Gate A could not complete: {exception.Message}";
        }
        finally
        {
            if (benchmarkProcess is not null)
            {
                try
                {
                    if (!benchmarkProcess.HasExited)
                    {
                        benchmarkProcess.Kill(entireProcessTree: true);
                        await benchmarkProcess.WaitForExitAsync();
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
                {
                    Logger.Warning(exception, "The owner benchmark process could not be terminalized cleanly after Gate A.");
                }
                benchmarkProcess.Dispose();
            }

            if (progressWindow is not null)
            {
                await progressWindow.StopMonitoringAsync();
            }

            if (mainMinimized && AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Restore(activateWindow: false);
            }

            _gateAValidationButton.IsEnabled = true;
        }
    }

    private static async Task<string> ReadCleanSourceRevisionAsync(string repositoryRoot)
    {
        var head = (await RunGitAsync(repositoryRoot, "rev-parse", "HEAD")).Trim();
        var branch = (await RunGitAsync(repositoryRoot, "branch", "--show-current")).Trim();
        var status = await RunGitAsync(repositoryRoot, "status", "--porcelain");
        if (head.Length != 40 || !head.All(Uri.IsHexDigit) ||
            !string.Equals(branch, "main", StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(status))
        {
            throw new InvalidOperationException(
                "GPU Gate A requires a clean main checkout at one exact 40-character source revision.");
        }

        return head.ToLowerInvariant();
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
            ?? throw new InvalidOperationException("Could not start git for GPU Gate A source verification.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {stderr.Trim()}");
        }

        return stdout;
    }

    private static async Task EnsureBenchmarkExitedAsync(Process benchmarkProcess)
    {
        if (benchmarkProcess.HasExited)
        {
            return;
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            await benchmarkProcess.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            benchmarkProcess.Kill(entireProcessTree: true);
            await benchmarkProcess.WaitForExitAsync();
        }
    }

    private static bool IsGateATerminalStateVerified(int exitCode, GpuAutoAffinityReport report) =>
        exitCode switch
        {
            0 => report.FinalStateVerified,
            1 or 3 => report.FinalStateVerified && report.OriginalStateRestored,
            _ => false,
        };

    private static string BuildGateATerminalSummary(int exitCode, GpuAutoAffinityReport report)
    {
        var detail = BuildFinalSummary(report);
        return exitCode switch
        {
            0 when report.FinalStateVerified =>
                $"GPU Gate A finished with verified final state. {detail}",
            1 when report.FinalStateVerified && report.OriginalStateRestored =>
                $"GPU Gate A failed safely. The exact original GPU affinity state is verified and no unresolved mutation remains. {detail}",
            3 when report.FinalStateVerified && report.OriginalStateRestored =>
                $"GPU Gate A stopped safely. The exact original GPU affinity state is verified and no unresolved mutation remains. {detail}",
            4 =>
                $"GPU Gate A requires recovery attention. The helper could not verify a safe final machine state. {detail}",
            0 =>
                $"GPU Gate A returned success, but the final machine state is not verified. Recovery attention is required. {detail}",
            1 =>
                $"GPU Gate A failed and exact restoration could not be verified. Recovery attention is required. {detail}",
            3 =>
                $"GPU Gate A stop completed without verified exact restoration. Recovery attention is required. {detail}",
            _ =>
                $"GPU Gate A ended with unexpected helper exit code {exitCode.ToString(CultureInfo.InvariantCulture)}. Recovery status must be inspected before continuing. {detail}",
        };
    }

    private static string BuildFinalSummary(GpuAutoAffinityReport report)
    {
        var confirmation = report.Candidates.LastOrDefault(static candidate =>
            string.Equals(candidate.Phase, "confirmation", StringComparison.Ordinal));
        var headline = string.Equals(
                report.FinalRecommendation,
                "KeepCandidate",
                StringComparison.Ordinal)
            ? $"Verified finalist: CPU {report.FinalProcessor?.Number.ToString(CultureInfo.InvariantCulture) ?? "—"}."
            : report.OriginalStateRestored
                ? "Original GPU affinity state is verified/restored."
                : $"Final recommendation: {report.FinalRecommendation}.";

        var relative = confirmation?.RelativeFrameP99Improvement is { } improvement && double.IsFinite(improvement)
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" Canonical frame-p99 improvement: {improvement * 100d:F2}%.")
            : string.Empty;

        var originalP99 = AverageTrialMetric(report, "Original", static trial => trial.FrameP99Milliseconds);
        var candidateP99 = AverageTrialMetric(report, "Candidate", static trial => trial.FrameP99Milliseconds);
        var rawP99 = originalP99 is { } original && candidateP99 is { } candidate
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" Confirmation trial p99 average: {original:F2} ms → {candidate:F2} ms (Δ {candidate - original:+0.00;-0.00;0.00} ms).")
            : string.Empty;

        var originalLow = AverageTrialMetric(report, "Original", static trial => trial.OnePercentLowFps);
        var candidateLow = AverageTrialMetric(report, "Candidate", static trial => trial.OnePercentLowFps);
        var rawLow = originalLow is { } originalFps && candidateLow is { } candidateFps
            ? string.Create(
                CultureInfo.InvariantCulture,
                $" Confirmation 1% low average: {originalFps:F1} → {candidateFps:F1} FPS (Δ {candidateFps - originalFps:+0.0;-0.0;0.0}).")
            : string.Empty;

        return $"{headline}{relative}{rawP99}{rawLow}";
    }

    private static double? AverageTrialMetric(
        GpuAutoAffinityReport report,
        string role,
        Func<GpuAutoAffinityTrialReport, double?> selector)
    {
        var values = report.Trials
            .Where(trial =>
                string.Equals(trial.Phase, "confirmation", StringComparison.Ordinal) &&
                string.Equals(trial.Role, role, StringComparison.Ordinal))
            .Select(selector)
            .Where(static value => value is { } item && double.IsFinite(item))
            .Select(static value => value!.Value)
            .ToArray();
        return values.Length == 0 ? null : values.Average();
    }

    private static string GetValidationDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            documents = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents");
        }

        return Path.Combine(documents, "LatencyPilot", "validation");
    }

    private static string? TryFindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx")) &&
                (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                 File.Exists(Path.Combine(directory.FullName, ".git"))))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string ResolveDotnetExecutable()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var rootedCandidate = Path.Combine(dotnetRoot.Trim(), "dotnet.exe");
            if (File.Exists(rootedCandidate))
            {
                return Path.GetFullPath(rootedCandidate);
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            var defaultCandidate = Path.Combine(programFiles, "dotnet", "dotnet.exe");
            if (File.Exists(defaultCandidate))
            {
                return Path.GetFullPath(defaultCandidate);
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var segment in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalizedSegment = segment.Trim('"');
                if (string.IsNullOrWhiteSpace(normalizedSegment))
                {
                    continue;
                }

                var pathCandidate = Path.Combine(normalizedSegment, "dotnet.exe");
                if (File.Exists(pathCandidate))
                {
                    return Path.GetFullPath(pathCandidate);
                }
            }
        }

        throw new FileNotFoundException(
            "dotnet.exe could not be resolved for the benchmark/Gate A helper. Install the .NET 10 SDK or ensure DOTNET_ROOT/PATH points to it.");
    }

    private static void TryRevealReport(string reportPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{reportPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            Logger.Warning(exception, "GPU Gate A report was written, but File Explorer could not reveal it.");
        }
    }
}

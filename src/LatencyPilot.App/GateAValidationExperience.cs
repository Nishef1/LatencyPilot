using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private static readonly JsonSerializerOptions GateAJsonOptions = new(JsonSerializerDefaults.Web);
    private Button? _gateAValidationButton;
    private Border? _gateAValidationStateBadge;
    private TextBlock? _gateAValidationStateText;
    private string? _gateARepositoryRoot;
    private GpuOptimizationSourceAssessment? _gateASourceAssessment;
    private bool _gateAValidationRunning;

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

        _gateAValidationStateText = new TextBlock
        {
            Text = "Checking source",
            Style = (Style)Application.Current.Resources["MetricLabelTextStyle"],
        };
        _gateAValidationStateBadge = new Border
        {
            Child = _gateAValidationStateText,
            Padding = new Thickness(10, 5, 10, 5),
            CornerRadius = new CornerRadius(999),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ApplyGateAStateBadgeBrushes("SemanticAttentionBrush", "SemanticAttentionSoftBrush");

        _gateAValidationButton = new Button
        {
            Content = "Run GPU Gate A",
            MinHeight = 36,
            Padding = new Thickness(14, 7, 14, 7),
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
        };
        AutomationProperties.SetName(_gateAValidationButton, "Run GPU Gate A development validation");
        AutomationProperties.SetHelpText(
            _gateAValidationButton,
            "Runs deterministic physical GPU affinity validation with journal-owned rollback/recovery. Clean exact-source runs may contribute to Gate A closure; dirty main runs are development-only evidence.");
        ToolTipService.SetToolTip(
            _gateAValidationButton,
            "Checking source state before GPU affinity validation.");
        _gateAValidationButton.Click += GateAValidationButton_Click;
        DeveloperValidationHost.Spacing = 8;
        DeveloperValidationHost.Children.Add(_gateAValidationStateBadge);
        DeveloperValidationCard.Visibility = Visibility.Visible;
        DeveloperValidationHost.Children.Add(_gateAValidationButton);
        InitializeGateACpuScope();
        DeveloperValidationStatusText.Text = "Checking Git source state…";
        _ = RefreshGateASourceAssessmentAsync();
    }

    internal static bool IsDevelopmentGateAAvailable(string? repositoryRoot) =>
        !string.IsNullOrWhiteSpace(repositoryRoot);

    private async Task RefreshGateASourceAssessmentAsync()
    {
        if (_gateARepositoryRoot is null)
        {
            return;
        }

        try
        {
            var assessment = await ReadGateASourceAssessmentAsync(_gateARepositoryRoot);
            _gateASourceAssessment = assessment;
            ApplyGateASourceAssessmentUi(assessment);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            Win32Exception)
        {
            Logger.Warning(exception, "GPU Gate A source state could not be refreshed.");
            _gateASourceAssessment = new GpuOptimizationSourceAssessment(
                GpuOptimizationSourceState.Blocked,
                string.Empty,
                string.Empty,
                false,
                $"Source state could not be read: {exception.Message}");
            ApplyGateASourceAssessmentUi(_gateASourceAssessment);
        }
    }

    private void ApplyGateASourceAssessmentUi(GpuOptimizationSourceAssessment assessment)
    {
        if (_gateAValidationButton is null ||
            _gateAValidationStateText is null ||
            _gateAValidationStateBadge is null)
        {
            return;
        }

        switch (assessment.State)
        {
            case GpuOptimizationSourceState.EvidenceReady:
                _gateAValidationStateText.Text = "Evidence-ready";
                ApplyGateAStateBadgeBrushes("SemanticGoodBrush", "SemanticGoodSoftBrush");
                _gateAValidationButton.Content = "Run Gate A";
                _gateAValidationButton.IsEnabled = !_measurementBusy && !_gateAValidationRunning;
                AutomationProperties.SetName(_gateAValidationButton, "Run evidence-ready GPU Gate A validation");
                ToolTipService.SetToolTip(
                    _gateAValidationButton,
                    "Clean main checkout at an exact revision. A successful run may contribute to physical Gate A closure.");
                SetGateAValidationStatus(
                    "Clean main checkout detected. This run can produce evidence eligible for physical Gate A closure.",
                    syncEvidenceStatus: false);
                break;

            case GpuOptimizationSourceState.DevelopmentOnly:
                _gateAValidationStateText.Text = "Development only";
                ApplyGateAStateBadgeBrushes("SemanticAttentionBrush", "SemanticAttentionSoftBrush");
                _gateAValidationButton.Content = "Run development validation";
                _gateAValidationButton.IsEnabled = !_measurementBusy && !_gateAValidationRunning;
                AutomationProperties.SetName(_gateAValidationButton, "Run development-only GPU affinity validation");
                ToolTipService.SetToolTip(
                    _gateAValidationButton,
                    "Local changes are present. The run may test hardware behavior and rollback, but it cannot close physical Gate A.");
                SetGateAValidationStatus(
                    "Local changes detected. You can test apply/rollback and hardware behavior, but this run cannot close physical Gate A or arm product mutation.",
                    syncEvidenceStatus: false);
                break;

            default:
                _gateAValidationStateText.Text = "Blocked";
                ApplyGateAStateBadgeBrushes("SemanticFailureBrush", "SemanticFailureSoftBrush");
                _gateAValidationButton.Content = "Source state blocked";
                _gateAValidationButton.IsEnabled = false;
                AutomationProperties.SetName(_gateAValidationButton, "GPU Gate A blocked by source state");
                ToolTipService.SetToolTip(_gateAValidationButton, assessment.Reason);
                SetGateAValidationStatus(assessment.Reason, syncEvidenceStatus: false);
                break;
        }
        UpdateGateAScopeUi();
    }

    private void ApplyGateAStateBadgeBrushes(string foregroundResourceKey, string backgroundResourceKey)
    {
        if (_gateAValidationStateText is null || _gateAValidationStateBadge is null)
        {
            return;
        }

        var foreground = (Brush)Application.Current.Resources[foregroundResourceKey];
        var background = (Brush)Application.Current.Resources[backgroundResourceKey];
        _gateAValidationStateText.Foreground = foreground;
        _gateAValidationStateBadge.Background = background;
        _gateAValidationStateBadge.BorderBrush = foreground;
    }

    private async void GateAValidationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_gateAValidationButton is null || _gateARepositoryRoot is null)
        {
            return;
        }

        if (_measurementBusy || _gateAValidationRunning)
        {
            SetGateAValidationStatus("GPU Gate A is unavailable while another measurement is running.");
            return;
        }

        Process? benchmarkProcess = null;
        Task<string>? benchmarkStdoutTask = null;
        Task<string>? benchmarkStderrTask = null;
        GpuOptimizationProgressWindow? progressWindow = null;
        var mainMinimized = false;
        SetGateAValidationBusy(true);
        _gateAValidationButton.IsEnabled = false;

        try
        {
            SetGateAValidationStatus("Checking live source state before starting GPU affinity validation.");
            var sourceAssessment = await ReadGateASourceAssessmentAsync(_gateARepositoryRoot);
            _gateASourceAssessment = sourceAssessment;
            ApplyGateASourceAssessmentUi(sourceAssessment);
            if (!sourceAssessment.CanRun)
            {
                throw new InvalidOperationException(sourceAssessment.Reason);
            }

            var sourceRevision = sourceAssessment.HeadRevision;
            var topology = ProcessorTopologyReader.Capture();
            if (topology.ProcessorGroupCount != 1 || topology.PhysicalCoreCount <= 0)
            {
                throw new NotSupportedException(
                    "GPU Gate A v1 requires one Windows processor group with at least one physical core.");
            }

            var workerCount = Math.Min(
                topology.PhysicalCoreCount,
                GpuAffinityCandidatePlanner.MaximumCandidates);
            // Revalidate an ephemeral selection against fresh topology before
            // launching the subject; the elevated helper repeats this check.
            _ = GpuAutoAffinityProgressPlan.Create(
                topology, [], ProcessorCpuSetReader.Capture(), _gateASearchScope, _gateASelectedProcessors);
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

            SetGateAValidationStatus(
                sourceAssessment.State == GpuOptimizationSourceState.DevelopmentOnly
                    ? "Starting development-only GPU affinity validation. The result will be marked ineligible for physical Gate A closure."
                    : "Starting evidence-ready GPU Gate A validation. A compact progress window will remain available while the main window is minimized.");

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
            var helperArguments = new List<string>
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
            };
            if (sourceAssessment.State == GpuOptimizationSourceState.DevelopmentOnly)
            {
                helperArguments.Add("--allow-dirty-development-source");
            }
            if (_gateASearchScope == GpuAutoAffinitySearchScope.Custom)
            {
                helperArguments.Add("--candidate-cpus");
                helperArguments.Add(string.Join(",", _gateASelectedProcessors.Select(static processor => processor.Number.ToString(CultureInfo.InvariantCulture))));
            }
            else if (_gateASearchScope == GpuAutoAffinitySearchScope.OriginalDiagnostics)
            {
                helperArguments.Add("--diagnose-original");
            }
            foreach (var argument in helperArguments)
            {
                helperStartInfo.ArgumentList.Add(argument);
            }

            using var helper = Process.Start(helperStartInfo)
                ?? throw new InvalidOperationException("The elevated GPU Gate A helper could not be started.");
            var helperExitCode = await WaitForGateAHelperAsync(
                helper,
                reportPath,
                progressPath,
                sessionId);
            await progressWindow.StopMonitoringAsync();

            await EnsureBenchmarkExitedAsync(benchmarkProcess);
            var benchmarkStandardOutput = benchmarkStdoutTask is null
                ? string.Empty
                : await benchmarkStdoutTask;
            var benchmarkStandardError = benchmarkStderrTask is null
                ? string.Empty
                : await benchmarkStderrTask;
            Logger.Information(
                "GPU Gate A benchmark process {BenchmarkProcessId} exited with code {BenchmarkExitCode}. stdout={BenchmarkStandardOutput} stderr={BenchmarkStandardError}",
                benchmarkProcess.Id,
                benchmarkProcess.ExitCode,
                TruncateProcessOutput(benchmarkStandardOutput),
                TruncateProcessOutput(benchmarkStandardError));

            if (!File.Exists(reportPath))
            {
                var benchmarkDetails = string.IsNullOrWhiteSpace(benchmarkStandardError)
                    ? string.Empty
                    : $" Benchmark: {benchmarkStandardError.Trim()}";
                throw new InvalidOperationException(
                    $"GPU Gate A helper exited with code {helperExitCode}, but no report was produced.{benchmarkDetails}");
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
            if (sourceAssessment.State == GpuOptimizationSourceState.DevelopmentOnly && report.GateAClosureEligible)
            {
                throw new InvalidDataException(
                    "A development-only Gate A run was incorrectly marked closure-eligible. The report is rejected fail-closed.");
            }
            if (report.SearchScope != _gateASearchScope ||
                (_gateASearchScope != GpuAutoAffinitySearchScope.Full &&
                 (report.GateAClosureEligible || report.FinalProcessor is not null)))
            {
                throw new InvalidDataException("GPU result scope/Keep authority does not match the requested diagnostic scope.");
            }

            var evidenceBundle = await GateAEvidenceBundleExporter.TryCreateAsync(sessionDirectory);
            if (!evidenceBundle.Succeeded)
            {
                Logger.Warning(
                    "GPU Gate A completed with a valid report, but evidence packaging failed: {EvidenceBundleError}",
                    evidenceBundle.Error);
            }

            var resultPresentation = GateAResultPresentation.Create(
                report,
                sessionDirectory,
                reportPath,
                evidenceBundle);
            RenderGateAResult(resultPresentation);

            var terminalSummary = BuildGateATerminalSummary(helperExitCode, report);
            var terminalStateVerified = IsGateATerminalStateVerified(helperExitCode, report);
            progressWindow.ShowFinalOutcome(terminalSummary, reportPath, terminalStateVerified, report);
            SetGateAValidationStatus(
                evidenceBundle.Succeeded
                    ? $"{terminalSummary} Evidence ZIP: {evidenceBundle.ZipPath}"
                    : $"{terminalSummary} Evidence bundle could not be packaged: {evidenceBundle.Error}. Report: {reportPath}");

            if (mainMinimized && AppWindow.Presenter is OverlappedPresenter completionPresenter)
            {
                completionPresenter.Restore(activateWindow: true);
                mainMinimized = false;
            }
            Activate();
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            if (progressWindow is not null)
            {
                await progressWindow.StopMonitoringAsync();
                progressWindow.ShowStartupFailure(
                    "GPU Gate A was cancelled at the UAC prompt. The normal-user benchmark will be stopped; no privileged mutation helper was started.");
            }
            SetGateAValidationStatus(
                "GPU Gate A was cancelled at the UAC prompt. No privileged mutation helper was started.");
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
            SetGateAValidationStatus($"GPU Gate A could not complete: {exception.Message}");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Development GPU Gate A validation failed with an unexpected UI-bound error.");
            if (progressWindow is not null)
            {
                await progressWindow.StopMonitoringAsync();
                progressWindow.ShowStartupFailure(
                    $"GPU Gate A could not complete: {exception.Message}");
            }

            SetGateAValidationStatus($"GPU Gate A could not complete: {exception.Message}");
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

            SetGateAValidationBusy(false);
            _gateAValidationButton.IsEnabled = _gateASourceAssessment?.CanRun == true && !_measurementBusy;
        }
    }

    private void SetGateAValidationStatus(string message, bool syncEvidenceStatus = true)
    {
        DeveloperValidationStatusText.Text = message;
        if (syncEvidenceStatus)
        {
            EvidenceExportStatusText.Text = message;
        }
    }

    private void SetGateAValidationBusy(bool busy)
    {
        _gateAValidationRunning = busy;
        UpdateGateAScopeUi();
        SetObservationControlsBusy(_measurementBusy);
        UpdateScenarioSelectionEnabledState();

        UpdateMeasurementReadinessState();
    }

    private static async Task<GpuOptimizationSourceAssessment> ReadGateASourceAssessmentAsync(string repositoryRoot)
    {
        var head = (await RunGitAsync(repositoryRoot, "rev-parse", "HEAD")).Trim();
        var branch = (await RunGitAsync(repositoryRoot, "branch", "--show-current")).Trim();
        var status = await RunGitAsync(repositoryRoot, "status", "--porcelain");
        return GpuOptimizationSourceRevisionPolicy.Assess(
            head,
            branch,
            !string.IsNullOrWhiteSpace(status),
            head);
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

    private static async Task<int> WaitForGateAHelperAsync(
        Process helper,
        string reportPath,
        string progressPath,
        Guid sessionId)
    {
        while (!helper.HasExited)
        {
            var terminalSnapshot = await TryReadTerminalProgressAsync(
                reportPath,
                progressPath,
                sessionId);
            if (terminalSnapshot is not null)
            {
                Logger.Warning(
                    "GPU Gate A report and terminal progress are available while elevated helper process {HelperProcessId} is still running; terminalizing the orphaned wrapper.",
                    helper.Id);
                try
                {
                    helper.Kill(entireProcessTree: true);
                    await helper.WaitForExitAsync();
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
                {
                    Logger.Warning(
                        exception,
                        "The terminal GPU Gate A helper wrapper could not be stopped cleanly.");
                }

                return terminalSnapshot.Phase switch
                {
                    "complete" => 0,
                    "stopped-safely" => 3,
                    "failed-safely" => 1,
                    _ => 4,
                };
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return helper.ExitCode;
    }

    private static async Task<GpuOptimizationProgressSnapshot?> TryReadTerminalProgressAsync(
        string reportPath,
        string progressPath,
        Guid sessionId)
    {
        if (!File.Exists(reportPath) || !File.Exists(progressPath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                progressPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            var snapshot = await JsonSerializer.DeserializeAsync<GpuOptimizationProgressSnapshot>(
                stream,
                GateAJsonOptions);
            return snapshot is { IsTerminal: true } && snapshot.SessionId == sessionId
                ? snapshot
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
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
        var headline = string.Equals(report.FinalRecommendation, "KeepCandidate", StringComparison.Ordinal)
            ? $"Verified winner: CPU {report.FinalProcessor?.Number.ToString(CultureInfo.InvariantCulture) ?? "—"}."
            : report.OriginalStateRestored
                ? "Original GPU affinity state is verified/restored."
                : $"Final recommendation: {report.FinalRecommendation}.";

        var metrics = string.Empty;
        if (report.FinalProcessor is { } processor)
        {
            var low1 = MedianTrialMetric(report, processor, static trial => trial.OnePercentLowFps);
            var low01 = MedianTrialMetric(report, processor, static trial => trial.Low01PctFps);
            var avg = MedianTrialMetric(report, processor, static trial => trial.AvgFps);
            var p99 = MedianTrialMetric(report, processor, static trial => trial.FrameP99Milliseconds);
            if (low1 is not null || low01 is not null || avg is not null || p99 is not null)
            {
                metrics = string.Create(
                    CultureInfo.InvariantCulture,
                    $" Ranked medians: 1% {FormatMetric(low1, "F1")} FPS · 0.1% {FormatMetric(low01, "F1")} FPS · AVG {FormatMetric(avg, "F1")} FPS · p99 {FormatMetric(p99, "F2")} ms.");
            }
        }

        var finalVerification = report.Trials.LastOrDefault(static trial =>
            string.Equals(trial.Phase, "final-verification", StringComparison.Ordinal));
        var placement = finalVerification?.Placement is { ConfirmsRequestedPlacement: true } proof
            ? $" Final ISR placement verified on CPU {proof.TargetProcessor.Number}."
            : string.Equals(report.FinalRecommendation, "KeepCandidate", StringComparison.Ordinal)
                ? " Final ISR placement evidence is missing from the report."
                : string.Empty;
        var source = report.GateAClosureEligible
            ? " Source evidence is eligible for physical Gate A closure."
            : string.Equals(report.SourceState, GpuOptimizationSourceState.DevelopmentOnly.ToString(), StringComparison.Ordinal)
                ? " Development-only source: this report cannot close physical Gate A."
                : " Source evidence is not eligible for physical Gate A closure.";
        var reasons = string.Join(
            " ",
            report.Reasons
                .Where(static reason => !string.IsNullOrWhiteSpace(reason))
                .Distinct(StringComparer.Ordinal)
                .Take(2));
        return $"{headline}{metrics}{placement}{source} {reasons}".TrimEnd();
    }

    private static double? MedianTrialMetric(
        GpuAutoAffinityReport report,
        LogicalProcessorId processor,
        Func<GpuAutoAffinityTrialReport, double?> selector)
    {
        var values = report.Trials
            .Where(trial =>
                trial.Processor is { } trialProcessor && trialProcessor.Equals(processor) &&
                trial.Phase.StartsWith("screening-", StringComparison.Ordinal) &&
                !trial.Phase.EndsWith("-warmup", StringComparison.Ordinal))
            .Select(selector)
            .Where(static value => value is { } item && double.IsFinite(item) && item > 0)
            .Select(static value => value!.Value)
            .Order()
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }
        return values.Length % 2 == 0
            ? (values[(values.Length / 2) - 1] + values[values.Length / 2]) / 2d
            : values[values.Length / 2];
    }

    private static string FormatMetric(double? value, string format) =>
        value is { } number && double.IsFinite(number)
            ? number.ToString(format, CultureInfo.InvariantCulture)
            : "—";

    private static string TruncateProcessOutput(string value)
    {
        const int maximumCharacters = 8_000;
        var trimmed = value.Trim();
        return trimmed.Length <= maximumCharacters
            ? trimmed
            : trimmed[^maximumCharacters..];
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
        var startingDirectories = new[]
        {
            new DirectoryInfo(AppContext.BaseDirectory),
            new DirectoryInfo(Environment.CurrentDirectory),
        };

        foreach (var startingDirectory in startingDirectories)
        {
            for (var directory = startingDirectory; directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "LatencyPilot.slnx")) &&
                    (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                     File.Exists(Path.Combine(directory.FullName, ".git"))))
                {
                    return directory.FullName;
                }
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
}

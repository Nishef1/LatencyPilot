using System.Globalization;
using System.Text.Json;
using LatencyPilot.Core.Benchmarking;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace LatencyPilot.App;

public sealed partial class GpuOptimizationProgressWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Guid sessionId;
    private readonly string progressPath;
    private readonly string cancelPath;
    private Task? monitorTask;
    private bool monitoring;
    private bool stopRequested;
    private bool terminal;
    private bool terminalSnapshotReceived;

    internal GpuOptimizationProgressWindow(
        Guid sessionId,
        string progressPath,
        string cancelPath,
        ElementTheme requestedTheme)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("GPU optimizer progress requires a session identity.", nameof(sessionId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(progressPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(cancelPath);

        this.sessionId = sessionId;
        this.progressPath = Path.GetFullPath(progressPath);
        this.cancelPath = Path.GetFullPath(cancelPath);

        InitializeComponent();
        RootGrid.RequestedTheme = requestedTheme;
        Title = "GPU Auto Affinity";
        AppWindow.Resize(new SizeInt32(520, 650));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
        AppWindow.Closing += AppWindow_Closing;
    }

    internal void StartMonitoring()
    {
        if (monitorTask is not null)
        {
            return;
        }

        monitoring = true;
        monitorTask = MonitorAsync();
    }

    internal async Task StopMonitoringAsync()
    {
        monitoring = false;
        if (monitorTask is not null)
        {
            await monitorTask;
        }

        if (!terminalSnapshotReceived)
        {
            _ = await TryApplyLatestSnapshotAsync();
        }
    }

    internal void ShowFinalOutcome(string summary, string reportPath, bool finalStateVerified, GpuAutoAffinityReport? report = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);

        terminal = true;
        if (report is not null)
        {
            ShowRankedResults(report);
        }
        if (terminalSnapshotReceived)
        {
            StatusText.Text = $"{StatusText.Text}\n{summary}\nReport: {reportPath}";
        }
        else
        {
            PhaseText.Text = finalStateVerified
                ? "Ended early · final state verified"
                : "Ended early · recovery attention required";
            StatusText.Text = $"{summary}\nReport: {reportPath}";
            if (!finalStateVerified)
            {
                IsrPlacementText.Text = "Final state not verified — inspect recovery evidence";
            }
        }

        StopButton.Content = "Close";
        StopButton.IsEnabled = true;
        UpdateAutomationStatus(OptimizationProgressBar.Value);
    }

    private void ShowRankedResults(GpuAutoAffinityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        RankedCandidatesPanel.Children.Clear();

        static double? Median(IEnumerable<double?> values)
        {
            var finite = values
                .Where(static value => value is { } item && double.IsFinite(item) && item > 0)
                .Select(static value => value!.Value)
                .Order()
                .ToArray();
            if (finite.Length == 0)
            {
                return null;
            }
            return finite.Length % 2 == 0
                ? (finite[(finite.Length / 2) - 1] + finite[finite.Length / 2]) / 2d
                : finite[finite.Length / 2];
        }

        // Top candidates have one screening run plus one fresh score in each
        // of two independent re-test rounds. Use all scored v1 ranking runs so
        // the UI mirrors the decision engine.
        var rows = report.Trials
            .Where(static trial =>
                (string.Equals(trial.Phase, "screening", StringComparison.Ordinal) ||
                 string.Equals(trial.Phase, "screening-finalists", StringComparison.Ordinal)) &&
                trial.Processor.HasValue)
            .GroupBy(static trial => trial.Processor!.Value)
            .Select(group =>
            {
                var candidate = report.Candidates.LastOrDefault(item => item.Processor.Equals(group.Key));
                return new
                {
                    Processor = group.Key,
                    Core = candidate?.PhysicalCoreIndex,
                    Low1PctFps = Median(group.Select(static trial => trial.OnePercentLowFps)),
                    Low01PctFps = Median(group.Select(static trial => trial.Low01PctFps)),
                    AvgFps = Median(group.Select(static trial => trial.AvgFps)),
                    MedianP99 = Median(group.Select(static trial => trial.FrameP99Milliseconds)),
                    Verdict = candidate?.Verdict ?? "—",
                };
            })
            .Where(static row => row.Low1PctFps is { } low && double.IsFinite(low) && low > 0)
            .OrderByDescending(row =>
                report.FinalProcessor is not null &&
                report.FinalProcessor.Equals(row.Processor))
            .ThenByDescending(static row => row.Low1PctFps)
            .ThenByDescending(static row => row.AvgFps)
            .ThenBy(static row => row.MedianP99)
            .ThenByDescending(static row => row.Low01PctFps)
            .ToArray();

        if (rows.Length == 0)
        {
            RankedSummaryText.Text = "No ranked candidates. The search restored the original state or ended Inconclusive; open the JSON report for trial reasons.";
            AutomationProperties.SetName(RankedSummaryText, "Ranked candidates: none");
            return;
        }

        var inconclusive = report.Candidates
            .Where(static item => string.Equals(item.Verdict, "Inconclusive", StringComparison.Ordinal))
            .Select(static item => item.Processor.Number)
            .Distinct()
            .Count();
        var best = rows[0];
        RankedSummaryText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "Selected: CPU {0} (median 1% low {1:F1} FPS, {2} ranked{3}). Sub-1% differences in 1% low / AVG / p99 are treated as practical ties; 0.1% low uses a wider rare-tail margin.",
            best.Processor.Number,
            best.Low1PctFps!.Value,
            rows.Length,
            inconclusive > 0 ? $", {inconclusive} inconclusive" : string.Empty);
        AutomationProperties.SetName(RankedSummaryText, $"Ranked candidates. {RankedSummaryText.Text}");

        var minimumLow = rows.Min(static row => row.Low1PctFps!.Value);
        var maximumLow = rows.Max(static row => row.Low1PctFps!.Value);
        var span = maximumLow - minimumLow;
        foreach (var row in rows)
        {
            var isFinalist = report.FinalProcessor is not null && report.FinalProcessor.Equals(row.Processor);
            var label = new TextBlock
            {
                Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "CPU {0}{1} · 1% {2} · 0.1% {3} · AVG {4} FPS · p99 {5} ms · {6}{7}",
                    row.Processor.Number,
                    row.Core is { } core ? $" · core {core}" : string.Empty,
                    FormatFps(row.Low1PctFps),
                    FormatFps(row.Low01PctFps),
                    FormatFps(row.AvgFps),
                    row.MedianP99 is { } p99 ? p99.ToString("F2", CultureInfo.InvariantCulture) : "—",
                    row.Verdict,
                    isFinalist ? " · finalist" : string.Empty),
                Style = (Style)Application.Current.Resources["BodyTextStyle"],
                TextWrapping = TextWrapping.Wrap,
            };
            AutomationProperties.SetName(label, label.Text);
            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = span > 0 ? (row.Low1PctFps!.Value - minimumLow) / span * 100d : 100d,
                Height = 8,
            };
            AutomationProperties.SetName(
                bar,
                string.Create(CultureInfo.InvariantCulture, $"CPU {row.Processor.Number} relative 1 percent low bar"));
            var container = new StackPanel { Spacing = 2 };
            container.Children.Add(label);
            container.Children.Add(bar);
            RankedCandidatesPanel.Children.Add(container);
        }
    }

    internal void ShowStartupFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        terminal = true;
        PhaseText.Text = "Could not start";
        StatusText.Text = message;
        StopButton.Content = "Close";
        StopButton.IsEnabled = true;
        UpdateAutomationStatus(null);
    }

    private async Task MonitorAsync()
    {
        while (monitoring && !terminal)
        {
            if (await TryApplyLatestSnapshotAsync() && terminalSnapshotReceived)
            {
                return;
            }

            if (monitoring && !terminal)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }
    }

    private async Task<bool> TryApplyLatestSnapshotAsync()
    {
        try
        {
            if (!File.Exists(progressPath))
            {
                return false;
            }

            await using var stream = new FileStream(
                progressPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var snapshot = JsonSerializer.Deserialize<GpuOptimizationProgressSnapshot>(json, JsonOptions);
            if (snapshot is null ||
                !string.Equals(snapshot.Schema, GpuOptimizationProgressSnapshot.SchemaId, StringComparison.Ordinal) ||
                snapshot.SessionId != sessionId)
            {
                return false;
            }

            ApplySnapshot(snapshot);
            if (snapshot.IsTerminal)
            {
                terminalSnapshotReceived = true;
                terminal = true;
                StopButton.Content = "Close";
                StopButton.IsEnabled = true;
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void ApplySnapshot(GpuOptimizationProgressSnapshot snapshot)
    {
        CandidateText.Text = snapshot.Processor is { } processor
            ? snapshot.CandidateIndex is { } index && snapshot.CandidateCount is { } count
                ? $"Candidate {index} / {count} · CPU {processor.Number} · Physical core {snapshot.PhysicalCore?.ToString(CultureInfo.InvariantCulture) ?? "—"}"
                : $"CPU {processor.Number} · Physical core {snapshot.PhysicalCore?.ToString(CultureInfo.InvariantCulture) ?? "—"}"
            : "Original/default control";
        PhaseText.Text = snapshot.IsTerminal
            ? FormatTerminalPhase(snapshot)
            : $"{FormatPhase(snapshot.Phase)} · {snapshot.Message}";
        OptimizationProgressBar.Value = snapshot.PercentComplete;
        ProgressPercentText.Text = $"{snapshot.PercentComplete:F0}%";
        FrameP99Text.Text = snapshot.FrameP99Milliseconds is { } frameP99 && double.IsFinite(frameP99)
            ? string.Create(CultureInfo.InvariantCulture, $"{frameP99:F2} ms")
            : "—";
        OnePercentLowText.Text = snapshot.OnePercentLowFps is { } onePercentLow && double.IsFinite(onePercentLow)
            ? string.Create(CultureInfo.InvariantCulture, $"{onePercentLow:F1} FPS")
            : "—";
        IsrPlacementText.Text = snapshot.IsrPlacementState;
        LastCandidateText.Text = snapshot.LastCompletedCandidateVerdict;
        TimingText.Text = snapshot.EstimatedRemainingMilliseconds is { } remaining && double.IsFinite(remaining)
            ? $"Elapsed {FormatDuration(snapshot.ElapsedMilliseconds)} · Estimated remaining {FormatDuration(remaining)}"
            : $"Elapsed {FormatDuration(snapshot.ElapsedMilliseconds)} · estimating remaining time";
        StatusText.Text = snapshot.IsRestoring
            ? $"Restoring safely · {snapshot.Message}"
            : snapshot.Message;

        if (snapshot.IsRestoring && !snapshot.IsTerminal)
        {
            StopButton.IsEnabled = false;
            StopButton.Content = "Restoring safely…";
        }
        else if (!stopRequested && !snapshot.IsTerminal)
        {
            StopButton.IsEnabled = true;
            StopButton.Content = "Stop safely";
        }

        UpdateAutomationStatus(snapshot.PercentComplete);
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (terminal)
        {
            Close();
            return;
        }

        await RequestStopSafelyAsync();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (terminal)
        {
            return;
        }

        args.Cancel = true;
        _ = RequestStopSafelyAsync();
    }

    private async Task RequestStopSafelyAsync()
    {
        if (stopRequested || terminal)
        {
            return;
        }

        stopRequested = true;
        StopButton.IsEnabled = false;
        StopButton.Content = "Stopping safely…";
        StatusText.Text = "Stop requested. Future trials will not start; rollback/recovery remains owned until final state verification.";
        UpdateAutomationStatus(OptimizationProgressBar.Value);

        try
        {
            var directory = Path.GetDirectoryName(cancelPath)
                ?? throw new InvalidOperationException("GPU optimizer cancel path has no parent directory.");
            Directory.CreateDirectory(directory);
            var temporary = cancelPath + ".tmp";
            await File.WriteAllTextAsync(
                temporary,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            File.Move(temporary, cancelPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            stopRequested = false;
            StopButton.IsEnabled = true;
            StopButton.Content = "Stop safely";
            StatusText.Text = $"Stop request could not be recorded: {exception.Message}";
            UpdateAutomationStatus(OptimizationProgressBar.Value);
        }
    }

    private void UpdateAutomationStatus(double? percentComplete)
    {
        AutomationProperties.SetItemStatus(CandidateText, CandidateText.Text);
        AutomationProperties.SetItemStatus(PhaseText, PhaseText.Text);
        AutomationProperties.SetItemStatus(
            OptimizationProgressBar,
            percentComplete is { } percent
                ? $"{percent:F0}% complete. {CandidateText.Text}. {PhaseText.Text}"
                : $"{CandidateText.Text}. {PhaseText.Text}");
        AutomationProperties.SetItemStatus(StatusText, StatusText.Text);
    }

    private static string FormatTerminalPhase(GpuOptimizationProgressSnapshot snapshot)
    {
        var finalStateVerified = string.Equals(
            snapshot.IsrPlacementState,
            "Final state verified",
            StringComparison.Ordinal);
        return snapshot.Phase switch
        {
            "failed-safely" => finalStateVerified
                ? "Failed safely · original state verified"
                : "Failed · recovery attention required",
            "stopped-safely" => finalStateVerified
                ? "Stopped safely · original state verified"
                : "Stopped · recovery attention required",
            _ => finalStateVerified
                ? "Complete · final state verified"
                : "Complete · recovery attention required",
        };
    }

    private static string FormatPhase(string phase) => phase switch
    {
        "initializing" => "Initializing",
        "screening-warmup" => "Benchmark warm-up",
        "screening" => "Physical-core screening",
        "screening-finalists" => "Top-candidate re-test",
        "final-verification" => "Winner placement verification",
        "stopping-safely" => "Stopping safely",
        "restoring-original" => "Restoring original state",
        "failed-safely" => "Failed safely",
        "stopped-safely" => "Stopped safely",
        "complete" => "Complete",
        _ => phase,
    };

    private static string FormatFps(double? value) =>
        value is { } fps && double.IsFinite(fps) && fps > 0
            ? fps.ToString("F1", CultureInfo.InvariantCulture)
            : "—";

    private static string FormatDuration(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
        {
            return "—";
        }

        var duration = TimeSpan.FromMilliseconds(milliseconds);
        return duration.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}")
            : string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes:D2}:{duration.Seconds:D2}");
    }
}

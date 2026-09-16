using System.Globalization;
using System.Text.Json;
using LatencyPilot.Core.Benchmarking;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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

    internal void ShowFinalOutcome(string summary, string reportPath, bool finalStateVerified)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);

        terminal = true;
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
            // Atomic replacement can briefly move the file between directory entries.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // A file watcher or security scanner can briefly deny the shared progress read.
            return false;
        }
        catch (JsonException)
        {
            // Ignore a transient unreadable snapshot; the next atomic write supersedes it.
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
        "screening-control" => "Original control",
        "screening" => "Physical-core screening",
        "smt-refinement" => "SMT sibling refinement",
        "confirmation" => "Finalist confirmation",
        "stopping-safely" => "Stopping safely",
        "restoring-original" => "Restoring original state",
        "failed-safely" => "Failed safely",
        "stopped-safely" => "Stopped safely",
        "complete" => "Complete",
        _ => phase,
    };

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

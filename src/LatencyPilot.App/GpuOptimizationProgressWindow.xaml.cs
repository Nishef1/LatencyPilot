using System.Globalization;
using System.Text.Json;
using LatencyPilot.Core.Benchmarking;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;

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
        AppWindow.Resize(new SizeInt32(760, 780));
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
        if (!terminalSnapshotReceived)
        {
            PhaseText.Text = finalStateVerified
                ? "Ended early · final state verified"
                : "Ended early · recovery attention required";
            if (!finalStateVerified)
            {
                IsrPlacementText.Text = "Final state not verified — inspect recovery evidence";
            }
        }

        StatusText.Text = report is null ? summary : BuildTerminalStatus(report, finalStateVerified);
        AddReportActions(reportPath);
        StopButton.Content = "Close";
        StopButton.IsEnabled = true;
        UpdateAutomationStatus(OptimizationProgressBar.Value);
    }

    private void ShowRankedResults(GpuAutoAffinityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        RankedCandidatesPanel.Children.Clear();
        var screeningInvalidated = IsScreeningInvalidated(report);
        var coverage = BuildCoverageSummary(report);

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

        var rows = report.Trials
            .Where(static trial =>
                string.Equals(trial.Role, "Candidate", StringComparison.OrdinalIgnoreCase) &&
                trial.Processor.HasValue &&
                trial.Phase.StartsWith("screening-", StringComparison.Ordinal) &&
                !trial.Phase.EndsWith("-warmup", StringComparison.Ordinal) &&
                string.Equals(trial.ReadinessState, "Ready", StringComparison.Ordinal))
            .GroupBy(static trial => trial.Processor!.Value)
            .Select(group =>
            {
                var candidate = report.Candidates.LastOrDefault(item => item.Processor.Equals(group.Key));
                var rawLow1 = Median(group.Select(static trial => trial.OnePercentLowFps));
                var rawLow01 = Median(group.Select(static trial => trial.Low01PctFps));
                var rawAvg = Median(group.Select(static trial => trial.AvgFps));
                var rawP99 = Median(group.Select(static trial => trial.FrameP99Milliseconds));
                return new
                {
                    Processor = group.Key,
                    Core = candidate?.PhysicalCoreIndex,
                    DecisionRank = candidate?.DecisionRank,
                    RawLow1PctFps = rawLow1,
                    DecisionOnePercentLowFps = IsFinitePositive(candidate?.DecisionOnePercentLowFps) ? candidate!.DecisionOnePercentLowFps : rawLow1,
                    DecisionLow01PctFps = IsFinitePositive(candidate?.DecisionLow01PctFps) ? candidate!.DecisionLow01PctFps : rawLow01,
                    DecisionAvgFps = IsFinitePositive(candidate?.DecisionAvgFps) ? candidate!.DecisionAvgFps : rawAvg,
                    DecisionFrameP99Milliseconds = IsFinitePositive(candidate?.DecisionFrameP99Milliseconds) ? candidate!.DecisionFrameP99Milliseconds : rawP99,
                    LocalControlUncertainty = candidate?.LocalControlUncertainty,
                    UsesTimeLocalNormalization = candidate?.UsesTimeLocalNormalization == true,
                    Verdict = candidate?.Verdict ?? "—",
                };
            })
            .Where(static row => row.DecisionOnePercentLowFps is { } low && double.IsFinite(low) && low > 0)
            .OrderBy(static row => row.DecisionRank ?? int.MaxValue)
            .ThenBy(static row => row.Processor.Group)
            .ThenBy(static row => row.Processor.Number)
            .ToArray();

        if (rows.Length == 0)
        {
            RankedSummaryText.Text = screeningInvalidated
                ? $"{coverage}. Measurements were structurally invalidated — no valid winner. Original/default was restored; the report remains available for diagnostic evidence."
                : $"{coverage}. No decision-grade candidate could be ranked. Original/default is the verified terminal state; measured pair evidence remains diagnostic.";
            AutomationProperties.SetName(RankedSummaryText, screeningInvalidated
                ? "Candidate measurements structurally invalidated"
                : "Decision-grade candidates: none");
            return;
        }

        var inconclusive = report.Candidates
            .Where(static item => string.Equals(item.Verdict, "Inconclusive", StringComparison.Ordinal))
            .Select(static item => item.Processor.Number)
            .Distinct()
            .Count();
        var best = rows.FirstOrDefault(static row => row.DecisionRank == 1);
        var keptWinner = string.Equals(report.FinalRecommendation, "KeepCandidate", StringComparison.Ordinal) && report.FinalProcessor is not null;
        var maximumLocalUncertainty = rows
            .Select(static row => row.LocalControlUncertainty)
            .Where(static value => value is { } item && double.IsFinite(item) && item >= 0d)
            .Select(static value => value!.Value)
            .DefaultIfEmpty(0d)
            .Max();
        var hasLocalUncertaintyEvidence = rows.Any(static row => row.LocalControlUncertainty is { } uncertainty && uncertainty > 0d);

        if (screeningInvalidated)
        {
            RankedSummaryText.Text = $"{coverage}. Measurements were structurally invalidated — no valid winner. Original/default was restored; candidate measurements below are diagnostic only.";
            AutomationProperties.SetName(RankedSummaryText, "Candidate measurements structurally invalidated; no valid winner");
        }
        else if (best is null)
        {
            RankedSummaryText.Text = $"{coverage}. Candidates were measured, but this report has no authoritative persisted rank. Original/default remains the safe terminal state; rows below are diagnostic only.";
            AutomationProperties.SetName(RankedSummaryText, "GPU candidate evidence has no authoritative persisted rank");
        }
        else if (hasLocalUncertaintyEvidence)
        {
            RankedSummaryText.Text = string.Format(
                CultureInfo.InvariantCulture,
                keptWinner
                    ? "{0}. Selected and kept: CPU {1} using the optimizer's persisted decision rank. Maximum observed paired local-control uncertainty is {2:P1}."
                    : "{0}. Best decision-grade candidate: CPU {1} by the optimizer's persisted rank — not kept. Maximum observed paired local-control uncertainty is {2:P1}; Original/default is verified.",
                coverage,
                best.Processor.Number,
                maximumLocalUncertainty);
            AutomationProperties.SetName(RankedSummaryText, $"Paired GPU decision evidence. {RankedSummaryText.Text}");
        }
        else
        {
            RankedSummaryText.Text = string.Format(
                CultureInfo.InvariantCulture,
                keptWinner
                    ? "{0}. Selected and kept: CPU {1} (decision 1% low {2:F1} FPS, {3} ranked{4})."
                    : "{0}. Best decision-grade candidate: CPU {1} (decision 1% low {2:F1} FPS, {3} ranked{4}) — not kept; Original/default was restored.",
                coverage,
                best.Processor.Number,
                best.DecisionOnePercentLowFps!.Value,
                rows.Count(static row => row.DecisionRank is > 0),
                inconclusive > 0 ? $", {inconclusive} inconclusive" : string.Empty);
            AutomationProperties.SetName(RankedSummaryText, $"GPU candidate decision evidence. {RankedSummaryText.Text}");
        }

        foreach (var row in rows)
        {
            var isFinal = !screeningInvalidated && report.FinalProcessor is not null && report.FinalProcessor.Equals(row.Processor);
            var isBest = !screeningInvalidated && row.DecisionRank == 1;
            var displayedVerdict = screeningInvalidated
                ? "Measured · invalidated"
                : isFinal
                    ? "Kept"
                    : isBest && !keptWinner ? "Best decision-grade · not kept" : row.Verdict;
            var uncertainty = row.LocalControlUncertainty is { } value && double.IsFinite(value) && value > 0d
                ? value.ToString("P1", CultureInfo.InvariantCulture)
                : "—";
            var rawContext = row.UsesTimeLocalNormalization && row.RawLow1PctFps is { } raw && double.IsFinite(raw) && raw > 0d
                ? $" · raw 1% {raw:F1} FPS"
                : string.Empty;

            var rowBorder = new Border
            {
                Padding = new Thickness(8d, 10d, 8d, 10d),
                BorderThickness = new Thickness(0d, 0d, 0d, 1d),
                BorderBrush = DashboardThemeResources.Brush(RootGrid, "PremiumOverviewDividerBrush"),
                Background = isBest ? DashboardThemeResources.Brush(RootGrid, "BrandActionSoftBrush") : null,
                CornerRadius = isBest ? new CornerRadius(10d) : new CornerRadius(0d),
            };
            var content = new StackPanel { Spacing = 4d };
            var titleRow = new Grid { ColumnSpacing = 10d };
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleRow.Children.Add(new TextBlock
            {
                Text = $"{(row.DecisionRank is { } rank ? $"#{rank} · " : string.Empty)}CPU {row.Processor.Number}{(row.Core is { } core ? $" · core {core}" : string.Empty)}",
                Style = (Style)Application.Current.Resources["BodyTextStyle"],
                FontWeight = isBest || isFinal ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
            });
            var state = new TextBlock
            {
                Text = displayedVerdict,
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = DashboardThemeResources.Brush(RootGrid, isFinal ? "SemanticGoodBrush" : isBest ? "BrandActionBrush" : "MutedTextBrush"),
            };
            Grid.SetColumn(state, 1);
            titleRow.Children.Add(state);
            content.Children.Add(titleRow);
            content.Children.Add(new TextBlock
            {
                Text = $"1% low {FormatFps(row.DecisionOnePercentLowFps)} FPS · AVG {FormatFps(row.DecisionAvgFps)} FPS · p99 {(row.DecisionFrameP99Milliseconds is { } p99 ? p99.ToString("F2", CultureInfo.InvariantCulture) : "—")} ms · 0.1% low {FormatFps(row.DecisionLow01PctFps)} FPS · uncertainty {uncertainty}{rawContext}",
                Style = (Style)Application.Current.Resources["CaptionTextStyle"],
                TextWrapping = TextWrapping.Wrap,
            });
            rowBorder.Child = content;
            AutomationProperties.SetName(rowBorder, $"CPU {row.Processor.Number}. {displayedVerdict}.");
            RankedCandidatesPanel.Children.Add(rowBorder);
        }
    }

    private void AddReportActions(string reportPath)
    {
        var actions = new Grid { ColumnSpacing = 8d, Margin = new Thickness(0d, 8d, 0d, 0d) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
        var status = new TextBlock
        {
            Style = (Style)Application.Current.Resources["CaptionTextStyle"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0d, 4d, 0d, 0d),
        };

        var open = new Button
        {
            Content = "Open report",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 34d,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
        };
        AutomationProperties.SetName(open, "Open report");
        open.Click += async (_, _) =>
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(reportPath);
                status.Text = await Launcher.LaunchFileAsync(file) ? "Opened raw report." : "Windows could not open the raw report.";
            }
            catch (Exception exception)
            {
                status.Text = $"Could not open report: {exception.Message}";
            }
        };
        actions.Children.Add(open);

        var copy = new Button
        {
            Content = "Copy report path",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 34d,
            Style = (Style)Application.Current.Resources["SecondaryButtonStyle"],
        };
        AutomationProperties.SetName(copy, "Copy report path");
        copy.Click += (_, _) =>
        {
            try
            {
                var package = new DataPackage();
                package.SetText(reportPath);
                Clipboard.SetContent(package);
                Clipboard.Flush();
                status.Text = "Report path copied.";
            }
            catch (Exception exception)
            {
                status.Text = $"Could not copy report path: {exception.Message}";
            }
        };
        Grid.SetColumn(copy, 1);
        actions.Children.Add(copy);
        RankedCandidatesPanel.Children.Add(actions);
        RankedCandidatesPanel.Children.Add(status);
    }

    private static string BuildTerminalStatus(GpuAutoAffinityReport report, bool finalStateVerified)
    {
        var why = report.Reasons.FirstOrDefault(static reason =>
                      reason.Contains("stopped early", StringComparison.OrdinalIgnoreCase) ||
                      reason.Contains("inconclusive", StringComparison.OrdinalIgnoreCase) ||
                      reason.Contains("not repeatable", StringComparison.OrdinalIgnoreCase))
                  ?? (string.Equals(report.FinalRecommendation, "KeepCandidate", StringComparison.Ordinal)
                      ? "Repeated paired finalist evidence cleared the optimizer's Keep gates."
                      : "No candidate established a verified Keep decision in this run.");
        var finalState = report.FinalProcessor is { } processor && finalStateVerified
            ? $"CPU {processor.Number} kept and verified."
            : report.OriginalStateRestored && finalStateVerified
                ? "Original GPU affinity restored and verified."
                : "Final machine state is not fully verified; inspect recovery evidence.";
        return $"Why stopped: {why}\nTested: {BuildCoverageSummary(report)}\nFinal state: {finalState}";
    }

    private static string BuildCoverageSummary(GpuAutoAffinityReport report)
    {
        if (report.SearchScope == GpuAutoAffinitySearchScope.Custom)
        {
            var selected = report.RequestedProcessors.Count;
            var tested = report.ValidatedProcessors.Count;
            var notReached = Math.Max(0, selected - tested);
            return $"{selected} selected · {tested} tested · {notReached} not reached";
        }
        if (report.SearchScope == GpuAutoAffinitySearchScope.OriginalDiagnostics)
        {
            return "Original-only diagnostic · 0 candidate CPUs selected · 0 candidate CPUs tested";
        }
        var testedFull = report.ValidatedProcessors.Count;
        return report.FullTopologyCoverage
            ? $"Full search · {testedFull} CPU candidate(s) tested · topology screen complete"
            : $"Full search · {testedFull} CPU candidate(s) tested · topology screen ended early";
    }

    private static bool IsFinitePositive(double? value) =>
        value is { } item && double.IsFinite(item) && item > 0d;

    private static bool IsScreeningInvalidated(GpuAutoAffinityReport report)
    {
        if (string.Equals(report.FinalRecommendation, "KeepCandidate", StringComparison.Ordinal) || report.FinalProcessor is not null)
        {
            return false;
        }
        return report.Reasons.Any(static reason =>
            reason.Contains("drift", StringComparison.OrdinalIgnoreCase) &&
            (reason.Contains("invalidated", StringComparison.OrdinalIgnoreCase) ||
             reason.Contains("discarded", StringComparison.OrdinalIgnoreCase) ||
             reason.Contains("moving environment", StringComparison.OrdinalIgnoreCase)));
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
            await using var stream = new FileStream(progressPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var snapshot = JsonSerializer.Deserialize<GpuOptimizationProgressSnapshot>(json, JsonOptions);
            if (snapshot is null || !string.Equals(snapshot.Schema, GpuOptimizationProgressSnapshot.SchemaId, StringComparison.Ordinal) || snapshot.SessionId != sessionId)
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
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
    }

    private void ApplySnapshot(GpuOptimizationProgressSnapshot snapshot)
    {
        CandidateText.Text = snapshot.Processor is { } processor
            ? snapshot.CandidateIndex is { } index && snapshot.CandidateCount is { } count
                ? $"Candidate {index} / {count} · CPU {processor.Number} · Physical core {snapshot.PhysicalCore?.ToString(CultureInfo.InvariantCulture) ?? "—"}"
                : $"CPU {processor.Number} · Physical core {snapshot.PhysicalCore?.ToString(CultureInfo.InvariantCulture) ?? "—"}"
            : "Original/default control";
        PhaseText.Text = snapshot.IsTerminal ? FormatTerminalPhase(snapshot) : $"{FormatPhase(snapshot.Phase)} · {snapshot.Message}";
        OptimizationProgressBar.Value = snapshot.PercentComplete;
        ProgressPercentText.Text = $"{snapshot.PercentComplete:F0}%";
        var metricScope = snapshot.Processor is not null ? "Candidate" : snapshot.IsTerminal ? "Final Original" : "Original control";
        FrameP99LabelText.Text = $"{metricScope} frame p99";
        OnePercentLowLabelText.Text = $"{metricScope} 1% low";
        FrameP99Text.Text = snapshot.FrameP99Milliseconds is { } frameP99 && double.IsFinite(frameP99) ? string.Create(CultureInfo.InvariantCulture, $"{frameP99:F2} ms") : "—";
        OnePercentLowText.Text = snapshot.OnePercentLowFps is { } onePercentLow && double.IsFinite(onePercentLow) ? string.Create(CultureInfo.InvariantCulture, $"{onePercentLow:F1} FPS") : "—";
        IsrPlacementText.Text = snapshot.IsrPlacementState;
        LastCandidateText.Text = snapshot.LastCompletedCandidateVerdict;
        TimingText.Text = snapshot.IsTerminal
            ? $"Elapsed {FormatDuration(snapshot.ElapsedMilliseconds)} · finished"
            : snapshot.EstimatedRemainingMilliseconds is { } remaining && double.IsFinite(remaining)
                ? $"Elapsed {FormatDuration(snapshot.ElapsedMilliseconds)} · Estimated remaining {FormatDuration(remaining)}"
                : $"Elapsed {FormatDuration(snapshot.ElapsedMilliseconds)} · estimating remaining time";
        StatusText.Text = snapshot.IsRestoring ? $"Restoring safely · {snapshot.Message}" : snapshot.Message;

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
        if (terminal) { Close(); return; }
        await RequestStopSafelyAsync();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (terminal) { return; }
        args.Cancel = true;
        _ = RequestStopSafelyAsync();
    }

    private async Task RequestStopSafelyAsync()
    {
        if (stopRequested || terminal) { return; }
        stopRequested = true;
        StopButton.IsEnabled = false;
        StopButton.Content = "Stopping safely…";
        StatusText.Text = "Stop requested. Future trials will not start; rollback/recovery remains owned until final state verification.";
        UpdateAutomationStatus(OptimizationProgressBar.Value);
        try
        {
            var directory = Path.GetDirectoryName(cancelPath) ?? throw new InvalidOperationException("GPU optimizer cancel path has no parent directory.");
            Directory.CreateDirectory(directory);
            var temporary = cancelPath + ".tmp";
            await File.WriteAllTextAsync(temporary, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
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
        AutomationProperties.SetItemStatus(OptimizationProgressBar, percentComplete is { } percent
            ? $"{percent:F0}% complete. {CandidateText.Text}. {PhaseText.Text}"
            : $"{CandidateText.Text}. {PhaseText.Text}");
        AutomationProperties.SetItemStatus(StatusText, StatusText.Text);
    }

    private static string FormatTerminalPhase(GpuOptimizationProgressSnapshot snapshot)
    {
        var finalStateVerified = string.Equals(snapshot.IsrPlacementState, "Final state verified", StringComparison.Ordinal);
        return snapshot.Phase switch
        {
            "failed-safely" => finalStateVerified ? "Failed safely · original state verified" : "Failed · recovery attention required",
            "stopped-safely" => finalStateVerified ? "Stopped safely · original state verified" : "Stopped · recovery attention required",
            _ => finalStateVerified ? "Complete · final state verified" : "Complete · recovery attention required",
        };
    }

    private static string FormatPhase(string phase) => phase switch
    {
        "initializing" => "Initializing",
        "screening-warmup" => "Benchmark warm-up",
        "screening-original" => "Original qualification",
        "screening-representative" => "Core representative pair",
        "screening-sibling" => "SMT sibling pair",
        "screening-finalists" => "Finalist paired confirmation",
        "final-verification" => "Winner placement verification",
        "stopping-safely" => "Stopping safely",
        "restoring-original" => "Restoring original state",
        "failed-safely" => "Failed safely",
        "stopped-safely" => "Stopped safely",
        "complete" => "Complete",
        _ => phase,
    };

    private static string FormatFps(double? value) =>
        value is { } fps && double.IsFinite(fps) && fps > 0 ? fps.ToString("F1", CultureInfo.InvariantCulture) : "—";

    private static string FormatDuration(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0) { return "—"; }
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        return duration.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}")
            : string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes:D2}:{duration.Seconds:D2}");
    }
}

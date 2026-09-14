using System.ComponentModel;
using System.Globalization;
using LatencyPilot.App.Services;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private static readonly TimeSpan BaselineObservationDuration = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan BaselinePreSequenceSettleDelay = TimeSpan.FromSeconds(5);

    private ComboBox? _measurementScenarioComboBox;
    private TextBlock? _measurementScenarioGuidanceText;
    private TextBlock? _measurementRuntimeContextText;
    private Border? _measurementScenarioCard;
    private bool _measurementBusy;
    private bool _runtimeContextWarning;
    private string _lastRuntimeContextSummary =
        "Runtime context will appear after capture: average system CPU busy time, power source, active power plan, configured Windows power mode and Battery Saver state.";

    private void InitializeMeasurementExperience()
    {
        ToolTipService.SetToolTip(
            CaptureObservationButton,
            "Capture a five-second diagnostic snapshot for attribution and integrity checks (Ctrl+O). It is not a benchmark verdict.");
        ToolTipService.SetToolTip(
            CaptureBaselineButton,
            "Settle LatencyPilot for five seconds, then capture five repeated 20-second windows for baseline stability (Ctrl+B). The workload itself should already be warmed/repeatable when applicable.");

        RebuildMeasurementScenarioCard();
        RootGrid.ActualThemeChanged += (_, _) => RebuildMeasurementScenarioCard();
        TryRegisterHighContrastChanged(RebuildMeasurementScenarioCard);
    }

    private MeasurementScenario SelectedMeasurementScenario =>
        _measurementScenarioComboBox?.SelectedIndex switch
        {
            1 => MeasurementScenario.IdleBaseline,
            2 => MeasurementScenario.BeforeAfter,
            _ => MeasurementScenario.RealWorld,
        };

    private void RebuildMeasurementScenarioCard()
    {
        if (ObservationCard.Child is not StackPanel observationStack)
        {
            return;
        }

        var selectedScenario = SelectedMeasurementScenario;
        if (_measurementScenarioCard is not null)
        {
            observationStack.Children.Remove(_measurementScenarioCard);
        }

        _measurementScenarioCard = BuildMeasurementScenarioCard(selectedScenario);
        observationStack.Children.Insert(Math.Min(1, observationStack.Children.Count), _measurementScenarioCard);
    }

    private Border BuildMeasurementScenarioCard(MeasurementScenario selectedScenario)
    {
        var card = new Border
        {
            Padding = new Thickness(15),
            CornerRadius = new CornerRadius(14),
            Background = ThemeBrush("AccentSoftBrush"),
            BorderBrush = ThemeBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
        };

        var root = new StackPanel { Spacing = 9 };
        card.Child = root;

        root.Children.Add(new TextBlock
        {
            Text = "Measurement scenario",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
        });
        root.Children.Add(new TextBlock
        {
            Text = "Choose the context before capture so the result and exported evidence describe what was actually measured.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
        });

        _measurementScenarioComboBox = new ComboBox
        {
            MinWidth = 230,
            MaxWidth = 360,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = !_measurementBusy,
        };
        _measurementScenarioComboBox.Items.Add(EvidenceExportService.GetMeasurementDisplayName(MeasurementScenario.RealWorld));
        _measurementScenarioComboBox.Items.Add(EvidenceExportService.GetMeasurementDisplayName(MeasurementScenario.IdleBaseline));
        _measurementScenarioComboBox.Items.Add(EvidenceExportService.GetMeasurementDisplayName(MeasurementScenario.BeforeAfter));
        _measurementScenarioComboBox.SelectedIndex = selectedScenario switch
        {
            MeasurementScenario.IdleBaseline => 1,
            MeasurementScenario.BeforeAfter => 2,
            _ => 0,
        };
        _measurementScenarioComboBox.SelectionChanged += MeasurementScenarioComboBox_SelectionChanged;
        _measurementScenarioComboBox.SelectionChanged += MeasurementScenarioSelection_InvalidatesPreviousEvidence;
        AutomationProperties.SetName(_measurementScenarioComboBox, "Measurement scenario");
        AutomationProperties.SetHelpText(
            _measurementScenarioComboBox,
            "Select whether this run represents a real-world workload, controlled idle, or a before/after comparison.");
        root.Children.Add(_measurementScenarioComboBox);

        _measurementScenarioGuidanceText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
        };
        root.Children.Add(_measurementScenarioGuidanceText);
        UpdateMeasurementScenarioGuidance();

        _measurementRuntimeContextText = new TextBlock
        {
            Text = _lastRuntimeContextSummary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush(_runtimeContextWarning ? "WarningBrush" : "MutedTextBrush"),
        };
        AutomationProperties.SetName(_measurementRuntimeContextText, "Runtime measurement context");
        root.Children.Add(_measurementRuntimeContextText);

        return card;
    }

    private void MeasurementScenarioComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateMeasurementScenarioGuidance();

    private void MeasurementScenarioSelection_InvalidatesPreviousEvidence(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_latestEvidenceJson is null && BaselineWindowsList.ItemsSource is null)
        {
            ResetRuntimeContextSummary(
                "Runtime context will appear after capture: average system CPU busy time, power source, active power plan, configured Windows power mode and Battery Saver state.");
            return;
        }

        ClearCaptureMetrics();
        ClearExportEvidence("Measurement scenario changed. Capture again before exporting evidence for the new context.");

        BaselineProgressBar.Value = 0;
        BaselineVerdictText.Text = "Not captured";
        BaselineStatusText.Text = "Measurement scenario changed. Build a new baseline for this context.";
        BaselineMetricsText.Text = "No baseline has been captured for the selected scenario.";
        BaselineReasonsText.Text = EvidenceExportService.GetMeasurementGuidance(SelectedMeasurementScenario);
        BaselineWindowsList.ItemsSource = null;
        KernelCaptureStatusText.Text = "Measurement scenario changed. Capture again to produce context-matched evidence.";
        ObservationQualityText.Text = EvidenceExportService.GetMeasurementGuidance(SelectedMeasurementScenario);
        ResetRuntimeContextSummary(
            "Measurement scenario changed. Capture again to collect context-matched CPU and power evidence.");
    }

    private void UpdateMeasurementScenarioGuidance()
    {
        if (_measurementScenarioGuidanceText is null)
        {
            return;
        }

        var scenario = SelectedMeasurementScenario;
        _measurementScenarioGuidanceText.Text = EvidenceExportService.GetMeasurementGuidance(scenario);
    }

    private async Task CaptureObservationAsync()
    {
        if (!await EnsureObservationServiceReadyAsync())
        {
            return;
        }

        var scenario = SelectedMeasurementScenario;
        ClearExportEvidence("Quick snapshot in progress. Evidence export becomes available after completion.");
        SetMeasurementBusy(true);
        KernelCaptureStatusText.Text = "Capturing a five-second DPC/ISR diagnostic snapshot…";
        ObservationQualityText.Text =
            $"{EvidenceExportService.GetMeasurementDisplayName(scenario)}: {EvidenceExportService.GetMeasurementGuidance(scenario)} This quick snapshot is for integrity, attribution and concentration context; it is not a benchmark verdict.";

        try
        {
            var runtimeStart = TryCaptureRuntimeContext();
            var capture = await ObservationServiceClient.CaptureKernelLatencyAsync(
                ObservationDuration,
                ObservationMaximumEvents);
            var runtimeEnd = TryCaptureRuntimeContext();
            var runtimeContext = CreateRuntimeInterval(runtimeStart, runtimeEnd);

            RenderCapture(capture);
            ApplyP999Adequacy(capture);
            ApplyScenarioResultContext(capture, scenario);
            UpdateRuntimeContextSummary(runtimeContext);
            PrepareObservationEvidenceForScenario(capture, scenario, runtimeContext);
        }
        catch (Exception exception)
        {
            HandleCaptureFailure(exception, "Kernel observation");
        }
        finally
        {
            SetMeasurementBusy(false);
        }
    }

    private async Task CaptureBaselineAsync()
    {
        if (!await EnsureObservationServiceReadyAsync())
        {
            return;
        }

        var scenario = SelectedMeasurementScenario;
        var baselineWindowDescription = scenario == MeasurementScenario.IdleBaseline
            ? "quiet"
            : "repeatable";
        ClearExportEvidence("Baseline capture in progress. Export is prepared only after the capture sequence stops or completes.");
        ClearCaptureMetrics();
        SetMeasurementBusy(true);
        BaselineProgressBar.Value = 0;
        BaselineVerdictText.Text = "Capturing";
        BaselineStatusText.Text =
            $"Settling LatencyPilot/service for {BaselinePreSequenceSettleDelay.TotalSeconds:F0} seconds, then capturing {BaselineWindowCount} {baselineWindowDescription} 20-second windows. The workload should already be at its intended repeatable state.";
        BaselineMetricsText.Text = "Noise and drift will be computed after all required windows complete.";
        BaselineReasonsText.Text =
            $"Scenario: {EvidenceExportService.GetMeasurementDisplayName(scenario)}. {EvidenceExportService.GetMeasurementGuidance(scenario)} Detailed lists and charts are intentionally not redrawn between windows.";
        BaselineWindowsList.ItemsSource = null;
        ResetRuntimeContextSummary("Runtime context is being sampled around each authoritative window without redrawing detailed UI between captures.");

        var windows = new List<BaselineWindowEvidence>(BaselineWindowCount);
        var captures = new List<KernelLatencyCaptureResponse>(BaselineWindowCount);
        var runtimeWindows = new List<MeasurementRuntimeWindow>(BaselineWindowCount);

        try
        {
            await Task.Delay(BaselinePreSequenceSettleDelay);

            for (var index = 1; index <= BaselineWindowCount; index++)
            {
                var runtimeStart = TryCaptureRuntimeContext();
                var capture = await ObservationServiceClient.CaptureKernelLatencyAsync(
                    BaselineObservationDuration,
                    ObservationMaximumEvents);
                var runtimeEnd = TryCaptureRuntimeContext();

                captures.Add(capture);
                runtimeWindows.Add(new MeasurementRuntimeWindow(
                    index,
                    CreateRuntimeInterval(runtimeStart, runtimeEnd)));

                var integrityIssue = GetCaptureIntegrityIssue(capture);
                windows.Add(new BaselineWindowEvidence(
                    index,
                    capture.StartedAtUtc,
                    capture.RequestedDurationMilliseconds,
                    capture.ActualDurationMilliseconds,
                    integrityIssue is null,
                    integrityIssue,
                    capture.Dpc.Count,
                    capture.Dpc.P99Microseconds,
                    capture.Isr.Count,
                    capture.Isr.P99Microseconds));

                BaselineProgressBar.Value = index;
                BaselineStatusText.Text = index < BaselineWindowCount
                    ? $"Window {index} of {BaselineWindowCount} complete. Settling before the next 20-second measurement window…"
                    : $"Window {index} of {BaselineWindowCount} complete. Computing baseline quality…";

                if (index < BaselineWindowCount)
                {
                    await Task.Delay(BaselineInterWindowDelay);
                }
            }

            BaselineWindowsList.ItemsSource = CreateBaselineWindowRows(windows);

            var finalCapture = captures[^1];
            RenderCapture(finalCapture);
            ApplyP999Adequacy(finalCapture);
            UpdateRuntimeContextSummary(runtimeWindows);
            KernelCaptureStatusText.Text =
                "Repeated baseline complete. The observation cards show only the final 20-second window; the baseline verdict below uses all five windows.";
            ObservationQualityText.Text =
                $"Scenario: {EvidenceExportService.GetMeasurementDisplayName(scenario)}. {EvidenceExportService.GetMeasurementGuidance(scenario)} The five 20-second windows, rather than the final snapshot alone, are authoritative for baseline stability.";

            var quality = BaselineQualityAnalyzer.Analyze(windows, BaselinePolicy);
            RenderBaselineQuality(quality);
            PrepareBaselineEvidenceForScenario(captures, windows, runtimeWindows, quality, isPartial: false, scenario);
        }
        catch (Exception exception)
        {
            HandleCaptureFailure(exception, "Repeated baseline capture");
            BaselineVerdictText.Text = "Inconclusive";
            BaselineStatusText.Text = $"Baseline capture stopped after {windows.Count} of {BaselineWindowCount} windows.";
            BaselineWindowsList.ItemsSource = CreateBaselineWindowRows(windows);
            UpdateRuntimeContextSummary(runtimeWindows);

            if (windows.Count > 0)
            {
                var partialQuality = BaselineQualityAnalyzer.Analyze(windows, BaselinePolicy);
                RenderBaselineQuality(partialQuality, preserveStatusText: true);
                PrepareBaselineEvidenceForScenario(captures, windows, runtimeWindows, partialQuality, isPartial: true, scenario);
            }
            else
            {
                BaselineMetricsText.Text = "No baseline metric evidence was produced.";
                BaselineReasonsText.Text = "The repeated capture must complete before a baseline can be used for comparison.";
            }
        }
        finally
        {
            SetMeasurementBusy(false);
        }
    }

    private static RuntimeMeasurementContextSnapshot? TryCaptureRuntimeContext()
    {
        try
        {
            return RuntimeMeasurementContextReader.Capture();
        }
        catch (Win32Exception exception)
        {
            Logger.Warning(exception, "Runtime measurement context could not be captured; latency evidence remains usable without this optional context.");
            return null;
        }
    }

    private static RuntimeMeasurementContextInterval? CreateRuntimeInterval(
        RuntimeMeasurementContextSnapshot? start,
        RuntimeMeasurementContextSnapshot? end) =>
        start is null || end is null
            ? null
            : RuntimeMeasurementContextReader.CreateInterval(start, end);

    private void UpdateRuntimeContextSummary(RuntimeMeasurementContextInterval? context)
    {
        if (context is null)
        {
            ResetRuntimeContextSummary("Runtime CPU/power context was unavailable for this capture. The DPC/ISR evidence itself remains valid if capture integrity is clean.");
            return;
        }

        var cpu = context.SystemCpuBusyPercent is null
            ? "system CPU busy unavailable"
            : string.Create(CultureInfo.InvariantCulture, $"system CPU busy {context.SystemCpuBusyPercent.Value:F1}%");
        var power = FormatPowerContext(context.StartPower);
        var change = context.PowerContextChanged
            ? $" Power state changed during capture: {power} → {FormatPowerContext(context.EndPower)}."
            : $" Power context stayed stable: {power}.";

        ResetRuntimeContextSummary(
            $"Runtime context: {cpu}.{change}",
            warning: context.PowerContextChanged);
    }

    private void UpdateRuntimeContextSummary(List<MeasurementRuntimeWindow> runtimeWindows)
    {
        var contexts = runtimeWindows
            .Where(static window => window.Context is not null)
            .Select(static window => window.Context!)
            .ToArray();
        if (contexts.Length == 0)
        {
            ResetRuntimeContextSummary("Runtime CPU/power context was unavailable for the baseline windows. Baseline quality still depends on capture integrity, sample adequacy, duration, noise and drift.");
            return;
        }

        var cpuValues = contexts
            .Where(static context => context.SystemCpuBusyPercent is not null)
            .Select(static context => context.SystemCpuBusyPercent!.Value)
            .ToArray();
        var cpuSummary = cpuValues.Length == 0
            ? "system CPU busy unavailable"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"system CPU busy avg {cpuValues.Average():F1}% (range {cpuValues.Min():F1}–{cpuValues.Max():F1}%)");

        var firstPower = contexts[0].StartPower;
        var lastPower = contexts[^1].EndPower;
        var powerChanged = contexts.Any(static context => context.PowerContextChanged) ||
            !PowerStateEquivalent(firstPower, lastPower);
        var powerSummary = powerChanged
            ? $"power context changed during the sequence ({FormatPowerContext(firstPower)} → {FormatPowerContext(lastPower)})"
            : $"power context stayed stable ({FormatPowerContext(firstPower)})";

        ResetRuntimeContextSummary(
            $"Runtime context: {contexts.Length}/{runtimeWindows.Count} window(s) sampled; {cpuSummary}; {powerSummary}.",
            warning: powerChanged);
    }

    private void ResetRuntimeContextSummary(string message, bool warning = false)
    {
        _lastRuntimeContextSummary = message;
        _runtimeContextWarning = warning;
        if (_measurementRuntimeContextText is not null)
        {
            _measurementRuntimeContextText.Text = message;
            _measurementRuntimeContextText.Foreground = ThemeBrush(warning ? "WarningBrush" : "MutedTextBrush");
        }
    }

    private static bool PowerStateEquivalent(SystemPowerSnapshot first, SystemPowerSnapshot second) =>
        first.LineState == second.LineState &&
        first.Charging == second.Charging &&
        first.BatterySaverEnabled == second.BatterySaverEnabled &&
        first.ActiveSchemeId == second.ActiveSchemeId &&
        first.UserConfiguredPowerModeId == second.UserConfiguredPowerModeId;

    private static string FormatPowerContext(SystemPowerSnapshot power)
    {
        var source = power.LineState switch
        {
            SystemPowerLineState.Online => "AC power",
            SystemPowerLineState.Offline => "battery power",
            _ => "power source unknown",
        };
        var scheme = !string.IsNullOrWhiteSpace(power.ActiveSchemeName)
            ? $", plan {power.ActiveSchemeName}"
            : power.ActiveSchemeId is not null
                ? ", active power plan detected"
                : ", power plan unavailable";
        var mode = power.UserConfiguredPowerMode switch
        {
            UserConfiguredPowerMode.BestPowerEfficiency => ", configured mode Best power efficiency",
            UserConfiguredPowerMode.Balanced => ", configured mode Balanced",
            UserConfiguredPowerMode.BestPerformance => ", configured mode Best performance",
            UserConfiguredPowerMode.Unknown => ", configured mode unknown",
            null => ", configured mode unavailable",
            _ => ", configured mode unknown",
        };
        var battery = power.BatteryPresent == true && power.BatteryPercent is not null
            ? string.Create(CultureInfo.InvariantCulture, $", battery {power.BatteryPercent.Value}%")
            : string.Empty;
        var saver = power.BatterySaverEnabled switch
        {
            true => ", Battery Saver on",
            false => ", Battery Saver off",
            null => ", Battery Saver unknown",
        };

        return source + scheme + mode + battery + saver;
    }

    private void SetMeasurementBusy(bool busy)
    {
        _measurementBusy = busy;
        SetObservationControlsBusy(busy);
        if (_measurementScenarioComboBox is not null)
        {
            _measurementScenarioComboBox.IsEnabled = !busy;
        }
    }

    private void ApplyScenarioResultContext(
        KernelLatencyCaptureResponse capture,
        MeasurementScenario scenario)
    {
        var integrityIssue = GetCaptureIntegrityIssue(capture);
        var prefix =
            $"Scenario: {EvidenceExportService.GetMeasurementDisplayName(scenario)}. {EvidenceExportService.GetMeasurementGuidance(scenario)} ";

        ObservationQualityText.Text = integrityIssue is null
            ? $"{prefix}Quick snapshot integrity looks clean. This remains diagnostic evidence; use the repeated baseline for stability claims."
            : $"{prefix}Treat this quick snapshot as incomplete evidence. Tail/reference interpretation is withheld because capture integrity is not clean. Exact values remain visible for diagnosis.";
    }

    private void ApplyP999Adequacy(KernelLatencyCaptureResponse capture)
    {
        ApplyP999Adequacy(DpcP999Text, "DPC", capture.Dpc);
        ApplyP999Adequacy(IsrP999Text, "ISR", capture.Isr);
    }

    private static void ApplyP999Adequacy(
        TextBlock target,
        string label,
        LatencyDistribution distribution)
    {
        if (distribution.P999Microseconds is not null)
        {
            target.FontSize = 26;
            AutomationProperties.SetHelpText(
                target,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{label} p99.9 based on {distribution.Count:N0} observed samples."));
            ToolTipService.SetToolTip(
                target,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"p99.9 is shown because at least {ObservationProtocol.MinimumSamplesForP999:N0} {label} samples were observed. This is an adequacy floor, not a confidence guarantee."));
            return;
        }

        target.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Need ≥{ObservationProtocol.MinimumSamplesForP999:N0}");
        target.FontSize = 18;
        var explanation = string.Create(
            CultureInfo.InvariantCulture,
            $"p99.9 is withheld: {distribution.Count:N0} {label} samples were observed; at least {ObservationProtocol.MinimumSamplesForP999:N0} are required. p99 and max remain available.");
        AutomationProperties.SetHelpText(target, explanation);
        ToolTipService.SetToolTip(target, explanation);
    }

    private void PrepareObservationEvidenceForScenario(
        KernelLatencyCaptureResponse capture,
        MeasurementScenario scenario,
        RuntimeMeasurementContextInterval? runtimeContext)
    {
        try
        {
            SetExportEvidence(
                EvidenceExportService.CreateObservationJson(GetProductVersion(), scenario, capture, runtimeContext),
                EvidenceExportService.CreateSuggestedFileName("observation", capture.StartedAtUtc),
                $"Quick-observation evidence is ready for JSON export with scenario '{EvidenceExportService.GetMeasurementDisplayName(scenario)}' and best-effort runtime context.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Observation evidence preparation failed.");
            ClearExportEvidence("Observation completed, but evidence export preparation failed. See the diagnostics log for details.");
        }
    }

    private void PrepareBaselineEvidenceForScenario(
        List<KernelLatencyCaptureResponse> captures,
        List<BaselineWindowEvidence> windows,
        List<MeasurementRuntimeWindow> runtimeWindows,
        BaselineQualityResult quality,
        bool isPartial,
        MeasurementScenario scenario)
    {
        PrepareGpuAffinityCandidatePlan(captures, quality, isPartial, scenario);

        try
        {
            var evidenceType = isPartial ? "baseline-partial" : "baseline";
            SetExportEvidence(
                EvidenceExportService.CreateBaselineJson(GetProductVersion(), scenario, captures, windows, runtimeWindows, quality),
                EvidenceExportService.CreateSuggestedFileName(evidenceType, captures[0].StartedAtUtc),
                isPartial
                    ? $"Partial baseline evidence is ready for JSON export with scenario '{EvidenceExportService.GetMeasurementDisplayName(scenario)}' and best-effort runtime context."
                    : $"Full baseline evidence is ready for JSON export with scenario '{EvidenceExportService.GetMeasurementDisplayName(scenario)}' and best-effort runtime context.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Baseline evidence preparation failed.");
            ClearExportEvidence("Baseline result remains available, but evidence export preparation failed. See the diagnostics log for details.");
        }
    }
}

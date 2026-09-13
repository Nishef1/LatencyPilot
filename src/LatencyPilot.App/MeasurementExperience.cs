using System.Globalization;
using LatencyPilot.App.Services;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Protocol;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private ComboBox? _measurementScenarioComboBox;
    private TextBlock? _measurementScenarioGuidanceText;
    private Border? _measurementScenarioCard;
    private bool _measurementExperienceInitialized;

    internal void InitializeMeasurementExperience()
    {
        if (_measurementExperienceInitialized)
        {
            return;
        }

        _measurementExperienceInitialized = true;

        CaptureObservationButton.Click -= CaptureObservationButton_Click;
        CaptureBaselineButton.Click -= CaptureBaselineButton_Click;
        CaptureObservationButton.Click += CaptureObservationWithScenarioButton_Click;
        CaptureBaselineButton.Click += CaptureBaselineQuietButton_Click;

        RebuildMeasurementScenarioCard();
        RootGrid.ActualThemeChanged += (_, _) => RebuildMeasurementScenarioCard();
        _accessibilitySettings.HighContrastChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(RebuildMeasurementScenarioCard);
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

        var root = new Grid
        {
            ColumnSpacing = 16,
            RowSpacing = 8,
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Spacing = 3 };
        heading.Children.Add(new TextBlock
        {
            Text = "Measurement scenario",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeBrush("TextBrush"),
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Choose the context before capture so the result and exported evidence describe what was actually measured.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
        });
        root.Children.Add(heading);

        _measurementScenarioComboBox = new ComboBox
        {
            MinWidth = 230,
            VerticalAlignment = VerticalAlignment.Center,
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
        AutomationProperties.SetName(_measurementScenarioComboBox, "Measurement scenario");
        AutomationProperties.SetHelpText(
            _measurementScenarioComboBox,
            "Select whether this run represents a real-world workload, controlled idle, or a before/after comparison.");
        Grid.SetColumn(_measurementScenarioComboBox, 1);
        root.Children.Add(_measurementScenarioComboBox);

        _measurementScenarioGuidanceText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("MutedTextBrush"),
        };
        Grid.SetRow(_measurementScenarioGuidanceText, 1);
        Grid.SetColumnSpan(_measurementScenarioGuidanceText, 2);
        root.Children.Add(_measurementScenarioGuidanceText);
        UpdateMeasurementScenarioGuidance();

        card.Child = root;
        return card;
    }

    private void MeasurementScenarioComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateMeasurementScenarioGuidance();

    private void UpdateMeasurementScenarioGuidance()
    {
        if (_measurementScenarioGuidanceText is null)
        {
            return;
        }

        var scenario = SelectedMeasurementScenario;
        _measurementScenarioGuidanceText.Text = EvidenceExportService.GetMeasurementGuidance(scenario);
    }

    private async void CaptureObservationWithScenarioButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureObservationServiceReadyAsync())
        {
            return;
        }

        var scenario = SelectedMeasurementScenario;
        ClearExportEvidence("Capture in progress. Evidence export becomes available after completion.");
        SetMeasurementBusy(true);
        KernelCaptureStatusText.Text = "Capturing DPC/ISR activity for 5 seconds…";
        ObservationQualityText.Text =
            $"{EvidenceExportService.GetMeasurementDisplayName(scenario)}: {EvidenceExportService.GetMeasurementGuidance(scenario)} No interpretation is made until capture completes.";

        try
        {
            var capture = await ObservationServiceClient.CaptureKernelLatencyAsync(
                ObservationDuration,
                ObservationMaximumEvents);

            RenderCapture(capture);
            ApplyP999Adequacy(capture);
            ApplyScenarioResultContext(capture, scenario);
            PrepareObservationEvidenceForScenario(capture, scenario);
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

    private async void CaptureBaselineQuietButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureObservationServiceReadyAsync())
        {
            return;
        }

        var scenario = SelectedMeasurementScenario;
        ClearExportEvidence("Baseline capture in progress. Export is prepared only after the capture sequence stops or completes.");
        ClearCaptureMetrics();
        SetMeasurementBusy(true);
        BaselineProgressBar.Value = 0;
        BaselineVerdictText.Text = "Capturing";
        BaselineStatusText.Text = $"Preparing {BaselineWindowCount} quiet five-second windows. The UI will settle before the first capture.";
        BaselineMetricsText.Text = "Noise and drift will be computed after all required windows complete.";
        BaselineReasonsText.Text =
            $"Scenario: {EvidenceExportService.GetMeasurementDisplayName(scenario)}. {EvidenceExportService.GetMeasurementGuidance(scenario)} Detailed lists and charts are intentionally not redrawn between windows.";
        BaselineWindowsList.ItemsSource = null;

        var windows = new List<BaselineWindowEvidence>(BaselineWindowCount);
        var captures = new List<KernelLatencyCaptureResponse>(BaselineWindowCount);

        try
        {
            await Task.Delay(BaselineInterWindowDelay);

            for (var index = 1; index <= BaselineWindowCount; index++)
            {
                var capture = await ObservationServiceClient.CaptureKernelLatencyAsync(
                    ObservationDuration,
                    ObservationMaximumEvents);

                captures.Add(capture);
                var integrityIssue = GetCaptureIntegrityIssue(capture);
                windows.Add(new BaselineWindowEvidence(
                    index,
                    capture.StartedAtUtc,
                    integrityIssue is null,
                    integrityIssue,
                    capture.Dpc.Count,
                    capture.Dpc.P99Microseconds,
                    capture.Isr.Count,
                    capture.Isr.P99Microseconds));

                BaselineProgressBar.Value = index;
                BaselineStatusText.Text = index < BaselineWindowCount
                    ? $"Window {index} of {BaselineWindowCount} complete. Settling before the next quiet capture…"
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
            KernelCaptureStatusText.Text =
                "Repeated baseline complete. The observation cards show only the final window snapshot; the baseline verdict below uses all five windows.";
            ObservationQualityText.Text =
                $"Scenario: {EvidenceExportService.GetMeasurementDisplayName(scenario)}. {EvidenceExportService.GetMeasurementGuidance(scenario)} Full charts and contributor lists were withheld between windows to reduce observer activity.";

            var quality = BaselineQualityAnalyzer.Analyze(windows, BaselinePolicy);
            RenderBaselineQuality(quality);
            PrepareBaselineEvidenceForScenario(captures, windows, quality, isPartial: false, scenario);
        }
        catch (Exception exception)
        {
            HandleCaptureFailure(exception, "Repeated baseline capture");
            BaselineVerdictText.Text = "Inconclusive";
            BaselineStatusText.Text = $"Baseline capture stopped after {windows.Count} of {BaselineWindowCount} windows.";
            BaselineWindowsList.ItemsSource = CreateBaselineWindowRows(windows);

            if (windows.Count > 0)
            {
                var partialQuality = BaselineQualityAnalyzer.Analyze(windows, BaselinePolicy);
                RenderBaselineQuality(partialQuality, preserveStatusText: true);
                PrepareBaselineEvidenceForScenario(captures, windows, partialQuality, isPartial: true, scenario);
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

    private void SetMeasurementBusy(bool busy)
    {
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
            ? $"{prefix}Capture integrity looks clean. {FormatCaptureInterpretation(capture)}"
            : $"{prefix}Treat this observation as incomplete evidence. Tail/guidance classification is withheld because capture integrity is not clean. Exact values remain visible for diagnosis.";
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
                    $"p99.9 is shown because at least {ObservationProtocol.MinimumSamplesForP999:N0} {label} samples were observed."));
            return;
        }

        target.Text = "Need ≥1k";
        target.FontSize = 20;
        var explanation = string.Create(
            CultureInfo.InvariantCulture,
            $"p99.9 is withheld: {distribution.Count:N0} {label} samples were observed; at least {ObservationProtocol.MinimumSamplesForP999:N0} are required. p99 and max remain available.");
        AutomationProperties.SetHelpText(target, explanation);
        ToolTipService.SetToolTip(target, explanation);
    }

    private void PrepareObservationEvidenceForScenario(
        KernelLatencyCaptureResponse capture,
        MeasurementScenario scenario)
    {
        try
        {
            SetExportEvidence(
                EvidenceExportService.CreateObservationJson(GetProductVersion(), scenario, capture),
                EvidenceExportService.CreateSuggestedFileName("observation", capture.StartedAtUtc),
                $"Observation evidence is ready for JSON export with scenario '{EvidenceExportService.GetMeasurementDisplayName(scenario)}'.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Observation evidence preparation failed.");
            ClearExportEvidence("Observation completed, but evidence export preparation failed. See the diagnostics log for details.");
        }
    }

    private void PrepareBaselineEvidenceForScenario(
        List<KernelLatencyCaptureResponse> captures,
        IReadOnlyList<BaselineWindowEvidence> windows,
        BaselineQualityResult quality,
        bool isPartial,
        MeasurementScenario scenario)
    {
        try
        {
            var evidenceType = isPartial ? "baseline-partial" : "baseline";
            SetExportEvidence(
                EvidenceExportService.CreateBaselineJson(GetProductVersion(), scenario, captures, windows, quality),
                EvidenceExportService.CreateSuggestedFileName(evidenceType, captures[0].StartedAtUtc),
                isPartial
                    ? $"Partial baseline evidence is ready for JSON export with scenario '{EvidenceExportService.GetMeasurementDisplayName(scenario)}'."
                    : $"Full baseline evidence is ready for JSON export with scenario '{EvidenceExportService.GetMeasurementDisplayName(scenario)}'.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Baseline evidence preparation failed.");
            ClearExportEvidence("Baseline result remains available, but evidence export preparation failed. See the diagnostics log for details.");
        }
    }
}

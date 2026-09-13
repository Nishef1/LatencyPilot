using LatencyPilot.App.Services;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private ComboBox? _hardenedScenarioComboBox;
    private bool _observationExperienceHardeningInitialized;

    internal void InitializeObservationExperienceHardening()
    {
        if (_observationExperienceHardeningInitialized)
        {
            return;
        }

        _observationExperienceHardeningInitialized = true;
        ApplyObservationExperienceHardening();

        RootGrid.ActualThemeChanged += (_, _) => ApplyObservationExperienceHardening();
        _accessibilitySettings.HighContrastChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(ApplyObservationExperienceHardening);
    }

    private void ApplyObservationExperienceHardening()
    {
        HideDecorativeTailBarsFromControlView(_dpcGuidanceBar);
        HideDecorativeTailBarsFromControlView(_isrGuidanceBar);
        HideDecorativeTailBarsFromControlView(_oneMillisecondBar);
        HideDecorativeTailBarsFromControlView(_threeMillisecondBar);

        if (!ReferenceEquals(_hardenedScenarioComboBox, _measurementScenarioComboBox))
        {
            if (_hardenedScenarioComboBox is not null)
            {
                _hardenedScenarioComboBox.SelectionChanged -= MeasurementScenarioSelection_InvalidatesPreviousEvidence;
            }

            _hardenedScenarioComboBox = _measurementScenarioComboBox;
            if (_hardenedScenarioComboBox is not null)
            {
                _hardenedScenarioComboBox.SelectionChanged += MeasurementScenarioSelection_InvalidatesPreviousEvidence;
            }
        }

        if (_measurementScenarioComboBox is not null)
        {
            // RefreshServiceButton is disabled only while a measurement/service refresh operation is busy.
            // Rebuilt theme resources must not accidentally re-enable scenario changes mid-capture.
            _measurementScenarioComboBox.IsEnabled = RefreshServiceButton.IsEnabled;
        }
    }

    private static void HideDecorativeTailBarsFromControlView(ProgressBar? bar)
    {
        if (bar is null)
        {
            return;
        }

        // Exact counts/percentages remain visible as TextBlocks. The ProgressBar is only a data-bar visual,
        // so remove it from the control view to avoid announcing it as operation progress.
        AutomationProperties.SetAccessibilityView(bar, AccessibilityView.Raw);
    }

    private void MeasurementScenarioSelection_InvalidatesPreviousEvidence(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_latestEvidenceJson is null && BaselineWindowsList.ItemsSource is null)
        {
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
    }
}

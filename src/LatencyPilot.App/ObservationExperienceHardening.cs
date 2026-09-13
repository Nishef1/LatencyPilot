using LatencyPilot.App.Services;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private void InitializeObservationExperienceHardening()
    {
        ApplyObservationExperienceHardening();
        RootGrid.ActualThemeChanged += (_, _) => ApplyObservationExperienceHardening();
        _accessibilitySettings.HighContrastChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(ApplyObservationExperienceHardening);
    }

    private void ApplyObservationExperienceHardening()
    {
        if (_measurementScenarioComboBox is not null)
        {
            // Theme/high-contrast rebuilds can replace the ComboBox while a capture is active.
            // Restore the explicit measurement-busy state instead of inferring it from unrelated controls.
            _measurementScenarioComboBox.IsEnabled = !_measurementBusy;
        }
    }

    private void MeasurementScenarioSelection_InvalidatesPreviousEvidence(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_latestEvidenceJson is null && BaselineWindowsList.ItemsSource is null)
        {
            ResetRuntimeContextSummary(
                "Runtime context will appear after capture: average system CPU busy time, power source and Battery Saver state.");
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
}

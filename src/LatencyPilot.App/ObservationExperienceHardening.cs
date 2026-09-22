namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private void InitializeObservationExperienceHardening()
    {
        // Scenario invalidation and theme rebuild behavior live with the measurement experience.
        // Keep this seam for source-level evidence UX that must be initialized after XAML fields exist.
        UpdateScenarioSelectionEnabledState();

        ApplyBuildProvenanceUi();
        InitializeGateAValidationExperience();
        InitializeMeasurementReadinessExperience();
        InitializeBaselineProgressExperience();
        InitializePremiumOverviewExperience();
    }
}

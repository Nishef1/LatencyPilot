namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private void InitializeObservationExperienceHardening()
    {
        // Scenario invalidation and theme rebuild behavior now live with the measurement experience.
        // Keep this initialization seam focused on restoring the explicit busy-state invariant.
        if (_measurementScenarioComboBox is not null)
        {
            _measurementScenarioComboBox.IsEnabled = !_measurementBusy;
        }
    }
}

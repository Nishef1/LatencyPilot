using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private bool _dashboardCaptureSyncInitialized;

    internal void InitializeDashboardCaptureSync()
    {
        if (_dashboardCaptureSyncInitialized)
        {
            return;
        }

        _dashboardCaptureSyncInitialized = true;
        KernelCaptureStatusText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) =>
            {
                if (_lastPremiumCapture is null)
                {
                    ClearDashboardCaptureVisuals();
                    return;
                }

                RenderDashboardCapture(_lastPremiumCapture);
            });
    }
}

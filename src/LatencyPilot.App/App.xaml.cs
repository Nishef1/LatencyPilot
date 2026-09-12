using Microsoft.UI.Xaml;

namespace LatencyPilot.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            StartupFailureReporter.Report("WinUI application initialization failed.", exception);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
            _window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
            _window.Activate();
        }
        catch (Exception exception)
        {
            StartupFailureReporter.Report("LatencyPilot could not open its main window.", exception);
            throw;
        }
    }
}

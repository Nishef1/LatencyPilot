using Microsoft.UI.Xaml;
using Serilog;

namespace LatencyPilot.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        AppLogging.Initialize();
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => AppLogging.Close();
        AppDomain.CurrentDomain.UnhandledException += static (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "Unhandled AppDomain exception. Terminating={IsTerminating}.", args.IsTerminating);
            }
        };
        TaskScheduler.UnobservedTaskException += static (_, args) =>
            Log.Error(args.Exception, "Unobserved task exception.");
        UnhandledException += static (_, args) =>
            Log.Error(args.Exception, "Unhandled WinUI exception.");

        try
        {
            InitializeComponent();
            Log.Information("WinUI application initialized.");
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
            Log.Information("Main window activated.");
        }
        catch (Exception exception)
        {
            StartupFailureReporter.Report("LatencyPilot could not open its main window.", exception);
            throw;
        }
    }
}

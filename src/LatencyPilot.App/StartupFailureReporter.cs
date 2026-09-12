using System.Runtime.InteropServices;

namespace LatencyPilot.App;

internal static partial class StartupFailureReporter
{
    private const uint ErrorIcon = 0x00000010;

    public static void Report(string message, Exception exception)
    {
        var details = $"{message}{Environment.NewLine}{Environment.NewLine}{exception}";

        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LatencyPilot");
            Directory.CreateDirectory(logDirectory);
            File.WriteAllText(Path.Combine(logDirectory, "startup-error.log"), details);
        }
        catch
        {
            // Startup diagnostics must never hide the original failure.
        }

        try
        {
            MessageBoxW(0, details, "LatencyPilot startup error", ErrorIcon);
        }
        catch
        {
            // The original exception remains authoritative if diagnostics fail.
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint windowHandle, string text, string caption, uint type);
}

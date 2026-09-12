using Serilog;
using Serilog.Formatting.Compact;

namespace LatencyPilot.App;

internal static class AppLogging
{
    private static int initialized;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0)
        {
            return;
        }

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LatencyPilot",
            "Logs",
            "App");
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.WithProperty("Component", "App")
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .WriteTo.Async(
                sink => sink.File(
                    new CompactJsonFormatter(),
                    Path.Combine(logDirectory, "latencypilot-app-.json"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 32 * 1024 * 1024,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: 10,
                    flushToDiskInterval: TimeSpan.FromSeconds(2)),
                bufferSize: 4096,
                blockWhenFull: false)
            .CreateLogger();

        Log.Information("LatencyPilot application logging initialized.");
    }

    public static void Close()
    {
        if (Volatile.Read(ref initialized) == 0)
        {
            return;
        }

        try
        {
            Log.Information("LatencyPilot application shutting down.");
            Log.CloseAndFlush();
        }
        catch
        {
            // Logging must never interfere with process shutdown.
        }
    }
}

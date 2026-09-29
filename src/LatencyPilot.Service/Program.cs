using LatencyPilot.Persistence;
using LatencyPilot.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

if (args.Length > 0 &&
    args[0] is "--check-uninstall" or "--check-replacement")
{
    if (args.Length != 1)
    {
        Console.Error.WriteLine("The journal safety check accepts no additional arguments.");
        return 2;
    }

    var replacementCheck = args[0] == "--check-replacement";
    try
    {
        // Run before host/logging initialization: inspection must not create or
        // repair a missing journal and must never start observation or mutation.
        var databasePath = MutationJournal.GetDefaultDatabasePath();
        if (replacementCheck)
        {
            MutationJournalReadOnlyInspector.EnsureSafeForReplacement(databasePath);
            Console.WriteLine("LATENCYPILOT_REPLACEMENT_SAFE_V1");
        }
        else
        {
            MutationJournalReadOnlyInspector.EnsureSafeForUninstall(databasePath);
            Console.WriteLine("LATENCYPILOT_UNINSTALL_SAFE_V1");
        }

        return 0;
    }
    catch (Exception exception) when (exception is
        IOException or
        UnauthorizedAccessException or
        System.Security.SecurityException or
        InvalidOperationException or
        InvalidDataException or
        FormatException or
        OverflowException or
        Microsoft.Data.Sqlite.SqliteException)
    {
        Console.Error.WriteLine(
            $"{(replacementCheck ? "Replacement" : "Uninstall")} blocked: {exception.Message}");
        return 1;
    }
}

var builder = Host.CreateApplicationBuilder(args);

Exception? fileLoggingStartupFailure = null;
try
{
    var logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "LatencyPilot",
        "Logs",
        "Service");
    Directory.CreateDirectory(logDirectory);

    var fileLogger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .Enrich.WithProperty("Component", "Service")
        .Enrich.WithProperty("ProcessId", Environment.ProcessId)
        .WriteTo.Async(
            sink => sink.File(
                new RenderedCompactJsonFormatter(),
                Path.Combine(logDirectory, "latencypilot-service-.json"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 32 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 10,
                flushToDiskInterval: TimeSpan.FromSeconds(2)),
            bufferSize: 4096,
            blockWhenFull: false)
        .CreateLogger();

    builder.Services.AddSerilog(fileLogger, dispose: true);
}
catch (Exception exception) when (
    exception is IOException or
    UnauthorizedAccessException or
    System.Security.SecurityException)
{
    // Diagnostics must never prevent the observation service from starting.
    // Leave the default Microsoft.Extensions.Logging providers active.
    fileLoggingStartupFailure = exception;
}

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = ServiceBoundary.ServiceName;
});

// Recovery inspection starts before the pipe host. It initializes the durable
// journal and re-reads actual state for any unresolved known mutation without
// applying or reverting anything. Observation remains available even if this
// inspection fails, while mutation stays fail-closed.
builder.Services.Configure<HostOptions>(options => options.ServicesStartConcurrently = false);
builder.Services.AddSingleton<MutationRecoveryReadiness>();
builder.Services.AddHostedService<MutationRecoveryInspector>();
builder.Services.AddHostedService<ObservationHost>();

var host = builder.Build();
if (fileLoggingStartupFailure is not null)
{
    var startupLogger = host.Services
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("LatencyPilot.Service.Startup");
    var logFileUnavailable = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1000, "StructuredFileLoggingUnavailable"),
        "Structured file logging could not be initialized; default logging providers remain active.");
    logFileUnavailable(startupLogger, fileLoggingStartupFailure);
}

await host.RunAsync();
return 0;

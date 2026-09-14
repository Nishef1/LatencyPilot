using LatencyPilot.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

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

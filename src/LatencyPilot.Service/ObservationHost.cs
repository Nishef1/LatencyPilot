using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LatencyPilot.Service;

internal sealed class ObservationHost(ILogger<ObservationHost> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "LatencyPilot privileged observation host started. Mutation authority: {MutationAvailable}.",
            ServiceBoundary.MutationAvailable);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal Windows Service shutdown.
        }

        logger.LogInformation("LatencyPilot privileged observation host stopped.");
    }
}

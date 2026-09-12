using Microsoft.Extensions.Hosting;

namespace LatencyPilot.Service;

internal sealed class ObservationHost : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal Windows Service shutdown.
        }
    }
}

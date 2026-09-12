using LatencyPilot.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = ServiceBoundary.ServiceName;
});

builder.Services.AddHostedService<ObservationHost>();

await builder.Build().RunAsync();

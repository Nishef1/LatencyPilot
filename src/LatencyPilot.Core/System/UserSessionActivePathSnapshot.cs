using LatencyPilot.Core.Devices;

namespace LatencyPilot.Core.System;

public enum ActivePathDiscoveryStatus
{
    Available,
    Unavailable,
    Failed,
}

public sealed record ActivePathComponent<T>(
    ActivePathDiscoveryStatus Status,
    T? Value,
    string? Error)
    where T : class
{
    public static ActivePathComponent<T> Available(T value) =>
        new(ActivePathDiscoveryStatus.Available, value, null);

    public static ActivePathComponent<T> Unavailable(string reason) =>
        new(ActivePathDiscoveryStatus.Unavailable, null, reason);

    public static ActivePathComponent<T> Failed(string error) =>
        new(ActivePathDiscoveryStatus.Failed, null, error);
}

public sealed record UserSessionActivePathSnapshot(
    ActivePathComponent<ProcessorCpuSetSnapshot> CpuSets,
    ActivePathComponent<GraphicsAdapterInventory> GraphicsAdapters,
    ActivePathComponent<DefaultAudioRouteInventory> DefaultAudioRoutes,
    PresentMonDeviceInventory PresentMonDevices,
    DateTimeOffset CapturedAtUtc)
{
    public bool HasMultipleHardwareGraphicsAdapters =>
        GraphicsAdapters.Value?.HasMultipleHardwareAdapters == true;

    public bool HasResolvedDefaultAudioHardwareRoute =>
        DefaultAudioRoutes.Value?.Routes.Any(static route => route.HasResolvedHardwareRoute) == true;

    public IReadOnlyList<GraphicsAdapterSnapshot> PresentMonCorrelatedGraphicsAdapters =>
        GraphicsAdapters.Value is not { } graphics || !PresentMonDevices.IsAvailable
            ? []
            : graphics.Adapters
                .Where(adapter => PresentMonDevices.FindByLuid(adapter.Luid) is not null)
                .ToArray();
}

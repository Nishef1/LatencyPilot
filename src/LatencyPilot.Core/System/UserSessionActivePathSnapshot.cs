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
    where T : class;

public sealed record UserSessionActivePathSnapshot(
    ActivePathComponent<ProcessorCpuSetSnapshot> CpuSets,
    ActivePathComponent<GraphicsAdapterInventory> GraphicsAdapters,
    ActivePathComponent<DefaultAudioRouteInventory> DefaultAudioRoutes,
    ActivePathComponent<UserInputRouteInventory> InputRoutes,
    PresentMonDeviceInventory PresentMonDevices,
    DateTimeOffset CapturedAtUtc)
{
    public bool HasMultipleHardwareGraphicsAdapters =>
        GraphicsAdapters.Value?.HasMultipleHardwareAdapters == true;

    public bool HasResolvedDefaultAudioHardwareRoute =>
        DefaultAudioRoutes.Value?.Routes.Any(static route => route.HasResolvedHardwareRoute) == true;

    public bool HasResolvedUsbInputRoute =>
        InputRoutes.Value?.Routes.Any(static route =>
            route.RawInputDevice.ResolutionStatus == RawInputRouteResolutionStatus.Available &&
            route.IsUsbBacked) == true;

    public bool HasResolvedBluetoothInputRoute =>
        InputRoutes.Value?.Routes.Any(static route =>
            route.RawInputDevice.ResolutionStatus == RawInputRouteResolutionStatus.Available &&
            route.IsBluetoothBacked) == true;

    public IReadOnlyList<GraphicsAdapterSnapshot> PresentMonCorrelatedGraphicsAdapters =>
        GraphicsAdapters.Value is not { } graphics || !PresentMonDevices.IsAvailable
            ? []
            : graphics.Adapters
                .Where(adapter => PresentMonDevices.FindByLuid(adapter.Luid) is not null)
                .ToArray();
}

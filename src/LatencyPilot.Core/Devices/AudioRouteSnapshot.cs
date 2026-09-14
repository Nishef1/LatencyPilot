namespace LatencyPilot.Core.Devices;

public enum AudioEndpointRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

public enum AudioRouteResolutionStatus
{
    Available,
    EndpointUnavailable,
    TopologyUnavailable,
    ConnectedDeviceUnavailable,
    ReadFailed,
}

public sealed record DefaultAudioRouteSnapshot(
    AudioEndpointRole Role,
    AudioRouteResolutionStatus Status,
    string? EndpointId,
    string? ConnectedTopologyDeviceId,
    string? Error,
    DateTimeOffset CapturedAtUtc)
{
    public bool HasResolvedHardwareRoute =>
        Status == AudioRouteResolutionStatus.Available &&
        !string.IsNullOrWhiteSpace(ConnectedTopologyDeviceId);
}

public sealed record DefaultAudioRouteInventory(
    IReadOnlyList<DefaultAudioRouteSnapshot> Routes,
    DateTimeOffset CapturedAtUtc)
{
    public DefaultAudioRouteSnapshot? ForRole(AudioEndpointRole role) =>
        Routes.FirstOrDefault(route => route.Role == role);

    public bool RolesShareEndpoint => Routes
        .Where(static route => !string.IsNullOrWhiteSpace(route.EndpointId))
        .Select(static route => route.EndpointId)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(2)
        .Count() <= 1;
}

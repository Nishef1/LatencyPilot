namespace LatencyPilot.Core.Devices;

public enum NetworkRssReadStatus
{
    Available = 0,
    ProviderUnavailable = 1,
    AccessDenied = 2,
    ReadFailed = 3,
}

public enum NetworkRssPnpCorrelationStatus
{
    Available = 0,
    MissingIdentity = 1,
    NotFound = 2,
    Ambiguous = 3,
}

public sealed record NetworkRssPnpCorrelation(
    NetworkRssPnpCorrelationStatus Status,
    string? PnpInstanceId,
    string? Reason)
{
    public bool IsAvailable => Status == NetworkRssPnpCorrelationStatus.Available && PnpInstanceId is not null;
}

public sealed record NetworkRssAdapterSnapshot(
    string? Name,
    string? InterfaceDescription,
    bool? Enabled,
    bool? MsiSupported,
    bool? MsiXSupported,
    bool? MsiXEnabled,
    uint? NumberOfInterruptMessages,
    uint? NumberOfReceiveQueues,
    uint? Profile,
    ushort? BaseProcessorGroup,
    byte? BaseProcessorNumber,
    ushort? MaxProcessorGroup,
    byte? MaxProcessorNumber,
    uint? MaxProcessors,
    ushort? NumaNode,
    IReadOnlyList<string> IndirectionTable,
    IReadOnlyList<string> RssProcessorArray)
{
    public NetworkRssPnpCorrelation PnpCorrelation { get; init; } = new(
        NetworkRssPnpCorrelationStatus.MissingIdentity,
        null,
        "No authoritative PnP correlation has been established for this RSS provider row.");
}

public sealed record NetworkRssSnapshot(
    NetworkRssReadStatus Status,
    IReadOnlyList<NetworkRssAdapterSnapshot> Adapters,
    DateTimeOffset CapturedAtUtc,
    string? Error)
{
    public bool IsAvailable => Status == NetworkRssReadStatus.Available;
}

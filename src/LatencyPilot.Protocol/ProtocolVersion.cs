namespace LatencyPilot.Protocol;

public static class ProtocolVersion
{
    public const int Current = 1;
}

public sealed record ServiceCommandEnvelope(
    int ProtocolVersion,
    Guid RequestId,
    string CommandName);

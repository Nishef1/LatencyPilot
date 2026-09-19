using System.Text.Json;

namespace LatencyPilot.Platform.Windows.Devices;

public enum DeviceInterruptMutationOperation { EnableMsi = 0, XhciAffinity = 1 }

public sealed record DeviceInterruptMutationCandidate(
    DeviceInterruptMutationOperation Operation, ushort? ProcessorGroup, byte? ProcessorNumber, ulong? AffinityMask)
{
    public static DeviceInterruptMutationCandidate EnableMsi() => new(DeviceInterruptMutationOperation.EnableMsi, null, null, null);
    public static DeviceInterruptMutationCandidate XhciAffinity(DeviceInterruptAffinityCandidate c) => new(DeviceInterruptMutationOperation.XhciAffinity, c.ProcessorGroup, c.ProcessorNumber, c.AffinityMask);
    public DeviceInterruptAffinityCandidate ToAffinityCandidate() => Operation == DeviceInterruptMutationOperation.XhciAffinity && ProcessorGroup is { } g && ProcessorNumber is { } n && AffinityMask is { } m ? new(g, n, m) : throw new InvalidDataException("Journal candidate does not contain xHCI affinity identity.");
}

public static class DeviceInterruptMutationJournalCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string SerializeOriginal(DeviceInterruptConfigurationSnapshot value) => JsonSerializer.Serialize(value, Options);
    public static DeviceInterruptConfigurationSnapshot DeserializeOriginal(string json) => JsonSerializer.Deserialize<DeviceInterruptConfigurationSnapshot>(json, Options) ?? throw new InvalidDataException("Device interrupt original snapshot is empty.");
    public static string SerializeCandidate(DeviceInterruptMutationCandidate value) => JsonSerializer.Serialize(value, Options);
    public static DeviceInterruptMutationCandidate DeserializeCandidate(string json) => JsonSerializer.Deserialize<DeviceInterruptMutationCandidate>(json, Options) ?? throw new InvalidDataException("Device interrupt candidate is empty.");
}

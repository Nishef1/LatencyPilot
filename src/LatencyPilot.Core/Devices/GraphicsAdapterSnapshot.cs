namespace LatencyPilot.Core.Devices;

public readonly record struct GraphicsAdapterLuid(uint LowPart, int HighPart)
{
    public override string ToString() => $"{unchecked((uint)HighPart):X8}:{LowPart:X8}";
}

public sealed record GraphicsAdapterSnapshot(
    int EnumerationIndex,
    string Description,
    uint VendorId,
    uint DeviceId,
    uint SubSystemId,
    uint Revision,
    ulong DedicatedVideoMemoryBytes,
    ulong DedicatedSystemMemoryBytes,
    ulong SharedSystemMemoryBytes,
    GraphicsAdapterLuid Luid,
    uint NativeFlags)
{
    private const uint SoftwareFlag = 0x2;

    public bool IsSoftwareAdapter => (NativeFlags & SoftwareFlag) != 0;

    public bool IsHardwareAdapter => !IsSoftwareAdapter;
}

public sealed record GraphicsAdapterInventory(
    IReadOnlyList<GraphicsAdapterSnapshot> Adapters,
    DateTimeOffset CapturedAtUtc)
{
    public IReadOnlyList<GraphicsAdapterSnapshot> HardwareAdapters =>
        Adapters.Where(static adapter => adapter.IsHardwareAdapter).ToArray();

    public int HardwareAdapterCount => Adapters.Count(static adapter => adapter.IsHardwareAdapter);

    public bool HasMultipleHardwareAdapters => HardwareAdapterCount > 1;

    public GraphicsAdapterSnapshot? FindByLuid(GraphicsAdapterLuid luid) =>
        Adapters.FirstOrDefault(adapter => adapter.Luid == luid);
}

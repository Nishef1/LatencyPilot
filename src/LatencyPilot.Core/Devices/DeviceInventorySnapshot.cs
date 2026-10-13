namespace LatencyPilot.Core.Devices;

public sealed record DriverMetadataSnapshot(
    string? Version,
    string? Provider,
    string? InfPath)
{
    public bool IsAvailable => Version is not null || Provider is not null || InfPath is not null;
}

public enum InterruptConfigurationReadStatus
{
    Available,
    HardwareKeyUnavailable,
    ReadFailed,
}

public sealed record InterruptConfigurationSnapshot(
    InterruptConfigurationReadStatus ReadStatus,
    uint? NativeErrorCode,
    uint? MsiSupported,
    uint? MessageNumberLimit,
    uint? DevicePolicy,
    ulong? AssignmentSetOverrideMask)
{
    public bool InterruptManagementKeyExists { get; init; }

    public bool HasAnyConfiguration =>
        MsiSupported is not null ||
        MessageNumberLimit is not null ||
        DevicePolicy is not null ||
        AssignmentSetOverrideMask is not null;

    public bool IsMsiConfiguredEnabled => MsiSupported == 1;

    public static InterruptConfigurationSnapshot Available(
        uint? msiSupported,
        uint? messageNumberLimit,
        uint? devicePolicy,
        ulong? assignmentSetOverrideMask) =>
        new(
            InterruptConfigurationReadStatus.Available,
            null,
            msiSupported,
            messageNumberLimit,
            devicePolicy,
            assignmentSetOverrideMask);

    public static InterruptConfigurationSnapshot HardwareKeyUnavailable(uint nativeErrorCode) =>
        new(
            InterruptConfigurationReadStatus.HardwareKeyUnavailable,
            nativeErrorCode,
            null,
            null,
            null,
            null);

    public static InterruptConfigurationSnapshot ReadFailed(uint? nativeErrorCode = null) =>
        new(
            InterruptConfigurationReadStatus.ReadFailed,
            nativeErrorCode,
            null,
            null,
            null,
            null);
}

public enum InterruptResourceReadStatus
{
    Available,
    NoAllocatedConfiguration,
    ApiUnavailable,
    ReadFailed,
    /// <summary>
    /// ConfigMgr returned structurally valid IRQ descriptors whose IRQD_Affinity is 0,
    /// i.e. no explicit processor target. This is readable but is not placement evidence.
    /// </summary>
    AffinityUnspecified,
}

/// <summary>
/// <paramref name="ReportedProcessorGroup"/> is non-null only when ConfigMgr reported a
/// group that cannot exist on this machine and <paramref name="ProcessorGroup"/> was
/// normalized to the only group Windows exposes.
/// </summary>
public sealed record AllocatedInterruptResourceSnapshot(
    uint Irq,
    ushort ProcessorGroup,
    ulong AffinityMask,
    ushort RawFlags,
    ushort? ReportedProcessorGroup = null);

public sealed record InterruptResourceSnapshot(
    InterruptResourceReadStatus ReadStatus,
    uint? NativeStatusCode,
    IReadOnlyList<AllocatedInterruptResourceSnapshot> Resources)
{
    public bool HasAssignedInterrupts => Resources.Count > 0;

    public static InterruptResourceSnapshot Available(IReadOnlyList<AllocatedInterruptResourceSnapshot> resources) =>
        new(InterruptResourceReadStatus.Available, null, resources);

    public static InterruptResourceSnapshot NoAllocatedConfiguration(uint nativeStatusCode) =>
        new(InterruptResourceReadStatus.NoAllocatedConfiguration, nativeStatusCode, []);

    public static InterruptResourceSnapshot ApiUnavailable(uint nativeStatusCode) =>
        new(InterruptResourceReadStatus.ApiUnavailable, nativeStatusCode, []);

    public static InterruptResourceSnapshot ReadFailed(uint? nativeStatusCode = null) =>
        new(InterruptResourceReadStatus.ReadFailed, nativeStatusCode, []);

    public static InterruptResourceSnapshot AffinityUnspecified() =>
        new(InterruptResourceReadStatus.AffinityUnspecified, null, []);
}

public enum DeviceParentReadStatus
{
    Available,
    Unavailable,
    ReadFailed,
}

public sealed record DeviceParentSnapshot(
    DeviceParentReadStatus ReadStatus,
    uint? NativeStatusCode,
    string? ParentInstanceId)
{
    public static DeviceParentSnapshot Available(string parentInstanceId) =>
        new(DeviceParentReadStatus.Available, null, parentInstanceId);

    public static DeviceParentSnapshot Unavailable(uint nativeStatusCode) =>
        new(DeviceParentReadStatus.Unavailable, nativeStatusCode, null);

    public static DeviceParentSnapshot ReadFailed(uint? nativeStatusCode = null) =>
        new(DeviceParentReadStatus.ReadFailed, nativeStatusCode, null);
}

public sealed record PnPDeviceSnapshot(
    string InstanceId,
    Guid ClassGuid,
    string DisplayName,
    string? Manufacturer,
    string? EnumeratorName,
    string? ServiceName,
    DriverMetadataSnapshot Driver,
    InterruptConfigurationSnapshot InterruptConfiguration,
    InterruptResourceSnapshot InterruptResources)
{
    public DeviceParentSnapshot Parent { get; init; } = DeviceParentSnapshot.ReadFailed();
}

public sealed record DeviceInventorySnapshot(
    IReadOnlyList<PnPDeviceSnapshot> Devices,
    DateTimeOffset CapturedAtUtc)
{
    public int PresentDeviceCount => Devices.Count;

    public int DevicesWithDriverMetadataCount => Devices.Count(static device => device.Driver.IsAvailable);

    public int DevicesWithReadableInterruptConfigurationCount => Devices.Count(
        static device => device.InterruptConfiguration.ReadStatus == InterruptConfigurationReadStatus.Available);

    public int DevicesWithAssignedInterruptsCount => Devices.Count(
        static device => device.InterruptResources.HasAssignedInterrupts);

    public int DevicesWithKnownParentCount => Devices.Count(
        static device => device.Parent.ReadStatus == DeviceParentReadStatus.Available);
}

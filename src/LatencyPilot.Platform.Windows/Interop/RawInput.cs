using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct RawInputDeviceListEntry
{
    internal readonly nint DeviceHandle;
    internal readonly uint Type;
}

[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct RawInputDeviceInfo
{
    [FieldOffset(0)]
    internal uint Size;

    [FieldOffset(4)]
    internal uint Type;

    [FieldOffset(8)]
    internal uint HidVendorId;

    [FieldOffset(12)]
    internal uint HidProductId;

    [FieldOffset(16)]
    internal uint HidVersionNumber;

    [FieldOffset(20)]
    internal ushort HidUsagePage;

    [FieldOffset(22)]
    internal ushort HidUsage;

    internal static RawInputDeviceInfo Create() => new()
    {
        Size = checked((uint)Marshal.SizeOf<RawInputDeviceInfo>()),
    };
}

internal static partial class RawInput
{
    internal const uint TypeMouse = 0;
    internal const uint TypeKeyboard = 1;
    internal const uint TypeHid = 2;

    internal const uint DeviceName = 0x20000007;
    internal const uint DeviceInfo = 0x2000000B;
    internal const uint ErrorResult = uint.MaxValue;

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static unsafe partial uint GetRawInputDeviceList(
        RawInputDeviceListEntry* rawInputDeviceList,
        ref uint deviceCount,
        uint entrySize);

    [LibraryImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    internal static unsafe partial uint GetRawInputDeviceInfo(
        nint deviceHandle,
        uint command,
        void* data,
        ref uint dataSize);
}

using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.Interop;

internal enum NativeRawInputDeviceType : uint
{
    Mouse = 0,
    Keyboard = 1,
    HumanInterface = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct RawInputDeviceListEntry
{
    internal readonly nint DeviceHandle;
    internal readonly NativeRawInputDeviceType Type;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RidDeviceInfoMouse
{
    internal uint Id;
    internal uint NumberOfButtons;
    internal uint SampleRate;
    internal int HasHorizontalWheel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RidDeviceInfoKeyboard
{
    internal uint Type;
    internal uint SubType;
    internal uint KeyboardMode;
    internal uint NumberOfFunctionKeys;
    internal uint NumberOfIndicators;
    internal uint NumberOfKeysTotal;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RidDeviceInfoHid
{
    internal uint VendorId;
    internal uint ProductId;
    internal uint VersionNumber;
    internal ushort UsagePage;
    internal ushort Usage;
}

[StructLayout(LayoutKind.Explicit)]
internal struct RidDeviceInfo
{
    [FieldOffset(0)]
    internal uint Size;

    [FieldOffset(4)]
    internal NativeRawInputDeviceType Type;

    [FieldOffset(8)]
    internal RidDeviceInfoMouse Mouse;

    [FieldOffset(8)]
    internal RidDeviceInfoKeyboard Keyboard;

    [FieldOffset(8)]
    internal RidDeviceInfoHid Hid;

    internal static RidDeviceInfo Create() => new()
    {
        Size = checked((uint)Marshal.SizeOf<RidDeviceInfo>()),
    };
}

internal static partial class User32RawInput
{
    internal const uint DeviceNameCommand = 0x20000007;
    internal const uint DeviceInfoCommand = 0x2000000B;
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
        ref uint size);
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace LatencyPilot.Platform.Windows.Interop;

internal enum DeviceRegistryProperty : uint
{
    DeviceDescription = 0x00000000,
    Service = 0x00000004,
    Manufacturer = 0x0000000B,
    FriendlyName = 0x0000000C,
    EnumeratorName = 0x00000016,
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct DevicePropertyKey(Guid formatId, uint propertyId)
{
    internal readonly Guid FormatId = formatId;
    internal readonly uint PropertyId = propertyId;
}

internal static class DevicePropertyKeys
{
    private static readonly Guid DriverPackageFormatId = new("a8b865dd-2e3d-4094-ad97-e593a70c75d6");
    private static readonly Guid DeviceFormatId = new("78c34fc8-104a-4aca-9ea4-524d52996e57");

    internal static readonly DevicePropertyKey DriverVersion = new(DriverPackageFormatId, 3);
    internal static readonly DevicePropertyKey DriverInfPath = new(DriverPackageFormatId, 5);
    internal static readonly DevicePropertyKey DriverProvider = new(DriverPackageFormatId, 9);
    internal static readonly DevicePropertyKey DeviceInstanceId = new(DeviceFormatId, 256);
}

[StructLayout(LayoutKind.Sequential)]
internal struct SpDevInfoData
{
    internal uint Size;
    internal Guid ClassGuid;
    internal uint DevInst;
    internal nuint Reserved;

    internal static SpDevInfoData Create() => new()
    {
        Size = checked((uint)Marshal.SizeOf<SpDevInfoData>()),
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct SpClassInstallHeader
{
    internal uint Size;
    internal uint InstallFunction;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SpPropChangeParams
{
    internal SpClassInstallHeader ClassInstallHeader;
    internal uint StateChange;
    internal uint Scope;
    internal uint HardwareProfile;

    internal static SpPropChangeParams CreatePropertyChange() => new()
    {
        ClassInstallHeader = new SpClassInstallHeader
        {
            Size = checked((uint)Marshal.SizeOf<SpClassInstallHeader>()),
            InstallFunction = SetupApi.DifPropertyChange,
        },
        StateChange = SetupApi.DicsPropertyChange,
        Scope = SetupApi.DicsFlagGlobal,
        HardwareProfile = 0,
    };
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct SpDevInstallParams
{
    internal uint Size;
    internal uint Flags;
    internal uint FlagsEx;
    internal nint ParentWindow;
    internal nint InstallMessageHandler;
    internal nint InstallMessageHandlerContext;
    internal nint FileQueue;
    internal nuint ClassInstallReserved;
    internal uint Reserved;
    internal fixed char DriverPath[260];

    internal static SpDevInstallParams Create() => new()
    {
        Size = checked((uint)Marshal.SizeOf<SpDevInstallParams>()),
    };
}

internal sealed class SafeDeviceInfoSetHandle : SafeHandleMinusOneIsInvalid
{
    internal SafeDeviceInfoSetHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle() => SetupApi.SetupDiDestroyDeviceInfoList(handle);
}

internal static partial class SetupApi
{
    internal const uint DifPropertyChange = 0x00000012;
    internal const uint DicsPropertyChange = 0x00000003;
    internal const uint DicsFlagGlobal = 0x00000001;
    internal const uint DiNeedRestart = 0x00000080;
    internal const uint DiNeedReboot = 0x00000100;

    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfAllClasses = 0x00000004;
    private const uint DiregDev = 0x00000001;
    private const uint KeyRead = 0x00020019;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    internal static SafeDeviceInfoSetHandle GetPresentDeviceInfoSet()
    {
        var rawHandle = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (rawHandle == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to enumerate present Plug and Play devices.");
        }

        return new SafeDeviceInfoSetHandle(rawHandle);
    }

    internal static RegistryKey? TryOpenDeviceHardwareRegistryKey(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        out uint nativeErrorCode)
    {
        var rawHandle = SetupDiOpenDevRegKey(
            deviceInfoSet,
            ref deviceInfoData,
            DicsFlagGlobal,
            0,
            DiregDev,
            KeyRead);

        if (rawHandle == InvalidHandleValue)
        {
            nativeErrorCode = unchecked((uint)Marshal.GetLastPInvokeError());
            return null;
        }

        nativeErrorCode = 0;
        var safeHandle = new SafeRegistryHandle(rawHandle, ownsHandle: true);
        try
        {
            return RegistryKey.FromHandle(safeHandle, RegistryView.Default);
        }
        catch
        {
            safeHandle.Dispose();
            throw;
        }
    }

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        string? enumerator,
        IntPtr parentWindow,
        uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiEnumDeviceInfo(
        SafeDeviceInfoSetHandle deviceInfoSet,
        uint memberIndex,
        ref SpDevInfoData deviceInfoData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiOpenDeviceInfoW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiOpenDeviceInfo(
        SafeDeviceInfoSetHandle deviceInfoSet,
        string deviceInstanceId,
        IntPtr parentWindow,
        uint openFlags,
        ref SpDevInfoData deviceInfoData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SetupDiGetDeviceInstanceId(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        char* deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SetupDiGetDeviceRegistryProperty(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        DeviceRegistryProperty property,
        out uint propertyRegDataType,
        byte* propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SetupDiGetDeviceProperty(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        in DevicePropertyKey propertyKey,
        out uint propertyType,
        byte* propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize,
        uint flags);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiSetClassInstallParamsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiSetClassInstallParams(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        ref SpClassInstallHeader classInstallParams,
        uint classInstallParamsSize);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiCallClassInstaller(
        uint installFunction,
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstallParamsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiGetDeviceInstallParams(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        ref SpDevInstallParams deviceInstallParams);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    private static partial IntPtr SetupDiOpenDevRegKey(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfoData,
        uint scope,
        uint hardwareProfile,
        uint keyType,
        uint samDesired);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}

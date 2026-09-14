using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.Interop;

internal static partial class ConfigurationManager
{
    internal const uint Success = 0x00000000;
    internal const uint NoMoreLogConfigurations = 0x0000000E;
    internal const uint NoMoreResourceDescriptors = 0x0000000F;
    internal const uint CallNotImplemented = 0x00000034;

    internal const uint AllocatedLogConfiguration = 0x00000002;
    internal const uint ResourceTypeIrq = 0x00000004;

    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Get_Parent(
        out uint parentDeviceInstance,
        uint deviceInstance,
        uint flags);

    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Get_Device_ID_Size(
        out uint length,
        uint deviceInstance,
        uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW", StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial uint CM_Get_Device_ID(
        uint deviceInstance,
        char* buffer,
        uint bufferLength,
        uint flags);

    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Get_First_Log_Conf(
        out nint logConfiguration,
        uint deviceInstance,
        uint flags);

    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Get_Next_Res_Des(
        out nint resourceDescriptor,
        nint sourceResourceDescriptor,
        uint forResource,
        nint resourceId,
        uint flags);

    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Get_Res_Des_Data_Size(
        out uint size,
        nint resourceDescriptor,
        uint flags);

    [LibraryImport("cfgmgr32.dll")]
    internal static unsafe partial uint CM_Get_Res_Des_Data(
        nint resourceDescriptor,
        byte* buffer,
        uint bufferLength,
        uint flags);

    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Free_Log_Conf_Handle(nint logConfiguration);

    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Free_Res_Des_Handle(nint resourceDescriptor);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct IrqDescriptor64
{
    internal readonly uint Count;
    internal readonly uint Type;
    internal readonly ushort Flags;
    internal readonly ushort Group;
    internal readonly uint AllocatedIrq;
    internal readonly ulong Affinity;
}

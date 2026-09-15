using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.Interop;

internal static partial class ConfigurationManager
{
    internal const uint GetDeviceInterfaceListPresent = 0x00000000;
    internal const uint LocateDeviceNodeNormal = 0x00000000;

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_List_SizeW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint CM_Get_Device_Interface_List_Size(
        out uint length,
        in Guid interfaceClassGuid,
        string? deviceInstanceId,
        uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_ListW", StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial uint CM_Get_Device_Interface_List(
        in Guid interfaceClassGuid,
        string? deviceInstanceId,
        char* buffer,
        uint bufferLength,
        uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint CM_Locate_DevNode(
        out uint deviceInstance,
        string deviceInstanceId,
        uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    internal static unsafe partial uint CM_Get_DevNode_Property(
        uint deviceInstance,
        in DevicePropertyKey propertyKey,
        out uint propertyType,
        byte* propertyBuffer,
        ref uint propertyBufferSize,
        uint flags);
}

using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.Interop;

internal enum NativeAudioDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum NativeAudioRole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(NativeAudioDataFlow dataFlow, uint stateMask, out nint devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(
        NativeAudioDataFlow dataFlow,
        NativeAudioRole role,
        [MarshalAs(UnmanagedType.Interface)] out IMMDevice endpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Interface)] out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(nint client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(nint client);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(
        ref Guid iid,
        uint classContext,
        nint activationParameters,
        [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);

    [PreserveSig]
    int OpenPropertyStore(uint storageAccessMode, out nint propertyStore);

    [PreserveSig]
    int GetId(out nint id);

    [PreserveSig]
    int GetState(out uint state);
}

[ComImport]
[Guid("2A07407E-6497-4A18-9787-32F79BD0D98F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceTopology
{
    [PreserveSig]
    int GetConnectorCount(out uint count);

    [PreserveSig]
    int GetConnector(uint index, [MarshalAs(UnmanagedType.Interface)] out IConnector connector);

    [PreserveSig]
    int GetSubunitCount(out uint count);

    [PreserveSig]
    int GetSubunit(uint index, out nint subunit);

    [PreserveSig]
    int GetPartById(uint id, out nint part);

    [PreserveSig]
    int GetDeviceId(out nint deviceId);

    [PreserveSig]
    int GetSignalPath(nint from, nint to, [MarshalAs(UnmanagedType.Bool)] bool rejectMixedPaths, out nint parts);
}

[ComImport]
[Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IConnector
{
    [PreserveSig]
    int GetType(out int connectorType);

    [PreserveSig]
    int GetDataFlow(out int dataFlow);

    [PreserveSig]
    int ConnectTo([MarshalAs(UnmanagedType.Interface)] IConnector connectTo);

    [PreserveSig]
    int Disconnect();

    [PreserveSig]
    int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);

    [PreserveSig]
    int GetConnectedTo([MarshalAs(UnmanagedType.Interface)] out IConnector connectedTo);

    [PreserveSig]
    int GetConnectorIdConnectedTo(out nint connectorId);

    [PreserveSig]
    int GetDeviceIdConnectedTo(out nint deviceId);
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

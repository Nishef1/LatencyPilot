using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeLuid
{
    internal readonly uint LowPart;
    internal readonly int HighPart;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DxgiAdapterDesc1
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    internal string Description;

    internal uint VendorId;
    internal uint DeviceId;
    internal uint SubSysId;
    internal uint Revision;
    internal nuint DedicatedVideoMemory;
    internal nuint DedicatedSystemMemory;
    internal nuint SharedSystemMemory;
    internal NativeLuid AdapterLuid;
    internal uint Flags;
}

[ComImport]
[Guid("AEC22FB8-76F3-4639-9BE0-28EB43A67A2E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIObject
{
    [PreserveSig]
    int SetPrivateData(ref Guid name, uint dataSize, nint data);

    [PreserveSig]
    int SetPrivateDataInterface(ref Guid name, [MarshalAs(UnmanagedType.IUnknown)] object? unknown);

    [PreserveSig]
    int GetPrivateData(ref Guid name, ref uint dataSize, nint data);

    [PreserveSig]
    int GetParent(ref Guid iid, out nint parent);
}

[ComImport]
[Guid("2411E7E1-12AC-4CCF-BD14-9798E8534DC0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIAdapter : IDXGIObject
{
    [PreserveSig]
    int EnumOutputs(uint output, out nint dxgiOutput);

    [PreserveSig]
    int GetDesc(out nint description);

    [PreserveSig]
    int CheckInterfaceSupport(ref Guid interfaceName, out long userModeDriverVersion);
}

[ComImport]
[Guid("29038F61-3839-4626-91FD-086879011A05")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIAdapter1 : IDXGIAdapter
{
    [PreserveSig]
    int GetDesc1(out DxgiAdapterDesc1 description);
}

[ComImport]
[Guid("7B7166EC-21C7-44AE-B21A-C9AE321AE369")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIFactory : IDXGIObject
{
    [PreserveSig]
    int EnumAdapters(uint adapter, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter dxgiAdapter);

    [PreserveSig]
    int MakeWindowAssociation(nint windowHandle, uint flags);

    [PreserveSig]
    int GetWindowAssociation(out nint windowHandle);

    [PreserveSig]
    int CreateSwapChain([MarshalAs(UnmanagedType.IUnknown)] object device, nint description, out nint swapChain);

    [PreserveSig]
    int CreateSoftwareAdapter(nint module, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter adapter);
}

[ComImport]
[Guid("770AAE78-F26F-4DBA-A829-253C83D1B387")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIFactory1 : IDXGIFactory
{
    [PreserveSig]
    int EnumAdapters1(uint adapter, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter1 dxgiAdapter);

    [PreserveSig]
    [return: MarshalAs(UnmanagedType.Bool)]
    bool IsCurrent();
}

internal static class Dxgi
{
    internal const int ErrorNotFound = unchecked((int)0x887A0002);

    internal static readonly Guid Factory1InterfaceId = new("770AAE78-F26F-4DBA-A829-253C83D1B387");

    [DllImport("dxgi.dll", ExactSpelling = true)]
    internal static extern int CreateDXGIFactory1(
        ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1 factory);
}

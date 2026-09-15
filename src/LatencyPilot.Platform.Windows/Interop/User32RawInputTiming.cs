using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputDeviceRegistration
{
    internal ushort UsagePage;
    internal ushort Usage;
    internal uint Flags;
    internal nint TargetWindow;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRawInputHeader
{
    internal uint Type;
    internal uint Size;
    internal nint DeviceHandle;
    internal nint WParam;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    internal int X;
    internal int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessage
{
    internal nint Window;
    internal uint Message;
    internal nuint WParam;
    internal nint LParam;
    internal uint Time;
    internal NativePoint Point;
    internal uint Private;
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint NativeWindowProcedure(nint window, uint message, nuint wParam, nint lParam);

internal static partial class User32RawInput
{
    internal const uint InputCommand = 0x10000003;
    internal const uint HeaderCommand = 0x10000005;
    internal const uint InputSinkFlag = 0x00000100;
    internal const uint DeviceNotifyFlag = 0x00002000;
    internal const uint RemoveFlag = 0x00000001;
    internal const uint WmInputDeviceChange = 0x00FE;
    internal const uint WmInput = 0x00FF;
    internal const nuint GidcArrival = 1;
    internal const nuint GidcRemoval = 2;
    internal const uint WmApp = 0x8000;
    internal const int WindowProcedureIndex = -4;
    internal static readonly nint MessageOnlyWindow = new(-3);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool RegisterRawInputDevices(
        RawInputDeviceRegistration* devices,
        uint deviceCount,
        uint structureSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static unsafe partial uint GetRegisteredRawInputDevices(
        RawInputDeviceRegistration* devices,
        ref uint deviceCount,
        uint structureSize);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static unsafe partial uint GetRawInputData(
        nint rawInput,
        uint command,
        void* data,
        ref uint size,
        uint headerSize);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint window, int index, nint newValue);

    [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
    internal static partial nint CallWindowProc(
        nint previousWindowProcedure,
        nint window,
        uint message,
        nuint wParam,
        nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static partial int GetMessage(out NativeMessage message, nint window, uint minimumMessage, uint maximumMessage);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(in NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(in NativeMessage message);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int exitCode);
}

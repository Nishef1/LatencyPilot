using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LatencyPilot.Platform.Windows.Interop;

internal static partial class UsbKernelIo
{
    internal const uint IoctlGetNodeConnectionDriverKeyName = 0x00220420;
    internal const uint IoctlGetNodeConnectionInformationEx = 0x00220448;
    internal const uint IoctlGetHubInformationEx = 0x00220454;
    internal const uint IoctlGetNodeConnectionInformationExV2 = 0x0022045C;

    internal const uint GenericWrite = 0x40000000;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint OpenExisting = 3;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte* inputBuffer,
        uint inputBufferSize,
        byte* outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        nint overlapped);
}

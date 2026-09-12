using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.Interop;

internal enum LogicalProcessorRelationship : int
{
    ProcessorCore = 0,
    ProcessorPackage = 3,
    All = 0xFFFF,
}

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool GetLogicalProcessorInformationEx(
        LogicalProcessorRelationship relationshipType,
        byte* buffer,
        ref uint returnedLength);
}

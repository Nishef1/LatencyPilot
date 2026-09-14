using System.Runtime.InteropServices;

namespace LatencyPilot.Platform.Windows.Interop;

internal static partial class Ole32
{
    internal const uint CoInitMultithreaded = 0x0;
    internal const int RpcEChangedMode = unchecked((int)0x80010106);

    [LibraryImport("ole32.dll")]
    internal static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    internal static partial void CoUninitialize();
}

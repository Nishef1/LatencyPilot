using System.Runtime.InteropServices;
using LatencyPilot.Core.System;

namespace LatencyPilot.Platform.Windows.System;

public static class SystemInventoryReader
{
    public static SystemInventorySnapshot Capture() => new(
        RuntimeInformation.OSDescription,
        RuntimeInformation.OSArchitecture.ToString(),
        RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.ProcessorCount,
        DateTimeOffset.UtcNow);
}

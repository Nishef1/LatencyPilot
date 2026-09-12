using System.Runtime.InteropServices;
using LatencyPilot.Core.System;

namespace LatencyPilot.Platform.Windows.System;

public sealed class SystemInventoryReader
{
    public SystemInventorySnapshot Capture() => new(
        RuntimeInformation.OSDescription,
        RuntimeInformation.OSArchitecture.ToString(),
        RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.ProcessorCount,
        DateTimeOffset.UtcNow);
}

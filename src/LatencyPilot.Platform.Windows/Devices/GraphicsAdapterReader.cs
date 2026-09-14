using System.Runtime.InteropServices;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.Devices;

public static class GraphicsAdapterReader
{
    private const int MaximumAdapters = 64;

    public static GraphicsAdapterInventory Capture()
    {
        var factoryInterfaceId = Dxgi.Factory1InterfaceId;
        var result = Dxgi.CreateDXGIFactory1(ref factoryInterfaceId, out var factory);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        try
        {
            var adapters = new List<GraphicsAdapterSnapshot>();
            for (uint index = 0; index < MaximumAdapters; index++)
            {
                IDXGIAdapter1? adapter = null;
                try
                {
                    result = factory.EnumAdapters1(index, out adapter);
                    if (result == Dxgi.ErrorNotFound)
                    {
                        break;
                    }

                    if (result < 0)
                    {
                        Marshal.ThrowExceptionForHR(result);
                    }

                    result = adapter.GetDesc1(out var description);
                    if (result < 0)
                    {
                        Marshal.ThrowExceptionForHR(result);
                    }

                    adapters.Add(new GraphicsAdapterSnapshot(
                        checked((int)index),
                        description.Description?.TrimEnd('\0') ?? string.Empty,
                        description.VendorId,
                        description.DeviceId,
                        description.SubSysId,
                        description.Revision,
                        checked((ulong)description.DedicatedVideoMemory),
                        checked((ulong)description.DedicatedSystemMemory),
                        checked((ulong)description.SharedSystemMemory),
                        new GraphicsAdapterLuid(
                            description.AdapterLuid.LowPart,
                            description.AdapterLuid.HighPart),
                        description.Flags));
                }
                finally
                {
                    ReleaseComObject(adapter);
                }
            }

            if (adapters.Count == MaximumAdapters)
            {
                throw new InvalidDataException(
                    $"DXGI adapter enumeration hit the safety limit of {MaximumAdapters} adapters.");
            }

            return new GraphicsAdapterInventory(adapters.ToArray(), DateTimeOffset.UtcNow);
        }
        finally
        {
            ReleaseComObject(factory);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }
}

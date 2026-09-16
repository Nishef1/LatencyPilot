using LatencyPilot.Core.Devices;
using Vortice.DXGI;
using static Vortice.DXGI.DXGI;

namespace LatencyPilot.Platform.Windows.Devices;

public static class GraphicsAdapterReader
{
    private const int MaximumAdapters = 64;

    public static GraphicsAdapterInventory Capture()
    {
        using var factory = CreateDXGIFactory1<IDXGIFactory1>();
        var adapters = new List<GraphicsAdapterSnapshot>();
        for (uint index = 0; index < MaximumAdapters; index++)
        {
            if (!factory.EnumAdapters1(index, out IDXGIAdapter1? adapter).Success)
            {
                break;
            }

            using (adapter)
            {
                if (adapter is null)
                {
                    throw new InvalidDataException(
                        $"DXGI returned a successful adapter enumeration result without an adapter at index {index}.");
                }

                var description = adapter.Description1;
                adapters.Add(new GraphicsAdapterSnapshot(
                    checked((int)index),
                    description.Description.TrimEnd('\0'),
                    description.VendorId,
                    description.DeviceId,
                    description.SubsystemId,
                    description.Revision,
                    checked((ulong)description.DedicatedVideoMemory),
                    checked((ulong)description.DedicatedSystemMemory),
                    checked((ulong)description.SharedSystemMemory),
                    new GraphicsAdapterLuid(
                        description.Luid.LowPart,
                        description.Luid.HighPart),
                    (uint)description.Flags));
            }
        }

        if (adapters.Count == MaximumAdapters)
        {
            throw new InvalidDataException(
                $"DXGI adapter enumeration hit the safety limit of {MaximumAdapters} adapters.");
        }

        return new GraphicsAdapterInventory(adapters.ToArray(), DateTimeOffset.UtcNow);
    }
}

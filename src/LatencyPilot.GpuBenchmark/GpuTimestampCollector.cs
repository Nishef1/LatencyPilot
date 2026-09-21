using SharpGen.Runtime;
using Vortice.Direct3D12;

namespace LatencyPilot.GpuBenchmark;

internal sealed class GpuTimestampCollector : IDisposable
{
    private readonly ID3D12QueryHeap queryHeap;
    private readonly ID3D12Resource readback;
    private readonly ulong frequency;

    internal GpuTimestampCollector(ID3D12Device device, ID3D12CommandQueue queue)
    {
        queue.GetTimestampFrequency(out frequency).CheckError();
        if (frequency == 0)
        {
            throw new InvalidOperationException("D3D12 command queue reported a zero timestamp frequency.");
        }

        queryHeap = device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription(QueryHeapType.Timestamp, 2));
        readback = device.CreateCommittedResource(
            HeapType.Readback,
            ResourceDescription.Buffer(2 * sizeof(ulong)),
            ResourceStates.CopyDest);
    }

    internal ulong Frequency => frequency;

    internal void RecordBegin(ID3D12GraphicsCommandList commandList) =>
        commandList.EndQuery(queryHeap, QueryType.Timestamp, 0);

    internal void RecordEndAndResolve(ID3D12GraphicsCommandList commandList)
    {
        commandList.EndQuery(queryHeap, QueryType.Timestamp, 1);
        commandList.ResolveQueryData(queryHeap, QueryType.Timestamp, 0, 2, readback, 0);
    }

    internal double ReadElapsedMilliseconds()
    {
        Span<ulong> timestamps = stackalloc ulong[2];
        readback.GetData(timestamps);
        if (timestamps[1] < timestamps[0])
        {
            throw new InvalidDataException("D3D12 GPU timestamps moved backwards.");
        }

        return (timestamps[1] - timestamps[0]) * 1000d / frequency;
    }

    public void Dispose()
    {
        readback.Dispose();
        queryHeap.Dispose();
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using LatencyPilot.Core.System;
using Vortice;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace LatencyPilot.GpuBenchmark;

internal sealed class CpuRenderWorker : IDisposable
{
    private readonly ID3D12CommandAllocator allocator;
    private readonly ID3D12GraphicsCommandList commandList;
    private readonly AutoResetEvent start = new(false);
    private readonly ManualResetEventSlim completed = new(false);
    private readonly Thread thread;
    private readonly RawRect[] region;
    private readonly Color4[] colors;
    private readonly LogicalProcessorId processor;
    private FrameRequest request;
    private Exception? failure;
    private bool stop;
    private ulong simulationChecksum;

    internal CpuRenderWorker(ID3D12Device device, LogicalProcessorId processor, int workerIndex, int workerCount, int width, int height, int seed)
    {
        this.processor = processor;
        allocator = device.CreateCommandAllocator(CommandListType.Direct);
        commandList = device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, allocator, null);
        commandList.Close();

        var top = (height * workerIndex) / workerCount;
        var bottom = (height * (workerIndex + 1)) / workerCount;
        region = [new RawRect(0, top, width, Math.Max(top + 1, bottom))];

        var random = new Random(unchecked(seed + (workerIndex * 104729)));
        colors =
        [
            new Color4((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), 1f),
            new Color4((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), 1f),
            new Color4((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), 1f),
            new Color4((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), 1f),
        ];

        thread = new Thread(Run)
        {
            IsBackground = true,
            Name = $"LatencyPilot.GpuBenchmark CPU {processor}",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
    }

    internal ID3D12GraphicsCommandList CommandList => commandList;
    internal ulong SimulationChecksum => Volatile.Read(ref simulationChecksum);

    internal void BeginFrame(CpuDescriptorHandle renderTargetView, int simulationIterations, int commandBatches)
    {
        if (failure is not null) throw new InvalidOperationException($"Benchmark worker {processor} failed.", failure);
        completed.Reset();
        request = new FrameRequest(renderTargetView, simulationIterations, commandBatches);
        start.Set();
    }

    internal void WaitForFrame()
    {
        completed.Wait();
        if (failure is not null) throw new InvalidOperationException($"Benchmark worker {processor} failed.", failure);
    }

    public void Dispose()
    {
        stop = true;
        start.Set();
        thread.Join();
        completed.Dispose();
        start.Dispose();
        commandList.Dispose();
        allocator.Dispose();
    }

    private void Run()
    {
        try
        {
            PinCurrentThread(processor);
            while (true)
            {
                start.WaitOne();
                if (stop) return;
                RecordFrame(request);
                completed.Set();
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            completed.Set();
        }
    }

    private void RecordFrame(FrameRequest frame)
    {
        var state = simulationChecksum ^ 0x9E3779B97F4A7C15UL;
        for (var index = 0; index < frame.SimulationIterations; index++)
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            state *= 0x2545F4914F6CDD1DUL;
        }
        Volatile.Write(ref simulationChecksum, state);

        allocator.Reset();
        commandList.Reset(allocator, null);
        for (var batch = 0; batch < frame.CommandBatches; batch++)
        {
            commandList.ClearRenderTargetView(frame.RenderTargetView, colors[batch & 3], region);
        }
        commandList.Close();
    }

    private static void PinCurrentThread(LogicalProcessorId processor)
    {
        if (processor.Number >= 64)
        {
            throw new NotSupportedException("gpu-affinity-benchmark-v1 supports processor numbers below 64 in each group.");
        }

        var affinity = new GroupAffinity { Mask = 1UL << processor.Number, Group = processor.Group };
        if (!SetThreadGroupAffinity(GetCurrentThread(), in affinity, out _))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Unable to pin benchmark worker to processor {processor}.");
        }
    }

    private readonly record struct FrameRequest(CpuDescriptorHandle RenderTargetView, int SimulationIterations, int CommandBatches);

    [StructLayout(LayoutKind.Sequential)]
    private struct GroupAffinity
    {
        internal ulong Mask;
        internal ushort Group;
        internal ushort Reserved0;
        internal ushort Reserved1;
        internal ushort Reserved2;
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetThreadGroupAffinity(IntPtr thread, in GroupAffinity groupAffinity, out GroupAffinity previousGroupAffinity);
}

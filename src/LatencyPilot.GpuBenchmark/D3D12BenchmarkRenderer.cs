using System.Diagnostics;
using LatencyPilot.Core.System;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D12.D3D12;
using static Vortice.DXGI.DXGI;

namespace LatencyPilot.GpuBenchmark;

internal readonly record struct BenchmarkFrameTelemetry(long FrameIndex, double CpuRecordingMilliseconds, double GpuWorkMilliseconds);

internal sealed class D3D12BenchmarkRenderer : IDisposable
{
    private const int BufferCount = 2;
    private readonly BenchmarkWindow window;
    private readonly IDXGIFactory4 factory;
    private readonly ID3D12Device device;
    private readonly ID3D12CommandQueue queue;
    private readonly IDXGISwapChain3 swapChain;
    private readonly ID3D12DescriptorHeap rtvHeap;
    private readonly ID3D12Resource[] renderTargets;
    private readonly ID3D12CommandAllocator setupAllocator;
    private readonly ID3D12GraphicsCommandList setupList;
    private readonly ID3D12CommandAllocator finishAllocator;
    private readonly ID3D12GraphicsCommandList finishList;
    private readonly CpuRenderWorker[] workers;
    private readonly ID3D12CommandList[] executionLists;
    private readonly GpuTimestampCollector timestamps;
    private readonly ID3D12Fence fence;
    private readonly AutoResetEvent fenceEvent = new(false);
    private readonly uint rtvDescriptorSize;
    private ulong fenceValue;
    private long frameIndex;
    private bool disposed;

    internal D3D12BenchmarkRenderer(int width, int height, IReadOnlyList<LogicalProcessorId> workerProcessors, int seed)
    {
        ArgumentNullException.ThrowIfNull(workerProcessors);
        if (workerProcessors.Count == 0) throw new ArgumentException("At least one benchmark worker is required.", nameof(workerProcessors));
        window = new BenchmarkWindow(width, height);
        factory = CreateDXGIFactory2<IDXGIFactory4>(false);

        ID3D12Device? selectedDevice = null;
        string? adapterName = null;
        for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter).Success; adapterIndex++)
        {
            using (adapter)
            {
                var description = adapter.Description1;
                if ((description.Flags & AdapterFlags.Software) != 0) continue;
                if (D3D12CreateDevice(adapter, FeatureLevel.Level_11_0, out selectedDevice).Success)
                {
                    adapterName = description.Description;
                    break;
                }
            }
        }

        device = selectedDevice ?? throw new PlatformNotSupportedException("No hardware adapter capable of Direct3D 12 was found.");
        AdapterName = string.IsNullOrWhiteSpace(adapterName) ? "Unknown D3D12 adapter" : adapterName.Trim();
        queue = device.CreateCommandQueue(CommandListType.Direct);
        queue.Name = "LatencyPilot GPU benchmark queue";

        using (var factory5 = factory.QueryInterfaceOrNull<IDXGIFactory5>())
            TearingSupported = factory5?.PresentAllowTearing ?? false;

        var swapChainDescription = new SwapChainDescription1
        {
            BufferCount = BufferCount,
            Width = (uint)width,
            Height = (uint)height,
            Format = Format.R8G8B8A8_UNorm,
            BufferUsage = Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.FlipDiscard,
            SampleDescription = new SampleDescription(1, 0),
            Flags = TearingSupported ? SwapChainFlags.AllowTearing : SwapChainFlags.None,
        };

        using (var swapChain1 = factory.CreateSwapChainForHwnd(queue, window.Handle, swapChainDescription))
        {
            factory.MakeWindowAssociation(window.Handle, WindowAssociationFlags.IgnoreAltEnter);
            swapChain = swapChain1.QueryInterface<IDXGISwapChain3>();
        }
        PresentMode = TearingSupported ? "immediate-allow-tearing" : "immediate";

        rtvHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, BufferCount));
        rtvDescriptorSize = device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        renderTargets = new ID3D12Resource[BufferCount];
        var rtv = rtvHeap.GetCPUDescriptorHandleForHeapStart();
        for (var index = 0; index < BufferCount; index++)
        {
            renderTargets[index] = swapChain.GetBuffer<ID3D12Resource>((uint)index);
            device.CreateRenderTargetView(renderTargets[index], null, rtv);
            rtv += (int)rtvDescriptorSize;
        }

        setupAllocator = device.CreateCommandAllocator(CommandListType.Direct);
        setupList = device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, setupAllocator, null);
        setupList.Close();
        finishAllocator = device.CreateCommandAllocator(CommandListType.Direct);
        finishList = device.CreateCommandList<ID3D12GraphicsCommandList>(CommandListType.Direct, finishAllocator, null);
        finishList.Close();

        workers = workerProcessors.Select((processor, index) => new CpuRenderWorker(device, processor, index, workerProcessors.Count, width, height, seed)).ToArray();
        timestamps = new GpuTimestampCollector(device, queue);
        TimestampFrequency = timestamps.Frequency;
        executionLists = new ID3D12CommandList[workers.Length + 2];
        executionLists[0] = setupList;
        for (var index = 0; index < workers.Length; index++) executionLists[index + 1] = workers[index].CommandList;
        executionLists[^1] = finishList;
        fence = device.CreateFence(0);
    }

    internal string AdapterName { get; }
    internal string PresentMode { get; }
    internal bool TearingSupported { get; }
    internal ulong TimestampFrequency { get; }

    internal BenchmarkFrameTelemetry RenderFrame(int simulationIterationsPerWorker, int commandBatchesPerWorker)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!window.PumpMessages()) throw new OperationCanceledException("The benchmark render window was closed.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(simulationIterationsPerWorker);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandBatchesPerWorker);

        var backBufferIndex = swapChain.CurrentBackBufferIndex;
        var renderTargetView = new CpuDescriptorHandle(rtvHeap.GetCPUDescriptorHandleForHeapStart(), checked((int)backBufferIndex), rtvDescriptorSize);
        setupAllocator.Reset();
        setupList.Reset(setupAllocator, null);
        timestamps.RecordBegin(setupList);
        setupList.ResourceBarrierTransition(renderTargets[backBufferIndex], ResourceStates.Present, ResourceStates.RenderTarget);
        setupList.ClearRenderTargetView(renderTargetView, Colors.Black);
        setupList.Close();

        var cpuStart = Stopwatch.GetTimestamp();
        foreach (var worker in workers) worker.BeginFrame(renderTargetView, simulationIterationsPerWorker, commandBatchesPerWorker);
        foreach (var worker in workers) worker.WaitForFrame();
        var cpuMilliseconds = Stopwatch.GetElapsedTime(cpuStart).TotalMilliseconds;

        finishAllocator.Reset();
        finishList.Reset(finishAllocator, null);
        finishList.ResourceBarrierTransition(renderTargets[backBufferIndex], ResourceStates.RenderTarget, ResourceStates.Present);
        timestamps.RecordEndAndResolve(finishList);
        finishList.Close();
        queue.ExecuteCommandLists(executionLists);

        var presentFlags = TearingSupported ? PresentFlags.AllowTearing : PresentFlags.None;
        var presentResult = swapChain.Present(0, presentFlags);
        if (presentResult.Failure) throw new InvalidOperationException($"D3D12 benchmark Present failed with 0x{presentResult.Code:X8}.");
        WaitForGpu();
        var gpuMilliseconds = timestamps.ReadElapsedMilliseconds();
        return new BenchmarkFrameTelemetry(Interlocked.Increment(ref frameIndex), cpuMilliseconds, gpuMilliseconds);
    }

    internal IReadOnlyList<ulong> CaptureWorkerChecksums() => workers.Select(static worker => worker.SimulationChecksum).ToArray();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { WaitForGpu(); } catch { }
        foreach (var worker in workers) worker.Dispose();
        fenceEvent.Dispose();
        fence.Dispose();
        timestamps.Dispose();
        finishList.Dispose(); finishAllocator.Dispose(); setupList.Dispose(); setupAllocator.Dispose();
        foreach (var renderTarget in renderTargets) renderTarget.Dispose();
        rtvHeap.Dispose(); swapChain.Dispose(); queue.Dispose(); device.Dispose(); factory.Dispose(); window.Dispose();
    }

    private void WaitForGpu()
    {
        var value = ++fenceValue;
        queue.Signal(fence, value).CheckError();
        if (fence.CompletedValue >= value) return;
        fence.SetEventOnCompletion(value, fenceEvent).CheckError();
        fenceEvent.WaitOne();
    }
}

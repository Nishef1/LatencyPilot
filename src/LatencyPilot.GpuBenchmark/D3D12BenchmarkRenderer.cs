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

internal readonly record struct BenchmarkFrameTelemetry(
    long FrameIndex,
    double CpuRecordingMilliseconds,
    double GpuWorkMilliseconds,
    double FramePeriodMilliseconds = 0d);

internal sealed class D3D12BenchmarkRenderer : IDisposable
{
    private const int BufferCount = 3;
    private const int FrameContextCount = 2;

    private readonly BenchmarkWindow window;
    private readonly IDXGIFactory4 factory;
    private readonly ID3D12Device device;
    private readonly ID3D12CommandQueue queue;
    private readonly IDXGISwapChain3 swapChain;
    private readonly ID3D12DescriptorHeap rtvHeap;
    private readonly ID3D12Resource[] renderTargets;
    private readonly ID3D12CommandAllocator[] setupAllocators;
    private readonly ID3D12GraphicsCommandList[] setupLists;
    private readonly ID3D12CommandAllocator[] finishAllocators;
    private readonly ID3D12GraphicsCommandList[] finishLists;
    private readonly CpuRenderWorker[] workers;
    private readonly ID3D12CommandList[][] executionLists;
    private readonly GpuTimestampCollector[] timestamps;
    private readonly ID3D12Fence fence;
    private readonly AutoResetEvent fenceEvent = new(false);
    private readonly ulong[] frameContextFenceValues = new ulong[FrameContextCount];
    private readonly PendingFrame?[] pendingFrames = new PendingFrame?[FrameContextCount];
    private readonly uint rtvDescriptorSize;

    private ulong nextFenceValue;
    private long frameIndex;
    private long? previousPresentTimestamp;
    private int nextFrameContext;
    private bool disposed;

    internal D3D12BenchmarkRenderer(
        int width,
        int height,
        IReadOnlyList<LogicalProcessorId> workerProcessors,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(workerProcessors);
        if (workerProcessors.Count == 0)
        {
            throw new ArgumentException("At least one benchmark worker is required.", nameof(workerProcessors));
        }

        window = new BenchmarkWindow(width, height);
        factory = CreateDXGIFactory2<IDXGIFactory4>(false);

        ID3D12Device? selectedDevice = null;
        string? adapterName = null;
        for (uint adapterIndex = 0;
             factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter).Success;
             adapterIndex++)
        {
            using (adapter)
            {
                var description = adapter.Description1;
                if ((description.Flags & AdapterFlags.Software) != 0)
                {
                    continue;
                }

                if (D3D12CreateDevice(adapter, FeatureLevel.Level_11_0, out selectedDevice).Success)
                {
                    adapterName = description.Description;
                    break;
                }
            }
        }

        device = selectedDevice
            ?? throw new PlatformNotSupportedException(
                "No hardware adapter capable of Direct3D 12 was found.");
        AdapterName = string.IsNullOrWhiteSpace(adapterName)
            ? "Unknown D3D12 adapter"
            : adapterName.Trim();

        queue = device.CreateCommandQueue(CommandListType.Direct);
        queue.Name = "LatencyPilot GPU benchmark queue";

        using (var factory5 = factory.QueryInterfaceOrNull<IDXGIFactory5>())
        {
            TearingSupported = factory5?.PresentAllowTearing ?? false;
        }

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

        using (var swapChain1 = factory.CreateSwapChainForHwnd(
                   queue,
                   window.Handle,
                   swapChainDescription))
        {
            factory.MakeWindowAssociation(
                window.Handle,
                WindowAssociationFlags.IgnoreAltEnter);
            swapChain = swapChain1.QueryInterface<IDXGISwapChain3>();
        }

        PresentMode = TearingSupported
            ? "immediate-allow-tearing-buffered-2"
            : "immediate-buffered-2";

        rtvHeap = device.CreateDescriptorHeap(
            new DescriptorHeapDescription(
                DescriptorHeapType.RenderTargetView,
                BufferCount));
        rtvDescriptorSize = device.GetDescriptorHandleIncrementSize(
            DescriptorHeapType.RenderTargetView);
        renderTargets = new ID3D12Resource[BufferCount];
        var rtv = rtvHeap.GetCPUDescriptorHandleForHeapStart();
        for (var index = 0; index < BufferCount; index++)
        {
            renderTargets[index] = swapChain.GetBuffer<ID3D12Resource>((uint)index);
            device.CreateRenderTargetView(renderTargets[index], null, rtv);
            rtv += (int)rtvDescriptorSize;
        }

        setupAllocators = new ID3D12CommandAllocator[FrameContextCount];
        setupLists = new ID3D12GraphicsCommandList[FrameContextCount];
        finishAllocators = new ID3D12CommandAllocator[FrameContextCount];
        finishLists = new ID3D12GraphicsCommandList[FrameContextCount];
        for (var context = 0; context < FrameContextCount; context++)
        {
            setupAllocators[context] = device.CreateCommandAllocator(CommandListType.Direct);
            setupLists[context] = device.CreateCommandList<ID3D12GraphicsCommandList>(
                CommandListType.Direct,
                setupAllocators[context],
                null);
            setupLists[context].Close();

            finishAllocators[context] = device.CreateCommandAllocator(CommandListType.Direct);
            finishLists[context] = device.CreateCommandList<ID3D12GraphicsCommandList>(
                CommandListType.Direct,
                finishAllocators[context],
                null);
            finishLists[context].Close();
        }

        workers = workerProcessors
            .Select((processor, index) => new CpuRenderWorker(
                device,
                processor,
                index,
                workerProcessors.Count,
                width,
                height,
                seed,
                FrameContextCount))
            .ToArray();

        timestamps = Enumerable.Range(0, FrameContextCount)
            .Select(_ => new GpuTimestampCollector(device, queue))
            .ToArray();
        TimestampFrequency = timestamps[0].Frequency;
        if (timestamps.Any(item => item.Frequency != TimestampFrequency))
        {
            throw new InvalidOperationException(
                "D3D12 timestamp frequency changed while creating frame contexts.");
        }

        executionLists = new ID3D12CommandList[FrameContextCount][];
        for (var context = 0; context < FrameContextCount; context++)
        {
            var lists = new ID3D12CommandList[workers.Length + 2];
            lists[0] = setupLists[context];
            for (var workerIndex = 0; workerIndex < workers.Length; workerIndex++)
            {
                lists[workerIndex + 1] = workers[workerIndex].GetCommandList(context);
            }
            lists[^1] = finishLists[context];
            executionLists[context] = lists;
        }

        fence = device.CreateFence(0);
    }

    internal string AdapterName { get; }

    internal string PresentMode { get; }

    internal bool TearingSupported { get; }

    internal ulong TimestampFrequency { get; }

    internal void BeginMeasurementWindow()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (pendingFrames.Any(static item => item is not null))
        {
            throw new InvalidOperationException(
                "Pending GPU frames must be drained before a new benchmark measurement window begins.");
        }

        nextFrameContext = 0;
        previousPresentTimestamp = Stopwatch.GetTimestamp();
    }

    internal BenchmarkFrameTelemetry? RenderFrame(
        int simulationIterationsPerWorker,
        int commandBatchesPerWorker)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!window.PumpMessages())
        {
            throw new OperationCanceledException("The benchmark render window was closed.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(simulationIterationsPerWorker);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandBatchesPerWorker);

        var contextIndex = nextFrameContext;
        nextFrameContext = (nextFrameContext + 1) % FrameContextCount;

        // Reuse only a frame context whose prior GPU work has completed.
        // With two contexts and a three-buffer flip chain this bounds CPU
        // submission to two frames in flight rather than fully draining the GPU
        // after every Present.
        var completedFrame = CompleteFrameContext(contextIndex);

        var backBufferIndex = swapChain.CurrentBackBufferIndex;
        var renderTargetView = new CpuDescriptorHandle(
            rtvHeap.GetCPUDescriptorHandleForHeapStart(),
            checked((int)backBufferIndex),
            rtvDescriptorSize);

        var setupAllocator = setupAllocators[contextIndex];
        var setupList = setupLists[contextIndex];
        setupAllocator.Reset();
        setupList.Reset(setupAllocator, null);
        timestamps[contextIndex].RecordBegin(setupList);
        setupList.ResourceBarrierTransition(
            renderTargets[backBufferIndex],
            ResourceStates.Present,
            ResourceStates.RenderTarget);
        setupList.ClearRenderTargetView(renderTargetView, Colors.Black);
        setupList.Close();

        var cpuStart = Stopwatch.GetTimestamp();
        foreach (var worker in workers)
        {
            worker.BeginFrame(
                contextIndex,
                renderTargetView,
                simulationIterationsPerWorker,
                commandBatchesPerWorker);
        }
        foreach (var worker in workers)
        {
            worker.WaitForFrame();
        }
        var cpuMilliseconds = Stopwatch.GetElapsedTime(cpuStart).TotalMilliseconds;

        var finishAllocator = finishAllocators[contextIndex];
        var finishList = finishLists[contextIndex];
        finishAllocator.Reset();
        finishList.Reset(finishAllocator, null);
        finishList.ResourceBarrierTransition(
            renderTargets[backBufferIndex],
            ResourceStates.RenderTarget,
            ResourceStates.Present);
        timestamps[contextIndex].RecordEndAndResolve(finishList);
        finishList.Close();

        queue.ExecuteCommandLists(executionLists[contextIndex]);

        var presentFlags = TearingSupported
            ? PresentFlags.AllowTearing
            : PresentFlags.None;
        var presentResult = swapChain.Present(0, presentFlags);
        if (presentResult.Failure)
        {
            throw new InvalidOperationException(
                $"D3D12 benchmark Present failed with 0x{presentResult.Code:X8}.");
        }

        var presentedAt = Stopwatch.GetTimestamp();
        var framePeriodMilliseconds = previousPresentTimestamp is { } previous
            ? Stopwatch.GetElapsedTime(previous, presentedAt).TotalMilliseconds
            : 0d;
        previousPresentTimestamp = presentedAt;

        var submittedFrameIndex = Interlocked.Increment(ref frameIndex);
        var fenceValue = ++nextFenceValue;
        queue.Signal(fence, fenceValue).CheckError();
        frameContextFenceValues[contextIndex] = fenceValue;
        pendingFrames[contextIndex] = new PendingFrame(
            submittedFrameIndex,
            cpuMilliseconds,
            framePeriodMilliseconds);

        return completedFrame;
    }

    internal IReadOnlyList<BenchmarkFrameTelemetry> DrainFrames()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var completed = new List<BenchmarkFrameTelemetry>(FrameContextCount);
        for (var context = 0; context < FrameContextCount; context++)
        {
            if (CompleteFrameContext(context) is { } frame)
            {
                completed.Add(frame);
            }
        }

        return completed
            .OrderBy(static frame => frame.FrameIndex)
            .ToArray();
    }

    internal IReadOnlyList<ulong> CaptureWorkerChecksums() =>
        workers.Select(static worker => worker.SimulationChecksum).ToArray();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            _ = DrainFrames();
        }
        catch
        {
            // Dispose is best-effort after a device-removal/failure path.
        }

        disposed = true;
        foreach (var worker in workers)
        {
            worker.Dispose();
        }
        foreach (var collector in timestamps)
        {
            collector.Dispose();
        }
        for (var context = 0; context < FrameContextCount; context++)
        {
            finishLists[context].Dispose();
            finishAllocators[context].Dispose();
            setupLists[context].Dispose();
            setupAllocators[context].Dispose();
        }

        fenceEvent.Dispose();
        fence.Dispose();
        foreach (var renderTarget in renderTargets)
        {
            renderTarget.Dispose();
        }
        rtvHeap.Dispose();
        swapChain.Dispose();
        queue.Dispose();
        device.Dispose();
        factory.Dispose();
        window.Dispose();
    }

    private BenchmarkFrameTelemetry? CompleteFrameContext(int contextIndex)
    {
        var pending = pendingFrames[contextIndex];
        if (pending is null)
        {
            return null;
        }

        WaitForFence(frameContextFenceValues[contextIndex]);
        var gpuMilliseconds = timestamps[contextIndex].ReadElapsedMilliseconds();
        pendingFrames[contextIndex] = null;
        frameContextFenceValues[contextIndex] = 0;

        return new BenchmarkFrameTelemetry(
            pending.FrameIndex,
            pending.CpuRecordingMilliseconds,
            gpuMilliseconds,
            pending.FramePeriodMilliseconds);
    }

    private void WaitForFence(ulong value)
    {
        if (value == 0 || fence.CompletedValue >= value)
        {
            return;
        }

        fence.SetEventOnCompletion(value, fenceEvent).CheckError();
        fenceEvent.WaitOne();
    }

    private sealed record PendingFrame(
        long FrameIndex,
        double CpuRecordingMilliseconds,
        double FramePeriodMilliseconds);
}

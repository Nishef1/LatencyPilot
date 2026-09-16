using System.Diagnostics;
using System.Runtime.InteropServices;
using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

public static class PresentMonFrameMetricsReader
{
    private const int Success = 0;
    private const int InvalidProcessStatus = 6;
    private const int AlreadyTrackingProcessStatus = 7;
    private const int MiddlewareVersionLowStatus = 18;
    private const int MiddlewareVersionHighStatus = 19;
    private const int MiddlewareServiceMismatchStatus = 20;
    private const ushort SupportedApiMajor = 3;
    private const ushort MinimumApiMinor = 3;
    private const int FrameBatchCapacity = 4_096;
    private const int MaximumFrames = 500_000;
    private const int MaximumPostFlushDrainAttempts = 20;
    private static readonly TimeSpan PostFlushDrainDelay = TimeSpan.FromMilliseconds(100);
    private const double MinimumWindowMilliseconds = 250;
    private const double MaximumWindowMilliseconds = 60_000;

    public static async Task<PresentMonFrameCaptureSnapshot> CaptureAsync(
        uint processId,
        TimeSpan window,
        string? apiPath = null,
        string? controlPipeName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(processId);

        var windowMilliseconds = window.TotalMilliseconds;
        if (!double.IsFinite(windowMilliseconds) ||
            windowMilliseconds < MinimumWindowMilliseconds ||
            windowMilliseconds > MaximumWindowMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var resolvedPath = PresentMonApiLocator.Resolve(apiPath);
        if (resolvedPath is null)
        {
            return Failure(
                PresentMonWorkloadCaptureStatus.ApiUnavailable,
                processId,
                windowMilliseconds,
                0,
                null,
                null,
                null,
                "PresentMonAPI2.dll was not found in a trusted installed or LatencyPilot-controlled location.",
                startedAt,
                DateTimeOffset.UtcNow);
        }

        nint library = 0;
        nint session = 0;
        nint query = 0;
        var ownsTracking = false;
        PresentMonCloseSession? closeSession = null;
        PresentMonStopTrackingProcess? stopTracking = null;
        PresentMonFreeFrameQuery? freeQuery = null;

        try
        {
            library = NativeLibrary.Load(resolvedPath);
            var getApiVersion = GetDelegate<PresentMonGetApiVersion>(library, "pmGetApiVersion");
            var nativeVersion = new NativePresentMonVersion();
            var status = getApiVersion(out nativeVersion);
            if (status != Success)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.VersionIncompatible,
                    processId,
                    windowMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    null,
                    resolvedPath,
                    status,
                    "PresentMon API version could not be read.",
                    startedAt,
                    DateTimeOffset.UtcNow);
            }

            var apiVersion = new PresentMonApiVersionSnapshot(
                nativeVersion.Major,
                nativeVersion.Minor,
                nativeVersion.Patch);
            if (nativeVersion.Major != SupportedApiMajor || nativeVersion.Minor < MinimumApiMinor)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.VersionIncompatible,
                    processId,
                    windowMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    apiVersion,
                    resolvedPath,
                    null,
                    $"PresentMon API {apiVersion} is outside LatencyPilot's supported 3.{MinimumApiMinor}+ ABI range.",
                    startedAt,
                    DateTimeOffset.UtcNow);
            }

            var openSession = GetDelegate<PresentMonOpenSession>(library, "pmOpenSession");
            var openSessionWithPipe = GetDelegate<PresentMonOpenSessionWithPipe>(library, "pmOpenSessionWithPipe");
            closeSession = GetDelegate<PresentMonCloseSession>(library, "pmCloseSession");
            var startTracking = GetDelegate<PresentMonStartTrackingProcess>(library, "pmStartTrackingProcess");
            stopTracking = GetDelegate<PresentMonStopTrackingProcess>(library, "pmStopTrackingProcess");
            var flushFrames = GetDelegate<PresentMonFlushFrames>(library, "pmFlushFrames");
            var registerQuery = GetDelegate<PresentMonRegisterFrameQuery>(library, "pmRegisterFrameQuery");
            freeQuery = GetDelegate<PresentMonFreeFrameQuery>(library, "pmFreeFrameQuery");
            var consumeFrames = GetDelegate<PresentMonConsumeFrames>(library, "pmConsumeFrames");

            status = string.IsNullOrWhiteSpace(controlPipeName)
                ? openSession(out session)
                : openSessionWithPipe(out session, controlPipeName);
            if (status != Success || session == 0)
            {
                var incompatible = IsVersionMismatch(status);
                return Failure(
                    incompatible
                        ? PresentMonWorkloadCaptureStatus.VersionIncompatible
                        : PresentMonWorkloadCaptureStatus.ServiceUnavailable,
                    processId,
                    windowMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    apiVersion,
                    resolvedPath,
                    status,
                    incompatible
                        ? "PresentMon middleware and service versions are incompatible."
                        : "PresentMon API is present but a service session could not be opened.",
                    startedAt,
                    DateTimeOffset.UtcNow);
            }

            status = startTracking(session, processId);
            if (status == InvalidProcessStatus)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.InvalidProcess,
                    processId,
                    windowMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    apiVersion,
                    resolvedPath,
                    status,
                    "PresentMon rejected the workload process ID.",
                    startedAt,
                    DateTimeOffset.UtcNow);
            }

            // Raw-frame confirmation consumes a queue of individual frames. Sharing an
            // already-tracked process would make ownership of that queue ambiguous and
            // could contaminate or steal another consumer's samples, so fail closed.
            if (status == AlreadyTrackingProcessStatus)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.TrackingFailed,
                    processId,
                    windowMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    apiVersion,
                    resolvedPath,
                    status,
                    "Raw-frame confirmation requires exclusive PresentMon process tracking; the process was already being tracked.",
                    startedAt,
                    DateTimeOffset.UtcNow);
            }

            if (status != Success)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.TrackingFailed,
                    processId,
                    windowMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    apiVersion,
                    resolvedPath,
                    status,
                    "PresentMon could not start exclusive tracking for the workload process.",
                    startedAt,
                    DateTimeOffset.UtcNow);
            }

            ownsTracking = true;

            var essential = new[]
            {
                FrameMetric.SwapChainAddress,
                FrameMetric.CpuFrameTime,
            };
            var optional = new[]
            {
                FrameMetric.CpuBusy,
                FrameMetric.CpuWait,
                FrameMetric.GpuTime,
                FrameMetric.GpuBusy,
                FrameMetric.GpuWait,
                FrameMetric.DroppedFrame,
                FrameMetric.GpuLatency,
                FrameMetric.DisplayLatency,
            };

            var unavailable = new List<string>();
            var selected = essential.ToList();
            if (!TryRegisterProbe(session, registerQuery, freeQuery, selected, out var essentialStatus))
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.QueryUnavailable,
                    processId,
                    windowMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    apiVersion,
                    resolvedPath,
                    essentialStatus,
                    "PresentMon could not register LatencyPilot's essential raw-frame query.",
                    startedAt,
                    DateTimeOffset.UtcNow);
            }

            foreach (var candidate in optional)
            {
                var probe = selected.Append(candidate).ToArray();
                if (TryRegisterProbe(session, registerQuery, freeQuery, probe, out _))
                {
                    selected.Add(candidate);
                }
                else
                {
                    unavailable.Add(candidate.DisplayName);
                }
            }

            var elements = selected.Select(CreateQueryElement).ToArray();
            uint blobSize;
            unsafe
            {
                fixed (NativeQueryElement* elementPointer = elements)
                {
                    status = registerQuery(
                        session,
                        out query,
                        elementPointer,
                        checked((ulong)elements.Length),
                        out blobSize);
                }
            }

            if (status != Success || query == 0)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.QueryUnavailable,
                    processId,
                    windowMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    apiVersion,
                    resolvedPath,
                    status,
                    "PresentMon could not register the final raw-frame guardrail query.",
                    startedAt,
                    DateTimeOffset.UtcNow,
                    unavailable);
            }

            ValidateLayout(elements, selected, blobSize);
            await Task.Delay(window, cancellationToken).ConfigureAwait(false);

            status = flushFrames(session, processId);
            if (status == InvalidProcessStatus)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.InvalidProcess,
                    processId,
                    windowMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    apiVersion,
                    resolvedPath,
                    status,
                    "The workload process ended before PresentMon could finalize the requested raw-frame window.",
                    startedAt,
                    DateTimeOffset.UtcNow,
                    unavailable);
            }

            if (status != Success)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.PollFailed,
                    processId,
                    windowMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    apiVersion,
                    resolvedPath,
                    status,
                    "PresentMon could not flush buffered frame events before raw-frame consumption.",
                    startedAt,
                    DateTimeOffset.UtcNow,
                    unavailable);
            }

            var frames = await ConsumeAfterFlushAsync(
                    query,
                    processId,
                    consumeFrames,
                    blobSize,
                    selected,
                    elements,
                    cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            var endedAt = DateTimeOffset.UtcNow;
            if (frames.Count == 0)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.NoSwapChains,
                    processId,
                    windowMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    apiVersion,
                    resolvedPath,
                    null,
                    "The tracked process produced no consumable PresentMon frame rows during the requested window.",
                    startedAt,
                    endedAt,
                    unavailable);
            }

            return new PresentMonFrameCaptureSnapshot(
                PresentMonWorkloadCaptureStatus.Available,
                processId,
                windowMilliseconds,
                stopwatch.Elapsed.TotalMilliseconds,
                apiVersion,
                frames.AsReadOnly(),
                unavailable.AsReadOnly(),
                resolvedPath,
                null,
                null,
                startedAt,
                endedAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            BadImageFormatException or
            DllNotFoundException or
            EntryPointNotFoundException or
            FileLoadException or
            InvalidDataException or
            OverflowException)
        {
            stopwatch.Stop();
            return Failure(
                PresentMonWorkloadCaptureStatus.InvalidData,
                processId,
                windowMilliseconds,
                stopwatch.Elapsed.TotalMilliseconds,
                null,
                resolvedPath,
                null,
                $"{exception.GetType().Name}: {exception.Message}",
                startedAt,
                DateTimeOffset.UtcNow);
        }
        finally
        {
            if (query != 0 && freeQuery is not null)
            {
                _ = freeQuery(query);
            }

            if (ownsTracking && session != 0 && stopTracking is not null)
            {
                _ = stopTracking(session, processId);
            }

            if (session != 0 && closeSession is not null)
            {
                _ = closeSession(session);
            }

            if (library != 0)
            {
                NativeLibrary.Free(library);
            }
        }
    }

    private static bool IsVersionMismatch(int status) =>
        status is MiddlewareVersionLowStatus or MiddlewareVersionHighStatus or MiddlewareServiceMismatchStatus;

    private static async Task<List<PresentMonFrameMetricsSnapshot>> ConsumeAfterFlushAsync(
        nint query,
        uint processId,
        PresentMonConsumeFrames consumeFrames,
        uint blobSize,
        IReadOnlyList<FrameMetric> metrics,
        IReadOnlyList<NativeQueryElement> elements,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumPostFlushDrainAttempts; attempt++)
        {
            var frames = ConsumeAll(query, processId, consumeFrames, blobSize, metrics, elements);
            if (frames.Count > 0 || attempt == MaximumPostFlushDrainAttempts - 1)
            {
                return frames;
            }

            await Task.Delay(PostFlushDrainDelay, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("PresentMon raw-frame drain retry loop exited unexpectedly.");
    }

    private static bool TryRegisterProbe(
        nint session,
        PresentMonRegisterFrameQuery registerQuery,
        PresentMonFreeFrameQuery freeQuery,
        IReadOnlyList<FrameMetric> metrics,
        out int status)
    {
        nint probeQuery = 0;
        var elements = metrics.Select(CreateQueryElement).ToArray();
        uint blobSize;
        unsafe
        {
            fixed (NativeQueryElement* elementPointer = elements)
            {
                status = registerQuery(
                    session,
                    out probeQuery,
                    elementPointer,
                    checked((ulong)elements.Length),
                    out blobSize);
            }
        }

        if (probeQuery != 0)
        {
            _ = freeQuery(probeQuery);
        }

        if (status != Success)
        {
            return false;
        }

        try
        {
            ValidateLayout(elements, metrics, blobSize);
            return true;
        }
        catch (InvalidDataException)
        {
            status = -1;
            return false;
        }
    }

    private static unsafe List<PresentMonFrameMetricsSnapshot> ConsumeAll(
        nint query,
        uint processId,
        PresentMonConsumeFrames consumeFrames,
        uint blobSize,
        IReadOnlyList<FrameMetric> metrics,
        IReadOnlyList<NativeQueryElement> elements)
    {
        var frames = new List<PresentMonFrameMetricsSnapshot>(8_192);
        while (true)
        {
            var remaining = MaximumFrames - frames.Count;
            if (remaining <= 0)
            {
                throw new InvalidDataException(
                    $"PresentMon raw-frame capture exceeded LatencyPilot's {MaximumFrames} frame safety limit.");
            }

            var capacity = Math.Min(FrameBatchCapacity, remaining);
            var buffer = new byte[checked((int)blobSize * capacity)];
            var frameCount = checked((uint)capacity);
            int status;
            fixed (byte* bufferPointer = buffer)
            {
                status = consumeFrames(query, processId, bufferPointer, ref frameCount);
            }

            if (status != Success)
            {
                throw new InvalidDataException($"PresentMon raw-frame consumption failed with native status {status}.");
            }

            if (frameCount > capacity)
            {
                throw new InvalidDataException("PresentMon returned more raw frames than the supplied buffer can hold.");
            }

            for (var index = 0; index < frameCount; index++)
            {
                frames.Add(DecodeFrame(buffer, index, blobSize, metrics, elements));
            }

            if (frameCount < capacity)
            {
                return frames;
            }
        }
    }

    private static PresentMonFrameMetricsSnapshot DecodeFrame(
        byte[] buffer,
        int frameIndex,
        uint blobSize,
        IReadOnlyList<FrameMetric> metrics,
        IReadOnlyList<NativeQueryElement> elements)
    {
        ulong swapChainAddress = 0;
        double? cpuFrameTime = null;
        double? cpuBusy = null;
        double? cpuWait = null;
        double? gpuTime = null;
        double? gpuBusy = null;
        double? gpuWait = null;
        bool? dropped = null;
        double? gpuLatency = null;
        double? displayLatency = null;

        var rowOffset = checked(frameIndex * (int)blobSize);
        for (var metricIndex = 0; metricIndex < metrics.Count; metricIndex++)
        {
            var metric = metrics[metricIndex];
            var offset = checked(rowOffset + (int)elements[metricIndex].DataOffset);
            if (metric.ValueKind == FrameValueKind.UInt64)
            {
                swapChainAddress = BitConverter.ToUInt64(buffer, offset);
                continue;
            }

            if (metric.ValueKind == FrameValueKind.Bool)
            {
                var raw = buffer[offset];
                if (raw is not 0 and not 1)
                {
                    throw new InvalidDataException(
                        $"PresentMon metric {metric.DisplayName} returned an invalid boolean value {raw}.");
                }

                dropped = raw != 0;
                continue;
            }

            var value = BitConverter.ToDouble(buffer, offset);
            if (!double.IsFinite(value) || value < 0)
            {
                continue;
            }

            switch (metric.Id)
            {
                case NativeMetric.CpuFrameTime:
                    cpuFrameTime = value;
                    break;
                case NativeMetric.CpuBusy:
                    cpuBusy = value;
                    break;
                case NativeMetric.CpuWait:
                    cpuWait = value;
                    break;
                case NativeMetric.GpuTime:
                    gpuTime = value;
                    break;
                case NativeMetric.GpuBusy:
                    gpuBusy = value;
                    break;
                case NativeMetric.GpuWait:
                    gpuWait = value;
                    break;
                case NativeMetric.GpuLatency:
                    gpuLatency = value;
                    break;
                case NativeMetric.DisplayLatency:
                    displayLatency = value;
                    break;
            }
        }

        if (swapChainAddress == 0 || cpuFrameTime is null)
        {
            throw new InvalidDataException(
                "PresentMon returned a raw frame without LatencyPilot's required swap-chain address and CPU frame-time evidence.");
        }

        return new PresentMonFrameMetricsSnapshot(
            swapChainAddress,
            cpuFrameTime,
            cpuBusy,
            cpuWait,
            gpuTime,
            gpuBusy,
            gpuWait,
            dropped,
            gpuLatency,
            displayLatency);
    }

    private static void ValidateLayout(
        IReadOnlyList<NativeQueryElement> elements,
        IReadOnlyList<FrameMetric> metrics,
        uint blobSize)
    {
        if (elements.Count == 0 || elements.Count != metrics.Count || blobSize is 0 or > 64 * 1024)
        {
            throw new InvalidDataException("PresentMon returned an invalid raw-frame query layout.");
        }

        ulong maximumEnd = 0;
        for (var index = 0; index < elements.Count; index++)
        {
            var expectedSize = metrics[index].ValueKind switch
            {
                FrameValueKind.Bool => sizeof(byte),
                FrameValueKind.UInt64 => sizeof(ulong),
                _ => sizeof(double),
            };
            var element = elements[index];
            if (element.DataSize != (ulong)expectedSize)
            {
                throw new InvalidDataException(
                    $"PresentMon metric {metrics[index].DisplayName} returned unexpected raw-frame field size {element.DataSize}.");
            }

            maximumEnd = Math.Max(maximumEnd, checked(element.DataOffset + element.DataSize));
        }

        if (maximumEnd == 0 || maximumEnd > blobSize)
        {
            throw new InvalidDataException(
                $"PresentMon raw-frame query fields exceed the reported blob size {blobSize}.");
        }
    }

    private static NativeQueryElement CreateQueryElement(FrameMetric metric) => new()
    {
        Metric = metric.Id,
        Stat = NativeStat.None,
        DeviceId = 0,
        ArrayIndex = 0,
    };

    private static PresentMonFrameCaptureSnapshot Failure(
        PresentMonWorkloadCaptureStatus status,
        uint processId,
        double requestedWindowMilliseconds,
        double actualWindowMilliseconds,
        PresentMonApiVersionSnapshot? apiVersion,
        string? apiPath,
        int? nativeStatusCode,
        string error,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        IReadOnlyList<string>? unavailable = null) =>
        new(
            status,
            processId,
            requestedWindowMilliseconds,
            actualWindowMilliseconds,
            apiVersion,
            [],
            unavailable ?? [],
            apiPath,
            nativeStatusCode,
            error,
            startedAt,
            endedAt);

    private static T GetDelegate<T>(nint library, string exportName)
        where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, exportName));

    private enum NativeMetric
    {
        SwapChainAddress = 1,
        CpuFrameTime = 8,
        CpuBusy = 9,
        CpuWait = 10,
        GpuTime = 13,
        GpuBusy = 14,
        GpuWait = 15,
        DroppedFrames = 16,
        GpuLatency = 23,
        DisplayLatency = 24,
    }

    private enum NativeStat
    {
        None = 0,
    }

    private enum FrameValueKind
    {
        Double,
        UInt64,
        Bool,
    }

    private sealed record FrameMetric(
        NativeMetric Id,
        FrameValueKind ValueKind,
        string DisplayName)
    {
        internal static readonly FrameMetric SwapChainAddress =
            new(NativeMetric.SwapChainAddress, FrameValueKind.UInt64, "Swap-chain address");
        internal static readonly FrameMetric CpuFrameTime =
            new(NativeMetric.CpuFrameTime, FrameValueKind.Double, "CPU frame time");
        internal static readonly FrameMetric CpuBusy =
            new(NativeMetric.CpuBusy, FrameValueKind.Double, "CPU busy time");
        internal static readonly FrameMetric CpuWait =
            new(NativeMetric.CpuWait, FrameValueKind.Double, "CPU wait time");
        internal static readonly FrameMetric GpuTime =
            new(NativeMetric.GpuTime, FrameValueKind.Double, "GPU time");
        internal static readonly FrameMetric GpuBusy =
            new(NativeMetric.GpuBusy, FrameValueKind.Double, "GPU busy time");
        internal static readonly FrameMetric GpuWait =
            new(NativeMetric.GpuWait, FrameValueKind.Double, "GPU wait time");
        internal static readonly FrameMetric DroppedFrame =
            new(NativeMetric.DroppedFrames, FrameValueKind.Bool, "Dropped frame");
        internal static readonly FrameMetric GpuLatency =
            new(NativeMetric.GpuLatency, FrameValueKind.Double, "GPU latency");
        internal static readonly FrameMetric DisplayLatency =
            new(NativeMetric.DisplayLatency, FrameValueKind.Double, "Display latency");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeQueryElement
    {
        internal NativeMetric Metric;
        internal NativeStat Stat;
        internal uint DeviceId;
        internal uint ArrayIndex;
        internal ulong DataOffset;
        internal ulong DataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NativePresentMonVersion
    {
        internal ushort Major;
        internal ushort Minor;
        internal ushort Patch;
        internal fixed byte Tag[22];
        internal fixed byte Hash[8];
        internal fixed byte Config[4];
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int PresentMonGetApiVersion(out NativePresentMonVersion version);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonOpenSession(out nint session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonOpenSessionWithPipe(
        out nint session,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string controlPipeName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonCloseSession(nint session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonStartTrackingProcess(nint session, uint processId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonStopTrackingProcess(nint session, uint processId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonFlushFrames(nint session, uint processId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int PresentMonRegisterFrameQuery(
        nint session,
        out nint query,
        NativeQueryElement* elements,
        ulong elementCount,
        out uint blobSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonFreeFrameQuery(nint query);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int PresentMonConsumeFrames(
        nint query,
        uint processId,
        byte* blobs,
        ref uint frameCount);
}

using System.Runtime.InteropServices;
using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

public static class PresentMonWorkloadMetricsReader
{
    private const int Success = 0;
    private const int InvalidProcessStatus = 6;
    private const int AlreadyTrackingProcessStatus = 7;
    private const int InsufficientBufferStatus = 11;
    private const int MiddlewareVersionLowStatus = 18;
    private const int MiddlewareVersionHighStatus = 19;
    private const int MiddlewareServiceMismatchStatus = 20;
    private const ushort SupportedApiMajor = 3;
    private const ushort MinimumApiMinor = 4;
    private const uint InitialSwapChainCapacity = 8;
    private const uint MaximumSwapChainCapacity = 64;
    private const double MinimumWindowMilliseconds = 250;
    private const double MaximumWindowMilliseconds = 60_000;

    public static async Task<PresentMonWorkloadMetricsSnapshot> CaptureAsync(
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

        var capturedAt = DateTimeOffset.UtcNow;
        var resolvedPath = PresentMonApiLocator.Resolve(apiPath);
        if (resolvedPath is null)
        {
            return Failure(
                PresentMonWorkloadCaptureStatus.ApiUnavailable,
                processId,
                windowMilliseconds,
                null,
                null,
                null,
                "PresentMonAPI2.dll was not found in a trusted installed or LatencyPilot-controlled location.",
                capturedAt);
        }

        nint library = 0;
        nint session = 0;
        nint query = 0;
        var ownsTracking = false;
        PresentMonCloseSession? closeSession = null;
        PresentMonStopTrackingProcess? stopTracking = null;
        PresentMonFreeDynamicQuery? freeQuery = null;

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
                    null,
                    resolvedPath,
                    status,
                    "PresentMon API version could not be read.",
                    capturedAt);
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
                    apiVersion,
                    resolvedPath,
                    null,
                    $"PresentMon API {apiVersion} is outside LatencyPilot's supported 3.{MinimumApiMinor}+ ABI range.",
                    capturedAt);
            }

            var openSession = GetDelegate<PresentMonOpenSession>(library, "pmOpenSession");
            var openSessionWithPipe = GetDelegate<PresentMonOpenSessionWithPipe>(library, "pmOpenSessionWithPipe");
            closeSession = GetDelegate<PresentMonCloseSession>(library, "pmCloseSession");
            var startTracking = GetDelegate<PresentMonStartTrackingProcess>(library, "pmStartTrackingProcess");
            stopTracking = GetDelegate<PresentMonStopTrackingProcess>(library, "pmStopTrackingProcess");
            var flushFrames = GetDelegate<PresentMonFlushFrames>(library, "pmFlushFrames");
            var registerQuery = GetDelegate<PresentMonRegisterDynamicQuery>(library, "pmRegisterDynamicQuery");
            freeQuery = GetDelegate<PresentMonFreeDynamicQuery>(library, "pmFreeDynamicQuery");
            var pollQuery = GetDelegate<PresentMonPollDynamicQuery>(library, "pmPollDynamicQuery");

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
                    apiVersion,
                    resolvedPath,
                    status,
                    incompatible
                        ? "PresentMon middleware and service versions are incompatible."
                        : "PresentMon API is present but a service session could not be opened.",
                    capturedAt);
            }

            status = startTracking(session, processId);
            if (status == InvalidProcessStatus)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.InvalidProcess,
                    processId,
                    windowMilliseconds,
                    apiVersion,
                    resolvedPath,
                    status,
                    "PresentMon rejected the workload process ID.",
                    capturedAt);
            }

            if (status is not Success and not AlreadyTrackingProcessStatus)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.TrackingFailed,
                    processId,
                    windowMilliseconds,
                    apiVersion,
                    resolvedPath,
                    status,
                    "PresentMon could not start tracking the workload process.",
                    capturedAt);
            }

            ownsTracking = status == Success;

            var essential = new[]
            {
                QueryMetric.SwapChainAddress,
                QueryMetric.PresentedFps,
                QueryMetric.CpuFrameTime,
            };
            var optional = new[]
            {
                QueryMetric.DisplayedFps,
                QueryMetric.CpuBusy,
                QueryMetric.CpuWait,
                QueryMetric.GpuTime,
                QueryMetric.GpuBusy,
                QueryMetric.GpuWait,
                QueryMetric.DroppedFrameRatio,
                QueryMetric.GpuLatency,
                QueryMetric.DisplayLatency,
            };

            var unavailable = new List<string>();
            var selected = essential.ToList();
            if (!TryRegisterProbe(
                    session,
                    registerQuery,
                    freeQuery,
                    selected,
                    windowMilliseconds,
                    out var essentialQueryStatus))
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.QueryUnavailable,
                    processId,
                    windowMilliseconds,
                    apiVersion,
                    resolvedPath,
                    essentialQueryStatus,
                    "PresentMon could not register LatencyPilot's essential process/swap-chain query.",
                    capturedAt);
            }

            foreach (var candidate in optional)
            {
                var probe = selected.Append(candidate).ToArray();
                if (TryRegisterProbe(
                        session,
                        registerQuery,
                        freeQuery,
                        probe,
                        windowMilliseconds,
                        out _))
                {
                    selected.Add(candidate);
                }
                else
                {
                    unavailable.Add(candidate.DisplayName);
                }
            }

            var elements = selected.Select(CreateQueryElement).ToArray();
            unsafe
            {
                fixed (NativeQueryElement* elementPointer = elements)
                {
                    status = registerQuery(
                        session,
                        out query,
                        elementPointer,
                        checked((ulong)elements.Length),
                        windowMilliseconds,
                        0);
                }
            }

            if (status != Success || query == 0)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.QueryUnavailable,
                    processId,
                    windowMilliseconds,
                    apiVersion,
                    resolvedPath,
                    status,
                    "PresentMon could not register the final workload guardrail query.",
                    capturedAt,
                    unavailable);
            }

            var blobSize = ValidateAndGetBlobSize(elements, selected);
            await Task.Delay(window, cancellationToken).ConfigureAwait(false);

            status = flushFrames(session, processId);
            if (status == InvalidProcessStatus)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.InvalidProcess,
                    processId,
                    windowMilliseconds,
                    apiVersion,
                    resolvedPath,
                    status,
                    "The workload process ended before PresentMon could finalize the requested guardrail window.",
                    capturedAt,
                    unavailable);
            }

            if (status != Success)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.PollFailed,
                    processId,
                    windowMilliseconds,
                    apiVersion,
                    resolvedPath,
                    status,
                    "PresentMon could not flush buffered frame events before polling the workload guardrail query.",
                    capturedAt,
                    unavailable);
            }

            var poll = Poll(query, processId, pollQuery, blobSize);
            if (poll.Status != Success)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.PollFailed,
                    processId,
                    windowMilliseconds,
                    apiVersion,
                    resolvedPath,
                    poll.Status,
                    "PresentMon could not poll the workload guardrail query.",
                    capturedAt,
                    unavailable);
            }

            if (poll.SwapChainCount == 0)
            {
                return Failure(
                    PresentMonWorkloadCaptureStatus.NoSwapChains,
                    processId,
                    windowMilliseconds,
                    apiVersion,
                    resolvedPath,
                    null,
                    "The tracked process produced no PresentMon swap-chain rows during the requested window.",
                    capturedAt,
                    unavailable);
            }

            var rows = DecodeRows(poll.Blob, poll.SwapChainCount, blobSize, selected, elements);
            return new PresentMonWorkloadMetricsSnapshot(
                PresentMonWorkloadCaptureStatus.Available,
                processId,
                windowMilliseconds,
                apiVersion,
                rows,
                unavailable.ToArray(),
                resolvedPath,
                null,
                null,
                DateTimeOffset.UtcNow);
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
            return Failure(
                PresentMonWorkloadCaptureStatus.InvalidData,
                processId,
                windowMilliseconds,
                null,
                resolvedPath,
                null,
                $"{exception.GetType().Name}: {exception.Message}",
                capturedAt);
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

    private static bool TryRegisterProbe(
        nint session,
        PresentMonRegisterDynamicQuery registerQuery,
        PresentMonFreeDynamicQuery freeQuery,
        IReadOnlyList<QueryMetric> metrics,
        double windowMilliseconds,
        out int status)
    {
        nint probeQuery = 0;
        var elements = metrics.Select(CreateQueryElement).ToArray();
        unsafe
        {
            fixed (NativeQueryElement* elementPointer = elements)
            {
                status = registerQuery(
                    session,
                    out probeQuery,
                    elementPointer,
                    checked((ulong)elements.Length),
                    windowMilliseconds,
                    0);
            }
        }

        if (probeQuery != 0)
        {
            _ = freeQuery(probeQuery);
        }

        return status == Success;
    }

    private static unsafe PollResult Poll(
        nint query,
        uint processId,
        PresentMonPollDynamicQuery pollQuery,
        int blobSize)
    {
        var capacity = InitialSwapChainCapacity;
        while (capacity <= MaximumSwapChainCapacity)
        {
            var blob = new byte[checked(blobSize * (int)capacity)];
            var swapChainCount = capacity;
            int status;
            fixed (byte* blobPointer = blob)
            {
                status = pollQuery(query, processId, blobPointer, ref swapChainCount);
            }

            if (status == Success)
            {
                if (swapChainCount > capacity)
                {
                    throw new InvalidDataException("PresentMon returned more swap chains than the supplied query buffer can hold.");
                }

                return new PollResult(status, blob, swapChainCount);
            }

            if (status != InsufficientBufferStatus || swapChainCount <= capacity)
            {
                return new PollResult(status, [], 0);
            }

            capacity = Math.Min(swapChainCount, MaximumSwapChainCapacity);
            if (capacity < swapChainCount)
            {
                return new PollResult(InsufficientBufferStatus, [], 0);
            }
        }

        return new PollResult(InsufficientBufferStatus, [], 0);
    }

    private static int ValidateAndGetBlobSize(
        IReadOnlyList<NativeQueryElement> elements,
        IReadOnlyList<QueryMetric> metrics)
    {
        if (elements.Count == 0 || elements.Count != metrics.Count)
        {
            throw new InvalidDataException("PresentMon returned an invalid query layout.");
        }

        ulong maximumEnd = 0;
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var expectedSize = metrics[index].ValueKind == QueryValueKind.UInt64
                ? sizeof(ulong)
                : sizeof(double);
            if (element.DataSize != (ulong)expectedSize)
            {
                throw new InvalidDataException(
                    $"PresentMon metric {metrics[index].DisplayName} returned unexpected field size {element.DataSize}.");
            }

            maximumEnd = Math.Max(maximumEnd, checked(element.DataOffset + element.DataSize));
        }

        if (maximumEnd is 0 or > 64 * 1024)
        {
            throw new InvalidDataException($"PresentMon returned implausible dynamic-query row size {maximumEnd}.");
        }

        return checked((int)maximumEnd);
    }

    private static PresentMonSwapChainMetricsSnapshot[] DecodeRows(
        byte[] blob,
        uint swapChainCount,
        int blobSize,
        IReadOnlyList<QueryMetric> metrics,
        IReadOnlyList<NativeQueryElement> elements)
    {
        var rows = new PresentMonSwapChainMetricsSnapshot[checked((int)swapChainCount)];
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            ulong swapChainAddress = 0;
            double? presentedFps = null;
            double? displayedFps = null;
            double? cpuFrameTime = null;
            double? cpuBusy = null;
            double? cpuWait = null;
            double? gpuTime = null;
            double? gpuBusy = null;
            double? gpuWait = null;
            double? droppedFrameRatio = null;
            double? gpuLatency = null;
            double? displayLatency = null;

            var rowOffset = checked(rowIndex * blobSize);
            for (var metricIndex = 0; metricIndex < metrics.Count; metricIndex++)
            {
                var offset = checked(rowOffset + (int)elements[metricIndex].DataOffset);
                var metric = metrics[metricIndex];
                if (metric.ValueKind == QueryValueKind.UInt64)
                {
                    swapChainAddress = BitConverter.ToUInt64(blob, offset);
                    continue;
                }

                var value = BitConverter.ToDouble(blob, offset);
                if (!double.IsFinite(value))
                {
                    continue;
                }

                switch (metric.Id)
                {
                    case NativeMetric.PresentedFps:
                        presentedFps = value;
                        break;
                    case NativeMetric.DisplayedFps:
                        displayedFps = value;
                        break;
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
                    case NativeMetric.DroppedFrames:
                        droppedFrameRatio = value;
                        break;
                    case NativeMetric.GpuLatency:
                        gpuLatency = value;
                        break;
                    case NativeMetric.DisplayLatency:
                        displayLatency = value;
                        break;
                }
            }

            rows[rowIndex] = new PresentMonSwapChainMetricsSnapshot(
                swapChainAddress,
                presentedFps,
                displayedFps,
                cpuFrameTime,
                cpuBusy,
                cpuWait,
                gpuTime,
                gpuBusy,
                gpuWait,
                droppedFrameRatio,
                gpuLatency,
                displayLatency);
        }

        return rows;
    }

    private static NativeQueryElement CreateQueryElement(QueryMetric metric) => new()
    {
        Metric = metric.Id,
        Stat = metric.Stat,
        DeviceId = 0,
        ArrayIndex = 0,
    };

    private static PresentMonWorkloadMetricsSnapshot Failure(
        PresentMonWorkloadCaptureStatus status,
        uint processId,
        double windowMilliseconds,
        PresentMonApiVersionSnapshot? apiVersion,
        string? apiPath,
        int? nativeStatusCode,
        string error,
        DateTimeOffset capturedAt,
        IReadOnlyList<string>? unavailable = null) =>
        new(
            status,
            processId,
            windowMilliseconds,
            apiVersion,
            [],
            unavailable ?? [],
            apiPath,
            nativeStatusCode,
            error,
            capturedAt);

    private static T GetDelegate<T>(nint library, string exportName)
        where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, exportName));

    private enum NativeMetric
    {
        SwapChainAddress = 1,
        CpuFrameTime = 8,
        CpuBusy = 9,
        CpuWait = 10,
        DisplayedFps = 11,
        PresentedFps = 12,
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
        Average = 1,
    }

    private enum QueryValueKind
    {
        Double,
        UInt64,
    }

    private sealed record QueryMetric(
        NativeMetric Id,
        NativeStat Stat,
        QueryValueKind ValueKind,
        string DisplayName)
    {
        internal static readonly QueryMetric SwapChainAddress =
            new(NativeMetric.SwapChainAddress, NativeStat.None, QueryValueKind.UInt64, "Swap-chain address");
        internal static readonly QueryMetric PresentedFps =
            new(NativeMetric.PresentedFps, NativeStat.Average, QueryValueKind.Double, "Presented FPS");
        internal static readonly QueryMetric DisplayedFps =
            new(NativeMetric.DisplayedFps, NativeStat.Average, QueryValueKind.Double, "Displayed FPS");
        internal static readonly QueryMetric CpuFrameTime =
            new(NativeMetric.CpuFrameTime, NativeStat.Average, QueryValueKind.Double, "CPU frame time");
        internal static readonly QueryMetric CpuBusy =
            new(NativeMetric.CpuBusy, NativeStat.Average, QueryValueKind.Double, "CPU busy time");
        internal static readonly QueryMetric CpuWait =
            new(NativeMetric.CpuWait, NativeStat.Average, QueryValueKind.Double, "CPU wait time");
        internal static readonly QueryMetric GpuTime =
            new(NativeMetric.GpuTime, NativeStat.Average, QueryValueKind.Double, "GPU time");
        internal static readonly QueryMetric GpuBusy =
            new(NativeMetric.GpuBusy, NativeStat.Average, QueryValueKind.Double, "GPU busy time");
        internal static readonly QueryMetric GpuWait =
            new(NativeMetric.GpuWait, NativeStat.Average, QueryValueKind.Double, "GPU wait time");
        internal static readonly QueryMetric DroppedFrameRatio =
            new(NativeMetric.DroppedFrames, NativeStat.Average, QueryValueKind.Double, "Dropped-frame ratio");
        internal static readonly QueryMetric GpuLatency =
            new(NativeMetric.GpuLatency, NativeStat.Average, QueryValueKind.Double, "GPU latency");
        internal static readonly QueryMetric DisplayLatency =
            new(NativeMetric.DisplayLatency, NativeStat.Average, QueryValueKind.Double, "Display latency");
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

    private readonly record struct PollResult(int Status, byte[] Blob, uint SwapChainCount);

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
    private unsafe delegate int PresentMonRegisterDynamicQuery(
        nint session,
        out nint query,
        NativeQueryElement* elements,
        ulong elementCount,
        double windowSizeMilliseconds,
        double metricOffsetMilliseconds);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonFreeDynamicQuery(nint query);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int PresentMonPollDynamicQuery(
        nint query,
        uint processId,
        byte* blob,
        ref uint swapChainCount);
}

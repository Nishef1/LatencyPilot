using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.Devices;

public enum RawInputTimingCaptureStatus
{
    Available = 0,
    DeviceUnavailable = 1,
    RegistrationConflict = 2,
    ReadFailed = 3,
}

public sealed record RawInputTimingCaptureResult(
    RawInputTimingCaptureStatus Status,
    InputReportTimestampSeries? TimestampSeries,
    int RequestedDurationMilliseconds,
    double ActualDurationMilliseconds,
    bool ReportLimitReached,
    DateTimeOffset? StartedAtUtc,
    string? Error)
{
    public bool IsAvailable => Status == RawInputTimingCaptureStatus.Available && TimestampSeries is not null;
}

public static class RawInputTimingCapture
{
    private const int MaximumRawInputDevices = 512;
    private const int MaximumRawInputRegistrations = 512;
    private const int MaximumEnumerationAttempts = 4;
    private const int MaximumReports = 100_001;
    private const int MinimumDurationMilliseconds = 100;
    private const int MaximumDurationMilliseconds = 60_000;
    private const uint ErrorInsufficientBuffer = 122;
    private const uint StopCaptureMessage = User32RawInput.WmApp + 0x51;

    internal const uint DeviceArrivalChangeCode = User32RawInput.GidcArrival;
    internal const uint DeviceRemovalChangeCode = User32RawInput.GidcRemoval;

    private static readonly SemaphoreSlim CaptureGate = new(1, 1);
    private static readonly ConcurrentDictionary<nint, CaptureSession> Sessions = new();
    private static readonly NativeWindowProcedure WindowProcedure = WindowProc;
    private static readonly nint WindowProcedurePointer = Marshal.GetFunctionPointerForDelegate(WindowProcedure);

    public static async Task<RawInputTimingCaptureResult> CaptureAsync(
        RawInputDeviceSnapshot device,
        TimeSpan duration,
        int maximumReports = MaximumReports,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(device.DeviceInterfacePath);

        var durationMilliseconds = checked((int)Math.Ceiling(duration.TotalMilliseconds));
        if (durationMilliseconds is < MinimumDurationMilliseconds or > MaximumDurationMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                $"Raw Input timing capture duration must be between {MinimumDurationMilliseconds} and {MaximumDurationMilliseconds} ms.");
        }

        if (maximumReports is < 2 or > MaximumReports)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumReports),
                $"Raw Input timing capture is bounded to between 2 and {MaximumReports} reports.");
        }

        var (usagePage, usage) = ResolveTopLevelCollection(device);
        await CaptureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ready = new TaskCompletionSource<nint?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var completion = new TaskCompletionSource<RawInputTimingCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() => CaptureThread(
                device,
                usagePage,
                usage,
                durationMilliseconds,
                maximumReports,
                ready,
                completion))
            {
                IsBackground = true,
                Name = "LatencyPilot Raw Input timing capture",
            };
            thread.Start();

            var window = await ready.Task.ConfigureAwait(false);
            if (window is null || window == 0)
            {
                return await completion.Task.ConfigureAwait(false);
            }

            var delayTask = Task.Delay(durationMilliseconds, cancellationToken);
            var finished = await Task.WhenAny(completion.Task, delayTask).ConfigureAwait(false);
            if (finished != completion.Task &&
                !User32RawInput.PostMessage(window.Value, StopCaptureMessage, 0, 0) &&
                !completion.Task.IsCompleted)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Unable to stop the Raw Input timing capture message loop.");
            }

            var result = await completion.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            CaptureGate.Release();
        }
    }

    internal static bool IsSelectedDeviceRemoval(
        nint selectedDeviceHandle,
        nuint changeCode,
        nint changedDeviceHandle) =>
        changeCode == DeviceRemovalChangeCode && changedDeviceHandle == selectedDeviceHandle;

    private static void CaptureThread(
        RawInputDeviceSnapshot device,
        ushort usagePage,
        ushort usage,
        int durationMilliseconds,
        int maximumReports,
        TaskCompletionSource<nint?> ready,
        TaskCompletionSource<RawInputTimingCaptureResult> completion)
    {
        nint window = 0;
        nint previousWindowProcedure = 0;
        var registered = false;
        var startedAtUtc = default(DateTimeOffset?);
        var startTimestamp = 0L;
        CaptureSession? session = null;
        RawInputTimingCaptureResult? finalResult = null;

        try
        {
            var selectedDeviceHandle = FindRawInputDeviceHandle(device.DeviceInterfacePath!);
            if (selectedDeviceHandle == 0)
            {
                ready.TrySetResult(null);
                finalResult = Unavailable(
                    RawInputTimingCaptureStatus.DeviceUnavailable,
                    durationMilliseconds,
                    "The selected Raw Input device interface is no longer present.");
                return;
            }

            if (IsTopLevelCollectionRegistered(usagePage, usage))
            {
                ready.TrySetResult(null);
                finalResult = Unavailable(
                    RawInputTimingCaptureStatus.RegistrationConflict,
                    durationMilliseconds,
                    "This process already has a Raw Input registration for the selected top-level collection; LatencyPilot refuses to overwrite it.");
                return;
            }

            window = User32RawInput.CreateWindowEx(
                0,
                "STATIC",
                null,
                0,
                0,
                0,
                0,
                0,
                User32RawInput.MessageOnlyWindow,
                0,
                0,
                0);
            if (window == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to create the Raw Input message-only capture window.");
            }

            previousWindowProcedure = User32RawInput.SetWindowLongPtr(
                window,
                User32RawInput.WindowProcedureIndex,
                WindowProcedurePointer);
            if (previousWindowProcedure == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to install the Raw Input capture window procedure.");
            }

            session = new CaptureSession(selectedDeviceHandle, maximumReports, previousWindowProcedure);
            if (!Sessions.TryAdd(window, session))
            {
                throw new InvalidOperationException("Raw Input timing capture window identity collided with an active capture.");
            }

            RegisterTopLevelCollection(usagePage, usage, window, remove: false);
            registered = true;
            startedAtUtc = DateTimeOffset.UtcNow;
            startTimestamp = Stopwatch.GetTimestamp();
            ready.TrySetResult(window);

            while (true)
            {
                var getMessage = User32RawInput.GetMessage(out var message, 0, 0, 0);
                if (getMessage == 0)
                {
                    break;
                }

                if (getMessage < 0)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Raw Input capture message loop failed.");
                }

                User32RawInput.TranslateMessage(in message);
                User32RawInput.DispatchMessage(in message);
            }

            var endTimestamp = Stopwatch.GetTimestamp();
            var actualDurationMilliseconds = startTimestamp == 0
                ? 0d
                : (endTimestamp - startTimestamp) * 1_000d / Stopwatch.Frequency;

            if (session.DeviceRemoved)
            {
                finalResult = new RawInputTimingCaptureResult(
                    RawInputTimingCaptureStatus.DeviceUnavailable,
                    null,
                    durationMilliseconds,
                    actualDurationMilliseconds,
                    session.ReportLimitReached,
                    startedAtUtc,
                    "The selected Raw Input device was removed while timing capture was active.");
                return;
            }

            var status = session.ReadError is null
                ? RawInputTimingCaptureStatus.Available
                : RawInputTimingCaptureStatus.ReadFailed;
            var identity = device.PnPInstanceId ?? device.DeviceInterfacePath!;
            var series = new InputReportTimestampSeries(
                identity,
                Stopwatch.Frequency,
                session.Timestamps.ToArray());

            finalResult = new RawInputTimingCaptureResult(
                status,
                series,
                durationMilliseconds,
                actualDurationMilliseconds,
                session.ReportLimitReached,
                startedAtUtc,
                session.ReadError);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ready.TrySetResult(null);
            finalResult = Unavailable(
                RawInputTimingCaptureStatus.ReadFailed,
                durationMilliseconds,
                exception.Message,
                startedAtUtc);
        }
        finally
        {
            if (registered)
            {
                try
                {
                    RegisterTopLevelCollection(usagePage, usage, 0, remove: true);
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                {
                    finalResult = (finalResult ?? Unavailable(
                            RawInputTimingCaptureStatus.ReadFailed,
                            durationMilliseconds,
                            exception.Message,
                            startedAtUtc)) with
                    {
                        Status = RawInputTimingCaptureStatus.ReadFailed,
                        Error = $"Raw Input registration cleanup failed: {exception.Message}",
                    };
                }
            }

            if (window != 0)
            {
                Sessions.TryRemove(window, out _);
                if (previousWindowProcedure != 0)
                {
                    User32RawInput.SetWindowLongPtr(
                        window,
                        User32RawInput.WindowProcedureIndex,
                        previousWindowProcedure);
                }

                User32RawInput.DestroyWindow(window);
            }

            ready.TrySetResult(null);
            completion.TrySetResult(finalResult ?? Unavailable(
                RawInputTimingCaptureStatus.ReadFailed,
                durationMilliseconds,
                "Raw Input timing capture ended without a result.",
                startedAtUtc));
        }
    }

    private static unsafe nint WindowProc(nint window, uint message, nuint wParam, nint lParam)
    {
        if (!Sessions.TryGetValue(window, out var session))
        {
            return User32RawInput.DefWindowProc(window, message, wParam, lParam);
        }

        if (message == StopCaptureMessage)
        {
            User32RawInput.PostQuitMessage(0);
            return 0;
        }

        if (message == User32RawInput.WmInputDeviceChange)
        {
            if (IsSelectedDeviceRemoval(session.SelectedDeviceHandle, wParam, lParam))
            {
                session.DeviceRemoved = true;
                User32RawInput.PostQuitMessage(0);
            }

            return 0;
        }

        if (message == User32RawInput.WmInput)
        {
            try
            {
                var observedAt = Stopwatch.GetTimestamp();
                var header = default(NativeRawInputHeader);
                var headerSize = checked((uint)Marshal.SizeOf<NativeRawInputHeader>());
                var bufferSize = headerSize;
                var result = User32RawInput.GetRawInputData(
                    lParam,
                    User32RawInput.HeaderCommand,
                    &header,
                    ref bufferSize,
                    headerSize);
                if (result == User32RawInput.ErrorResult)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to read the Raw Input event header.");
                }

                if (header.DeviceHandle == session.SelectedDeviceHandle)
                {
                    if (session.Timestamps.Count < session.MaximumReports)
                    {
                        session.Timestamps.Add(observedAt);
                    }

                    if (session.Timestamps.Count >= session.MaximumReports)
                    {
                        session.ReportLimitReached = true;
                        User32RawInput.PostQuitMessage(0);
                    }
                }
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidDataException)
            {
                session.ReadError = exception.Message;
                User32RawInput.PostQuitMessage(0);
            }
        }

        return User32RawInput.CallWindowProc(
            session.PreviousWindowProcedure,
            window,
            message,
            wParam,
            lParam);
    }

    private static (ushort UsagePage, ushort Usage) ResolveTopLevelCollection(RawInputDeviceSnapshot device) =>
        device.Kind switch
        {
            RawInputDeviceKind.Mouse => (0x01, 0x02),
            RawInputDeviceKind.Keyboard => (0x01, 0x06),
            RawInputDeviceKind.HumanInterface when device.UsagePage is > 0 && device.Usage is > 0 =>
                (device.UsagePage.Value, device.Usage.Value),
            _ => throw new ArgumentException(
                "The selected Raw Input HID does not expose a usable top-level UsagePage/Usage identity.",
                nameof(device)),
        };

    private static unsafe nint FindRawInputDeviceHandle(string deviceInterfacePath)
    {
        var entrySize = checked((uint)Marshal.SizeOf<RawInputDeviceListEntry>());

        for (var attempt = 0; attempt < MaximumEnumerationAttempts; attempt++)
        {
            uint count = 0;
            if (User32RawInput.GetRawInputDeviceList(null, ref count, entrySize) == User32RawInput.ErrorResult)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to determine the Raw Input device count.");
            }

            if (count == 0)
            {
                return 0;
            }

            if (count > MaximumRawInputDevices)
            {
                throw new InvalidDataException($"Raw Input reported {count} devices, above the safety limit of {MaximumRawInputDevices}.");
            }

            var entries = new RawInputDeviceListEntry[checked((int)count)];
            fixed (RawInputDeviceListEntry* entriesPointer = entries)
            {
                var capacity = count;
                var returned = User32RawInput.GetRawInputDeviceList(entriesPointer, ref capacity, entrySize);
                if (returned == User32RawInput.ErrorResult)
                {
                    var error = unchecked((uint)Marshal.GetLastPInvokeError());
                    if (error == ErrorInsufficientBuffer)
                    {
                        continue;
                    }

                    throw new Win32Exception(
                        unchecked((int)error),
                        "Unable to enumerate Raw Input devices for timing capture.");
                }

                foreach (var entry in entries.Take(checked((int)Math.Min(returned, count))))
                {
                    var path = ReadRawInputDeviceInterfacePath(entry.DeviceHandle);
                    if (string.Equals(path, deviceInterfacePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.DeviceHandle;
                    }
                }
            }

            return 0;
        }

        throw new InvalidOperationException(
            "The Raw Input device list kept changing while timing capture was starting.");
    }

    private static unsafe string? ReadRawInputDeviceInterfacePath(nint deviceHandle)
    {
        uint characterCount = 0;
        if (User32RawInput.GetRawInputDeviceInfo(
                deviceHandle,
                User32RawInput.DeviceNameCommand,
                null,
                ref characterCount) == User32RawInput.ErrorResult)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to size a Raw Input device-interface name.");
        }

        if (characterCount == 0)
        {
            return null;
        }

        var buffer = new char[checked((int)characterCount)];
        fixed (char* bufferPointer = buffer)
        {
            if (User32RawInput.GetRawInputDeviceInfo(
                    deviceHandle,
                    User32RawInput.DeviceNameCommand,
                    bufferPointer,
                    ref characterCount) == User32RawInput.ErrorResult)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to read a Raw Input device-interface name.");
            }
        }

        var terminator = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, terminator >= 0 ? terminator : buffer.Length);
    }

    private static unsafe bool IsTopLevelCollectionRegistered(ushort usagePage, ushort usage)
    {
        var registrationSize = checked((uint)Marshal.SizeOf<RawInputDeviceRegistration>());

        for (var attempt = 0; attempt < MaximumEnumerationAttempts; attempt++)
        {
            uint count = 0;
            var result = User32RawInput.GetRegisteredRawInputDevices(null, ref count, registrationSize);
            if (result == User32RawInput.ErrorResult)
            {
                var error = unchecked((uint)Marshal.GetLastPInvokeError());
                if (error != ErrorInsufficientBuffer)
                {
                    throw new Win32Exception(
                        unchecked((int)error),
                        "Unable to inspect existing Raw Input registrations.");
                }
            }

            if (count == 0)
            {
                return false;
            }

            if (count > MaximumRawInputRegistrations)
            {
                throw new InvalidDataException(
                    $"Raw Input reported {count} registrations, above the safety limit of {MaximumRawInputRegistrations}.");
            }

            var registrations = new RawInputDeviceRegistration[checked((int)count)];
            fixed (RawInputDeviceRegistration* registrationsPointer = registrations)
            {
                var capacity = count;
                result = User32RawInput.GetRegisteredRawInputDevices(
                    registrationsPointer,
                    ref capacity,
                    registrationSize);
                if (result == User32RawInput.ErrorResult)
                {
                    var error = unchecked((uint)Marshal.GetLastPInvokeError());
                    if (error == ErrorInsufficientBuffer)
                    {
                        continue;
                    }

                    throw new Win32Exception(
                        unchecked((int)error),
                        "Unable to enumerate existing Raw Input registrations.");
                }

                return registrations
                    .Take(checked((int)Math.Min(result, count)))
                    .Any(item => item.UsagePage == usagePage && item.Usage == usage);
            }
        }

        throw new InvalidOperationException(
            "The Raw Input registration list kept changing while timing capture was starting.");
    }

    private static unsafe void RegisterTopLevelCollection(
        ushort usagePage,
        ushort usage,
        nint targetWindow,
        bool remove)
    {
        var registration = new RawInputDeviceRegistration
        {
            UsagePage = usagePage,
            Usage = usage,
            Flags = remove
                ? User32RawInput.RemoveFlag
                : User32RawInput.InputSinkFlag | User32RawInput.DeviceNotifyFlag,
            TargetWindow = remove ? 0 : targetWindow,
        };
        var size = checked((uint)Marshal.SizeOf<RawInputDeviceRegistration>());
        if (!User32RawInput.RegisterRawInputDevices(&registration, 1, size))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                remove
                    ? "Unable to remove the Raw Input timing capture registration."
                    : "Unable to register the selected Raw Input top-level collection for timing capture.");
        }
    }

    private static RawInputTimingCaptureResult Unavailable(
        RawInputTimingCaptureStatus status,
        int requestedDurationMilliseconds,
        string error,
        DateTimeOffset? startedAtUtc = null) =>
        new(status, null, requestedDurationMilliseconds, 0d, false, startedAtUtc, error);

    private sealed class CaptureSession(
        nint selectedDeviceHandle,
        int maximumReports,
        nint previousWindowProcedure)
    {
        internal nint SelectedDeviceHandle { get; } = selectedDeviceHandle;
        internal int MaximumReports { get; } = maximumReports;
        internal nint PreviousWindowProcedure { get; } = previousWindowProcedure;
        internal List<long> Timestamps { get; } = new(maximumReports);
        internal bool ReportLimitReached { get; set; }
        internal bool DeviceRemoved { get; set; }
        internal string? ReadError { get; set; }
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;

namespace LatencyPilot.Platform.Windows.System;

public static class UserSessionActivePathReader
{
    public static UserSessionActivePathSnapshot Capture(
        string? presentMonApiPath = null,
        string? presentMonControlPipeName = null)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        return new UserSessionActivePathSnapshot(
            CaptureComponent(ProcessorCpuSetReader.Capture),
            CaptureComponent(GraphicsAdapterReader.Capture),
            CaptureComponent(DefaultAudioRouteReader.CaptureDefaultRenderRoutes),
            CaptureComponent(InputDeviceRouteReader.Capture),
            PresentMonDeviceReader.Capture(presentMonApiPath, presentMonControlPipeName),
            capturedAt);
    }

    private static ActivePathComponent<T> CaptureComponent<T>(Func<T> capture)
        where T : class
    {
        try
        {
            return ActivePathComponent<T>.Available(capture());
        }
        catch (Exception exception) when (IsRecoverableDiscoveryException(exception))
        {
            return ActivePathComponent<T>.Failed(
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool IsRecoverableDiscoveryException(Exception exception) =>
        exception is Win32Exception or
        COMException or
        InvalidDataException or
        DllNotFoundException or
        EntryPointNotFoundException or
        PlatformNotSupportedException or
        UnauthorizedAccessException;
}

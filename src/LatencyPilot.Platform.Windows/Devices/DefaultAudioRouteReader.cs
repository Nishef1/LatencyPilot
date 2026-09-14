using System.Runtime.InteropServices;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.Devices;

public static class DefaultAudioRouteReader
{
    private const uint ClsCtxInprocServer = 0x1;
    private const int HResultNotFound = unchecked((int)0x80070490);

    private static readonly Guid DeviceTopologyInterfaceId = new("2A07407E-6497-4A18-9787-32F79BD0D98F");

    public static DefaultAudioRouteInventory CaptureDefaultRenderRoutes()
    {
        using var apartment = ComApartmentScope.Enter();
        var enumeratorObject = new MMDeviceEnumeratorComObject();
        var enumerator = (IMMDeviceEnumerator)enumeratorObject;

        try
        {
            var capturedAt = DateTimeOffset.UtcNow;
            var routes = new[]
            {
                CaptureRole(enumerator, AudioEndpointRole.Console, NativeAudioRole.Console, capturedAt),
                CaptureRole(enumerator, AudioEndpointRole.Multimedia, NativeAudioRole.Multimedia, capturedAt),
                CaptureRole(enumerator, AudioEndpointRole.Communications, NativeAudioRole.Communications, capturedAt),
            };

            return new DefaultAudioRouteInventory(routes, capturedAt);
        }
        finally
        {
            ReleaseComObject(enumeratorObject);
        }
    }

    private static DefaultAudioRouteSnapshot CaptureRole(
        IMMDeviceEnumerator enumerator,
        AudioEndpointRole role,
        NativeAudioRole nativeRole,
        DateTimeOffset capturedAt)
    {
        IMMDevice? endpoint = null;
        try
        {
            var result = enumerator.GetDefaultAudioEndpoint(
                NativeAudioDataFlow.Render,
                nativeRole,
                out endpoint);
            if (result == HResultNotFound)
            {
                return new DefaultAudioRouteSnapshot(
                    role,
                    AudioRouteResolutionStatus.EndpointUnavailable,
                    null,
                    null,
                    "No default render endpoint is available for this role.",
                    capturedAt);
            }

            if (result < 0)
            {
                return Failed(role, capturedAt, "GetDefaultAudioEndpoint", result);
            }

            var endpointId = ReadAllocatedString(endpoint.GetId, out var endpointIdResult);
            if (endpointIdResult < 0 || string.IsNullOrWhiteSpace(endpointId))
            {
                return Failed(role, capturedAt, "IMMDevice.GetId", endpointIdResult);
            }

            var topologyId = TryResolveConnectedTopologyDeviceId(endpoint, out var topologyStatus, out var topologyError);
            return new DefaultAudioRouteSnapshot(
                role,
                topologyStatus,
                endpointId,
                topologyId,
                topologyError,
                capturedAt);
        }
        catch (COMException exception)
        {
            return new DefaultAudioRouteSnapshot(
                role,
                AudioRouteResolutionStatus.ReadFailed,
                null,
                null,
                $"COM failure 0x{exception.HResult:X8}.",
                capturedAt);
        }
        finally
        {
            ReleaseComObject(endpoint);
        }
    }

    private static string? TryResolveConnectedTopologyDeviceId(
        IMMDevice endpoint,
        out AudioRouteResolutionStatus status,
        out string? error)
    {
        object? activated = null;
        try
        {
            var interfaceId = DeviceTopologyInterfaceId;
            var result = endpoint.Activate(
                ref interfaceId,
                ClsCtxInprocServer,
                0,
                out activated!);
            if (result < 0 || activated is not IDeviceTopology topology)
            {
                status = AudioRouteResolutionStatus.TopologyUnavailable;
                error = $"IDeviceTopology activation failed with 0x{result:X8}.";
                return null;
            }

            result = topology.GetConnectorCount(out var connectorCount);
            if (result < 0)
            {
                status = AudioRouteResolutionStatus.TopologyUnavailable;
                error = $"GetConnectorCount failed with 0x{result:X8}.";
                return null;
            }

            for (uint index = 0; index < connectorCount; index++)
            {
                IConnector? connector = null;
                try
                {
                    result = topology.GetConnector(index, out connector);
                    if (result < 0)
                    {
                        continue;
                    }

                    var connectedId = ReadAllocatedString(connector.GetDeviceIdConnectedTo, out result);
                    if (result >= 0 && !string.IsNullOrWhiteSpace(connectedId))
                    {
                        status = AudioRouteResolutionStatus.Available;
                        error = null;
                        return connectedId;
                    }
                }
                finally
                {
                    ReleaseComObject(connector);
                }
            }

            status = AudioRouteResolutionStatus.ConnectedDeviceUnavailable;
            error = "The endpoint topology did not expose a connected hardware-topology device ID.";
            return null;
        }
        catch (COMException exception)
        {
            status = AudioRouteResolutionStatus.ReadFailed;
            error = $"Topology COM failure 0x{exception.HResult:X8}.";
            return null;
        }
        finally
        {
            ReleaseComObject(activated);
        }
    }

    private static string? ReadAllocatedString(StringGetter getter, out int result)
    {
        nint value = 0;
        try
        {
            result = getter(out value);
            return result >= 0 && value != 0 ? Marshal.PtrToStringUni(value) : null;
        }
        finally
        {
            if (value != 0)
            {
                Marshal.FreeCoTaskMem(value);
            }
        }
    }

    private static DefaultAudioRouteSnapshot Failed(
        AudioEndpointRole role,
        DateTimeOffset capturedAt,
        string operation,
        int hresult) =>
        new(
            role,
            AudioRouteResolutionStatus.ReadFailed,
            null,
            null,
            $"{operation} failed with 0x{hresult:X8}.",
            capturedAt);

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    private delegate int StringGetter(out nint value);

    private sealed class ComApartmentScope : IDisposable
    {
        private readonly bool uninitialize;

        private ComApartmentScope(bool uninitialize)
        {
            this.uninitialize = uninitialize;
        }

        public static ComApartmentScope Enter()
        {
            var result = Ole32.CoInitializeEx(0, Ole32.CoInitMultithreaded);
            if (result < 0 && result != Ole32.RpcEChangedMode)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            return new ComApartmentScope(result >= 0);
        }

        public void Dispose()
        {
            if (uninitialize)
            {
                Ole32.CoUninitialize();
            }
        }
    }
}

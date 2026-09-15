using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Interop;
using Microsoft.Win32.SafeHandles;

namespace LatencyPilot.Platform.Windows.Devices;

public static class UsbTopologyReader
{
    private const int MaximumHubInterfaces = 256;
    private const int MaximumPortsPerHub = 255;
    private const int MaximumInterfaceListCharacters = 1_048_576;
    private const int MaximumPropertyBytes = 65_536;
    private const int HubInformationBufferBytes = 512;
    private const int ConnectionInformationBufferBytes = 4_096;
    private const int DriverKeyBufferBytes = 4_096;
    private const int ConnectionInformationMinimumBytes = 35;
    private const int ConnectionInformationV2Bytes = 16;
    private const uint DevPropTypeString = 0x00000012;
    private const uint Usb300ProtocolFlag = 1u << 2;
    private const uint OperatingAtSuperSpeedOrHigherFlag = 1u << 0;
    private const uint OperatingAtSuperSpeedPlusOrHigherFlag = 1u << 2;

    private static readonly Guid UsbHubInterfaceClass = new("F18A0E88-C30C-11D0-8815-00A0C906BED8");
    private static readonly DevicePropertyKey DeviceDriverProperty = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        11);

    public static UsbTopologySnapshot Capture(DeviceInventorySnapshot inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var ports = new List<UsbHubPortSnapshot>();
        var errors = new List<string>();
        string[] hubInterfaces;

        try
        {
            hubInterfaces = EnumeratePresentHubInterfaces();
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            errors.Add($"USB hub interface enumeration failed: {exception.Message}");
            return new UsbTopologySnapshot([], DateTimeOffset.UtcNow, errors);
        }

        var graph = new DeviceRelationshipGraph(inventory);
        foreach (var hubDevicePath in hubInterfaces)
        {
            try
            {
                var hubInstanceId = TryReadInterfaceInstanceId(hubDevicePath, out var interfaceError);
                if (interfaceError is not null)
                {
                    errors.Add($"USB hub '{hubDevicePath}' instance identity unavailable: {interfaceError}");
                }

                var hostControllerInstanceId = ResolveHostControllerInstanceId(hubInstanceId, graph);
                using var hub = UsbKernelIo.CreateFile(
                    hubDevicePath,
                    UsbKernelIo.GenericWrite,
                    UsbKernelIo.FileShareWrite,
                    0,
                    UsbKernelIo.OpenExisting,
                    0,
                    0);

                if (hub.IsInvalid)
                {
                    errors.Add($"USB hub '{hubDevicePath}' could not be opened (Win32 {Marshal.GetLastPInvokeError()}).");
                    continue;
                }

                var highestPortNumber = ReadHighestPortNumber(hub);
                if (highestPortNumber > MaximumPortsPerHub)
                {
                    errors.Add($"USB hub '{hubDevicePath}' reported {highestPortNumber} ports, above the safety limit of {MaximumPortsPerHub}.");
                    continue;
                }

                for (uint connectionIndex = 1; connectionIndex <= highestPortNumber; connectionIndex++)
                {
                    try
                    {
                        ports.Add(ReadPort(
                            hub,
                            hubDevicePath,
                            hubInstanceId,
                            hostControllerInstanceId,
                            connectionIndex));
                    }
                    catch (Exception exception) when (IsRecoverable(exception))
                    {
                        errors.Add($"USB hub '{hubDevicePath}' port {connectionIndex} could not be read: {exception.Message}");
                    }
                }
            }
            catch (Exception exception) when (IsRecoverable(exception))
            {
                errors.Add($"USB hub '{hubDevicePath}' could not be inspected: {exception.Message}");
            }
        }

        ports.Sort(static (left, right) =>
        {
            var byHub = StringComparer.OrdinalIgnoreCase.Compare(left.HubDevicePath, right.HubDevicePath);
            return byHub != 0 ? byHub : left.ConnectionIndex.CompareTo(right.ConnectionIndex);
        });

        return new UsbTopologySnapshot(ports.ToArray(), DateTimeOffset.UtcNow, errors.ToArray());
    }

    internal static string? TryReadDriverKeyName(string deviceInstanceId, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);

        var locateStatus = ConfigurationManager.CM_Locate_DevNode(
            out var deviceInstance,
            deviceInstanceId,
            ConfigurationManager.LocateDeviceNodeNormal);
        if (locateStatus != ConfigurationManager.Success)
        {
            error = $"CM_Locate_DevNode failed for '{deviceInstanceId}' (0x{locateStatus:X8}).";
            return null;
        }

        unsafe
        {
            uint propertyType;
            uint requiredBytes = 0;
            var status = ConfigurationManager.CM_Get_DevNode_Property(
                deviceInstance,
                in DeviceDriverProperty,
                out propertyType,
                null,
                ref requiredBytes,
                0);
            if (status != ConfigurationManager.BufferSmall || requiredBytes < sizeof(char))
            {
                error = $"DEVPKEY_Device_Driver size query failed (0x{status:X8}).";
                return null;
            }

            if (requiredBytes > MaximumPropertyBytes)
            {
                error = $"DEVPKEY_Device_Driver requires {requiredBytes} bytes, above the safety limit.";
                return null;
            }

            var buffer = new byte[checked((int)requiredBytes)];
            fixed (byte* bufferPointer = buffer)
            {
                status = ConfigurationManager.CM_Get_DevNode_Property(
                    deviceInstance,
                    in DeviceDriverProperty,
                    out propertyType,
                    bufferPointer,
                    ref requiredBytes,
                    0);
            }

            if (status != ConfigurationManager.Success)
            {
                error = $"DEVPKEY_Device_Driver read failed (0x{status:X8}).";
                return null;
            }

            if (propertyType != DevPropTypeString)
            {
                error = $"DEVPKEY_Device_Driver returned unexpected DEVPROPTYPE 0x{propertyType:X8}.";
                return null;
            }

            error = null;
            return DecodeUtf16(buffer, requiredBytes, "DEVPKEY_Device_Driver");
        }
    }

    private static unsafe string[] EnumeratePresentHubInterfaces()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var sizeStatus = ConfigurationManager.CM_Get_Device_Interface_List_Size(
                out var characterCount,
                in UsbHubInterfaceClass,
                null,
                ConfigurationManager.GetDeviceInterfaceListPresent);
            if (sizeStatus != ConfigurationManager.Success)
            {
                throw new InvalidDataException($"CM_Get_Device_Interface_List_Size failed (0x{sizeStatus:X8}).");
            }

            if (characterCount <= 1)
            {
                return [];
            }

            if (characterCount > MaximumInterfaceListCharacters)
            {
                throw new InvalidDataException($"USB hub interface list requires {characterCount} characters, above the safety limit.");
            }

            var buffer = new char[checked((int)characterCount)];
            uint listStatus;
            fixed (char* bufferPointer = buffer)
            {
                listStatus = ConfigurationManager.CM_Get_Device_Interface_List(
                    in UsbHubInterfaceClass,
                    null,
                    bufferPointer,
                    checked((uint)buffer.Length),
                    ConfigurationManager.GetDeviceInterfaceListPresent);
            }

            if (listStatus == ConfigurationManager.BufferSmall)
            {
                continue;
            }

            if (listStatus != ConfigurationManager.Success)
            {
                throw new InvalidDataException($"CM_Get_Device_Interface_List failed (0x{listStatus:X8}).");
            }

            var interfaces = ParseMultiString(buffer);
            if (interfaces.Length > MaximumHubInterfaces)
            {
                throw new InvalidDataException($"Windows reported {interfaces.Length} USB hub interfaces, above the safety limit of {MaximumHubInterfaces}.");
            }

            return interfaces;
        }

        throw new InvalidDataException("USB hub interface enumeration did not stabilize after three attempts.");
    }

    private static string[] ParseMultiString(char[] buffer)
    {
        var values = new List<string>();
        var start = 0;
        for (var index = 0; index < buffer.Length; index++)
        {
            if (buffer[index] != '\0')
            {
                continue;
            }

            if (index == start)
            {
                break;
            }

            values.Add(new string(buffer, start, index - start));
            start = index + 1;
        }

        return values.ToArray();
    }

    private static unsafe string? TryReadInterfaceInstanceId(string deviceInterface, out string? error)
    {
        uint propertyType;
        uint requiredBytes = 0;
        var status = ConfigurationManager.CM_Get_Device_Interface_Property(
            deviceInterface,
            in DevicePropertyKeys.DeviceInstanceId,
            out propertyType,
            null,
            ref requiredBytes,
            0);
        if (status != ConfigurationManager.BufferSmall || requiredBytes < sizeof(char))
        {
            error = $"DEVPKEY_Device_InstanceId size query failed (0x{status:X8}).";
            return null;
        }

        if (requiredBytes > MaximumPropertyBytes)
        {
            error = $"DEVPKEY_Device_InstanceId requires {requiredBytes} bytes, above the safety limit.";
            return null;
        }

        var buffer = new byte[checked((int)requiredBytes)];
        fixed (byte* bufferPointer = buffer)
        {
            status = ConfigurationManager.CM_Get_Device_Interface_Property(
                deviceInterface,
                in DevicePropertyKeys.DeviceInstanceId,
                out propertyType,
                bufferPointer,
                ref requiredBytes,
                0);
        }

        if (status != ConfigurationManager.Success)
        {
            error = $"DEVPKEY_Device_InstanceId read failed (0x{status:X8}).";
            return null;
        }

        if (propertyType != DevPropTypeString)
        {
            error = $"DEVPKEY_Device_InstanceId returned unexpected DEVPROPTYPE 0x{propertyType:X8}.";
            return null;
        }

        error = null;
        return DecodeUtf16(buffer, requiredBytes, "DEVPKEY_Device_InstanceId");
    }

    private static string? ResolveHostControllerInstanceId(
        string? hubInstanceId,
        DeviceRelationshipGraph graph)
    {
        if (string.IsNullOrWhiteSpace(hubInstanceId))
        {
            return null;
        }

        var hubDevice = graph.TryGetDevice(hubInstanceId);
        if (hubDevice is null)
        {
            return null;
        }

        return new[] { hubDevice }
            .Concat(graph.GetKnownAncestors(hubDevice.InstanceId))
            .FirstOrDefault(static device =>
                string.Equals(device.ServiceName, "USBXHCI", StringComparison.OrdinalIgnoreCase))
            ?.InstanceId;
    }

    private static unsafe uint ReadHighestPortNumber(SafeFileHandle hub)
    {
        var buffer = new byte[HubInformationBufferBytes];
        fixed (byte* pointer = buffer)
        {
            if (!UsbKernelIo.DeviceIoControl(
                    hub,
                    UsbKernelIo.IoctlGetHubInformationEx,
                    pointer,
                    checked((uint)buffer.Length),
                    pointer,
                    checked((uint)buffer.Length),
                    out var bytesReturned,
                    0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "IOCTL_USB_GET_HUB_INFORMATION_EX failed.");
            }

            if (bytesReturned < 6)
            {
                throw new InvalidDataException("IOCTL_USB_GET_HUB_INFORMATION_EX returned a truncated structure.");
            }
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(4, 2));
    }

    private static unsafe UsbHubPortSnapshot ReadPort(
        SafeFileHandle hub,
        string hubDevicePath,
        string? hubInstanceId,
        string? hostControllerInstanceId,
        uint connectionIndex)
    {
        var buffer = new byte[ConnectionInformationBufferBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, sizeof(uint)), connectionIndex);

        uint bytesReturned;
        fixed (byte* pointer = buffer)
        {
            if (!UsbKernelIo.DeviceIoControl(
                    hub,
                    UsbKernelIo.IoctlGetNodeConnectionInformationEx,
                    pointer,
                    checked((uint)buffer.Length),
                    pointer,
                    checked((uint)buffer.Length),
                    out bytesReturned,
                    0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX failed.");
            }
        }

        if (bytesReturned < ConnectionInformationMinimumBytes)
        {
            throw new InvalidDataException("IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX returned a truncated structure.");
        }

        var nativeStatus = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(31, sizeof(uint)));
        var connectionStatus = nativeStatus switch
        {
            0 => UsbPortConnectionStatus.NotConnected,
            1 => UsbPortConnectionStatus.Connected,
            _ => UsbPortConnectionStatus.Failed,
        };
        var speed = connectionStatus == UsbPortConnectionStatus.Connected
            ? MapSpeed(buffer[23])
            : UsbDeviceSpeed.Unknown;
        var deviceIsHub = buffer[24] != 0;
        var driverKey = connectionStatus == UsbPortConnectionStatus.Connected
            ? TryReadPortDriverKeyName(hub, connectionIndex)
            : null;

        var (supportsUsb1, supportsUsb2, supportsUsb3, operatingAtSuperSpeed, operatingAtSuperSpeedPlus) =
            TryReadProtocolCapabilities(hub, connectionIndex);
        if (operatingAtSuperSpeedPlus)
        {
            speed = UsbDeviceSpeed.SuperPlus;
        }
        else if (operatingAtSuperSpeed && speed < UsbDeviceSpeed.Super)
        {
            speed = UsbDeviceSpeed.Super;
        }

        return new UsbHubPortSnapshot(
            hubDevicePath,
            hubInstanceId,
            hostControllerInstanceId,
            connectionIndex,
            driverKey,
            connectionStatus,
            speed,
            deviceIsHub)
        {
            SupportsUsb1 = supportsUsb1,
            SupportsUsb2 = supportsUsb2,
            SupportsUsb3 = supportsUsb3,
            OperatingAtSuperSpeedOrHigher = operatingAtSuperSpeed,
        };
    }

    private static UsbDeviceSpeed MapSpeed(byte nativeSpeed) => nativeSpeed switch
    {
        0 => UsbDeviceSpeed.Low,
        1 => UsbDeviceSpeed.Full,
        2 => UsbDeviceSpeed.High,
        3 => UsbDeviceSpeed.Super,
        _ => UsbDeviceSpeed.Unknown,
    };

    private static unsafe string? TryReadPortDriverKeyName(SafeFileHandle hub, uint connectionIndex)
    {
        var buffer = new byte[DriverKeyBufferBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, sizeof(uint)), connectionIndex);

        fixed (byte* pointer = buffer)
        {
            if (!UsbKernelIo.DeviceIoControl(
                    hub,
                    UsbKernelIo.IoctlGetNodeConnectionDriverKeyName,
                    pointer,
                    checked((uint)buffer.Length),
                    pointer,
                    checked((uint)buffer.Length),
                    out var bytesReturned,
                    0))
            {
                return null;
            }

            if (bytesReturned < 10)
            {
                return null;
            }
        }

        var actualLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4, sizeof(uint)));
        if (actualLength <= 8 || actualLength > buffer.Length || (actualLength & 1) != 0)
        {
            return null;
        }

        var byteLength = checked((int)actualLength - 8);
        return Encoding.Unicode.GetString(buffer, 8, byteLength).TrimEnd('\0') is { Length: > 0 } value
            ? value
            : null;
    }

    private static unsafe (bool Usb1, bool Usb2, bool Usb3, bool OperatingSuper, bool OperatingSuperPlus)
        TryReadProtocolCapabilities(SafeFileHandle hub, uint connectionIndex)
    {
        var buffer = new byte[ConnectionInformationV2Bytes];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), connectionIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), ConnectionInformationV2Bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), Usb300ProtocolFlag);

        fixed (byte* pointer = buffer)
        {
            if (!UsbKernelIo.DeviceIoControl(
                    hub,
                    UsbKernelIo.IoctlGetNodeConnectionInformationExV2,
                    pointer,
                    ConnectionInformationV2Bytes,
                    pointer,
                    ConnectionInformationV2Bytes,
                    out var bytesReturned,
                    0) || bytesReturned < ConnectionInformationV2Bytes)
            {
                return (false, false, false, false, false);
            }
        }

        var protocols = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8, 4));
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(12, 4));
        return (
            (protocols & (1u << 0)) != 0,
            (protocols & (1u << 1)) != 0,
            (protocols & (1u << 2)) != 0,
            (flags & OperatingAtSuperSpeedOrHigherFlag) != 0,
            (flags & OperatingAtSuperSpeedPlusOrHigherFlag) != 0);
    }

    private static string DecodeUtf16(byte[] buffer, uint actualBytes, string propertyName)
    {
        if (actualBytes > buffer.Length || (actualBytes & 1) != 0)
        {
            throw new InvalidDataException($"{propertyName} returned an invalid UTF-16 byte count.");
        }

        var value = Encoding.Unicode.GetString(buffer, 0, checked((int)actualBytes)).TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"{propertyName} returned an empty value.");
        }

        return value;
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is Win32Exception or
        InvalidDataException or
        IOException or
        UnauthorizedAccessException or
        global::System.Security.SecurityException;
}

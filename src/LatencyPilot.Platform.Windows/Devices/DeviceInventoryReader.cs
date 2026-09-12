using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.Devices;

public static class DeviceInventoryReader
{
    private const int ErrorInvalidData = 13;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoMoreItems = 259;
    private const uint RegSz = 1;

    public static DeviceInventorySnapshot CapturePresentDevices()
    {
        using var deviceInfoSet = SetupApi.GetPresentDeviceInfoSet();
        var devices = new List<PnPDeviceSnapshot>();

        for (uint index = 0; ; index++)
        {
            var deviceInfo = SpDevInfoData.Create();
            if (!SetupApi.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfo))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == ErrorNoMoreItems)
                {
                    break;
                }

                throw new Win32Exception(error, $"Unable to enumerate Plug and Play device at index {index}.");
            }

            var instanceId = ReadInstanceId(deviceInfoSet, ref deviceInfo);
            var displayName = ReadStringProperty(deviceInfoSet, ref deviceInfo, DeviceRegistryProperty.FriendlyName)
                ?? ReadStringProperty(deviceInfoSet, ref deviceInfo, DeviceRegistryProperty.DeviceDescription)
                ?? instanceId;

            devices.Add(new PnPDeviceSnapshot(
                instanceId,
                deviceInfo.ClassGuid,
                displayName,
                ReadStringProperty(deviceInfoSet, ref deviceInfo, DeviceRegistryProperty.Manufacturer),
                ReadStringProperty(deviceInfoSet, ref deviceInfo, DeviceRegistryProperty.EnumeratorName),
                ReadStringProperty(deviceInfoSet, ref deviceInfo, DeviceRegistryProperty.Service)));
        }

        devices.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.InstanceId, right.InstanceId));
        return new DeviceInventorySnapshot(devices.ToArray(), DateTimeOffset.UtcNow);
    }

    private static unsafe string ReadInstanceId(SafeDeviceInfoSetHandle deviceInfoSet, ref SpDevInfoData deviceInfo)
    {
        if (SetupApi.SetupDiGetDeviceInstanceId(deviceInfoSet, ref deviceInfo, null, 0, out var requiredSize))
        {
            throw new InvalidDataException("SetupAPI unexpectedly returned a device instance ID without a destination buffer.");
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != ErrorInsufficientBuffer || requiredSize <= 1)
        {
            throw new Win32Exception(error, "Unable to determine the device instance ID buffer size.");
        }

        while (true)
        {
            var buffer = new char[checked((int)requiredSize)];
            fixed (char* pointer = buffer)
            {
                if (SetupApi.SetupDiGetDeviceInstanceId(
                    deviceInfoSet,
                    ref deviceInfo,
                    pointer,
                    checked((uint)buffer.Length),
                    out var actualSize))
                {
                    var terminator = Array.IndexOf(buffer, '\0');
                    var length = terminator >= 0 ? terminator : buffer.Length;
                    if (length == 0)
                    {
                        throw new InvalidDataException("SetupAPI returned an empty device instance ID.");
                    }

                    return new string(buffer, 0, length);
                }

                error = Marshal.GetLastPInvokeError();
                if (error == ErrorInsufficientBuffer && actualSize > buffer.Length)
                {
                    requiredSize = actualSize;
                    continue;
                }

                throw new Win32Exception(error, "Unable to read the device instance ID.");
            }
        }
    }

    private static unsafe string? ReadStringProperty(
        SafeDeviceInfoSetHandle deviceInfoSet,
        ref SpDevInfoData deviceInfo,
        DeviceRegistryProperty property)
    {
        if (SetupApi.SetupDiGetDeviceRegistryProperty(
            deviceInfoSet,
            ref deviceInfo,
            property,
            out var propertyType,
            null,
            0,
            out var requiredSize))
        {
            return null;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error == ErrorInvalidData)
        {
            return null;
        }

        if (error != ErrorInsufficientBuffer || requiredSize == 0)
        {
            throw new Win32Exception(error, $"Unable to determine device property buffer size for {property}.");
        }

        while (true)
        {
            var buffer = new byte[checked((int)requiredSize)];
            fixed (byte* pointer = buffer)
            {
                if (SetupApi.SetupDiGetDeviceRegistryProperty(
                    deviceInfoSet,
                    ref deviceInfo,
                    property,
                    out propertyType,
                    pointer,
                    checked((uint)buffer.Length),
                    out var actualSize))
                {
                    if (propertyType != RegSz)
                    {
                        throw new InvalidDataException($"Device property {property} was expected to be REG_SZ but returned registry type {propertyType}.");
                    }

                    if (actualSize > buffer.Length || (actualSize & 1) != 0)
                    {
                        throw new InvalidDataException($"Device property {property} returned an invalid UTF-16 buffer size.");
                    }

                    var value = Encoding.Unicode.GetString(buffer, 0, checked((int)actualSize)).TrimEnd('\0');
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }

                error = Marshal.GetLastPInvokeError();
                if (error == ErrorInvalidData)
                {
                    return null;
                }

                if (error == ErrorInsufficientBuffer && actualSize > buffer.Length)
                {
                    requiredSize = actualSize;
                    continue;
                }

                throw new Win32Exception(error, $"Unable to read device property {property}.");
            }
        }
    }
}

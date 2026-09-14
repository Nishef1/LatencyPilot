using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.Devices;

public static class RawInputDeviceReader
{
    private const int MaximumDevices = 512;
    private const uint DevicePropertyTypeString = 0x00000012;

    public static unsafe RawInputDeviceInventory Capture()
    {
        var entrySize = checked((uint)Marshal.SizeOf<RawInputDeviceListEntry>());
        uint requiredCount = 0;
        var firstResult = RawInput.GetRawInputDeviceList(null, ref requiredCount, entrySize);
        if (firstResult == RawInput.ErrorResult)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to determine the raw input device count.");
        }

        if (requiredCount == 0)
        {
            return new RawInputDeviceInventory([], DateTimeOffset.UtcNow);
        }

        if (requiredCount > MaximumDevices)
        {
            throw new InvalidDataException($"Raw Input reported {requiredCount} devices, above the safety limit of {MaximumDevices}.");
        }

        while (true)
        {
            var entries = new RawInputDeviceListEntry[checked((int)requiredCount)];
            fixed (RawInputDeviceListEntry* entriesPointer = entries)
            {
                var capacity = requiredCount;
                var result = RawInput.GetRawInputDeviceList(entriesPointer, ref capacity, entrySize);
                if (result == RawInput.ErrorResult)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == 122 && capacity > requiredCount && capacity <= MaximumDevices)
                    {
                        requiredCount = capacity;
                        continue;
                    }

                    throw new Win32Exception(error, "Unable to enumerate raw input devices.");
                }

                if (result > entries.Length)
                {
                    throw new InvalidDataException("Raw Input returned more devices than fit in the supplied buffer.");
                }

                var devices = new List<RawInputDeviceSnapshot>(checked((int)result));
                for (var index = 0; index < result; index++)
                {
                    devices.Add(ReadDevice(entries[index]));
                }

                return new RawInputDeviceInventory(devices.ToArray(), DateTimeOffset.UtcNow);
            }
        }
    }

    private static unsafe RawInputDeviceSnapshot ReadDevice(RawInputDeviceListEntry entry)
    {
        var kind = entry.Type switch
        {
            RawInput.TypeMouse => RawInputDeviceKind.Mouse,
            RawInput.TypeKeyboard => RawInputDeviceKind.Keyboard,
            RawInput.TypeHid => RawInputDeviceKind.HumanInterface,
            _ => RawInputDeviceKind.Unknown,
        };

        var interfaceName = ReadDeviceName(entry.DeviceHandle);
        var mapping = TryResolveDeviceInstanceId(interfaceName);
        var info = TryReadDeviceInfo(entry.DeviceHandle);

        var isHid = info is not null && info.Value.Type == RawInput.TypeHid;
        return new RawInputDeviceSnapshot(
            kind,
            interfaceName,
            mapping.DeviceInstanceId,
            isHid ? info.Value.HidVendorId : null,
            isHid ? info.Value.HidProductId : null,
            isHid ? info.Value.HidVersionNumber : null,
            isHid ? info.Value.HidUsagePage : null,
            isHid ? info.Value.HidUsage : null,
            mapping.Error);
    }

    private static unsafe string ReadDeviceName(nint deviceHandle)
    {
        uint characterCount = 0;
        var probeResult = RawInput.GetRawInputDeviceInfo(
            deviceHandle,
            RawInput.DeviceName,
            null,
            ref characterCount);
        if (probeResult == RawInput.ErrorResult)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to determine a raw input device interface name length.");
        }

        if (characterCount is 0 or > 32_768)
        {
            throw new InvalidDataException($"Raw Input reported invalid device-name length {characterCount}.");
        }

        var buffer = new char[checked((int)characterCount)];
        fixed (char* bufferPointer = buffer)
        {
            var capacity = characterCount;
            var result = RawInput.GetRawInputDeviceInfo(
                deviceHandle,
                RawInput.DeviceName,
                bufferPointer,
                ref capacity);
            if (result == RawInput.ErrorResult)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to read a raw input device interface name.");
            }

            var length = Array.IndexOf(buffer, '\0');
            if (length < 0)
            {
                length = checked((int)Math.Min(result, (uint)buffer.Length));
            }

            return new string(buffer, 0, length);
        }
    }

    private static unsafe RawInputDeviceInfo? TryReadDeviceInfo(nint deviceHandle)
    {
        var info = RawInputDeviceInfo.Create();
        var size = info.Size;
        var result = RawInput.GetRawInputDeviceInfo(
            deviceHandle,
            RawInput.DeviceInfo,
            &info,
            ref size);

        return result == RawInput.ErrorResult ? null : info;
    }

    private static unsafe DeviceInstanceMapping TryResolveDeviceInstanceId(string interfaceName)
    {
        if (string.IsNullOrWhiteSpace(interfaceName))
        {
            return new DeviceInstanceMapping(null, "Raw Input returned an empty device interface name.");
        }

        try
        {
            uint propertyType;
            uint requiredBytes = 0;
            var propertyKey = DevicePropertyKeys.DeviceInstanceId;
            var probe = ConfigurationManager.CM_Get_Device_Interface_Property(
                interfaceName,
                in propertyKey,
                out propertyType,
                null,
                ref requiredBytes,
                0);

            if (requiredBytes == 0 || (probe != ConfigurationManager.BufferSmall && probe != ConfigurationManager.Success))
            {
                return new DeviceInstanceMapping(null, $"CM_Get_Device_Interface_Property probe failed with CONFIGRET 0x{probe:X8}.");
            }

            if (requiredBytes > 64 * 1024)
            {
                return new DeviceInstanceMapping(null, $"Device instance property required an implausible {requiredBytes} bytes.");
            }

            var bytes = new byte[checked((int)requiredBytes)];
            fixed (byte* bytesPointer = bytes)
            {
                var actualBytes = requiredBytes;
                var result = ConfigurationManager.CM_Get_Device_Interface_Property(
                    interfaceName,
                    in propertyKey,
                    out propertyType,
                    bytesPointer,
                    ref actualBytes,
                    0);

                if (result != ConfigurationManager.Success)
                {
                    return new DeviceInstanceMapping(null, $"CM_Get_Device_Interface_Property failed with CONFIGRET 0x{result:X8}.");
                }

                if (propertyType != DevicePropertyTypeString)
                {
                    return new DeviceInstanceMapping(null, $"Device instance property had unexpected DEVPROPTYPE 0x{propertyType:X8}.");
                }

                if (actualBytes < sizeof(char) || (actualBytes & 1) != 0 || actualBytes > bytes.Length)
                {
                    return new DeviceInstanceMapping(null, $"Device instance property returned invalid length {actualBytes}.");
                }

                var value = Encoding.Unicode.GetString(bytes, 0, checked((int)actualBytes)).TrimEnd('\0');
                return string.IsNullOrWhiteSpace(value)
                    ? new DeviceInstanceMapping(null, "Device instance property was empty.")
                    : new DeviceInstanceMapping(value, null);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return new DeviceInstanceMapping(null, $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private readonly record struct DeviceInstanceMapping(string? DeviceInstanceId, string? Error);
}

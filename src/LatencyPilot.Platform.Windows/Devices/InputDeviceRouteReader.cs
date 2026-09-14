using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.Devices;

public static class InputDeviceRouteReader
{
    private const int MaximumRawInputDevices = 512;
    private const uint DevPropTypeString = 0x00000012;
    private static readonly DevicePropertyKey DeviceInstanceIdProperty = new(
        new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"),
        256);
    private static readonly Guid BluetoothClass = new("E0CBF06C-CD8B-4647-BB8A-263B43F0F974");

    public static UserInputRouteInventory Capture()
    {
        var inventory = DeviceInventoryReader.CapturePresentDevices();
        return Capture(inventory);
    }

    public static UserInputRouteInventory Capture(DeviceInventorySnapshot inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var graph = new DeviceRelationshipGraph(inventory);
        var rawDevices = EnumerateRawInputDevices();
        var routes = rawDevices
            .Select(rawDevice => BuildRoute(rawDevice, graph))
            .ToArray();

        return new UserInputRouteInventory(routes, DateTimeOffset.UtcNow);
    }

    private static InputDeviceRouteSnapshot BuildRoute(
        RawInputDeviceSnapshot rawDevice,
        DeviceRelationshipGraph graph)
    {
        if (rawDevice.PnPInstanceId is null)
        {
            return new InputDeviceRouteSnapshot(rawDevice, null, null, null, []);
        }

        var device = graph.TryGetDevice(rawDevice.PnPInstanceId);
        if (device is null)
        {
            var unresolved = rawDevice with
            {
                ResolutionStatus = RawInputRouteResolutionStatus.PnPDeviceUnavailable,
                Error = "The Raw Input device interface resolved to a PnP instance that was not present in the captured inventory.",
            };
            return new InputDeviceRouteSnapshot(unresolved, null, null, null, []);
        }

        var ancestors = graph.GetKnownAncestors(device.InstanceId);
        var chain = new[] { device }.Concat(ancestors).ToArray();
        var xhci = chain.FirstOrDefault(static candidate =>
            string.Equals(candidate.ServiceName, "USBXHCI", StringComparison.OrdinalIgnoreCase));
        var bluetooth = chain.FirstOrDefault(candidate =>
            candidate.ClassGuid == BluetoothClass ||
            candidate.EnumeratorName?.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) == true);

        return new InputDeviceRouteSnapshot(
            rawDevice,
            device.DisplayName,
            xhci?.InstanceId,
            bluetooth?.InstanceId,
            ancestors.Select(static ancestor => ancestor.InstanceId).ToArray());
    }

    private static unsafe RawInputDeviceSnapshot[] EnumerateRawInputDevices()
    {
        var entrySize = checked((uint)Marshal.SizeOf<RawInputDeviceListEntry>());
        uint deviceCount = 0;
        var result = User32RawInput.GetRawInputDeviceList(null, ref deviceCount, entrySize);
        if (result == User32RawInput.ErrorResult)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to determine the Raw Input device count.");
        }

        if (deviceCount == 0)
        {
            return [];
        }

        if (deviceCount > MaximumRawInputDevices)
        {
            throw new InvalidDataException(
                $"Raw Input reported {deviceCount} devices, above the safety limit of {MaximumRawInputDevices}.");
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var entries = new RawInputDeviceListEntry[checked((int)deviceCount)];
            fixed (RawInputDeviceListEntry* entriesPointer = entries)
            {
                var capacity = deviceCount;
                result = User32RawInput.GetRawInputDeviceList(entriesPointer, ref capacity, entrySize);
                if (result == User32RawInput.ErrorResult)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == 122 && capacity > deviceCount && capacity <= MaximumRawInputDevices)
                    {
                        deviceCount = capacity;
                        continue;
                    }

                    throw new Win32Exception(error, "Unable to enumerate Raw Input devices.");
                }

                return entries
                    .Take(checked((int)result))
                    .Select(CaptureRawInputDevice)
                    .ToArray();
            }
        }

        throw new InvalidDataException("Raw Input device enumeration did not stabilize after three attempts.");
    }

    private static RawInputDeviceSnapshot CaptureRawInputDevice(RawInputDeviceListEntry entry)
    {
        var kind = entry.Type switch
        {
            NativeRawInputDeviceType.Mouse => RawInputDeviceKind.Mouse,
            NativeRawInputDeviceType.Keyboard => RawInputDeviceKind.Keyboard,
            _ => RawInputDeviceKind.HumanInterface,
        };

        try
        {
            var interfacePath = ReadDeviceInterfacePath(entry.DeviceHandle);
            if (string.IsNullOrWhiteSpace(interfacePath))
            {
                return new RawInputDeviceSnapshot(
                    kind,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    RawInputRouteResolutionStatus.DeviceInterfaceUnavailable,
                    null,
                    "Raw Input did not expose a device interface path.");
            }

            var info = ReadDeviceInfo(entry.DeviceHandle);
            var instanceId = TryReadDeviceInstanceId(interfacePath, out var configStatus, out var mappingError);
            var status = instanceId is null
                ? RawInputRouteResolutionStatus.PnPInstanceUnavailable
                : RawInputRouteResolutionStatus.Available;

            return new RawInputDeviceSnapshot(
                kind,
                interfacePath,
                instanceId,
                info?.VendorId,
                info?.ProductId,
                info?.VersionNumber,
                info?.UsagePage,
                info?.Usage,
                status,
                configStatus,
                mappingError);
        }
        catch (Win32Exception exception)
        {
            return new RawInputDeviceSnapshot(
                kind,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                RawInputRouteResolutionStatus.ReadFailed,
                unchecked((uint)exception.NativeErrorCode),
                exception.Message);
        }
    }

    private static unsafe string? ReadDeviceInterfacePath(nint deviceHandle)
    {
        uint characterCount = 0;
        var result = User32RawInput.GetRawInputDeviceInfo(
            deviceHandle,
            User32RawInput.DeviceNameCommand,
            null,
            ref characterCount);
        if (result == User32RawInput.ErrorResult)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to determine the Raw Input device-interface name length.");
        }

        if (characterCount == 0)
        {
            return null;
        }

        var buffer = new char[checked((int)characterCount)];
        fixed (char* bufferPointer = buffer)
        {
            result = User32RawInput.GetRawInputDeviceInfo(
                deviceHandle,
                User32RawInput.DeviceNameCommand,
                bufferPointer,
                ref characterCount);
            if (result == User32RawInput.ErrorResult)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to read the Raw Input device-interface name.");
            }
        }

        var terminator = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, terminator >= 0 ? terminator : buffer.Length);
    }

    private static unsafe HidIdentity? ReadDeviceInfo(nint deviceHandle)
    {
        var info = RidDeviceInfo.Create();
        var size = info.Size;
        var result = User32RawInput.GetRawInputDeviceInfo(
            deviceHandle,
            User32RawInput.DeviceInfoCommand,
            &info,
            ref size);
        if (result == User32RawInput.ErrorResult)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to read Raw Input device information.");
        }

        if (info.Type != NativeRawInputDeviceType.HumanInterface)
        {
            return null;
        }

        return new HidIdentity(
            info.Hid.VendorId,
            info.Hid.ProductId,
            info.Hid.VersionNumber,
            info.Hid.UsagePage,
            info.Hid.Usage);
    }

    private static unsafe string? TryReadDeviceInstanceId(
        string deviceInterface,
        out uint? nativeStatus,
        out string? error)
    {
        uint propertyType;
        uint requiredBytes = 0;
        var result = ConfigurationManager.CM_Get_Device_Interface_Property(
            deviceInterface,
            in DeviceInstanceIdProperty,
            out propertyType,
            null,
            ref requiredBytes,
            0);
        if (result != ConfigurationManager.BufferSmall || requiredBytes < sizeof(char))
        {
            nativeStatus = result;
            error = $"CM_Get_Device_Interface_Property could not size DEVPKEY_Device_InstanceId (0x{result:X8}).";
            return null;
        }

        var buffer = new byte[checked((int)requiredBytes)];
        fixed (byte* bufferPointer = buffer)
        {
            result = ConfigurationManager.CM_Get_Device_Interface_Property(
                deviceInterface,
                in DeviceInstanceIdProperty,
                out propertyType,
                bufferPointer,
                ref requiredBytes,
                0);
        }

        if (result != ConfigurationManager.Success)
        {
            nativeStatus = result;
            error = $"CM_Get_Device_Interface_Property could not read DEVPKEY_Device_InstanceId (0x{result:X8}).";
            return null;
        }

        if (propertyType != DevPropTypeString)
        {
            nativeStatus = result;
            error = $"DEVPKEY_Device_InstanceId had unexpected DEVPROPTYPE 0x{propertyType:X8}.";
            return null;
        }

        nativeStatus = null;
        error = null;
        var byteCount = Math.Min(checked((int)requiredBytes), buffer.Length);
        return Encoding.Unicode.GetString(buffer, 0, byteCount).TrimEnd('\0');
    }

    private sealed record HidIdentity(
        uint VendorId,
        uint ProductId,
        uint VersionNumber,
        ushort UsagePage,
        ushort Usage);
}

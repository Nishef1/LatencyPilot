using System.ComponentModel;
using System.Runtime.InteropServices;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record DeviceConfigurationRestartResult(
    string DeviceInstanceId, bool SystemRestartRequired, bool DeviceStarted, bool DeviceHasProblem,
    uint? ProblemCode, uint DeviceNodeStatusFlags, uint DeviceInstallFlags)
{
    public bool RestartedInPlace => !SystemRestartRequired && DeviceStarted && !DeviceHasProblem;
}

public static class DeviceConfigurationRestartCoordinator
{
    public static DeviceConfigurationRestartResult RestartAfterConfigurationChange(string deviceInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        if (deviceInstanceId.Contains('\0')) throw new ArgumentException("Device instance ID contains NUL.", nameof(deviceInstanceId));
        if (!DeviceInventoryReader.CapturePresentDevices().Devices.Any(device =>
            string.Equals(device.InstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The requested restart target is not a present PnP device.");

        using var set = SetupApi.GetPresentDeviceInfoSet();
        var data = SpDevInfoData.Create();
        if (!SetupApi.SetupDiOpenDeviceInfo(set, deviceInstanceId, IntPtr.Zero, 0, ref data))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to open target device for configuration restart.");
        var change = SpPropChangeParams.CreatePropertyChange();
        if (!SetupApi.SetupDiSetClassInstallParams(set, ref data, ref change, checked((uint)Marshal.SizeOf<SpPropChangeParams>())))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to configure property-change restart.");
        if (!SetupApi.SetupDiCallClassInstaller(SetupApi.DifPropertyChange, set, ref data))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Windows rejected device property-change restart.");
        var install = SpDevInstallParams.Create();
        if (!SetupApi.SetupDiGetDeviceInstallParams(set, ref data, ref install))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to read device install flags after restart.");
        var result = ConfigurationManager.CM_Get_DevNode_Status(out var status, out var problem, data.DevInst, 0);
        if (result != ConfigurationManager.Success)
            throw new InvalidOperationException($"Unable to verify devnode after restart (CONFIGRET 0x{result:X8}).");
        var hasProblem = (status & ConfigurationManager.DeviceNodeHasProblem) != 0;
        var started = (status & ConfigurationManager.DeviceNodeStarted) != 0;
        var reboot = (install.Flags & (SetupApi.DiNeedRestart | SetupApi.DiNeedReboot)) != 0 ||
            (hasProblem && problem == ConfigurationManager.ProblemNeedRestart);
        return new DeviceConfigurationRestartResult(deviceInstanceId, reboot, started, hasProblem,
            hasProblem ? problem : null, status, install.Flags);
    }
}

using System.ComponentModel;
using System.Runtime.InteropServices;
using LatencyPilot.Platform.Windows.Interop;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record GpuDeviceRestartResult(
    string DeviceInstanceId,
    bool SystemRestartRequired,
    bool DeviceStarted,
    bool DeviceHasProblem,
    uint? ProblemCode,
    uint DeviceNodeStatusFlags,
    uint DeviceInstallFlags)
{
    public bool RestartedInPlace =>
        !SystemRestartRequired &&
        DeviceStarted &&
        !DeviceHasProblem;
}

public static class GpuDeviceRestartCoordinator
{
    private static readonly Guid DisplayDeviceClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");

    public static GpuDeviceRestartResult RestartAfterConfigurationChange(string deviceInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        if (deviceInstanceId.Contains('\0'))
        {
            throw new ArgumentException("Device instance ID contains an invalid NUL character.", nameof(deviceInstanceId));
        }

        EnsurePresentDisplayAdapter(deviceInstanceId);

        using var deviceInfoSet = SetupApi.GetPresentDeviceInfoSet();
        var deviceInfoData = SpDevInfoData.Create();
        if (!SetupApi.SetupDiOpenDeviceInfo(
                deviceInfoSet,
                deviceInstanceId,
                IntPtr.Zero,
                0,
                ref deviceInfoData))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Unable to open the target display adapter for a configuration-change restart.");
        }

        if (deviceInfoData.ClassGuid != DisplayDeviceClass)
        {
            throw new InvalidOperationException(
                "The requested restart target is not a display-class device.");
        }

        var propertyChange = SpPropChangeParams.CreatePropertyChange();
        if (!SetupApi.SetupDiSetClassInstallParams(
                deviceInfoSet,
                ref deviceInfoData,
                ref propertyChange,
                checked((uint)Marshal.SizeOf<SpPropChangeParams>())))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Unable to configure the display adapter property-change request.");
        }

        if (!SetupApi.SetupDiCallClassInstaller(
                SetupApi.DifPropertyChange,
                deviceInfoSet,
                ref deviceInfoData))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Windows rejected the display adapter configuration-change restart.");
        }

        var installParams = SpDevInstallParams.Create();
        if (!SetupApi.SetupDiGetDeviceInstallParams(
                deviceInfoSet,
                ref deviceInfoData,
                ref installParams))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "Unable to read the display adapter installation parameters after restart.");
        }

        var configResult = ConfigurationManager.CM_Get_DevNode_Status(
            out var deviceNodeStatus,
            out var problemNumber,
            deviceInfoData.DevInst,
            0);
        if (configResult != ConfigurationManager.Success)
        {
            throw new InvalidOperationException(
                $"Unable to verify display adapter devnode status after restart (CONFIGRET 0x{configResult:X8}).");
        }

        var hasProblem = (deviceNodeStatus & ConfigurationManager.DeviceNodeHasProblem) != 0;
        var started = (deviceNodeStatus & ConfigurationManager.DeviceNodeStarted) != 0;
        var systemRestartRequired =
            (installParams.Flags & (SetupApi.DiNeedRestart | SetupApi.DiNeedReboot)) != 0 ||
            (hasProblem && problemNumber == ConfigurationManager.ProblemNeedRestart);

        return new GpuDeviceRestartResult(
            deviceInstanceId,
            systemRestartRequired,
            started,
            hasProblem,
            hasProblem ? problemNumber : null,
            deviceNodeStatus,
            installParams.Flags);
    }

    private static void EnsurePresentDisplayAdapter(string deviceInstanceId)
    {
        var device = DeviceInventoryReader.CapturePresentDevices().Devices.FirstOrDefault(candidate =>
            candidate.ClassGuid == DisplayDeviceClass &&
            string.Equals(candidate.InstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase));

        if (device is null)
        {
            throw new InvalidOperationException(
                "The requested GPU restart target is not a present display adapter discovered by SetupAPI.");
        }
    }
}

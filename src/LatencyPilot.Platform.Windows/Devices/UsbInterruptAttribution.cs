using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record UsbInterruptAttributionEvidence(
    string ControllerInstanceId,
    string ControllerServiceName,
    string TargetModuleStem,
    int MatchingDpcEventCount,
    int MatchingIsrEventCount,
    IReadOnlyList<double> DpcDurationsMicroseconds,
    IReadOnlyList<double> IsrDurationsMicroseconds,
    int UnresolvedDpcIsrEventCount,
    bool CaptureIntegrityValid)
{
    public bool HasTargetEvidence => MatchingDpcEventCount > 0 || MatchingIsrEventCount > 0;
}

public static class UsbInterruptAttribution
{
    public static UsbInterruptAttributionEvidence Analyze(
        KernelLatencyCaptureResult capture,
        PnPDeviceSnapshot controller)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentException.ThrowIfNullOrWhiteSpace(controller.InstanceId);

        if (string.IsNullOrWhiteSpace(controller.ServiceName))
        {
            throw new InvalidOperationException(
                "The xHCI controller does not expose a driver service name, so module ownership cannot be attributed exactly.");
        }

        var targetModule = NormalizeModuleStem(controller.ServiceName);
        var matching = capture.Events
            .Where(static item => item.Kind is KernelLatencyEventKind.Dpc or KernelLatencyEventKind.Isr)
            .Where(item => ModuleMatchesService(item.ModulePath, targetModule))
            .ToArray();

        var dpcDurations = matching
            .Where(static item => item.Kind == KernelLatencyEventKind.Dpc)
            .Select(static item => item.DurationMicroseconds)
            .Where(static duration => double.IsFinite(duration) && duration >= 0)
            .ToArray();
        var isrDurations = matching
            .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
            .Select(static item => item.DurationMicroseconds)
            .Where(static duration => double.IsFinite(duration) && duration >= 0)
            .ToArray();
        var unresolved = capture.Events.Count(static item =>
            item.Kind is KernelLatencyEventKind.Dpc or KernelLatencyEventKind.Isr &&
            string.IsNullOrWhiteSpace(item.ModulePath));

        return new UsbInterruptAttributionEvidence(
            controller.InstanceId,
            controller.ServiceName,
            targetModule,
            dpcDurations.Length,
            isrDurations.Length,
            dpcDurations,
            isrDurations,
            unresolved,
            capture.IsValid);
    }

    private static bool ModuleMatchesService(string? modulePath, string serviceName)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
        {
            return false;
        }

        return string.Equals(
            NormalizeModuleStem(modulePath),
            serviceName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeModuleStem(string value)
    {
        var fileName = Path.GetFileName(value.Trim());
        return Path.GetFileNameWithoutExtension(fileName);
    }
}

using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;

namespace LatencyPilot.Platform.Windows.Devices;

public enum UsbInterruptAttributionScope { DriverWide = 0 }

public sealed record UsbInterruptAttributionEvidence(
    string ControllerInstanceId, string ControllerServiceName, string TargetModuleStem,
    int MatchingDpcEventCount, int MatchingIsrEventCount, IReadOnlyList<double> DpcDurationsMicroseconds,
    IReadOnlyList<double> IsrDurationsMicroseconds, int UnresolvedDpcIsrEventCount, bool CaptureIntegrityValid)
{
    public bool HasTargetEvidence => MatchingDpcEventCount > 0 || MatchingIsrEventCount > 0;
    public UsbInterruptAttributionScope Scope { get; init; } = UsbInterruptAttributionScope.DriverWide;
    public int SameServiceControllerCount { get; init; } = -1;
    public bool ControllerOwnershipUnambiguous => SameServiceControllerCount == 1;
}

public static class UsbInterruptAttribution
{
    public static UsbInterruptAttributionEvidence Analyze(KernelLatencyCaptureResult capture, PnPDeviceSnapshot controller) =>
        Analyze(capture, controller, null);

    public static UsbInterruptAttributionEvidence Analyze(
        KernelLatencyCaptureResult capture, PnPDeviceSnapshot controller, IReadOnlyList<PnPDeviceSnapshot>? presentXhciControllers)
    {
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(controller);
        ArgumentException.ThrowIfNullOrWhiteSpace(controller.InstanceId);
        if (string.IsNullOrWhiteSpace(controller.ServiceName))
            throw new InvalidOperationException("The xHCI controller does not expose a driver service name, so module ownership cannot be attributed.");

        var targetModule = NormalizeModuleStem(controller.ServiceName);
        var matching = capture.Events.Where(static item => item.Kind is KernelLatencyEventKind.Dpc or KernelLatencyEventKind.Isr)
            .Where(item => ModuleMatchesService(item.ModulePath, targetModule)).ToArray();
        var dpc = matching.Where(static item => item.Kind == KernelLatencyEventKind.Dpc).Select(static item => item.DurationMicroseconds)
            .Where(static value => double.IsFinite(value) && value >= 0).ToArray();
        var isr = matching.Where(static item => item.Kind == KernelLatencyEventKind.Isr).Select(static item => item.DurationMicroseconds)
            .Where(static value => double.IsFinite(value) && value >= 0).ToArray();
        var unresolved = capture.Events.Count(static item =>
            item.Kind is KernelLatencyEventKind.Dpc or KernelLatencyEventKind.Isr && string.IsNullOrWhiteSpace(item.ModulePath));
        var sameServiceCount = presentXhciControllers is null ? -1 : presentXhciControllers.Count(candidate =>
            string.Equals(candidate.ServiceName, controller.ServiceName, StringComparison.OrdinalIgnoreCase));
        return new UsbInterruptAttributionEvidence(controller.InstanceId, controller.ServiceName, targetModule,
            dpc.Length, isr.Length, dpc, isr, unresolved, capture.IsValid) { SameServiceControllerCount = sameServiceCount };
    }

    private static bool ModuleMatchesService(string? modulePath, string serviceName) =>
        !string.IsNullOrWhiteSpace(modulePath) && string.Equals(NormalizeModuleStem(modulePath), serviceName, StringComparison.OrdinalIgnoreCase);
    private static string NormalizeModuleStem(string value) => Path.GetFileNameWithoutExtension(Path.GetFileName(value.Trim()));
}

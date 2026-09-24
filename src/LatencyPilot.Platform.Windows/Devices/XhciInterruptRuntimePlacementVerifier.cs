using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record XhciInterruptIsrAttribution(
    string DeviceInstanceId,
    string DriverServiceName,
    IReadOnlyList<KernelLatencyEvent> Events,
    int UnresolvedIsrEventCount);

public sealed record XhciObservedInterruptCount(
    int ProcessorNumber,
    int IsrEventCount);

public sealed record XhciInterruptRuntimePlacementEvidence(
    string DeviceInstanceId,
    string DriverServiceName,
    byte TargetProcessorNumber,
    int MatchingResolvedIsrEventCount,
    int TargetProcessorIsrEventCount,
    int OffTargetIsrEventCount,
    int UnresolvedIsrEventCount,
    IReadOnlyList<XhciObservedInterruptCount> ObservedProcessors)
{
    public bool HasRuntimeEvidence => MatchingResolvedIsrEventCount > 0;

    public bool ConfirmsRequestedPlacement =>
        HasRuntimeEvidence &&
        TargetProcessorIsrEventCount == MatchingResolvedIsrEventCount &&
        OffTargetIsrEventCount == 0;
}

public static class XhciInterruptRuntimePlacementVerifier
{
    public static XhciInterruptRuntimePlacementEvidence Analyze(
        KernelLatencyCaptureResult capture,
        string deviceInstanceId,
        DeviceInterruptAffinityCandidate candidate) =>
        Analyze(
            ResolveIsrAttribution(
                capture,
                deviceInstanceId,
                DeviceInventoryReader.CapturePresentDevices().Devices),
            candidate);

    public static XhciInterruptIsrAttribution ResolveIsrAttribution(
        KernelLatencyCaptureResult capture,
        string deviceInstanceId,
        IReadOnlyList<PnPDeviceSnapshot> devices)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        ArgumentNullException.ThrowIfNull(devices);

        var target = devices.FirstOrDefault(device =>
            string.Equals(device.InstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            throw new InvalidOperationException(
                "The requested xHCI runtime-verification target is not a present PnP device.");
        }

        if (string.IsNullOrWhiteSpace(target.ServiceName) ||
            !string.Equals(
                NormalizeModuleStem(target.ServiceName),
                "USBXHCI",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "Manual xHCI runtime verification requires a present target owned by the USBXHCI service.");
        }

        var serviceName = NormalizeModuleStem(target.ServiceName);
        var matchingControllers = devices
            .Where(device =>
                !string.IsNullOrWhiteSpace(device.ServiceName) &&
                string.Equals(
                    NormalizeModuleStem(device.ServiceName),
                    serviceName,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matchingControllers.Length != 1)
        {
            throw new NotSupportedException(
                $"Controller-specific {serviceName} ISR attribution requires exactly one present controller using that driver service; found {matchingControllers.Length}. Shared driver-module ISR events cannot be assigned safely to one controller.");
        }

        var matching = capture.Events
            .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
            .Where(item => ModuleMatchesService(item.ModulePath, serviceName))
            .ToArray();

        return new XhciInterruptIsrAttribution(
            deviceInstanceId,
            serviceName,
            Array.AsReadOnly(matching),
            capture.Events.Count(static item =>
                item.Kind == KernelLatencyEventKind.Isr && item.ModulePath is null));
    }

    public static XhciInterruptRuntimePlacementEvidence Analyze(
        XhciInterruptIsrAttribution attribution,
        DeviceInterruptAffinityCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(attribution);
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.ProcessorGroup != 0 || candidate.ProcessorNumber >= 64)
        {
            throw new NotSupportedException(
                "xHCI runtime placement verification v1 supports only a group-0 x64 affinity candidate.");
        }

        var observedProcessors = attribution.Events
            .GroupBy(static item => item.ProcessorNumber)
            .Select(static group => new XhciObservedInterruptCount(group.Key, group.Count()))
            .OrderByDescending(static item => item.IsrEventCount)
            .ThenBy(static item => item.ProcessorNumber)
            .ToArray();
        var targetCount = attribution.Events.Count(
            item => item.ProcessorNumber == candidate.ProcessorNumber);

        return new XhciInterruptRuntimePlacementEvidence(
            attribution.DeviceInstanceId,
            attribution.DriverServiceName,
            candidate.ProcessorNumber,
            attribution.Events.Count,
            targetCount,
            attribution.Events.Count - targetCount,
            attribution.UnresolvedIsrEventCount,
            observedProcessors);
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

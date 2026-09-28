using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;

namespace LatencyPilot.Platform.Windows.Devices;

public enum XhciInterruptIsrAttributionMode
{
    SingleServiceInstance = 0,
    DisjointAllocatedAffinity = 1,
}

public sealed record XhciInterruptIsrAttribution(
    string DeviceInstanceId,
    string DriverServiceName,
    IReadOnlyList<KernelLatencyEvent> Events,
    int UnresolvedIsrEventCount,
    XhciInterruptIsrAttributionMode AttributionMode);

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
            capture,
            deviceInstanceId,
            DeviceInventoryReader.CapturePresentDevices().Devices,
            candidate);

    public static XhciInterruptRuntimePlacementEvidence Analyze(
        KernelLatencyCaptureResult capture,
        string deviceInstanceId,
        IReadOnlyList<PnPDeviceSnapshot> devices,
        DeviceInterruptAffinityCandidate candidate) =>
        Analyze(
            ResolveIsrAttribution(
                capture,
                deviceInstanceId,
                devices,
                candidate),
            candidate);

    public static XhciInterruptIsrAttribution ResolveIsrAttribution(
        KernelLatencyCaptureResult capture,
        string deviceInstanceId,
        IReadOnlyList<PnPDeviceSnapshot> devices) =>
        ResolveIsrAttribution(capture, deviceInstanceId, devices, candidate: null);

    public static XhciInterruptIsrAttribution ResolveIsrAttribution(
        KernelLatencyCaptureResult capture,
        string deviceInstanceId,
        IReadOnlyList<PnPDeviceSnapshot> devices,
        DeviceInterruptAffinityCandidate? candidate)
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

        var matchingModuleIsr = capture.Events
            .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
            .Where(item => ModuleMatchesService(item.ModulePath, serviceName))
            .ToArray();

        if (matchingControllers.Length == 1)
        {
            return new XhciInterruptIsrAttribution(
                deviceInstanceId,
                serviceName,
                Array.AsReadOnly(matchingModuleIsr),
                capture.Events.Count(static item =>
                    item.Kind == KernelLatencyEventKind.Isr && item.ModulePath is null),
                XhciInterruptIsrAttributionMode.SingleServiceInstance);
        }

        if (candidate is null)
        {
            throw new NotSupportedException(
                $"Controller-specific {serviceName} ISR attribution found {matchingControllers.Length} present controllers using the same driver service. A requested processor mask is required to prove allocation-disjoint ownership.");
        }

        ValidateDisjointAllocationAttribution(target, matchingControllers, candidate);

        var candidateMask = candidate.AffinityMask;
        var targetOnly = matchingModuleIsr
            .Where(item =>
                item.ProcessorNumber is >= 0 and < 64 &&
                (candidateMask & (1UL << item.ProcessorNumber)) != 0)
            .ToArray();

        return new XhciInterruptIsrAttribution(
            deviceInstanceId,
            serviceName,
            Array.AsReadOnly(targetOnly),
            capture.Events.Count(static item =>
                item.Kind == KernelLatencyEventKind.Isr && item.ModulePath is null),
            XhciInterruptIsrAttributionMode.DisjointAllocatedAffinity);
    }

    private static void ValidateDisjointAllocationAttribution(
        PnPDeviceSnapshot target,
        IReadOnlyList<PnPDeviceSnapshot> matchingControllers,
        DeviceInterruptAffinityCandidate candidate)
    {
        if (candidate.ProcessorGroup != 0 ||
            candidate.AffinityMask == 0)
        {
            throw new NotSupportedException(
                "Allocation-disjoint xHCI attribution v1 requires a non-empty group-0 affinity mask.");
        }

        if (!HasKnownAllocationInsideMask(target, candidate.AffinityMask))
        {
            throw new NotSupportedException(
                "The target xHCI controller does not expose translated interrupt allocation confined to the requested processor mask.");
        }

        foreach (var peer in matchingControllers)
        {
            if (string.Equals(peer.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (peer.InterruptResources.ReadStatus != InterruptResourceReadStatus.Available ||
                peer.InterruptResources.Resources.Count == 0)
            {
                throw new NotSupportedException(
                    $"Shared-driver xHCI attribution is ambiguous because peer controller '{peer.InstanceId}' has no readable translated interrupt allocation.");
            }

            foreach (var resource in peer.InterruptResources.Resources)
            {
                if (resource.ProcessorGroup != candidate.ProcessorGroup ||
                    (resource.AffinityMask & candidate.AffinityMask) != 0)
                {
                    throw new NotSupportedException(
                        $"Shared-driver xHCI attribution is ambiguous because peer controller '{peer.InstanceId}' can service interrupts on the requested processor mask.");
                }
            }
        }
    }

    private static bool HasKnownAllocationInsideMask(
        PnPDeviceSnapshot target,
        ulong requestedMask) =>
        target.InterruptResources.ReadStatus == InterruptResourceReadStatus.Available &&
        target.InterruptResources.Resources.Count > 0 &&
        target.InterruptResources.Resources.All(resource =>
            resource.ProcessorGroup == 0 &&
            resource.AffinityMask != 0 &&
            (resource.AffinityMask & ~requestedMask) == 0);

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

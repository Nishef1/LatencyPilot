using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;

namespace LatencyPilot.Platform.Windows.Devices;

public enum XhciInterruptIsrAttributionMode
{
    SingleServiceInstance = 0,
    DisjointAllocatedAffinity = 1,
    DeviceInterruptVector = 2,
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
    ulong RequestedAffinityMask,
    int MatchingResolvedIsrEventCount,
    int TargetProcessorIsrEventCount,
    int InRequestedMaskIsrEventCount,
    int OffTargetIsrEventCount,
    int UnresolvedIsrEventCount,
    IReadOnlyList<XhciObservedInterruptCount> ObservedProcessors)
{
    public bool HasRuntimeEvidence => MatchingResolvedIsrEventCount > 0;

    public bool ConfirmsRequestedPlacement =>
        HasRuntimeEvidence &&
        InRequestedMaskIsrEventCount == MatchingResolvedIsrEventCount &&
        OffTargetIsrEventCount == 0;
}

public sealed record XhciInterruptVerificationPreflight(
    bool CanAttemptControllerSpecificVerification,
    string Reason);


public static class XhciInterruptRuntimePlacementVerifier
{
    public static XhciInterruptVerificationPreflight AssessApplyPreflight(
        string deviceInstanceId,
        IReadOnlyList<PnPDeviceSnapshot> devices,
        DeviceInterruptAffinityCandidate candidate)
    {
        var controllerIds = GetMatchingControllerIds(deviceInstanceId, devices);
        var vectors = PnpInterruptVectorReader.CaptureMany(controllerIds);
        return AssessApplyPreflight(
            deviceInstanceId,
            devices,
            candidate,
            vectors);
    }

    public static XhciInterruptVerificationPreflight AssessApplyPreflight(
        string deviceInstanceId,
        IReadOnlyList<PnPDeviceSnapshot> devices,
        DeviceInterruptAffinityCandidate candidate,
        IReadOnlyDictionary<string, DeviceInterruptVectorSnapshot> vectorSnapshots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(vectorSnapshots);

        var target = GetTargetController(deviceInstanceId, devices);
        var serviceName = NormalizeModuleStem(target.ServiceName!);
        var peers = devices
            .Where(device =>
                !string.Equals(device.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(device.ServiceName) &&
                string.Equals(
                    NormalizeModuleStem(device.ServiceName),
                    serviceName,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (peers.Length == 0)
        {
            return new(
                true,
                "The routed controller is the only present USBXHCI service instance; controller-specific ETW attribution can be attempted directly.");
        }

        if (TryGetUniqueTargetVectors(
                target,
                peers,
                vectorSnapshots,
                out var targetVectors,
                out var vectorReason))
        {
            return new(
                true,
                $"Controller-specific ETW vector attribution is available for IRQ vector(s) {FormatVectors(targetVectors)}. {vectorReason}");
        }

        foreach (var peer in peers)
        {
            if (peer.InterruptResources.ReadStatus != InterruptResourceReadStatus.Available ||
                peer.InterruptResources.Resources.Count == 0)
            {
                return new(
                    false,
                    $"USB benchmark is complete, but Apply is gated because neither device-specific IRQ-vector attribution nor ConfigMgr allocation-disjoint attribution can currently prove controller ownership. Vector path: {vectorReason} ConfigMgr peer '{peer.InstanceId}' has no readable translated allocation.");
            }

            if (peer.InterruptResources.Resources.Any(resource =>
                    resource.ProcessorGroup != candidate.ProcessorGroup ||
                    resource.AffinityMask == 0))
            {
                return new(
                    false,
                    $"USB benchmark is complete, but Apply is gated because peer xHCI controller '{peer.InstanceId}' exposes unsupported translated allocation and the IRQ-vector fallback was unavailable. Vector path: {vectorReason}");
            }

            if (peer.InterruptResources.Resources.Any(resource =>
                    (resource.AffinityMask & candidate.AffinityMask) != 0))
            {
                return new(
                    false,
                    $"USB benchmark is complete, but Apply is gated because the recommended CPU overlaps translated allocation owned by peer xHCI controller '{peer.InstanceId}', and the IRQ-vector fallback was unavailable. Vector path: {vectorReason}");
            }
        }

        return new(
            true,
            $"The recommended CPU is disjoint from all {peers.Length} same-service peer xHCI controller allocation(s); controller-specific runtime verification can be attempted after Apply. IRQ-vector fallback was not required.");
    }

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
        DeviceInterruptAffinityCandidate candidate)
    {
        var controllerIds = GetMatchingControllerIds(deviceInstanceId, devices);
        var vectors = PnpInterruptVectorReader.CaptureMany(controllerIds);
        return Analyze(
            capture,
            deviceInstanceId,
            devices,
            candidate,
            vectors);
    }

    public static XhciInterruptRuntimePlacementEvidence Analyze(
        KernelLatencyCaptureResult capture,
        string deviceInstanceId,
        IReadOnlyList<PnPDeviceSnapshot> devices,
        DeviceInterruptAffinityCandidate candidate,
        IReadOnlyDictionary<string, DeviceInterruptVectorSnapshot> vectorSnapshots) =>
        Analyze(
            ResolveIsrAttribution(
                capture,
                deviceInstanceId,
                devices,
                candidate,
                vectorSnapshots),
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
        var controllerIds = GetMatchingControllerIds(deviceInstanceId, devices);
        var vectors = PnpInterruptVectorReader.CaptureMany(controllerIds);
        return ResolveIsrAttribution(
            capture,
            deviceInstanceId,
            devices,
            candidate,
            vectors);
    }

    public static XhciInterruptIsrAttribution ResolveIsrAttribution(
        KernelLatencyCaptureResult capture,
        string deviceInstanceId,
        IReadOnlyList<PnPDeviceSnapshot> devices,
        DeviceInterruptAffinityCandidate? candidate,
        IReadOnlyDictionary<string, DeviceInterruptVectorSnapshot> vectorSnapshots)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(vectorSnapshots);

        var target = GetTargetController(deviceInstanceId, devices);
        var serviceName = NormalizeModuleStem(target.ServiceName!);
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
                $"Controller-specific {serviceName} ISR attribution found {matchingControllers.Length} present controllers using the same driver service. A requested processor mask is required.");
        }

        var peers = matchingControllers
            .Where(device =>
                !string.Equals(device.InstanceId, target.InstanceId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (TryGetUniqueTargetVectors(
                target,
                peers,
                vectorSnapshots,
                out var targetVectors,
                out _))
        {
            var targetVectorSet = targetVectors.ToHashSet();
            var targetVectorIsr = capture.Events
                .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
                .Where(item =>
                    item.InterruptVector is { } vector &&
                    targetVectorSet.Contains(vector))
                .ToArray();

            var conflictingKnownModules = targetVectorIsr
                .Where(item =>
                    item.ModulePath is not null &&
                    !ModuleMatchesService(item.ModulePath, serviceName))
                .ToArray();
            if (conflictingKnownModules.Length != 0)
            {
                throw new NotSupportedException(
                    $"Device-associated IRQ vector attribution observed {conflictingKnownModules.Length} ISR event(s) on the target vector(s) from a different resolved module. Controller ownership is inconsistent, so verification fails closed.");
            }

            return new XhciInterruptIsrAttribution(
                deviceInstanceId,
                serviceName,
                Array.AsReadOnly(targetVectorIsr),
                targetVectorIsr.Count(static item => item.ModulePath is null),
                XhciInterruptIsrAttributionMode.DeviceInterruptVector);
        }

        ValidateDisjointAllocationAttribution(
            target,
            matchingControllers,
            candidate);

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

    private static PnPDeviceSnapshot GetTargetController(
        string deviceInstanceId,
        IReadOnlyList<PnPDeviceSnapshot> devices)
    {
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

        return target;
    }

    private static IReadOnlyList<string> GetMatchingControllerIds(
        string deviceInstanceId,
        IReadOnlyList<PnPDeviceSnapshot> devices)
    {
        var target = GetTargetController(deviceInstanceId, devices);
        var serviceName = NormalizeModuleStem(target.ServiceName!);
        return devices
            .Where(device =>
                !string.IsNullOrWhiteSpace(device.ServiceName) &&
                string.Equals(
                    NormalizeModuleStem(device.ServiceName),
                    serviceName,
                    StringComparison.OrdinalIgnoreCase))
            .Select(static device => device.InstanceId)
            .ToArray();
    }

    private static bool TryGetUniqueTargetVectors(
        PnPDeviceSnapshot target,
        IReadOnlyList<PnPDeviceSnapshot> peers,
        IReadOnlyDictionary<string, DeviceInterruptVectorSnapshot> vectorSnapshots,
        out IReadOnlyList<int> targetVectors,
        out string reason)
    {
        targetVectors = [];
        if (!vectorSnapshots.TryGetValue(target.InstanceId, out var targetSnapshot) ||
            !targetSnapshot.HasVectors)
        {
            reason = vectorSnapshots.TryGetValue(target.InstanceId, out targetSnapshot)
                ? $"target PnP IRQ mapping is {targetSnapshot.Status}" +
                  (string.IsNullOrWhiteSpace(targetSnapshot.Error) ? "." : $": {targetSnapshot.Error}")
                : "target PnP IRQ mapping was not captured.";
            return false;
        }

        var targetSet = targetSnapshot.Vectors
            .Where(static vector => vector is > 0 and <= byte.MaxValue)
            .Distinct()
            .OrderBy(static vector => vector)
            .ToArray();
        if (targetSet.Length == 0)
        {
            reason = "target PnP IRQ mapping returned no ETW-compatible interrupt vectors.";
            return false;
        }

        foreach (var peer in peers)
        {
            if (!vectorSnapshots.TryGetValue(peer.InstanceId, out var peerSnapshot) ||
                !peerSnapshot.HasVectors)
            {
                reason = vectorSnapshots.TryGetValue(peer.InstanceId, out peerSnapshot)
                    ? $"peer '{peer.InstanceId}' PnP IRQ mapping is {peerSnapshot.Status}" +
                      (string.IsNullOrWhiteSpace(peerSnapshot.Error) ? "." : $": {peerSnapshot.Error}")
                    : $"peer '{peer.InstanceId}' PnP IRQ mapping was not captured.";
                return false;
            }

            var overlap = peerSnapshot.Vectors
                .Intersect(targetSet)
                .OrderBy(static vector => vector)
                .ToArray();
            if (overlap.Length != 0)
            {
                reason =
                    $"peer '{peer.InstanceId}' shares IRQ vector(s) {FormatVectors(overlap)} with the target.";
                return false;
            }
        }

        targetVectors = targetSet;
        reason =
            $"Win32_PnPAllocatedResource mapped the target and all {peers.Count} same-service peer controller(s) to disjoint IRQ vector sets.";
        return true;
    }

    private static string FormatVectors(IEnumerable<int> vectors) =>
        string.Join(", ", vectors.Select(static vector => $"0x{vector:X2}"));

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
        var inRequestedMaskCount = attribution.Events.Count(item =>
            item.ProcessorNumber is >= 0 and < 64 &&
            (candidate.AffinityMask & (1UL << item.ProcessorNumber)) != 0);

        return new XhciInterruptRuntimePlacementEvidence(
            attribution.DeviceInstanceId,
            attribution.DriverServiceName,
            candidate.ProcessorNumber,
            candidate.AffinityMask,
            attribution.Events.Count,
            targetCount,
            inRequestedMaskCount,
            attribution.Events.Count - inRequestedMaskCount,
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

using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public enum UsbAffinityRecommendationStatus { Ready = 0, NotReady = 1, DiagnosticOnly = 2 }

public sealed record UsbAffinityRecommendation(
    UsbAffinityRecommendationStatus Status, string? ControllerInstanceId, LogicalProcessorId? Processor,
    IReadOnlyList<string> InputDeviceInstanceIds, UsbAffinityCpuCandidate? CpuEvidence, string Reason)
{
    public bool HasCandidate =>
        ControllerInstanceId is not null &&
        Processor is not null &&
        CpuEvidence is not null;

    public bool IsReady =>
        Status == UsbAffinityRecommendationStatus.Ready &&
        HasCandidate;
}

public static class UsbAffinityRecommendationPlanner
{
    private static readonly TimeSpan MinimumCaptureDuration = TimeSpan.FromSeconds(5);

    public static UsbAffinityRecommendation Create(
        ProcessorTopologySnapshot topology, KernelLatencyCaptureResult quietCapture,
        UserInputRouteInventory inputRoutes, LogicalProcessorId gpuWinner) =>
        NotReady("Primary Raw Input PnP identity is required before automatic USB/xHCI affinity selection; LatencyPilot will not substitute another resolved mouse.");

    public static UsbAffinityRecommendation Create(
        ProcessorTopologySnapshot topology, KernelLatencyCaptureResult quietCapture,
        UserInputRouteInventory inputRoutes, LogicalProcessorId gpuWinner, string primaryInputDeviceInstanceId) =>
        Create(topology, quietCapture, inputRoutes, gpuWinner, primaryInputDeviceInstanceId, null);

    public static UsbAffinityRecommendation CreateIndependent(
        ProcessorTopologySnapshot topology,
        KernelLatencyCaptureResult quietCapture,
        UserInputRouteInventory inputRoutes,
        string primaryInputDeviceInstanceId,
        DeviceInventorySnapshot? deviceInventory,
        LogicalProcessorId? reservedGpuProcessor)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(quietCapture);
        ArgumentNullException.ThrowIfNull(inputRoutes);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryInputDeviceInstanceId);

        if (!TryValidateCapture(quietCapture, out var captureReason))
        {
            return NotReady(captureReason);
        }

        var primaryRoutes = inputRoutes.Routes.Where(route =>
            route.RawInputDevice.Kind == RawInputDeviceKind.Mouse &&
            route.RawInputDevice.ResolutionStatus == RawInputRouteResolutionStatus.Available &&
            string.Equals(
                route.RawInputDevice.PnPInstanceId,
                primaryInputDeviceInstanceId,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (primaryRoutes.Length != 1)
        {
            return NotReady(primaryRoutes.Length == 0
                ? "The selected primary Raw Input mouse is not present in the resolved route inventory."
                : "The selected primary Raw Input identity maps to more than one route; automatic mutation requires one exact route.");
        }

        var route = primaryRoutes[0];
        if (route.UsbPortRoute?.IsAvailable != true ||
            string.IsNullOrWhiteSpace(route.UsbHostControllerInstanceId) ||
            !string.Equals(
                route.UsbPortRoute.Port?.HostControllerInstanceId,
                route.UsbHostControllerInstanceId,
                StringComparison.OrdinalIgnoreCase))
        {
            return NotReady("The selected primary mouse does not have one exact USB hub/port/xHCI route.");
        }

        UsbAffinityCpuCandidate[] ranked;
        try
        {
            ranked = UsbAffinityCpuSelector.Rank(
                topology,
                quietCapture,
                reservedGpuProcessor).ToArray();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            NotSupportedException or
            ArgumentException)
        {
            return NotReady(exception.Message);
        }

        var controller = route.UsbHostControllerInstanceId!;
        var selected = ranked.FirstOrDefault(candidate =>
            IsCandidateAttributableAcrossPeerControllers(
                controller,
                candidate.Processor,
                deviceInventory,
                out _));
        if (selected is null)
        {
            var attributionReason = "No eligible xHCI CPU remains after peer-controller attribution checks.";
            if (ranked.Length > 0)
            {
                _ = IsCandidateAttributableAcrossPeerControllers(
                    controller,
                    ranked[0].Processor,
                    deviceInventory,
                    out attributionReason);
            }

            return NotReady(attributionReason);
        }

        _ = IsCandidateAttributableAcrossPeerControllers(
            controller,
            selected.Processor,
            deviceInventory,
            out var selectedAttributionReason);

        var status = reservedGpuProcessor is null
            ? UsbAffinityRecommendationStatus.DiagnosticOnly
            : UsbAffinityRecommendationStatus.Ready;
        var reservationReason = reservedGpuProcessor is { } gpuProcessor
            ? $"The physical core containing verified GPU CPU {gpuProcessor.Number} was excluded."
            : "No separately verified GPU reservation was available, so this CPU ranking is diagnostic only and cannot authorize xHCI Apply.";

        var reason =
            $"Selected CPU {selected.Processor.Number} for primary input {primaryInputDeviceInstanceId} on xHCI {controller}: " +
            $"{selected.TotalInterruptDurationMicroseconds:F1} us observed DPC+ISR time and " +
            $"{selected.InterruptTailP99Microseconds:F1} us p99 tail. " +
            $"{reservationReason} {selectedAttributionReason}";
        return new UsbAffinityRecommendation(
            status,
            controller,
            selected.Processor,
            [primaryInputDeviceInstanceId],
            selected,
            reason);
    }

    public static UsbAffinityRecommendation Create(
        ProcessorTopologySnapshot topology, KernelLatencyCaptureResult quietCapture,
        UserInputRouteInventory inputRoutes, LogicalProcessorId gpuWinner, string primaryInputDeviceInstanceId,
        DeviceInventorySnapshot? deviceInventory)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(quietCapture);
        ArgumentNullException.ThrowIfNull(inputRoutes);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryInputDeviceInstanceId);

        return CreateIndependent(
            topology,
            quietCapture,
            inputRoutes,
            primaryInputDeviceInstanceId,
            deviceInventory,
            gpuWinner);
    }

    private static bool IsCandidateAttributableAcrossPeerControllers(
        string controllerInstanceId,
        LogicalProcessorId processor,
        DeviceInventorySnapshot? deviceInventory,
        out string reason)
    {
        if (deviceInventory is null)
        {
            reason = "Peer-controller allocation was not supplied; final xHCI runtime verification remains authoritative.";
            return true;
        }

        var target = deviceInventory.Devices.FirstOrDefault(device =>
            string.Equals(device.InstanceId, controllerInstanceId, StringComparison.OrdinalIgnoreCase));
        if (target is null || string.IsNullOrWhiteSpace(target.ServiceName))
        {
            reason = "The routed xHCI controller is not present in the captured device inventory.";
            return false;
        }

        var peers = deviceInventory.Devices
            .Where(device =>
                !string.Equals(device.InstanceId, controllerInstanceId, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(device.ServiceName) &&
                string.Equals(device.ServiceName, target.ServiceName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (peers.Length == 0)
        {
            reason = "The target is the only present controller using this driver service.";
            return true;
        }

        var candidateMask = 1UL << processor.Number;
        foreach (var peer in peers)
        {
            if (peer.InterruptResources.ReadStatus != InterruptResourceReadStatus.Available ||
                peer.InterruptResources.Resources.Count == 0)
            {
                reason = $"Peer xHCI controller '{peer.InstanceId}' has no readable translated interrupt allocation, so controller-specific runtime proof cannot be planned safely.";
                return false;
            }

            if (peer.InterruptResources.Resources.Any(resource =>
                    resource.ProcessorGroup != processor.Group ||
                    resource.AffinityMask == 0))
            {
                reason = $"Peer xHCI controller '{peer.InstanceId}' exposes an unsupported or empty translated interrupt allocation.";
                return false;
            }

            if (peer.InterruptResources.Resources.Any(resource =>
                    (resource.AffinityMask & candidateMask) != 0))
            {
                reason = $"CPU {processor.Number} overlaps translated interrupt allocation owned by peer xHCI controller '{peer.InstanceId}'.";
                return false;
            }
        }

        reason = $"CPU {processor.Number} is disjoint from all {peers.Length} same-service peer xHCI controller allocation(s).";
        return true;
    }

    private static bool TryValidateCapture(KernelLatencyCaptureResult capture, out string reason)
    {
        if (!capture.IsValid) { reason = "The USB/xHCI quiet ETW capture is not clean enough for CPU-headroom selection."; return false; }
        if (capture.RequestedDuration < MinimumCaptureDuration || capture.ActualDuration < MinimumCaptureDuration ||
            capture.ActualDuration < TimeSpan.FromTicks((long)(capture.RequestedDuration.Ticks * 0.90)))
        { reason = "The USB/xHCI quiet ETW capture is too short or incomplete for CPU-headroom selection."; return false; }
        if (capture.DpcCount + capture.IsrCount == 0)
        { reason = "The USB/xHCI quiet ETW capture contains no DPC/ISR evidence; CPU headroom is unknown."; return false; }
        reason = string.Empty; return true;
    }

    private static UsbAffinityRecommendation NotReady(string reason) =>
        new(UsbAffinityRecommendationStatus.NotReady, null, null, [], null, reason);
}

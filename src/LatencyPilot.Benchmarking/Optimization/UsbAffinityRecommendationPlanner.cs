using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public enum UsbAffinityRecommendationStatus { Ready = 0, NotReady = 1 }

public sealed record UsbAffinityRecommendation(
    UsbAffinityRecommendationStatus Status, string? ControllerInstanceId, LogicalProcessorId? Processor,
    IReadOnlyList<string> InputDeviceInstanceIds, UsbAffinityCpuCandidate? CpuEvidence, string Reason)
{
    public bool IsReady => Status == UsbAffinityRecommendationStatus.Ready && ControllerInstanceId is not null && Processor is not null && CpuEvidence is not null;
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
        UserInputRouteInventory inputRoutes, LogicalProcessorId gpuWinner, string primaryInputDeviceInstanceId)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(quietCapture);
        ArgumentNullException.ThrowIfNull(inputRoutes);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryInputDeviceInstanceId);

        if (!TryValidateCapture(quietCapture, out var captureReason)) return NotReady(captureReason);

        var primaryRoutes = inputRoutes.Routes.Where(route =>
            route.RawInputDevice.Kind == RawInputDeviceKind.Mouse &&
            route.RawInputDevice.ResolutionStatus == RawInputRouteResolutionStatus.Available &&
            string.Equals(route.RawInputDevice.PnPInstanceId, primaryInputDeviceInstanceId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (primaryRoutes.Length != 1)
            return NotReady(primaryRoutes.Length == 0
                ? "The selected primary Raw Input mouse is not present in the resolved route inventory."
                : "The selected primary Raw Input identity maps to more than one route; automatic mutation requires one exact route.");

        var route = primaryRoutes[0];
        if (route.UsbPortRoute?.IsAvailable != true || string.IsNullOrWhiteSpace(route.UsbHostControllerInstanceId) ||
            !string.Equals(route.UsbPortRoute.Port?.HostControllerInstanceId, route.UsbHostControllerInstanceId, StringComparison.OrdinalIgnoreCase))
            return NotReady("The selected primary mouse does not have one exact USB hub/port/xHCI route.");

        UsbAffinityCpuCandidate selected;
        try { selected = UsbAffinityCpuSelector.Select(topology, quietCapture, gpuWinner); }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        { return NotReady(exception.Message); }

        var controller = route.UsbHostControllerInstanceId!;
        var reason = $"Selected CPU {selected.Processor.Number} for primary input {primaryInputDeviceInstanceId} on xHCI {controller}: " +
            $"{selected.TotalInterruptDurationMicroseconds:F1} us observed DPC+ISR time and {selected.InterruptTailP99Microseconds:F1} us p99 tail. " +
            $"The physical core containing GPU CPU {gpuWinner.Number} was excluded.";
        return new UsbAffinityRecommendation(UsbAffinityRecommendationStatus.Ready, controller, selected.Processor,
            [primaryInputDeviceInstanceId], selected, reason);
    }

    private static bool TryValidateCapture(KernelLatencyCaptureResult capture, out string reason)
    {
        if (!capture.IsValid) { reason = "The post-GPU ETW capture is not clean enough for USB CPU-headroom selection."; return false; }
        if (capture.RequestedDuration < MinimumCaptureDuration || capture.ActualDuration < MinimumCaptureDuration ||
            capture.ActualDuration < TimeSpan.FromTicks((long)(capture.RequestedDuration.Ticks * 0.90)))
        { reason = "The post-GPU quiet ETW capture is too short or incomplete for USB CPU-headroom selection."; return false; }
        if (capture.DpcCount + capture.IsrCount == 0)
        { reason = "The post-GPU quiet ETW capture contains no DPC/ISR evidence; CPU headroom is unknown."; return false; }
        reason = string.Empty; return true;
    }

    private static UsbAffinityRecommendation NotReady(string reason) =>
        new(UsbAffinityRecommendationStatus.NotReady, null, null, [], null, reason);
}

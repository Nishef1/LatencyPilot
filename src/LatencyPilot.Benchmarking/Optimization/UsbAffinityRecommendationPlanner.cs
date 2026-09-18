using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public enum UsbAffinityRecommendationStatus
{
    Ready = 0,
    NotReady = 1,
}

public sealed record UsbAffinityRecommendation(
    UsbAffinityRecommendationStatus Status,
    string? ControllerInstanceId,
    LogicalProcessorId? Processor,
    IReadOnlyList<string> InputDeviceInstanceIds,
    UsbAffinityCpuCandidate? CpuEvidence,
    string Reason)
{
    public bool IsReady =>
        Status == UsbAffinityRecommendationStatus.Ready &&
        ControllerInstanceId is not null &&
        Processor is not null &&
        CpuEvidence is not null;
}

public static class UsbAffinityRecommendationPlanner
{
    public static UsbAffinityRecommendation Create(
        ProcessorTopologySnapshot topology,
        KernelLatencyCaptureResult quietCapture,
        UserInputRouteInventory inputRoutes,
        LogicalProcessorId gpuWinner)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(quietCapture);
        ArgumentNullException.ThrowIfNull(inputRoutes);

        if (!quietCapture.IsValid)
        {
            return NotReady("The post-GPU ETW capture is not clean enough for USB CPU-headroom selection.");
        }

        var mouseRoutes = inputRoutes.Routes
            .Where(static route => route.RawInputDevice.Kind == RawInputDeviceKind.Mouse)
            .Where(static route =>
                route.RawInputDevice.ResolutionStatus == RawInputRouteResolutionStatus.Available &&
                route.UsbPortRoute?.IsAvailable == true &&
                !string.IsNullOrWhiteSpace(route.UsbHostControllerInstanceId) &&
                string.Equals(
                    route.UsbPortRoute.Port?.HostControllerInstanceId,
                    route.UsbHostControllerInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (mouseRoutes.Length == 0)
        {
            return NotReady(
                "No Raw Input mouse has one exact USB hub/port/xHCI route; automatic USB affinity will not guess a controller.");
        }

        var controllerGroups = mouseRoutes
            .GroupBy(
                static route => route.UsbHostControllerInstanceId!,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (controllerGroups.Length != 1)
        {
            return NotReady(
                "Mouse routes resolve to more than one xHCI controller; automatic v1 selection requires one unambiguous interrupt-owning controller.");
        }

        UsbAffinityCpuCandidate selected;
        try
        {
            selected = UsbAffinityCpuSelector.Select(topology, quietCapture, gpuWinner);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return NotReady(exception.Message);
        }

        var controller = controllerGroups[0].Key;
        var inputIds = controllerGroups[0]
            .Select(static route => route.RawInputDevice.PnPInstanceId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var reason =
            $"Selected CPU {selected.Processor.Number} for xHCI {controller}: " +
            $"{selected.TotalInterruptDurationMicroseconds:F1} us total observed DPC+ISR time, " +
            $"{selected.InterruptTailP99Microseconds:F1} us p99 tail, " +
            $"{selected.InterruptCount} events. The physical core containing GPU CPU {gpuWinner.Number} was excluded.";

        return new UsbAffinityRecommendation(
            UsbAffinityRecommendationStatus.Ready,
            controller,
            selected.Processor,
            inputIds,
            selected,
            reason);
    }

    private static UsbAffinityRecommendation NotReady(string reason) =>
        new(
            UsbAffinityRecommendationStatus.NotReady,
            null,
            null,
            [],
            null,
            reason);
}

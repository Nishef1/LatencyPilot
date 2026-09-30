using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public enum UsbAffinityRecommendationStatus { Ready = 0, NotReady = 1, DiagnosticOnly = 2 }

public sealed record UsbAffinityRecommendation(
    UsbAffinityRecommendationStatus Status, string? ControllerInstanceId, LogicalProcessorId? Processor,
    IReadOnlyList<string> InputDeviceInstanceIds, UsbAffinityCpuCandidate? CpuEvidence, string Reason,
    int? WinningPhysicalCoreIndex = null,
    int StabilityWindowCount = 1,
    int WinningCoreVotes = 1)
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

    public static UsbAffinityRecommendation CreateWithReservations(
        ProcessorTopologySnapshot topology,
        KernelLatencyCaptureResult quietCapture,
        UserInputRouteInventory inputRoutes,
        string primaryInputDeviceInstanceId,
        IReadOnlyCollection<LogicalProcessorId> reservedProcessors) =>
        CreateWithReservations(
            topology,
            [quietCapture],
            inputRoutes,
            primaryInputDeviceInstanceId,
            reservedProcessors);

    public static UsbAffinityRecommendation CreateWithReservations(
        ProcessorTopologySnapshot topology,
        IReadOnlyList<KernelLatencyCaptureResult> quietCaptures,
        UserInputRouteInventory inputRoutes,
        string primaryInputDeviceInstanceId,
        IReadOnlyCollection<LogicalProcessorId> reservedProcessors)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(quietCaptures);
        ArgumentNullException.ThrowIfNull(inputRoutes);
        ArgumentNullException.ThrowIfNull(reservedProcessors);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryInputDeviceInstanceId);
        if (quietCaptures.Count == 0)
        {
            return NotReady("USB/xHCI requires at least one quiet headroom capture.");
        }

        foreach (var capture in quietCaptures)
        {
            if (!TryValidateCapture(capture, out var captureReason))
            {
                return NotReady(captureReason);
            }
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

        UsbAffinityStableSelection stable;
        try
        {
            stable = UsbAffinityCpuSelector.SelectStable(
                topology,
                quietCaptures,
                reservedProcessors);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            NotSupportedException or
            ArgumentException)
        {
            return NotReady(exception.Message);
        }

        if (!stable.IsStable || stable.Candidate is null)
        {
            return new UsbAffinityRecommendation(
                UsbAffinityRecommendationStatus.NotReady,
                route.UsbHostControllerInstanceId,
                null,
                [primaryInputDeviceInstanceId],
                null,
                stable.Reason,
                stable.PhysicalCoreIndex,
                stable.WindowCount,
                stable.WinningCoreVotes);
        }

        var selected = stable.Candidate;
        var controller = route.UsbHostControllerInstanceId!;
        var reservedList = reservedProcessors
            .Distinct()
            .OrderBy(static processor => processor.Group)
            .ThenBy(static processor => processor.Number)
            .ToArray();
        var reservationReason = reservedList.Length == 0
            ? "No existing explicit interrupt-affinity CPU reservations were detected."
            : $"Skipped {reservedList.Length} reserved logical CPU(s): {string.Join(", ", reservedList.Select(static processor => $"CPU {processor.Number}"))}. Their physical cores were excluded from this benchmark.";

        var reason =
            $"Selected CPU {selected.Processor.Number} for primary input {primaryInputDeviceInstanceId} on xHCI {controller}: " +
            $"{selected.TotalInterruptDurationMicroseconds:F1} us median observed DPC+ISR time and " +
            $"{selected.InterruptTailP99Microseconds:F1} us median p99 tail. " +
            $"{stable.Reason} {reservationReason}";
        return new UsbAffinityRecommendation(
            UsbAffinityRecommendationStatus.Ready,
            controller,
            selected.Processor,
            [primaryInputDeviceInstanceId],
            selected,
            reason,
            stable.PhysicalCoreIndex,
            stable.WindowCount,
            stable.WinningCoreVotes);
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

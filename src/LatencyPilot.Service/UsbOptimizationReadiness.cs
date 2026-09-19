using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Metrics;
using LatencyPilot.Platform.Windows.Devices;

namespace LatencyPilot.Service;

internal static class UsbOptimizationReadiness
{
    private static readonly string[] RequiredGuardrails =
    [
        UsbOptimizationGuardrailNames.NetworkLatencyJitter,
        UsbOptimizationGuardrailNames.AudioStability,
        UsbOptimizationGuardrailNames.GraphicsFrameTime,
        UsbOptimizationGuardrailNames.SystemStability,
    ];

    internal static UsbOptimizationReadinessResult Evaluate(
        InputDeviceRouteSnapshot route,
        InputTimingSnapshot timing,
        UsbInterruptAttributionEvidence attribution)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(timing);
        ArgumentNullException.ThrowIfNull(attribution);

        var routeController = route.UsbHostControllerInstanceId;
        if (string.IsNullOrWhiteSpace(routeController) || route.UsbPortRoute?.Status != UsbPortRouteResolutionStatus.Available)
        {
            return Result(
                UsbOptimizationReadinessStatus.NotReady,
                routeController,
                "The input device does not have one exact authoritative USB hub/port/xHCI route.");
        }

        var port = route.UsbPortRoute.Port;
        if (port is null ||
            string.IsNullOrWhiteSpace(port.HostControllerInstanceId) ||
            !string.Equals(port.HostControllerInstanceId, routeController, StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                UsbOptimizationReadinessStatus.NotReady,
                routeController,
                "The exact USB port evidence does not resolve to the same xHCI controller as the PnP ancestry.");
        }

        if (!string.Equals(attribution.ControllerInstanceId, routeController, StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                UsbOptimizationReadinessStatus.NotReady,
                routeController,
                "Kernel attribution evidence belongs to a different host controller.");
        }

        var expectedInputIdentity = route.RawInputDevice.PnPInstanceId;
        if (string.IsNullOrWhiteSpace(expectedInputIdentity) ||
            !string.Equals(timing.DeviceIdentity, expectedInputIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return Result(
                UsbOptimizationReadinessStatus.NotReady,
                routeController,
                "Input timing evidence belongs to a different or unknown Raw Input PnP device.");
        }

        if (timing.Status != InputTimingAnalysisStatus.Available)
        {
            return Result(
                UsbOptimizationReadinessStatus.Inconclusive,
                routeController,
                "Host-observable input timing evidence is insufficient for comparison.");
        }

        if (!attribution.ControllerOwnershipUnambiguous)
        {
            return Result(
                UsbOptimizationReadinessStatus.Inconclusive,
                routeController,
                "The xHCI service/module attribution is driver-wide and is shared by multiple or unknown controllers; controller-specific ownership is not proven.");
        }

        if (!attribution.CaptureIntegrityValid)
        {
            return Result(
                UsbOptimizationReadinessStatus.Inconclusive,
                routeController,
                "Kernel DPC/ISR capture integrity is not clean.");
        }

        if (!attribution.HasTargetEvidence ||
            attribution.DpcDurationsMicroseconds.Count == 0 ||
            attribution.IsrDurationsMicroseconds.Count == 0)
        {
            return Result(
                UsbOptimizationReadinessStatus.Inconclusive,
                routeController,
                "Exact xHCI DPC and ISR module evidence is required before an experiment is considered ready.");
        }

        if (timing.IntervalsMilliseconds.Count < InputTimingAnalyzer.MinimumIntervals)
        {
            return Result(
                UsbOptimizationReadinessStatus.Inconclusive,
                routeController,
                "Input timing interval evidence is below the minimum sample requirement.");
        }

        var metrics = new Dictionary<string, MetricSeries>(StringComparer.Ordinal)
        {
            [UsbOptimizationMetricNames.XhciDpcDuration] = new(
                UsbOptimizationMetricNames.XhciDpcDuration,
                MetricDirection.LowerIsBetter,
                attribution.DpcDurationsMicroseconds),
            [UsbOptimizationMetricNames.XhciIsrDuration] = new(
                UsbOptimizationMetricNames.XhciIsrDuration,
                MetricDirection.LowerIsBetter,
                attribution.IsrDurationsMicroseconds),
            [UsbOptimizationMetricNames.InputReportInterval] = new(
                UsbOptimizationMetricNames.InputReportInterval,
                MetricDirection.LowerIsBetter,
                timing.IntervalsMilliseconds),
        };

        return new UsbOptimizationReadinessResult(
            UsbOptimizationReadinessStatus.Ready,
            routeController,
            metrics,
            RequiredGuardrails,
            []);
    }

    private static UsbOptimizationReadinessResult Result(
        UsbOptimizationReadinessStatus status,
        string? controllerInstanceId,
        string reason) =>
        new(
            status,
            controllerInstanceId,
            new Dictionary<string, MetricSeries>(StringComparer.Ordinal),
            RequiredGuardrails,
            [reason]);
}

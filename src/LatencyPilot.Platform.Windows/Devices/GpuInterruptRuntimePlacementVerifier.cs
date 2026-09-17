using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record ProcessorObservedInterruptCount(
    int ProcessorNumber,
    int IsrEventCount);

public sealed record GpuInterruptIsrAttribution(
    string DeviceInstanceId,
    string DriverServiceName,
    string ModuleName,
    string Mode,
    IReadOnlyList<KernelLatencyEvent> Events,
    int UnresolvedIsrEventCount);

public sealed record GpuInterruptRuntimePlacementEvidence(
    string DeviceInstanceId,
    string DriverServiceName,
    byte TargetProcessorNumber,
    int MatchingResolvedIsrEventCount,
    int TargetProcessorIsrEventCount,
    int OffTargetIsrEventCount,
    int UnresolvedIsrEventCount,
    IReadOnlyList<ProcessorObservedInterruptCount> ObservedProcessors)
{
    public string AttributionModuleName { get; init; } = DriverServiceName;

    public string AttributionMode { get; init; } = "display-driver-kmd";

    public bool HasRuntimeEvidence => MatchingResolvedIsrEventCount > 0;

    public bool? ObservedOnlyOnTarget =>
        HasRuntimeEvidence
            ? OffTargetIsrEventCount == 0
            : null;

    public bool ConfirmsRequestedPlacement =>
        HasRuntimeEvidence &&
        TargetProcessorIsrEventCount == MatchingResolvedIsrEventCount &&
        OffTargetIsrEventCount == 0;
}

public static class GpuInterruptRuntimePlacementVerifier
{
    private static readonly Guid DisplayDeviceClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private const string WddmGraphicsKernelModule = "dxgkrnl";

    public static bool ConfirmsGateAPlacement(
        bool storedCandidateBefore,
        bool storedCandidateAfter,
        bool captureIsValid,
        GpuInterruptRuntimePlacementEvidence runtimePlacement)
    {
        ArgumentNullException.ThrowIfNull(runtimePlacement);

        return storedCandidateBefore &&
               storedCandidateAfter &&
               captureIsValid &&
               runtimePlacement.ConfirmsRequestedPlacement;
    }

    public static GpuInterruptRuntimePlacementEvidence Analyze(
        KernelLatencyCaptureResult capture,
        string deviceInstanceId,
        GpuInterruptAffinityCandidate candidate) =>
        Analyze(CaptureIsrAttribution(capture, deviceInstanceId), candidate);

    public static GpuInterruptIsrAttribution CaptureIsrAttribution(
        KernelLatencyCaptureResult capture,
        string deviceInstanceId) =>
        ResolveIsrAttribution(capture, deviceInstanceId,
            DeviceInventoryReader.CapturePresentDevices().Devices);

    public static GpuInterruptIsrAttribution ResolveIsrAttribution(
        KernelLatencyCaptureResult capture,
        string deviceInstanceId,
        IReadOnlyList<PnPDeviceSnapshot> devices)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        ArgumentNullException.ThrowIfNull(devices);

        var displayAdapters = devices
            .Where(device => device.ClassGuid == DisplayDeviceClass)
            .ToArray();
        var target = displayAdapters.FirstOrDefault(device =>
            string.Equals(device.InstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            throw new InvalidOperationException(
                "The requested GPU runtime-verification target is not a present display adapter.");
        }

        if (displayAdapters.Length != 1)
        {
            throw new NotSupportedException(
                "WDDM graphics-kernel ISR fallback requires exactly one present display adapter so the shared dxgkrnl ISR stream cannot be attributed to the wrong GPU.");
        }

        if (string.IsNullOrWhiteSpace(target.ServiceName))
        {
            throw new InvalidOperationException(
                "The target display adapter does not expose a driver service name, so runtime ISR attribution cannot be matched authoritatively.");
        }

        var serviceName = NormalizeModuleStem(target.ServiceName);
        var driverMatching = capture.Events
            .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
            .Where(item => ModuleMatchesService(item.ModulePath, serviceName))
            .ToArray();
        var useWddmFallback = driverMatching.Length == 0;
        var attributionModuleName = useWddmFallback
            ? WddmGraphicsKernelModule
            : serviceName;
        var attributionMode = useWddmFallback
            ? "wddm-graphics-kernel-dispatch"
            : "display-driver-kmd";
        var matching = useWddmFallback
            ? capture.Events
                .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
                .Where(item => ModuleMatchesService(item.ModulePath, WddmGraphicsKernelModule))
                .ToArray()
            : driverMatching;

        return new GpuInterruptIsrAttribution(
            deviceInstanceId, serviceName, attributionModuleName, attributionMode,
            Array.AsReadOnly(matching),
            capture.Events.Count(static item =>
                item.Kind == KernelLatencyEventKind.Isr && item.ModulePath is null));
    }

    public static GpuInterruptRuntimePlacementEvidence Analyze(
        GpuInterruptIsrAttribution attribution,
        GpuInterruptAffinityCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(attribution);
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.ProcessorGroup != 0 || candidate.ProcessorNumber >= 64)
        {
            throw new NotSupportedException(
                "GPU runtime placement verification v1 supports only a group-0 x64 affinity candidate.");
        }

        var matching = attribution.Events;

        var observedProcessors = matching
            .GroupBy(static item => item.ProcessorNumber)
            .Select(static group => new ProcessorObservedInterruptCount(group.Key, group.Count()))
            .OrderByDescending(static item => item.IsrEventCount)
            .ThenBy(static item => item.ProcessorNumber)
            .ToArray();

        var targetCount = matching.Count(item => item.ProcessorNumber == candidate.ProcessorNumber);
        return new GpuInterruptRuntimePlacementEvidence(
            attribution.DeviceInstanceId,
            attribution.DriverServiceName,
            candidate.ProcessorNumber,
            matching.Count,
            targetCount,
            matching.Count - targetCount,
            attribution.UnresolvedIsrEventCount,
            observedProcessors)
        {
            AttributionModuleName = attribution.ModuleName,
            AttributionMode = attribution.Mode,
        };
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

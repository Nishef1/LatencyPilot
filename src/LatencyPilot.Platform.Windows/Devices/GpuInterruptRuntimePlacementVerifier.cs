using LatencyPilot.Core.Observation;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record ProcessorObservedInterruptCount(
    int ProcessorNumber,
    int IsrEventCount);

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

    public static GpuInterruptRuntimePlacementEvidence Analyze(
        KernelLatencyCaptureResult capture,
        string deviceInstanceId,
        GpuInterruptAffinityCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceInstanceId);
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.ProcessorGroup != 0 || candidate.ProcessorNumber >= 64)
        {
            throw new NotSupportedException(
                "GPU runtime placement verification v1 supports only a group-0 x64 affinity candidate.");
        }

        var target = DeviceInventoryReader.CapturePresentDevices().Devices.FirstOrDefault(device =>
            device.ClassGuid == DisplayDeviceClass &&
            string.Equals(device.InstanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            throw new InvalidOperationException(
                "The requested GPU runtime-verification target is not a present display adapter.");
        }

        if (string.IsNullOrWhiteSpace(target.ServiceName))
        {
            throw new InvalidOperationException(
                "The target display adapter does not expose a driver service name, so runtime ISR attribution cannot be matched authoritatively.");
        }

        var serviceName = NormalizeModuleStem(target.ServiceName);
        var matching = capture.Events
            .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
            .Where(item => ModuleMatchesService(item.ModulePath, serviceName))
            .ToArray();

        var observedProcessors = matching
            .GroupBy(static item => item.ProcessorNumber)
            .Select(static group => new ProcessorObservedInterruptCount(group.Key, group.Count()))
            .OrderByDescending(static item => item.IsrEventCount)
            .ThenBy(static item => item.ProcessorNumber)
            .ToArray();

        var targetCount = matching.Count(item => item.ProcessorNumber == candidate.ProcessorNumber);
        var unresolvedIsrCount = capture.Events.Count(static item =>
            item.Kind == KernelLatencyEventKind.Isr && item.ModulePath is null);

        return new GpuInterruptRuntimePlacementEvidence(
            deviceInstanceId,
            serviceName,
            candidate.ProcessorNumber,
            matching.Length,
            targetCount,
            matching.Length - targetCount,
            unresolvedIsrCount,
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

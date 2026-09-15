using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record NetworkInterruptAttributionEvidence(
    string AdapterInstanceId,
    string DriverServiceName,
    int MatchingDpcEventCount,
    int MatchingIsrEventCount,
    int GenericNdisEventCount,
    int UnresolvedInterruptEventCount,
    IReadOnlyList<double> DpcDurationsMicroseconds,
    IReadOnlyList<double> IsrDurationsMicroseconds,
    bool CaptureIntegrityValid)
{
    public bool HasTargetEvidence =>
        CaptureIntegrityValid &&
        MatchingDpcEventCount > 0 &&
        MatchingIsrEventCount > 0;
}

public static class NetworkInterruptAttribution
{
    public static NetworkInterruptAttributionEvidence Analyze(
        KernelLatencyCaptureResult capture,
        PnPDeviceSnapshot adapter)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(adapter);

        if (string.IsNullOrWhiteSpace(adapter.InstanceId))
        {
            throw new ArgumentException("Network adapter PnP identity is required.", nameof(adapter));
        }

        if (string.IsNullOrWhiteSpace(adapter.ServiceName))
        {
            throw new InvalidOperationException(
                "The target network adapter does not expose a driver service name, so exact miniport attribution is unavailable.");
        }

        var serviceName = NormalizeModuleStem(adapter.ServiceName);
        var matching = capture.Events
            .Where(item => ModuleMatchesService(item.ModulePath, serviceName))
            .ToArray();
        var matchingDpc = matching
            .Where(static item => item.Kind == KernelLatencyEventKind.Dpc)
            .ToArray();
        var matchingIsr = matching
            .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
            .ToArray();
        var genericNdis = capture.Events.Count(static item =>
            string.Equals(
                NormalizeModuleStem(item.ModulePath ?? string.Empty),
                "ndis",
                StringComparison.OrdinalIgnoreCase));
        var unresolved = capture.Events.Count(static item =>
            item.ModulePath is null &&
            item.Kind is KernelLatencyEventKind.Dpc or KernelLatencyEventKind.Isr);

        return new NetworkInterruptAttributionEvidence(
            adapter.InstanceId,
            serviceName,
            matchingDpc.Length,
            matchingIsr.Length,
            genericNdis,
            unresolved,
            Array.AsReadOnly(matchingDpc.Select(static item => item.DurationMicroseconds).ToArray()),
            Array.AsReadOnly(matchingIsr.Select(static item => item.DurationMicroseconds).ToArray()),
            capture.IsValid);
    }

    private static bool ModuleMatchesService(string? modulePath, string serviceName) =>
        !string.IsNullOrWhiteSpace(modulePath) &&
        string.Equals(
            NormalizeModuleStem(modulePath),
            serviceName,
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeModuleStem(string value)
    {
        var fileName = Path.GetFileName(value.Trim());
        return Path.GetFileNameWithoutExtension(fileName);
    }
}

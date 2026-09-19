using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record UsbDriverKeyCandidate(string DeviceInstanceId, string? DriverKeyName, string? Error);
public sealed record UsbCompositePortRouteResolution(UsbPortRouteEvidence Evidence, string? MatchedDeviceInstanceId);

public static class UsbPortRouteCorrelator
{
    public static UsbCompositePortRouteResolution ResolveFromCandidates(
        IReadOnlyList<UsbDriverKeyCandidate> candidates, string? expectedHostControllerInstanceId,
        IReadOnlyList<UsbHubPortSnapshot> ports)
    {
        ArgumentNullException.ThrowIfNull(candidates); ArgumentNullException.ThrowIfNull(ports);
        var available = candidates.Where(static c => !string.IsNullOrWhiteSpace(c.DriverKeyName))
            .Select(c => (Candidate: c, Evidence: Resolve(c.DriverKeyName, expectedHostControllerInstanceId, ports)))
            .Where(static item => item.Evidence.IsAvailable).ToArray();
        var distinct = available.GroupBy(item =>
            $"{item.Evidence.Port!.HubDevicePath}|{item.Evidence.Port.ConnectionIndex}|{item.Evidence.Port.HostControllerInstanceId}",
            StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length == 1)
        {
            var match = distinct[0].First();
            return new UsbCompositePortRouteResolution(match.Evidence, match.Candidate.DeviceInstanceId);
        }
        if (distinct.Length > 1)
            return new UsbCompositePortRouteResolution(new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.Ambiguous, null,
                "More than one HID/USB ancestor resolves to a different connected USB port; composite transport ownership is ambiguous."), null);
        var detail = candidates.FirstOrDefault(static c => !string.IsNullOrWhiteSpace(c.Error))?.Error;
        return new UsbCompositePortRouteResolution(new UsbPortRouteEvidence(
            candidates.Any(static c => !string.IsNullOrWhiteSpace(c.DriverKeyName)) ? UsbPortRouteResolutionStatus.NotFound : UsbPortRouteResolutionStatus.DriverKeyUnavailable,
            null, detail ?? "No HID/USB ancestor exposes a unique driver-key match to a connected USB port."), null);
    }

    public static UsbPortRouteEvidence Resolve(string? driverKeyName, string? expectedHostControllerInstanceId, IReadOnlyList<UsbHubPortSnapshot> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        if (string.IsNullOrWhiteSpace(driverKeyName))
            return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.DriverKeyUnavailable, null,
                "The PnP device does not expose the exact driver-key identity needed for USB hub-port correlation.");
        var matches = ports.Where(port => port.ConnectionStatus == UsbPortConnectionStatus.Connected &&
            !string.IsNullOrWhiteSpace(port.DriverKeyName) && string.Equals(port.DriverKeyName, driverKeyName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.NotFound, null,
            "No connected USB hub port exposes the same exact driver-key identity.");
        if (matches.Length != 1) return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.Ambiguous, null,
            $"{matches.Length} connected USB ports expose the same driver-key identity; the route is ambiguous.");
        var match = matches[0];
        if (!string.IsNullOrWhiteSpace(expectedHostControllerInstanceId))
        {
            if (string.IsNullOrWhiteSpace(match.HostControllerInstanceId))
                return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.HostControllerUnavailable, null,
                    "The matching USB port does not have an authoritative host-controller identity.");
            if (!string.Equals(match.HostControllerInstanceId, expectedHostControllerInstanceId, StringComparison.OrdinalIgnoreCase))
                return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.HostControllerMismatch, null,
                    "The exact driver-key match belongs to a different USB host controller than the known PnP ancestry.");
        }
        return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.Available, match, null);
    }
}

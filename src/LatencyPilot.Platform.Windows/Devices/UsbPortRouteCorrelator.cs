using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

public static class UsbPortRouteCorrelator
{
    public static UsbPortRouteEvidence Resolve(
        string? driverKeyName,
        string? expectedHostControllerInstanceId,
        IReadOnlyList<UsbHubPortSnapshot> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);

        if (string.IsNullOrWhiteSpace(driverKeyName))
        {
            return new UsbPortRouteEvidence(
                UsbPortRouteResolutionStatus.DriverKeyUnavailable,
                null,
                "The PnP device does not expose the exact driver-key identity needed for USB hub-port correlation.");
        }

        var matches = ports
            .Where(port =>
                port.ConnectionStatus == UsbPortConnectionStatus.Connected &&
                !string.IsNullOrWhiteSpace(port.DriverKeyName) &&
                string.Equals(port.DriverKeyName, driverKeyName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            return new UsbPortRouteEvidence(
                UsbPortRouteResolutionStatus.NotFound,
                null,
                "No connected USB hub port exposes the same exact driver-key identity.");
        }

        if (matches.Length != 1)
        {
            return new UsbPortRouteEvidence(
                UsbPortRouteResolutionStatus.Ambiguous,
                null,
                $"{matches.Length} connected USB ports expose the same driver-key identity; the route is ambiguous.");
        }

        var match = matches[0];
        if (!string.IsNullOrWhiteSpace(expectedHostControllerInstanceId))
        {
            if (string.IsNullOrWhiteSpace(match.HostControllerInstanceId))
            {
                return new UsbPortRouteEvidence(
                    UsbPortRouteResolutionStatus.HostControllerUnavailable,
                    null,
                    "The matching USB port does not have an authoritative host-controller identity.");
            }

            if (!string.Equals(
                    match.HostControllerInstanceId,
                    expectedHostControllerInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new UsbPortRouteEvidence(
                    UsbPortRouteResolutionStatus.HostControllerMismatch,
                    null,
                    "The exact driver-key match belongs to a different USB host controller than the known PnP ancestry.");
            }
        }

        return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.Available, match, null);
    }
}

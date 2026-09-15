using System.Net.NetworkInformation;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.System;
using ManagedNativeWifi;

namespace LatencyPilot.Platform.Windows.Devices;

public enum WifiConnectionContinuityStatus
{
    NotApplicable = 0,
    Available = 1,
    PermissionDenied = 2,
    NotConnected = 3,
    Unavailable = 4,
}

public sealed record NetworkInterfaceContinuitySnapshot(
    string InterfaceId,
    string InterfaceDescription,
    NetworkInterfaceType InterfaceType,
    OperationalStatus OperationalStatus,
    int? Ipv4Index,
    int? Ipv6Index,
    IReadOnlyList<string> UnicastAddresses,
    IReadOnlyList<string> GatewayAddresses,
    WifiConnectionContinuityStatus WifiStatus,
    string? WifiSsid,
    string? WifiBssid);

public sealed record NetworkEnvironmentContinuitySnapshot(
    string TargetPnpInstanceId,
    NetworkRssAdapterSnapshot Rss,
    NetworkInterfaceContinuitySnapshot TargetInterface,
    IReadOnlyList<string> UpInterfaceIds,
    SystemAwakeTimeSnapshot AwakeTime);

public sealed record NetworkEnvironmentContinuityCaptureResult(
    bool IsAvailable,
    NetworkEnvironmentContinuitySnapshot? Snapshot,
    string? Reason);

public sealed record NetworkEnvironmentContinuityResult(
    bool IsStable,
    IReadOnlyList<string> Reasons);

public static class NetworkEnvironmentContinuity
{
    public static NetworkEnvironmentContinuityCaptureResult CaptureForTarget(string targetPnpInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPnpInstanceId);
        var rssSnapshot = NetworkRssReader.Capture();
        if (!rssSnapshot.IsAvailable)
        {
            return Unavailable($"RSS provider evidence is unavailable: {rssSnapshot.Status}: {rssSnapshot.Error}");
        }

        var matches = rssSnapshot.Adapters.Where(adapter =>
                adapter.PnpCorrelation.IsAvailable &&
                string.Equals(
                    adapter.PnpCorrelation.PnpInstanceId,
                    targetPnpInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            return Unavailable(
                "The selected PnP network adapter does not map uniquely to one current RSS provider row.");
        }

        return Capture(matches[0]);
    }

    public static NetworkEnvironmentContinuityCaptureResult Capture(NetworkRssAdapterSnapshot rss)
    {
        ArgumentNullException.ThrowIfNull(rss);
        if (!rss.PnpCorrelation.IsAvailable || string.IsNullOrWhiteSpace(rss.PnpCorrelation.PnpInstanceId))
        {
            return Unavailable("The selected RSS row has no authoritative PnP identity.");
        }

        if (string.IsNullOrWhiteSpace(rss.InterfaceDescription))
        {
            return Unavailable("The selected RSS row has no interface description for managed-interface correlation.");
        }

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            var targetMatches = interfaces.Where(item =>
                    string.Equals(item.Description, rss.InterfaceDescription, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (targetMatches.Length != 1)
            {
                return Unavailable(
                    "The RSS target could not be mapped uniquely to one managed network interface.");
            }

            var target = targetMatches[0];
            var targetSnapshot = CaptureInterface(target);
            if (targetSnapshot.OperationalStatus != OperationalStatus.Up)
            {
                return Unavailable("The selected network interface is not operationally Up.");
            }

            if (targetSnapshot.InterfaceType == NetworkInterfaceType.Wireless80211 &&
                targetSnapshot.WifiStatus != WifiConnectionContinuityStatus.Available)
            {
                return Unavailable(
                    $"Authoritative Wi-Fi continuity evidence is unavailable: {targetSnapshot.WifiStatus}.");
            }

            var upInterfaceIds = interfaces
                .Where(static item => item.OperationalStatus == OperationalStatus.Up)
                .Select(static item => item.Id)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new NetworkEnvironmentContinuityCaptureResult(
                true,
                new NetworkEnvironmentContinuitySnapshot(
                    rss.PnpCorrelation.PnpInstanceId!,
                    rss,
                    targetSnapshot,
                    upInterfaceIds,
                    SystemAwakeTimeReader.Capture()),
                null);
        }
        catch (Exception exception) when (exception is
            NetworkInformationException or
            PlatformNotSupportedException or
            InvalidOperationException or
            UnauthorizedAccessException or
            global::System.ComponentModel.Win32Exception)
        {
            return Unavailable(
                $"Network continuity evidence could not be captured: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public static NetworkEnvironmentContinuityResult Evaluate(
        NetworkEnvironmentContinuitySnapshot before,
        NetworkEnvironmentContinuitySnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var reasons = new List<string>();
        var awake = SystemAwakeTimeReader.Evaluate(before.AwakeTime, after.AwakeTime);
        if (!awake.IsValid || awake.SleepOrSuspendDetected)
        {
            reasons.Add(awake.Reason ?? "The system sleep/awake interval is not usable for comparison.");
        }

        if (!string.Equals(before.TargetPnpInstanceId, after.TargetPnpInstanceId, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("The selected PnP network adapter identity changed during the benchmark.");
        }

        if (!RssMatches(before.Rss, after.Rss))
        {
            reasons.Add("RSS, MSI/MSI-X, queue, processor, NUMA, or provider identity changed during the benchmark.");
        }

        if (!InterfaceMatches(before.TargetInterface, after.TargetInterface, reasons))
        {
            reasons.Add("The selected managed network interface or IP/gateway route surface changed during the benchmark.");
        }

        if (!SequenceEqual(before.UpInterfaceIds, after.UpInterfaceIds))
        {
            reasons.Add("The set of operationally Up interfaces changed during the benchmark (for example VPN or adapter churn).");
        }

        return new NetworkEnvironmentContinuityResult(reasons.Count == 0, reasons.AsReadOnly());
    }

    private static NetworkInterfaceContinuitySnapshot CaptureInterface(NetworkInterface networkInterface)
    {
        var properties = networkInterface.GetIPProperties();
        int? ipv4Index = null;
        int? ipv6Index = null;
        try
        {
            ipv4Index = properties.GetIPv4Properties()?.Index;
        }
        catch (NetworkInformationException)
        {
        }

        try
        {
            ipv6Index = properties.GetIPv6Properties()?.Index;
        }
        catch (NetworkInformationException)
        {
        }

        var unicast = properties.UnicastAddresses
            .Select(static address => address.Address.ToString())
            .OrderBy(static address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var gateways = properties.GatewayAddresses
            .Select(static gateway => gateway.Address.ToString())
            .OrderBy(static address => address, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var wifiStatus = WifiConnectionContinuityStatus.NotApplicable;
        string? wifiSsid = null;
        string? wifiBssid = null;
        if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
        {
            (wifiStatus, wifiSsid, wifiBssid) = CaptureWifi(networkInterface.Id);
        }

        return new NetworkInterfaceContinuitySnapshot(
            networkInterface.Id,
            networkInterface.Description,
            networkInterface.NetworkInterfaceType,
            networkInterface.OperationalStatus,
            ipv4Index,
            ipv6Index,
            unicast,
            gateways,
            wifiStatus,
            wifiSsid,
            wifiBssid);
    }

    private static (WifiConnectionContinuityStatus Status, string? Ssid, string? Bssid) CaptureWifi(string interfaceId)
    {
        if (!Guid.TryParse(interfaceId, out var interfaceGuid))
        {
            return (WifiConnectionContinuityStatus.Unavailable, null, null);
        }

        try
        {
            var (result, connection) = NativeWifi.GetCurrentConnection(interfaceGuid);
            if (result == ActionResult.NotConnected)
            {
                return (WifiConnectionContinuityStatus.NotConnected, null, null);
            }

            if (result != ActionResult.Success || connection is null)
            {
                return (WifiConnectionContinuityStatus.Unavailable, null, null);
            }

            var bssidBytes = connection.Bssid?.ToBytes();
            var bssid = bssidBytes is { Length: > 0 }
                ? Convert.ToHexString(bssidBytes)
                : null;
            if (string.IsNullOrWhiteSpace(bssid))
            {
                return (WifiConnectionContinuityStatus.Unavailable, null, null);
            }

            return (
                WifiConnectionContinuityStatus.Available,
                connection.Ssid?.ToString(),
                bssid);
        }
        catch (UnauthorizedAccessException)
        {
            return (WifiConnectionContinuityStatus.PermissionDenied, null, null);
        }
    }

    private static bool RssMatches(NetworkRssAdapterSnapshot left, NetworkRssAdapterSnapshot right) =>
        StringEquals(left.Name, right.Name) &&
        StringEquals(left.InterfaceDescription, right.InterfaceDescription) &&
        left.Enabled == right.Enabled &&
        left.MsiSupported == right.MsiSupported &&
        left.MsiXSupported == right.MsiXSupported &&
        left.MsiXEnabled == right.MsiXEnabled &&
        left.NumberOfInterruptMessages == right.NumberOfInterruptMessages &&
        left.NumberOfReceiveQueues == right.NumberOfReceiveQueues &&
        left.Profile == right.Profile &&
        left.BaseProcessorGroup == right.BaseProcessorGroup &&
        left.BaseProcessorNumber == right.BaseProcessorNumber &&
        left.MaxProcessorGroup == right.MaxProcessorGroup &&
        left.MaxProcessorNumber == right.MaxProcessorNumber &&
        left.MaxProcessors == right.MaxProcessors &&
        left.NumaNode == right.NumaNode &&
        left.PnpCorrelation.Status == right.PnpCorrelation.Status &&
        StringEquals(left.PnpCorrelation.PnpInstanceId, right.PnpCorrelation.PnpInstanceId) &&
        SequenceEqual(left.IndirectionTable, right.IndirectionTable) &&
        SequenceEqual(left.RssProcessorArray, right.RssProcessorArray);

    private static bool InterfaceMatches(
        NetworkInterfaceContinuitySnapshot left,
        NetworkInterfaceContinuitySnapshot right,
        List<string> reasons)
    {
        var baseMatches =
            StringEquals(left.InterfaceId, right.InterfaceId) &&
            StringEquals(left.InterfaceDescription, right.InterfaceDescription) &&
            left.InterfaceType == right.InterfaceType &&
            left.OperationalStatus == OperationalStatus.Up &&
            right.OperationalStatus == OperationalStatus.Up &&
            left.Ipv4Index == right.Ipv4Index &&
            left.Ipv6Index == right.Ipv6Index &&
            SequenceEqual(left.UnicastAddresses, right.UnicastAddresses) &&
            SequenceEqual(left.GatewayAddresses, right.GatewayAddresses);

        if (left.InterfaceType == NetworkInterfaceType.Wireless80211 ||
            right.InterfaceType == NetworkInterfaceType.Wireless80211)
        {
            if (left.WifiStatus != WifiConnectionContinuityStatus.Available ||
                right.WifiStatus != WifiConnectionContinuityStatus.Available)
            {
                reasons.Add("Wi-Fi BSSID continuity is unavailable; Windows location permission or connection state may have changed.");
                return false;
            }

            if (!StringEquals(left.WifiSsid, right.WifiSsid) ||
                !StringEquals(left.WifiBssid, right.WifiBssid))
            {
                reasons.Add("The Wi-Fi association changed SSID or BSSID during the benchmark (roam/reconnect detected).");
                return false;
            }
        }

        return baseMatches;
    }

    private static bool StringEquals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);

    private static NetworkEnvironmentContinuityCaptureResult Unavailable(string reason) =>
        new(false, null, reason);
}

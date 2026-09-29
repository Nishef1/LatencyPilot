using System.Collections.ObjectModel;
using System.Management;
using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

public static class NetworkRssReader
{
    private const string NamespacePath = @"\\.\root\StandardCimv2";
    private const int MaximumRssRows = 256;
    private const int MaximumAdapterIdentityRows = 512;

    private static readonly string[] RssPropertyNames =
    [
        "Name",
        "InterfaceDescription",
        "Enabled",
        "MsiSupported",
        "MsiXSupported",
        "MsiXEnabled",
        "NumberOfInterruptMessages",
        "NumberOfReceiveQueues",
        "Profile",
        "BaseProcessorGroup",
        "BaseProcessorNumber",
        "MaxProcessorGroup",
        "MaxProcessorNumber",
        "MaxProcessors",
        "NumaNode",
        "IndirectionTable",
        "RssProcessorArray",
    ];

    public static NetworkRssSnapshot Capture()
    {
        var capturedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            var scope = new ManagementScope(NamespacePath);
            scope.Connect();

            var identities = ReadAdapterIdentities(scope);
            var adapters = new List<NetworkRssAdapterSnapshot>();
            var rssProviderWarning = TryReadRssAdapters(scope, identities, adapters);
            AddPhysicalAdaptersWithoutRssRows(adapters, identities);

            return new NetworkRssSnapshot(
                NetworkRssReadStatus.Available,
                adapters.AsReadOnly(),
                capturedAtUtc,
                rssProviderWarning);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(NetworkRssReadStatus.AccessDenied, capturedAtUtc, exception);
        }
        catch (ManagementException exception) when (exception.ErrorCode == ManagementStatus.AccessDenied)
        {
            return Failure(NetworkRssReadStatus.AccessDenied, capturedAtUtc, exception);
        }
        catch (ManagementException exception) when (
            exception.ErrorCode is ManagementStatus.InvalidNamespace or ManagementStatus.InvalidClass)
        {
            return Failure(NetworkRssReadStatus.ProviderUnavailable, capturedAtUtc, exception);
        }
        catch (PlatformNotSupportedException exception)
        {
            return Failure(NetworkRssReadStatus.ProviderUnavailable, capturedAtUtc, exception);
        }
        catch (Exception exception) when (
            exception is ManagementException or InvalidDataException or InvalidOperationException)
        {
            return Failure(NetworkRssReadStatus.ReadFailed, capturedAtUtc, exception);
        }
    }

    private static string? TryReadRssAdapters(
        ManagementScope scope,
        IReadOnlyList<NetworkAdapterPnpIdentity> identities,
        List<NetworkRssAdapterSnapshot> adapters)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                scope,
                new ObjectQuery(
                    "SELECT Name, InterfaceDescription, Enabled, MsiSupported, MsiXSupported, MsiXEnabled, " +
                    "NumberOfInterruptMessages, NumberOfReceiveQueues, Profile, BaseProcessorGroup, " +
                    "BaseProcessorNumber, MaxProcessorGroup, MaxProcessorNumber, MaxProcessors, NumaNode, " +
                    "IndirectionTable, RssProcessorArray FROM MSFT_NetAdapterRssSettingData"));
            using var rows = searcher.Get();

            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    if (adapters.Count >= MaximumRssRows)
                    {
                        throw new InvalidDataException(
                            $"MSFT_NetAdapterRssSettingData returned more than {MaximumRssRows} rows.");
                    }

                    var mapped = NetworkRssPropertyMapper.Map(ReadRssProperties(row));
                    var correlation = NetworkRssPnpCorrelator.Resolve(
                        mapped.InterfaceDescription,
                        identities);
                    var identity = correlation.IsAvailable
                        ? identities.SingleOrDefault(item =>
                            string.Equals(
                                item.PnpInstanceId,
                                correlation.PnpInstanceId,
                                StringComparison.OrdinalIgnoreCase))
                        : null;
                    adapters.Add(mapped with
                    {
                        PnpCorrelation = correlation,
                        HardwareInterface = identity?.HardwareInterface,
                        ConnectorPresent = identity?.ConnectorPresent,
                    });
                }
            }

            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            return $"RSS settings provider access was denied: {exception.Message}";
        }
        catch (ManagementException exception) when (
            exception.ErrorCode is
                ManagementStatus.AccessDenied or
                ManagementStatus.InvalidClass or
                ManagementStatus.InvalidNamespace or
                ManagementStatus.NotFound)
        {
            return $"RSS settings provider is unavailable: {exception.ErrorCode}: {exception.Message}";
        }
    }

    private static ReadOnlyCollection<NetworkAdapterPnpIdentity> ReadAdapterIdentities(ManagementScope scope)
    {
        var identities = new List<NetworkAdapterPnpIdentity>();
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery(
                "SELECT InterfaceDescription, PnPDeviceID, HardwareInterface, ConnectorPresent FROM MSFT_NetAdapter WHERE InterfaceDescription IS NOT NULL"));
        using var rows = searcher.Get();

        foreach (ManagementObject row in rows)
        {
            using (row)
            {
                if (identities.Count >= MaximumAdapterIdentityRows)
                {
                    throw new InvalidDataException(
                        $"MSFT_NetAdapter returned more than {MaximumAdapterIdentityRows} rows.");
                }

                var interfaceDescription = row.Properties["InterfaceDescription"]?.Value as string;
                var pnpDeviceId = row.Properties["PnPDeviceID"]?.Value as string;
                if (string.IsNullOrWhiteSpace(interfaceDescription) || string.IsNullOrWhiteSpace(pnpDeviceId))
                {
                    continue;
                }

                identities.Add(new NetworkAdapterPnpIdentity(
                    interfaceDescription,
                    pnpDeviceId,
                    row.Properties["HardwareInterface"]?.Value is bool hardwareInterface
                        ? hardwareInterface
                        : null,
                    row.Properties["ConnectorPresent"]?.Value is bool connectorPresent
                        ? connectorPresent
                        : null));
            }
        }

        return identities.AsReadOnly();
    }

    private static void AddPhysicalAdaptersWithoutRssRows(
        List<NetworkRssAdapterSnapshot> adapters,
        IReadOnlyList<NetworkAdapterPnpIdentity> identities)
    {
        var representedPnpIds = adapters
            .Where(static adapter => adapter.PnpCorrelation.IsAvailable)
            .Select(static adapter => adapter.PnpCorrelation.PnpInstanceId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var identity in identities)
        {
            if (!IsPhysicalHardwareIdentity(identity) ||
                representedPnpIds.Contains(identity.PnpInstanceId))
            {
                continue;
            }

            adapters.Add(new NetworkRssAdapterSnapshot(
                identity.InterfaceDescription,
                identity.InterfaceDescription,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                [])
            {
                RssSettingsAvailable = false,
                HardwareInterface = identity.HardwareInterface,
                ConnectorPresent = identity.ConnectorPresent,
                PnpCorrelation = new NetworkRssPnpCorrelation(
                    NetworkRssPnpCorrelationStatus.Available,
                    identity.PnpInstanceId,
                    "Physical adapter discovered through MSFT_NetAdapter; no RSS settings row was exposed by Windows or the driver."),
            });
        }
    }

    private static bool IsPhysicalHardwareIdentity(NetworkAdapterPnpIdentity identity) =>
        identity.HardwareInterface == true ||
        identity.PnpInstanceId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) ||
        identity.PnpInstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, object?> ReadRssProperties(ManagementObject row)
    {
        var properties = new Dictionary<string, object?>(RssPropertyNames.Length, StringComparer.Ordinal);
        foreach (var name in RssPropertyNames)
        {
            properties[name] = row.Properties[name]?.Value;
        }

        return properties;
    }

    private static NetworkRssSnapshot Failure(
        NetworkRssReadStatus status,
        DateTimeOffset capturedAtUtc,
        Exception exception) =>
        new(status, [], capturedAtUtc, exception.Message);
}

public sealed record NetworkRssInspectionCoverage(
    bool IsUsable,
    int ProviderRowCount,
    int PnpCorrelatedRowCount,
    string? Reason)
{
    public static NetworkRssInspectionCoverage Evaluate(NetworkRssSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var providerRows = snapshot.Adapters.Count(static adapter => adapter.RssSettingsAvailable);
        var correlatedRows = snapshot.Adapters.Count(static adapter =>
            adapter.RssSettingsAvailable &&
            adapter.PnpCorrelation.IsAvailable);
        if (!snapshot.IsAvailable)
        {
            return new NetworkRssInspectionCoverage(
                false,
                providerRows,
                correlatedRows,
                $"RSS provider read is {snapshot.Status}: {snapshot.Error}");
        }

        if (providerRows == 0)
        {
            return new NetworkRssInspectionCoverage(
                false,
                0,
                0,
                snapshot.Adapters.Any(static adapter => adapter.PnpCorrelation.IsAvailable)
                    ? "Physical network adapter evidence is available, but Windows/driver exposed no RSS settings rows."
                    : "RSS provider returned no adapter setting rows.");
        }

        if (correlatedRows == 0)
        {
            return new NetworkRssInspectionCoverage(
                false,
                providerRows,
                0,
                "RSS provider rows could not be correlated to any present PnP network adapter.");
        }

        return new NetworkRssInspectionCoverage(true, providerRows, correlatedRows, null);
    }
}


public static class NetworkRssPhysicalAdapterSelector
{
    public static IReadOnlyList<NetworkRssAdapterSnapshot> Select(NetworkRssSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsAvailable)
        {
            return [];
        }

        return snapshot.Adapters
            .Where(static adapter =>
                adapter.PnpCorrelation.IsAvailable &&
                (adapter.HardwareInterface == true ||
                 adapter.PnpCorrelation.PnpInstanceId!.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase) ||
                 adapter.PnpCorrelation.PnpInstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(static adapter => adapter.Name ?? adapter.InterfaceDescription, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

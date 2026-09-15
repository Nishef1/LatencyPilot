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
                    adapters.Add(mapped with
                    {
                        PnpCorrelation = NetworkRssPnpCorrelator.Resolve(mapped.InterfaceDescription, identities),
                    });
                }
            }

            return new NetworkRssSnapshot(
                NetworkRssReadStatus.Available,
                adapters.AsReadOnly(),
                capturedAtUtc,
                null);
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

    private static ReadOnlyCollection<NetworkAdapterPnpIdentity> ReadAdapterIdentities(ManagementScope scope)
    {
        var identities = new List<NetworkAdapterPnpIdentity>();
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery(
                "SELECT InterfaceDescription, PnPDeviceID FROM MSFT_NetAdapter WHERE InterfaceDescription IS NOT NULL"));
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

                identities.Add(new NetworkAdapterPnpIdentity(interfaceDescription, pnpDeviceId));
            }
        }

        return identities.AsReadOnly();
    }

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

        var providerRows = snapshot.Adapters.Count;
        var correlatedRows = snapshot.Adapters.Count(static adapter => adapter.PnpCorrelation.IsAvailable);
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
                "RSS provider returned no adapter setting rows.");
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

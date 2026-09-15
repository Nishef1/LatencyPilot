using System.Globalization;
using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

internal sealed record NetworkAdapterPnpIdentity(
    string InterfaceDescription,
    string PnpInstanceId);

internal static class NetworkRssPropertyMapper
{
    private const int MaximumArrayItems = 4096;
    private const int MaximumStringLength = 4096;

    internal static NetworkRssAdapterSnapshot Map(IReadOnlyDictionary<string, object?> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        return new NetworkRssAdapterSnapshot(
            ReadString(properties, "Name"),
            ReadString(properties, "InterfaceDescription"),
            ReadBoolean(properties, "Enabled"),
            ReadBoolean(properties, "MsiSupported"),
            ReadBoolean(properties, "MsiXSupported"),
            ReadBoolean(properties, "MsiXEnabled"),
            ReadUInt32(properties, "NumberOfInterruptMessages"),
            ReadUInt32(properties, "NumberOfReceiveQueues"),
            ReadUInt32(properties, "Profile"),
            ReadUInt16(properties, "BaseProcessorGroup"),
            ReadByte(properties, "BaseProcessorNumber"),
            ReadUInt16(properties, "MaxProcessorGroup"),
            ReadByte(properties, "MaxProcessorNumber"),
            ReadUInt32(properties, "MaxProcessors"),
            ReadUInt16(properties, "NumaNode"),
            ReadStringArray(properties, "IndirectionTable"),
            ReadStringArray(properties, "RssProcessorArray"));
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        if (value is not string text)
        {
            throw UnexpectedType(name, value);
        }

        if (text.Length > MaximumStringLength)
        {
            throw new InvalidDataException($"RSS provider property '{name}' exceeds the maximum accepted string length.");
        }

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool? ReadBoolean(IReadOnlyDictionary<string, object?> properties, string name) =>
        ReadConverted(properties, name, static value => Convert.ToBoolean(value, CultureInfo.InvariantCulture));

    private static byte? ReadByte(IReadOnlyDictionary<string, object?> properties, string name) =>
        ReadConverted(properties, name, static value => Convert.ToByte(value, CultureInfo.InvariantCulture));

    private static ushort? ReadUInt16(IReadOnlyDictionary<string, object?> properties, string name) =>
        ReadConverted(properties, name, static value => Convert.ToUInt16(value, CultureInfo.InvariantCulture));

    private static uint? ReadUInt32(IReadOnlyDictionary<string, object?> properties, string name) =>
        ReadConverted(properties, name, static value => Convert.ToUInt32(value, CultureInfo.InvariantCulture));

    private static T? ReadConverted<T>(
        IReadOnlyDictionary<string, object?> properties,
        string name,
        Func<object, T> converter)
        where T : struct
    {
        if (!properties.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return converter(value);
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            throw new InvalidDataException(
                $"RSS provider property '{name}' has an unexpected value type or range.",
                exception);
        }
    }

    private static IReadOnlyList<string> ReadStringArray(
        IReadOnlyDictionary<string, object?> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var value) || value is null)
        {
            return Array.Empty<string>();
        }

        IEnumerable<string?> sequence = value switch
        {
            string[] strings => strings,
            IEnumerable<string> strings => strings,
            _ => throw UnexpectedType(name, value),
        };

        var materialized = sequence.ToArray();
        if (materialized.Length > MaximumArrayItems)
        {
            throw new InvalidDataException($"RSS provider property '{name}' exceeds the maximum accepted item count.");
        }

        if (materialized.Any(static item => item is null))
        {
            throw new InvalidDataException($"RSS provider property '{name}' contains a null array item.");
        }

        var strings = materialized.Select(static item => item!).ToArray();
        if (strings.Any(static item => item.Length > MaximumStringLength))
        {
            throw new InvalidDataException($"RSS provider property '{name}' contains an oversized string item.");
        }

        return Array.AsReadOnly(strings);
    }

    private static InvalidDataException UnexpectedType(string name, object value) =>
        new($"RSS provider property '{name}' has unexpected type '{value.GetType().FullName}'.");
}

internal static class NetworkRssPnpCorrelator
{
    internal static NetworkRssPnpCorrelation Resolve(
        string? interfaceDescription,
        IReadOnlyList<NetworkAdapterPnpIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);

        if (string.IsNullOrWhiteSpace(interfaceDescription))
        {
            return new NetworkRssPnpCorrelation(
                NetworkRssPnpCorrelationStatus.MissingIdentity,
                null,
                "The RSS provider row does not expose InterfaceDescription, so it cannot be correlated to a PnP device.");
        }

        var matches = identities
            .Where(identity => string.Equals(
                identity.InterfaceDescription,
                interfaceDescription,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            return new NetworkRssPnpCorrelation(
                NetworkRssPnpCorrelationStatus.NotFound,
                null,
                "No MSFT_NetAdapter row exposes the same InterfaceDescription as the RSS provider row.");
        }

        if (matches.Length != 1)
        {
            return new NetworkRssPnpCorrelation(
                NetworkRssPnpCorrelationStatus.Ambiguous,
                null,
                $"{matches.Length} MSFT_NetAdapter rows expose the same InterfaceDescription; PnP identity is ambiguous.");
        }

        if (string.IsNullOrWhiteSpace(matches[0].PnpInstanceId))
        {
            return new NetworkRssPnpCorrelation(
                NetworkRssPnpCorrelationStatus.NotFound,
                null,
                "The unique MSFT_NetAdapter match does not expose PnPDeviceID.");
        }

        return new NetworkRssPnpCorrelation(
            NetworkRssPnpCorrelationStatus.Available,
            matches[0].PnpInstanceId,
            null);
    }
}

using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LatencyPilot.Persistence;

public static class MutationJournalReadOnlyInspector
{
    private const int SupportedSchemaVersion = 1;

    public static IReadOnlyList<MutationJournalEntry> GetUnresolved(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The LatencyPilot mutation journal does not exist yet. Start the protected Service once before inspecting it.",
                fullPath);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var queryOnly = connection.CreateCommand())
        {
            queryOnly.CommandText = "PRAGMA query_only = ON;";
            queryOnly.ExecuteNonQuery();
        }

        ValidateSchema(connection);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT * FROM mutation_journal
            WHERE state NOT IN ('Reverted', 'Kept', 'AbortedBeforeApply')
            ORDER BY created_utc ASC;
            """;
        using var reader = command.ExecuteReader();
        var entries = new List<MutationJournalEntry>();
        while (reader.Read())
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    private static void ValidateSchema(SqliteConnection connection)
    {
        object? persistedVersion;
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT version FROM schema_info WHERE id = 1;";
            persistedVersion = command.ExecuteScalar();
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException(
                "The LatencyPilot mutation journal schema could not be read in inspection mode.",
                exception);
        }

        if (persistedVersion is null or DBNull)
        {
            throw new InvalidDataException("The LatencyPilot mutation journal schema version is missing.");
        }

        var version = Convert.ToInt32(persistedVersion, CultureInfo.InvariantCulture);
        if (version != SupportedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported LatencyPilot mutation journal schema {version}; expected {SupportedSchemaVersion}.");
        }
    }

    private static MutationJournalEntry ReadEntry(SqliteDataReader reader)
    {
        var stateText = reader.GetString(reader.GetOrdinal("state"));
        if (!Enum.TryParse<MutationJournalState>(stateText, ignoreCase: false, out var state))
        {
            throw new InvalidDataException($"Unknown mutation journal state '{stateText}'.");
        }

        return new MutationJournalEntry(
            Guid.ParseExact(reader.GetString(reader.GetOrdinal("experiment_id")), "D"),
            reader.GetString(reader.GetOrdinal("kind")),
            reader.GetString(reader.GetOrdinal("target_id")),
            reader.GetString(reader.GetOrdinal("original_state_json")),
            reader.GetString(reader.GetOrdinal("candidate_state_json")),
            state,
            DateTimeOffset.Parse(
                reader.GetString(reader.GetOrdinal("created_utc")),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(
                reader.GetString(reader.GetOrdinal("updated_utc")),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            reader.IsDBNull(reader.GetOrdinal("failure_reason"))
                ? null
                : reader.GetString(reader.GetOrdinal("failure_reason")),
            reader.GetInt64(reader.GetOrdinal("revision")));
    }
}

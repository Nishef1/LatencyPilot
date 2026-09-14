using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LatencyPilot.Persistence;

public static class MutationJournalReadOnlyInspector
{
    private const int SupportedSchemaVersion = 1;

    public static void EnsureSafeForUninstall(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        using var connection = OpenReadOnly(databasePath);
        using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(integrity.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
            {
                throw new InvalidDataException("The mutation journal failed its integrity check. Keep the recovery tools installed.");
            }
        }

        using var command = connection.CreateCommand();
        // Name every required column so an empty but malformed table cannot pass.
        command.CommandText =
            """
            SELECT experiment_id, kind, target_id, original_state_json, candidate_state_json,
                   state, created_utc, updated_utc, failure_reason, revision
            FROM mutation_journal;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var entry = ReadEntry(reader);
            var storedState = reader.GetString(reader.GetOrdinal("state"));
            // Kept is terminal for experimentation, but still owns a machine change.
            if (entry.State is not (MutationJournalState.Reverted or MutationJournalState.AbortedBeforeApply) ||
                !string.Equals(storedState, entry.State.ToString(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Experiment {entry.ExperimentId:D} is {storedState}. Restore and verify all managed changes before uninstalling.");
            }
        }
    }

    public static MutationJournalEntry? TryGet(string databasePath, Guid experimentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (experimentId == Guid.Empty)
        {
            return null;
        }

        using var connection = OpenReadOnly(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mutation_journal WHERE experiment_id = $experimentId;";
        command.Parameters.AddWithValue("$experimentId", experimentId.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    public static IReadOnlyList<MutationJournalEntry> GetUnresolved(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        using var connection = OpenReadOnly(databasePath);
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

    private static SqliteConnection OpenReadOnly(string databasePath)
    {
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

        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();

            using (var queryOnly = connection.CreateCommand())
            {
                queryOnly.CommandText = "PRAGMA query_only = ON;";
                queryOnly.ExecuteNonQuery();
            }

            ValidateSchema(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
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

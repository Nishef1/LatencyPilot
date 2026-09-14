using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LatencyPilot.Persistence;

public enum MutationJournalState
{
    Prepared = 1,
    Applying = 2,
    Applied = 3,
    Measuring = 4,
    AwaitingDecision = 5,
    Reverting = 6,
    Reverted = 7,
    Kept = 8,
    RecoveryRequired = 9,
    AbortedBeforeApply = 10,
}

public sealed record MutationJournalEntry(
    Guid ExperimentId,
    string Kind,
    string TargetId,
    string OriginalStateJson,
    string CandidateStateJson,
    MutationJournalState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? FailureReason,
    long Revision)
{
    public bool IsTerminal => MutationJournal.IsTerminal(State);
}

public static class MutationJournalStateMachine
{
    public static bool CanTransition(MutationJournalState from, MutationJournalState to) => (from, to) switch
    {
        (MutationJournalState.Prepared, MutationJournalState.Applying) => true,
        (MutationJournalState.Prepared, MutationJournalState.AbortedBeforeApply) => true,
        (MutationJournalState.Applying, MutationJournalState.Applied) => true,
        (MutationJournalState.Applying, MutationJournalState.RecoveryRequired) => true,
        (MutationJournalState.Applied, MutationJournalState.Measuring) => true,
        (MutationJournalState.Applied, MutationJournalState.Reverting) => true,
        (MutationJournalState.Applied, MutationJournalState.RecoveryRequired) => true,
        (MutationJournalState.Measuring, MutationJournalState.AwaitingDecision) => true,
        (MutationJournalState.Measuring, MutationJournalState.Reverting) => true,
        (MutationJournalState.Measuring, MutationJournalState.RecoveryRequired) => true,
        (MutationJournalState.AwaitingDecision, MutationJournalState.Kept) => true,
        (MutationJournalState.AwaitingDecision, MutationJournalState.Reverting) => true,
        (MutationJournalState.AwaitingDecision, MutationJournalState.RecoveryRequired) => true,
        (MutationJournalState.Reverting, MutationJournalState.Reverted) => true,
        (MutationJournalState.Reverting, MutationJournalState.RecoveryRequired) => true,
        // Recovery deliberately resumes only through rollback in v1. A future
        // version may add verified resume semantics after re-reading machine state.
        (MutationJournalState.RecoveryRequired, MutationJournalState.Reverting) => true,
        _ => false,
    };

    public static void EnsureTransition(MutationJournalState from, MutationJournalState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Illegal mutation journal transition: {from} -> {to}.");
        }
    }
}

public sealed class MutationJournal
{
    private const int SchemaVersion = 1;
    private const int MaximumJsonPayloadBytes = 128 * 1024;
    private readonly string connectionString;

    public MutationJournal(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Unable to resolve the mutation journal directory."));

        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
        }.ToString();
    }

    public static string GetDefaultDatabasePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LatencyPilot",
            "State",
            "latencypilot.db");

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS schema_info (
                id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                version INTEGER NOT NULL
            );
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS mutation_journal (
                experiment_id TEXT NOT NULL PRIMARY KEY,
                kind TEXT NOT NULL,
                target_id TEXT NOT NULL,
                original_state_json TEXT NOT NULL,
                candidate_state_json TEXT NOT NULL,
                state TEXT NOT NULL,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                failure_reason TEXT NULL,
                revision INTEGER NOT NULL DEFAULT 0
            );
            """);

        ExecuteNonQuery(
            connection,
            transaction,
            "CREATE INDEX IF NOT EXISTS ix_mutation_journal_state ON mutation_journal(state);");

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO schema_info(id, version) VALUES(1, $version) " +
                "ON CONFLICT(id) DO NOTHING;";
            command.Parameters.AddWithValue("$version", SchemaVersion);
            command.ExecuteNonQuery();
        }

        using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "SELECT version FROM schema_info WHERE id = 1;";
            var version = Convert.ToInt32(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (version != SchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unsupported LatencyPilot mutation journal schema {version}; expected {SchemaVersion}.");
            }
        }

        transaction.Commit();
    }

    public MutationJournalEntry CreatePrepared(
        Guid experimentId,
        string kind,
        string targetId,
        string originalStateJson,
        string candidateStateJson,
        DateTimeOffset? nowUtc = null)
    {
        if (experimentId == Guid.Empty)
        {
            throw new ArgumentException("ExperimentId must not be empty.", nameof(experimentId));
        }

        ValidateNonEmpty(kind, nameof(kind));
        ValidateNonEmpty(targetId, nameof(targetId));
        ValidateJsonPayload(originalStateJson, nameof(originalStateJson));
        ValidateJsonPayload(candidateStateJson, nameof(candidateStateJson));

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);

        if (HasUnresolvedEntry(connection, transaction))
        {
            throw new InvalidOperationException(
                "A mutation experiment is already unresolved. Recover or revert it before starting another mutation.");
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO mutation_journal(
                experiment_id, kind, target_id, original_state_json, candidate_state_json,
                state, created_utc, updated_utc, failure_reason, revision)
            VALUES(
                $experimentId, $kind, $targetId, $originalStateJson, $candidateStateJson,
                $state, $createdUtc, $updatedUtc, NULL, 0);
            """;
        command.Parameters.AddWithValue("$experimentId", experimentId.ToString("D"));
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$targetId", targetId);
        command.Parameters.AddWithValue("$originalStateJson", originalStateJson);
        command.Parameters.AddWithValue("$candidateStateJson", candidateStateJson);
        command.Parameters.AddWithValue("$state", MutationJournalState.Prepared.ToString());
        command.Parameters.AddWithValue("$createdUtc", FormatTimestamp(now));
        command.Parameters.AddWithValue("$updatedUtc", FormatTimestamp(now));
        command.ExecuteNonQuery();
        transaction.Commit();

        return GetRequired(experimentId);
    }

    public MutationJournalEntry Transition(
        Guid experimentId,
        long expectedRevision,
        MutationJournalState expectedState,
        MutationJournalState newState,
        string? failureReason = null,
        DateTimeOffset? nowUtc = null)
    {
        if (experimentId == Guid.Empty)
        {
            throw new ArgumentException("ExperimentId must not be empty.", nameof(experimentId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        MutationJournalStateMachine.EnsureTransition(expectedState, newState);

        if (failureReason is { Length: > 2048 })
        {
            throw new ArgumentOutOfRangeException(nameof(failureReason), "Failure reason must be 2048 characters or fewer.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE mutation_journal
            SET state = $newState,
                updated_utc = $updatedUtc,
                failure_reason = $failureReason,
                revision = revision + 1
            WHERE experiment_id = $experimentId
              AND revision = $expectedRevision
              AND state = $expectedState;
            """;
        command.Parameters.AddWithValue("$newState", newState.ToString());
        command.Parameters.AddWithValue("$updatedUtc", FormatTimestamp(nowUtc ?? DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$failureReason", (object?)failureReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$experimentId", experimentId.ToString("D"));
        command.Parameters.AddWithValue("$expectedRevision", expectedRevision);
        command.Parameters.AddWithValue("$expectedState", expectedState.ToString());

        if (command.ExecuteNonQuery() != 1)
        {
            transaction.Rollback();
            throw new InvalidOperationException(
                "Mutation journal transition lost its compare-and-swap race or the persisted state no longer matches the expected state.");
        }

        transaction.Commit();
        return GetRequired(experimentId);
    }

    public MutationJournalEntry? TryGet(Guid experimentId)
    {
        if (experimentId == Guid.Empty)
        {
            return null;
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM mutation_journal WHERE experiment_id = $experimentId;";
        command.Parameters.AddWithValue("$experimentId", experimentId.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    public IReadOnlyList<MutationJournalEntry> GetUnresolved()
    {
        using var connection = OpenConnection();
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

    public bool HasUnresolved() => GetUnresolved().Count != 0;

    public static bool IsTerminal(MutationJournalState state) =>
        state is MutationJournalState.Reverted or MutationJournalState.Kept or MutationJournalState.AbortedBeforeApply;

    private MutationJournalEntry GetRequired(Guid experimentId) =>
        TryGet(experimentId)
        ?? throw new InvalidOperationException($"Mutation journal entry {experimentId:D} was not found after persistence.");

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static bool HasUnresolvedEntry(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1 FROM mutation_journal
                WHERE state NOT IN ('Reverted', 'Kept', 'AbortedBeforeApply')
            );
            """;
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
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
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_utc")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_utc")), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.IsDBNull(reader.GetOrdinal("failure_reason")) ? null : reader.GetString(reader.GetOrdinal("failure_reason")),
            reader.GetInt64(reader.GetOrdinal("revision")));
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static void ValidateNonEmpty(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be empty.", parameterName);
        }
    }

    private static void ValidateJsonPayload(string value, string parameterName)
    {
        ValidateNonEmpty(value, parameterName);
        var byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > MaximumJsonPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Journal JSON payload must be {MaximumJsonPayloadBytes} UTF-8 bytes or smaller.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                value,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Journal JSON payload root must be an object.", parameterName);
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Journal JSON payload must contain valid JSON.", parameterName, exception);
        }
    }
}

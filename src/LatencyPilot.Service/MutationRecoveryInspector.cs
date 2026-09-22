using System.Security;
using LatencyPilot.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LatencyPilot.Service;

internal sealed class MutationRecoveryReadiness
{
    private readonly object sync = new();
    private bool journalInitialized;
    private string? initializationError;
    private MutationRecoveryInspection[] unresolved = [];

    public bool JournalInitialized
    {
        get
        {
            lock (sync)
            {
                return journalInitialized;
            }
        }
    }

    public string? InitializationError
    {
        get
        {
            lock (sync)
            {
                return initializationError;
            }
        }
    }

    public IReadOnlyList<MutationRecoveryInspection> Unresolved
    {
        get
        {
            lock (sync)
            {
                return unresolved;
            }
        }
    }

    public bool BlocksMutation
    {
        get
        {
            lock (sync)
            {
                return !journalInitialized || unresolved.Length != 0;
            }
        }
    }

    public void SetReady(IReadOnlyList<MutationRecoveryInspection> inspections)
    {
        ArgumentNullException.ThrowIfNull(inspections);
        lock (sync)
        {
            journalInitialized = true;
            initializationError = null;
            unresolved = inspections.ToArray();
        }
    }

    public void SetUnavailable(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        lock (sync)
        {
            journalInitialized = false;
            initializationError = error;
            unresolved = [];
        }
    }
}

internal sealed class MutationRecoveryInspector : IHostedService
{
    private static readonly Action<ILogger, int, Exception?> JournalReady =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(2001, nameof(JournalReady)),
            "Mutation journal initialized. Unresolved experiment count: {UnresolvedCount}. Mutation remains disabled by protocol.");

    private static readonly Action<ILogger, Guid, string, string, Exception?> UnresolvedExperimentFound =
        LoggerMessage.Define<Guid, string, string>(
            LogLevel.Warning,
            new EventId(2002, nameof(UnresolvedExperimentFound)),
            "Unresolved mutation experiment {ExperimentId} is in state {State} for target {TargetId}; mutation must remain blocked until recovery is explicit.");

    private static readonly Action<ILogger, Guid, string, Exception?> ActualGpuStateRead =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(2003, nameof(ActualGpuStateRead)),
            "Startup recovery re-read stored GPU interrupt-affinity state for experiment {ExperimentId}, target {TargetId}.");

    private static readonly Action<ILogger, Guid, string, Exception?> ActualStateReadFailed =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Error,
            new EventId(2004, nameof(ActualStateReadFailed)),
            "Startup recovery could not re-read/classify actual machine state for experiment {ExperimentId}: {Reason}.");

    private static readonly Action<ILogger, string, Exception?> JournalUnavailable =
        LoggerMessage.Define<string>(
            LogLevel.Critical,
            new EventId(2005, nameof(JournalUnavailable)),
            "Mutation journal startup inspection failed: {Reason}. Read-only observation can continue, but mutation must remain blocked.");

    private static readonly Action<ILogger, Guid, string, Exception?> StoredStateClassified =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(2006, nameof(StoredStateClassified)),
            "Startup recovery classified stored state for experiment {ExperimentId} as {StoredStateRelation}.");

    private static readonly Action<ILogger, Guid, string, string, Exception?> RecoveryPlanSelected =
        LoggerMessage.Define<Guid, string, string>(
            LogLevel.Warning,
            new EventId(2007, nameof(RecoveryPlanSelected)),
            "Startup recovery plan for experiment {ExperimentId}: {RecoveryAction}. {RecoveryReason}");

    private readonly ILogger<MutationRecoveryInspector> logger;
    private readonly MutationRecoveryReadiness readiness;

    public MutationRecoveryInspector(
        ILogger<MutationRecoveryInspector> logger,
        MutationRecoveryReadiness readiness)
    {
        this.logger = logger;
        this.readiness = readiness;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var journal = new MutationJournal(MutationJournal.GetDefaultDatabasePath());
            journal.Initialize();
            var unresolvedEntries = journal.GetUnresolved();
            var inspections = new List<MutationRecoveryInspection>(unresolvedEntries.Count);

            foreach (var entry in unresolvedEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UnresolvedExperimentFound(
                    logger,
                    entry.ExperimentId,
                    entry.State.ToString(),
                    entry.TargetId,
                    null);

                var inspection = MutationRecoveryAssessment.Inspect(entry);
                inspections.Add(inspection);

                if (inspection.ActualStateRead)
                {
                    ActualGpuStateRead(logger, entry.ExperimentId, entry.TargetId, null);
                    StoredStateClassified(
                        logger,
                        entry.ExperimentId,
                        inspection.StoredStateRelation.ToString(),
                        null);
                }
                else
                {
                    ActualStateReadFailed(
                        logger,
                        entry.ExperimentId,
                        inspection.Error ?? "actual state is unavailable",
                        null);
                }

                RecoveryPlanSelected(
                    logger,
                    entry.ExperimentId,
                    inspection.RecoveryPlan.Action.ToString(),
                    inspection.RecoveryPlan.Reason,
                    null);
            }

            readiness.SetReady(inspections);
            JournalReady(logger, inspections.Count, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverableStartupFailure(exception))
        {
            var reason = $"{exception.GetType().Name}: {exception.Message}";
            readiness.SetUnavailable(reason);
            JournalUnavailable(logger, reason, exception);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool IsRecoverableStartupFailure(Exception exception) =>
        exception is IOException or
        UnauthorizedAccessException or
        SecurityException or
        InvalidDataException or
        InvalidOperationException or
        FormatException or
        OverflowException or
        ArgumentException or
        System.Text.Json.JsonException or
        Microsoft.Data.Sqlite.SqliteException;
}

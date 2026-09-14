using System.ComponentModel;
using System.Security;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LatencyPilot.Service;

internal sealed record MutationRecoveryInspection(
    MutationJournalEntry Entry,
    bool ActualStateRead,
    GpuInterruptAffinitySnapshot? GpuInterruptAffinity,
    string? Error);

internal sealed class MutationRecoveryReadiness
{
    private readonly object sync = new();
    private bool journalInitialized;
    private string? initializationError;
    private IReadOnlyList<MutationRecoveryInspection> unresolved = [];

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
                return !journalInitialized || unresolved.Count != 0;
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
    internal const string GpuInterruptAffinityKind = "gpu-interrupt-affinity";

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
            "Startup recovery could not re-read actual machine state for experiment {ExperimentId}: {Reason}.");

    private static readonly Action<ILogger, string, Exception?> JournalUnavailable =
        LoggerMessage.Define<string>(
            LogLevel.Critical,
            new EventId(2005, nameof(JournalUnavailable)),
            "Mutation journal startup inspection failed: {Reason}. Read-only observation can continue, but mutation must remain blocked.");

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
                inspections.Add(InspectActualState(entry));
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

    private MutationRecoveryInspection InspectActualState(MutationJournalEntry entry)
    {
        if (!string.Equals(entry.Kind, GpuInterruptAffinityKind, StringComparison.Ordinal))
        {
            const string reason = "mutation kind is not supported by startup recovery inspection";
            ActualStateReadFailed(logger, entry.ExperimentId, reason, null);
            return new MutationRecoveryInspection(entry, false, null, reason);
        }

        try
        {
            var actual = GpuInterruptAffinityPolicyStore.Capture(entry.TargetId);
            ActualGpuStateRead(logger, entry.ExperimentId, entry.TargetId, null);
            return new MutationRecoveryInspection(entry, true, actual, null);
        }
        catch (Exception exception) when (IsRecoverableActualStateFailure(exception))
        {
            var reason = $"{exception.GetType().Name}: {exception.Message}";
            ActualStateReadFailed(logger, entry.ExperimentId, reason, exception);
            return new MutationRecoveryInspection(entry, false, null, reason);
        }
    }

    private static bool IsRecoverableStartupFailure(Exception exception) =>
        exception is IOException or
        UnauthorizedAccessException or
        SecurityException or
        InvalidDataException or
        InvalidOperationException or
        Microsoft.Data.Sqlite.SqliteException;

    private static bool IsRecoverableActualStateFailure(Exception exception) =>
        exception is Win32Exception or
        IOException or
        UnauthorizedAccessException or
        SecurityException or
        InvalidDataException or
        InvalidOperationException or
        NotSupportedException;
}

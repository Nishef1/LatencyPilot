using System.Diagnostics;
using System.Text.Json;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;

namespace LatencyPilot.GateAValidation;

internal sealed class GpuGateAProgressFile
{
    private const int MaximumWriteAttempts = 8;
    private static readonly TimeSpan WriteRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly Guid sessionId;
    private readonly string path;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly GpuAutoAffinityProgressPlan progressPlan;
    private readonly Dictionary<int, int> screeningCandidates = [];
    private readonly Dictionary<string, int> refinementCandidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> candidatePassesStarted = new(StringComparer.Ordinal);
    private readonly object writeLock = new();
    private int totalUnits;
    private int retryUnits;
    private int completedUnits;
    private int screeningControlPassesStarted;
    private int confirmationRunsStarted;
    private int? refinementPhysicalCore;
    private string lastCompletedCandidateVerdict = "None yet";
    private GpuOptimizationProgressSnapshot? latestSnapshot;

    internal GpuGateAProgressFile(
        Guid sessionId,
        string path,
        GpuAutoAffinityProgressPlan progressPlan)
    {
        ArgumentNullException.ThrowIfNull(progressPlan);
        if (sessionId == Guid.Empty || progressPlan.PhysicalCandidateCount <= 0)
        {
            throw new ArgumentException("Progress identity and physical candidate plan are required.");
        }

        this.sessionId = sessionId;
        this.path = Path.GetFullPath(path);
        this.progressPlan = progressPlan;
        totalUnits = progressPlan.InitialTotalUnits;
    }

    internal Task ReportInitializingAsync(string message) =>
        WriteAsync(Create(
            "initializing",
            null,
            null,
            null,
            null,
            message,
            null,
            null,
            "Not measured",
            isRestoring: false,
            isTerminal: false));

    internal Task ReportTrialStartingAsync(GpuAutoAffinityTrialRequest request)
    {
        lock (writeLock)
        {
            AlignRefinementPlan(request);
            if (request.RetryAttempt > 0)
            {
                retryUnits = checked(retryUnits + 1);
                totalUnits = checked(totalUnits + 1);
            }

            var (candidateIndex, candidateCount) = GetCandidateOrdinal(request.Phase, request.Candidate);
            return WriteAsync(Create(
                request.Phase,
                request.Candidate,
                candidateIndex,
                candidateCount,
                request.RunNumber,
                DescribeTrialStart(request),
                null,
                null,
                request.Candidate is null ? "Control run" : "Awaiting placement proof",
                isRestoring: false,
                isTerminal: false));
        }
    }

    internal Task ReportTrialCompletedAsync(
        GpuAutoAffinityTrialRequest request,
        GpuAutoAffinityTrialObservation observation)
    {
        lock (writeLock)
        {
            completedUnits = Math.Min(totalUnits, completedUnits + 1);
            var (candidateIndex, candidateCount) = GetCandidateOrdinal(request.Phase, request.Candidate);
            var interpretation = GpuBenchmarkEvidenceInterpreter.Interpret(observation.Evidence);
            var placementState = request.Candidate is null
                ? "Original state"
                : observation.Placement is { ConfirmsRequestedPlacement: true }
                    ? "Confirmed on requested CPU"
                    : "Placement not confirmed";
            var trialState = request.Candidate is null
                ? "Control complete"
                : interpretation.IsValid && observation.Placement is { ConfirmsRequestedPlacement: true }
                    ? "Trial valid · decision pending"
                    : "Trial not decision-grade";

            return WriteAsync(Create(
                request.Phase,
                request.Candidate,
                candidateIndex,
                candidateCount,
                request.RunNumber,
                trialState,
                interpretation.IsValid ? interpretation.FrameP99Milliseconds : null,
                interpretation.IsValid ? interpretation.OnePercentLowFps : null,
                placementState,
                isRestoring: false,
                isTerminal: false));
        }
    }

    internal Task ReportCandidateEvaluatedAsync(GpuAutoAffinityCandidateReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (writeLock)
        {
            lastCompletedCandidateVerdict =
                $"CPU {report.Processor.Number} — {FormatVerdict(report.Verdict)}";
            return latestSnapshot is null
                ? Task.CompletedTask
                : WriteAsync(latestSnapshot with
                {
                    LastCompletedCandidateVerdict = lastCompletedCandidateVerdict,
                    Message = lastCompletedCandidateVerdict,
                });
        }
    }

    internal Task ReportStopRequestedAsync()
    {
        lock (writeLock)
        {
            return latestSnapshot is null
                ? ReportInitializingAsync("Stop requested. Waiting for the safe rollback boundary.")
                : WriteAsync(latestSnapshot with
                {
                    Phase = "stopping-safely",
                    Message = "Stop requested. Finishing the current safe boundary and restoring the original state.",
                    IsrPlacementState = "Rollback/recovery pending",
                    IsRestoring = true,
                    IsTerminal = false,
                });
        }
    }

    internal Task ReportRestoringAsync(GpuAffinityCandidate? candidate, string message) =>
        WriteAsync(Create(
            "restoring-original",
            candidate,
            null,
            null,
            null,
            message,
            null,
            null,
            "Rollback/recovery in progress",
            isRestoring: true,
            isTerminal: false));

    internal Task ReportTerminalAsync(
        string recommendation,
        GpuAffinityCandidate? candidate,
        bool finalStateVerified,
        string message)
    {
        lock (writeLock)
        {
            var terminalPhase = recommendation switch
            {
                "Failed safely" => "failed-safely",
                "Stopped safely" => "stopped-safely",
                _ => "complete",
            };
            if (string.Equals(terminalPhase, "complete", StringComparison.Ordinal))
            {
                completedUnits = totalUnits;
            }

            lastCompletedCandidateVerdict = recommendation;
            return WriteAsync(Create(
                terminalPhase,
                candidate,
                null,
                null,
                null,
                message,
                null,
                null,
                finalStateVerified ? "Final state verified" : "Final state not verified",
                isRestoring: false,
                isTerminal: true));
        }
    }

    private void AlignRefinementPlan(GpuAutoAffinityTrialRequest request)
    {
        if (!string.Equals(request.Phase, "smt-refinement", StringComparison.Ordinal) ||
            request.Candidate is null)
        {
            return;
        }

        var physicalCore = request.Candidate.PhysicalCoreIndex;
        if (refinementPhysicalCore is null)
        {
            refinementPhysicalCore = physicalCore;
            totalUnits = checked(progressPlan.GetTotalUnitsForFinalist(physicalCore) + retryUnits);
            return;
        }

        if (refinementPhysicalCore.Value != physicalCore)
        {
            throw new InvalidOperationException(
                "GPU SMT refinement progress switched physical cores within one session.");
        }
    }

    private string DescribeTrialStart(GpuAutoAffinityTrialRequest request)
    {
        if (request.RetryAttempt > 0)
        {
            return "Retrying the contaminated trial once.";
        }

        if (string.Equals(request.Phase, "screening-control", StringComparison.Ordinal))
        {
            screeningControlPassesStarted++;
            return screeningControlPassesStarted == 1
                ? "Original warm-up control; not used as the decision reference."
                : $"Original decision control {screeningControlPassesStarted - 1} / 2.";
        }

        if (string.Equals(request.Phase, "confirmation", StringComparison.Ordinal))
        {
            confirmationRunsStarted++;
            return $"{request.Role} confirmation run {confirmationRunsStarted} / 8.";
        }

        if (request.Candidate is not null &&
            (string.Equals(request.Phase, "screening", StringComparison.Ordinal) ||
             string.Equals(request.Phase, "smt-refinement", StringComparison.Ordinal)))
        {
            var key = $"{request.Phase}|{request.Candidate.Processor}";
            candidatePassesStarted.TryGetValue(key, out var pass);
            pass++;
            candidatePassesStarted[key] = pass;
            return $"Pass {pass} / 2.";
        }

        return "Measuring benchmark trial.";
    }

    private GpuOptimizationProgressSnapshot Create(
        string phase,
        GpuAffinityCandidate? candidate,
        int? candidateIndex,
        int? candidateCount,
        int? runNumber,
        string message,
        double? frameP99,
        double? onePercentLow,
        string placementState,
        bool isRestoring,
        bool isTerminal)
    {
        var elapsed = stopwatch.Elapsed.TotalMilliseconds;
        double? remaining = null;
        if (!isTerminal && completedUnits > 0 && totalUnits > completedUnits)
        {
            remaining = elapsed / completedUnits * (totalUnits - completedUnits);
        }

        return new GpuOptimizationProgressSnapshot(
            GpuOptimizationProgressSnapshot.SchemaId,
            sessionId,
            phase,
            completedUnits,
            totalUnits,
            candidate?.Processor,
            candidate?.PhysicalCoreIndex,
            candidateIndex,
            candidateCount,
            runNumber is null ? message : $"{message} Run {runNumber.Value}.",
            elapsed,
            remaining,
            frameP99,
            onePercentLow,
            placementState,
            lastCompletedCandidateVerdict,
            isRestoring,
            isTerminal);
    }

    private (int? Index, int? Count) GetCandidateOrdinal(string phase, GpuAffinityCandidate? candidate)
    {
        if (candidate is null)
        {
            return (null, null);
        }

        if (string.Equals(phase, "screening", StringComparison.Ordinal))
        {
            if (!screeningCandidates.TryGetValue(candidate.PhysicalCoreIndex, out var index))
            {
                index = screeningCandidates.Count + 1;
                screeningCandidates.Add(candidate.PhysicalCoreIndex, index);
            }

            return (index, progressPlan.PhysicalCandidateCount);
        }

        if (string.Equals(phase, "smt-refinement", StringComparison.Ordinal))
        {
            var key = candidate.Processor.ToString();
            if (!refinementCandidates.TryGetValue(key, out var index))
            {
                index = refinementCandidates.Count + 1;
                refinementCandidates.Add(key, index);
            }

            return (index, progressPlan.GetRefinementCandidateCount(candidate.PhysicalCoreIndex));
        }

        return string.Equals(phase, "confirmation", StringComparison.Ordinal)
            ? (1, 1)
            : (null, null);
    }

    private Task WriteAsync(GpuOptimizationProgressSnapshot snapshot)
    {
        lock (writeLock)
        {
            latestSnapshot = snapshot;
            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("GPU Gate A progress path has no parent directory.");
            Directory.CreateDirectory(directory);
            var temporary = path + ".tmp";
            try
            {
                for (var attempt = 1; attempt <= MaximumWriteAttempts; attempt++)
                {
                    try
                    {
                        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, JsonOptions));
                        File.Move(temporary, path, overwrite: true);
                        break;
                    }
                    catch (IOException) when (attempt < MaximumWriteAttempts)
                    {
                        Thread.Sleep(WriteRetryDelay);
                    }
                    catch (UnauthorizedAccessException) when (attempt < MaximumWriteAttempts)
                    {
                        // The WinUI progress reader or an antivirus scanner can briefly hold
                        // the destination while it is being replaced. Keep the last valid
                        // snapshot and retry the atomic publish instead of aborting the gate.
                        Thread.Sleep(WriteRetryDelay);
                    }
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        return Task.CompletedTask;
    }

    private static string FormatVerdict(string verdict) => verdict switch
    {
        "Improved" => "measurable improvement",
        "NoMeasurableDifference" => "no measurable improvement",
        "Regressed" => "regressed",
        _ => verdict,
    };
}

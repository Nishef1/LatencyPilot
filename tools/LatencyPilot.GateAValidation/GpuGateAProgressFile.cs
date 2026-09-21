using System.Diagnostics;
using System.Text.Json;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.System;

namespace LatencyPilot.GateAValidation;

internal sealed class GpuGateAProgressFile
{
    private const int MaximumWriteAttempts = 8;
    private const int RequiredRepeatabilityRuns = 3;
    private const int PairRetryAdditionalUnits = 6;
    private const int RecoveryOriginalAdditionalUnits = 2;
    private const int MaximumRepeatabilityObservations = GpuOriginalBaselinePolicy.MaximumScoredObservationCount;
    private static readonly TimeSpan WriteRetryDelay = TimeSpan.FromMilliseconds(25);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly Guid sessionId;
    private readonly string path;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly GpuAutoAffinityProgressPlan progressPlan;
    private readonly Dictionary<LogicalProcessorId, int> screeningCandidates = [];
    private readonly Dictionary<LogicalProcessorId, int> finalistCandidates = [];
    private readonly Dictionary<string, int> candidatePassesStarted = new(StringComparer.Ordinal);
    private readonly object writeLock = new();
    private int originalScoredPassesStarted;
    private int totalUnits;
    private int completedUnits;
    private string lastCompletedCandidateVerdict = "None yet";
    private GpuOptimizationProgressSnapshot? latestSnapshot;

    internal GpuGateAProgressFile(
        Guid sessionId,
        string path,
        GpuAutoAffinityProgressPlan progressPlan)
    {
        ArgumentNullException.ThrowIfNull(progressPlan);
        if (sessionId == Guid.Empty || (progressPlan.CandidateCount <= 0 && progressPlan.SearchScope != GpuAutoAffinitySearchScope.OriginalDiagnostics))
        {
            throw new ArgumentException("Progress identity and logical-CPU candidate plan are required.");
        }

        this.sessionId = sessionId;
        this.path = Path.GetFullPath(path);
        this.progressPlan = progressPlan;
        totalUnits = progressPlan.InitialTotalUnits;
    }

    internal Task ReportInitializingAsync(string message) =>
        WriteAsync(Create(
            "initializing", null, null, null, null, message,
            null, null, "Not measured", isRestoring: false, isTerminal: false));

    internal Task ReportTrialStartingAsync(GpuAutoAffinityTrialRequest request)
    {
        lock (writeLock)
        {
            if (request.RetryAttempt > 0)
            {
                // CaptureAcceptedAsync adds one extra capture for a transient
                // benchmark/collector retry. The surrounding pair budget is
                // unchanged, so only this one additional unit is added here.
                totalUnits = checked(totalUnits + 1);
            }
            else if (request.Phase.EndsWith("-retry-original-before-warmup", StringComparison.Ordinal))
            {
                // A statistical pair retry is a complete new local comparison:
                // fresh Original-before warm-up/score + candidate warm-up/score
                // + Original-after warm-up/score. Budget all six units at the
                // first retry boundary so ETA/progress never reports 100% while
                // the bounded retry is still running.
                totalUnits = checked(totalUnits + PairRetryAdditionalUnits);
            }
            else if (request.Phase.EndsWith("-recovery-original-control-warmup", StringComparison.Ordinal))
            {
                // An exhausted/inconclusive pair cannot donate its final Original
                // as the next chain anchor. The session reacquires a fresh
                // Original control before continuing with another candidate.
                totalUnits = checked(totalUnits + RecoveryOriginalAdditionalUnits);
            }
            else if (string.Equals(request.Phase, "screening-original", StringComparison.Ordinal))
            {
                originalScoredPassesStarted++;
                if (originalScoredPassesStarted > RequiredRepeatabilityRuns)
                {
                    totalUnits = checked(totalUnits + 1);
                }
            }

            totalUnits = Math.Max(totalUnits, completedUnits + 1);

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
                request.Candidate is null ? "Reference/warm-up" : "Awaiting placement evidence",
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
            var isWarmup = request.Phase.EndsWith("-warmup", StringComparison.Ordinal);
            var placementState = isWarmup
                ? "Not measured during warm-up"
                : request.Candidate is null
                    ? "Original/reference state"
                    : observation.Placement is { ConfirmsRequestedPlacement: true }
                        ? "Confirmed on requested CPU"
                        : observation.Evidence.EtwIntegrityComplete
                            ? "Requested placement not confirmed"
                            : "Placement unverified (ETW unavailable)";
            var trialState = isWarmup
                ? "Warm-up complete · not scored"
                : string.Equals(request.Phase, "final-verification", StringComparison.Ordinal)
                    ? placementState
                    : request.Candidate is null
                        ? "Reference capture complete"
                        : interpretation.IsValid
                            ? "Scored run complete"
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
            lastCompletedCandidateVerdict = $"CPU {report.Processor.Number} — {FormatVerdict(report.Verdict)}";
            return latestSnapshot is null
                ? Task.CompletedTask
                : WriteAsync(latestSnapshot with
                {
                    LastCompletedCandidateVerdict = lastCompletedCandidateVerdict,
                    Message = $"{lastCompletedCandidateVerdict}. {report.Reason}",
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
                    Message = "Stop requested. Future trials will not start; rollback/recovery remains owned until final-state verification.",
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
                totalUnits = Math.Max(1, completedUnits);
            }

            var terminalCandidate = string.Equals(
                recommendation,
                GpuOptimizationRecommendation.KeepCandidate.ToString(),
                StringComparison.Ordinal)
                ? candidate
                : null;
            var terminalUsesLatestMetrics =
                terminalCandidate is not null ||
                latestSnapshot?.Processor is null;
            return WriteAsync(Create(
                terminalPhase,
                terminalCandidate,
                null,
                null,
                null,
                message,
                terminalUsesLatestMetrics ? latestSnapshot?.FrameP99Milliseconds : null,
                terminalUsesLatestMetrics ? latestSnapshot?.OnePercentLowFps : null,
                finalStateVerified ? "Final state verified" : "Final state not verified",
                isRestoring: false,
                isTerminal: true));
        }
    }

    private string DescribeTrialStart(GpuAutoAffinityTrialRequest request)
    {
        if (request.RetryAttempt > 0)
        {
            return "Retrying this benchmark block once after genuine transient contamination or a transient collector failure.";
        }

        if (string.Equals(request.Phase, "screening-original", StringComparison.Ordinal))
        {
            return $"Original repeatability sample {originalScoredPassesStarted}; target {RequiredRepeatabilityRuns} stable samples, maximum {MaximumRepeatabilityObservations} scored observations.";
        }

        if (string.Equals(request.Phase, "screening-warmup", StringComparison.Ordinal))
        {
            return "Original-state startup warm-up/reference; not scored and never used as a decision control.";
        }

        if (request.Phase.EndsWith("-warmup", StringComparison.Ordinal))
        {
            return request.Candidate is null
                ? "Original-state benchmark warm-up/reference; not scored."
                : "Post-affinity warm-up on the requested CPU; not scored.";
        }

        if (string.Equals(request.Phase, "final-verification", StringComparison.Ordinal))
        {
            return "Verifying final stored state and target-only runtime GPU ISR placement before Keep.";
        }

        if (request.Phase == "diagnostic-original")
        {
            return "Original-only measurement; all five observations are retained, with no affinity change or device restart.";
        }

        if (request.Candidate is not null && request.Phase.StartsWith("screening-", StringComparison.Ordinal))
        {
            var key = $"{request.Phase}|{request.Candidate.Processor}";
            candidatePassesStarted.TryGetValue(key, out var pass);
            pass++;
            candidatePassesStarted[key] = pass;
            if (!string.Equals(request.Phase, "screening-finalists", StringComparison.Ordinal))
            {
                return "Scored candidate in a local Original → Candidate → Original pair; confirmation follows rollback.";
            }
            return $"Finalist scored observation {pass}; three valid independent pairs required, with at most one retry per pair.";
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
            $"{progressPlan.SearchScope}: " + (runNumber is null ? message : $"{message} Run {runNumber.Value}."),
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

        if (phase.StartsWith("screening-representative", StringComparison.Ordinal) ||
            phase.StartsWith("screening-sibling", StringComparison.Ordinal))
        {
            if (!screeningCandidates.TryGetValue(candidate.Processor, out var index))
            {
                index = screeningCandidates.Count + 1;
                screeningCandidates.Add(candidate.Processor, index);
            }
            return (index, progressPlan.ScreeningCandidateCount);
        }

        if (string.Equals(phase, "screening-finalists", StringComparison.Ordinal) ||
            string.Equals(phase, "screening-finalists-warmup", StringComparison.Ordinal))
        {
            if (!finalistCandidates.TryGetValue(candidate.Processor, out var index))
            {
                index = finalistCandidates.Count + 1;
                finalistCandidates.Add(candidate.Processor, index);
                if (finalistCandidates.Count > progressPlan.FinalistCandidateCount)
                {
                    totalUnits = checked(
                        totalUnits + GpuAutoAffinityProgressPlan.AdditionalFinalistUnitsPerCandidate);
                }
            }
            return (
                index,
                Math.Max(progressPlan.FinalistCandidateCount, finalistCandidates.Count));
        }

        return string.Equals(phase, "final-verification", StringComparison.Ordinal)
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
        "Ranked" => "ranked",
        "Inconclusive" => "not rankable",
        _ => verdict,
    };
}
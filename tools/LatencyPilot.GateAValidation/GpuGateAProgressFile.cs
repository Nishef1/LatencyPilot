using System.Diagnostics;
using System.Text.Json;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Benchmarking;

namespace LatencyPilot.GateAValidation;

internal sealed class GpuGateAProgressFile
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly Guid sessionId;
    private readonly string path;
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly int totalUnits;
    private readonly Dictionary<int, int> screeningCandidates = [];
    private readonly Dictionary<string, int> refinementCandidates = new(StringComparer.Ordinal);
    private int completedUnits;
    private string lastCompletedCandidateVerdict = "None yet";

    internal GpuGateAProgressFile(Guid sessionId, string path, int physicalCandidateCount)
    {
        if (sessionId == Guid.Empty || physicalCandidateCount <= 0)
        {
            throw new ArgumentException("Progress identity and physical candidate count are required.");
        }

        this.sessionId = sessionId;
        this.path = Path.GetFullPath(path);
        totalUnits = checked(2 + physicalCandidateCount * 2 + 4 + 8);
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
        var (candidateIndex, candidateCount) = GetCandidateOrdinal(request.Phase, request.Candidate);
        return WriteAsync(Create(
            request.Phase,
            request.Candidate,
            candidateIndex,
            candidateCount,
            null,
            request.RetryAttempt == 0
                ? $"Measuring run {request.RunNumber}."
                : $"Repeating contaminated run {request.RunNumber} once.",
            null,
            null,
            request.Candidate is null ? "Control run" : "Awaiting placement proof",
            isRestoring: false,
            isTerminal: false));
    }

    internal Task ReportTrialCompletedAsync(
        GpuAutoAffinityTrialRequest request,
        GpuAutoAffinityTrialObservation observation)
    {
        completedUnits = Math.Min(totalUnits, completedUnits + 1);
        var (candidateIndex, candidateCount) = GetCandidateOrdinal(request.Phase, request.Candidate);
        var interpretation = GpuBenchmarkEvidenceInterpreter.Interpret(observation.Evidence);
        var placementState = request.Candidate is null
            ? "Original state"
            : observation.Placement is { ConfirmsRequestedPlacement: true }
                ? "Confirmed on requested CPU"
                : "Placement not confirmed";
        lastCompletedCandidateVerdict = request.Candidate is null
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
            lastCompletedCandidateVerdict,
            interpretation.IsValid ? interpretation.FrameP99Milliseconds : null,
            interpretation.IsValid ? interpretation.OnePercentLowFps : null,
            placementState,
            isRestoring: false,
            isTerminal: false));
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
        completedUnits = totalUnits;
        lastCompletedCandidateVerdict = recommendation;
        return WriteAsync(Create(
            "complete",
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

            return (index, (totalUnits - 14) / 2);
        }

        if (string.Equals(phase, "smt-refinement", StringComparison.Ordinal))
        {
            var key = candidate.Processor.ToString();
            if (!refinementCandidates.TryGetValue(key, out var index))
            {
                index = refinementCandidates.Count + 1;
                refinementCandidates.Add(key, index);
            }

            return (index, 2);
        }

        return string.Equals(phase, "confirmation", StringComparison.Ordinal)
            ? (1, 1)
            : (null, null);
    }

    private async Task WriteAsync(GpuOptimizationProgressSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("GPU Gate A progress path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(snapshot, JsonOptions)).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }
}

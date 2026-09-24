using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed class GpuAutoAffinityProgressPlan
{
    private GpuAutoAffinityProgressPlan(int candidateCount, int screeningCount, GpuAutoAffinitySearchScope scope)
    {
        CandidateCount = candidateCount;
        ScreeningCandidateCount = screeningCount;
        SearchScope = scope;
    }

    public int CandidateCount { get; }

    public int ScreeningCandidateCount { get; }
    public GpuAutoAffinitySearchScope SearchScope { get; }
    public int FinalistCandidateCount => SearchScope == GpuAutoAffinitySearchScope.Full
        ? Math.Min(GpuAutoAffinitySession.MaximumFinalists, CandidateCount)
        : 0;
    public int AdaptiveShortlistCandidateCount => SearchScope == GpuAutoAffinitySearchScope.Full
        ? Math.Min(GpuAutoAffinitySession.MaximumAdaptiveShortlistCandidates, CandidateCount)
        : 0;
    public static int AdditionalFinalistUnitsPerCandidate => 12;
    public static int AdditionalShortlistUnitsPerCandidate => 4;

    // Maximum adaptive work estimate. Pair retries add their own units dynamically.
    public int InitialTotalUnits => SearchScope == GpuAutoAffinitySearchScope.OriginalDiagnostics
        ? 1 + GpuOriginalBaselinePolicy.MaximumScoredObservationCount
        : 1 + 3 + 2 + (ScreeningCandidateCount * 4) +
          (AdaptiveShortlistCandidateCount * AdditionalShortlistUnitsPerCandidate) +
          (FinalistCandidateCount == 0 ? 0 : 2 + (FinalistCandidateCount * AdditionalFinalistUnitsPerCandidate) + 2);

    public static GpuAutoAffinityProgressPlan Create(
        ProcessorTopologySnapshot topology,
        IEnumerable<ProcessorPressureEvidence> pressureEvidence,
        ProcessorCpuSetSnapshot? cpuSets,
        GpuAutoAffinitySearchScope scope = GpuAutoAffinitySearchScope.Full,
        IReadOnlyList<LogicalProcessorId>? requestedProcessors = null)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(pressureEvidence);

        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope));
        }
        var candidates = GpuAffinityCandidatePlanner.Create(topology, pressureEvidence.ToArray(), cpuSets);
        var requested = requestedProcessors?.ToArray() ?? [];
        if (scope == GpuAutoAffinitySearchScope.Custom)
        {
            var eligible = candidates.Select(static candidate => candidate.Processor).ToHashSet();
            if (requested.Length == 0 || requested.Distinct().Count() != requested.Length ||
                requested.Any(processor => processor.Group != 0 || !eligible.Contains(processor)))
            {
                throw new ArgumentException("Custom CPU scope must contain unique currently eligible group-0 processors.", nameof(requestedProcessors));
            }
            return new GpuAutoAffinityProgressPlan(requested.Length, requested.Length, scope);
        }
        if (requested.Length != 0)
        {
            throw new ArgumentException("Only custom scope accepts candidate processors.", nameof(requestedProcessors));
        }
        var cores = candidates.GroupBy(static candidate => candidate.PhysicalCoreIndex).ToArray();
        var siblingBudget = cores.Select(static core => core.Count() - 1).OrderDescending().Take(GpuAutoAffinitySession.MaximumPhysicalCoreHypotheses).Sum();
        return new GpuAutoAffinityProgressPlan(candidates.Count, cores.Length + siblingBudget, scope);
    }
}

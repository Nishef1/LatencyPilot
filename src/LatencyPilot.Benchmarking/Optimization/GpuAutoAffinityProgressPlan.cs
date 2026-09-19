using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed class GpuAutoAffinityProgressPlan
{
    private const int ControlWarmupCount = 1;
    private const int ScoredOriginalUnits = 3;
    private const int ScreeningCandidatesPerControlBlock = 4;
    private const int ScreeningBlockControlUnits = 2; // warm-up + scored Original control
    private const int PostScreeningControlUnits = 2; // warm-up + scored Original control
    private const int PostFinalistControlUnits = 2; // warm-up + scored Original control
    private const int ScreeningUnitsPerCandidate = 2; // transition warm-up + one scored run
    private const int FinalistUnitsPerCandidate = 4; // two independent transition warm-up + scored re-test rounds
    private const int MaximumFinalistCandidates = 5;
    private const int FinalVerificationUnits = 2; // transition warm-up + ETW placement verification

    private GpuAutoAffinityProgressPlan(IReadOnlyList<GpuAffinityCandidate> candidates)
    {
        CandidateCount = candidates.Count;
    }

    public int CandidateCount { get; }

    public int FinalistCandidateCount => Math.Min(MaximumFinalistCandidates, CandidateCount);

    public int IntermediateScreeningControlCount =>
        CandidateCount <= ScreeningCandidatesPerControlBlock
            ? 0
            : (CandidateCount - 1) / ScreeningCandidatesPerControlBlock;

    public static int AdditionalFinalistUnitsPerCandidate => FinalistUnitsPerCandidate;

    public int InitialTotalUnits => GetBaseTotalUnits();

    public static GpuAutoAffinityProgressPlan Create(
        ProcessorTopologySnapshot topology,
        IEnumerable<ProcessorPressureEvidence> pressureEvidence,
        ProcessorCpuSetSnapshot? cpuSets)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(pressureEvidence);

        var pressure = pressureEvidence.ToArray();
        var candidates = GpuAffinityCandidatePlanner.Create(topology, pressure, cpuSets);
        return new GpuAutoAffinityProgressPlan(candidates);
    }

    private int GetBaseTotalUnits() =>
        ControlWarmupCount +
        ScoredOriginalUnits +
        (IntermediateScreeningControlCount * ScreeningBlockControlUnits) +
        PostScreeningControlUnits +
        PostFinalistControlUnits +
        (CandidateCount * ScreeningUnitsPerCandidate) +
        (FinalistCandidateCount * FinalistUnitsPerCandidate) +
        FinalVerificationUnits;
}

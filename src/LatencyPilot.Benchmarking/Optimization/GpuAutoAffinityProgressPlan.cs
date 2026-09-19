using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed class GpuAutoAffinityProgressPlan
{
    private const int ControlWarmupCount = 1;
    private const int ScoredOriginalUnits = 3;
    private const int PostScreeningControlUnits = 1;
    private const int PostFinalistControlUnits = 1;
    private const int ScreeningUnitsPerCandidate = 2; // transition warm-up + one scored run
    private const int FinalistUnitsPerCandidate = 4; // two independent transition warm-up + scored re-test rounds
    private const int MaximumFinalistCandidates = 3;
    private const int FinalVerificationUnits = 2; // transition warm-up + ETW placement verification

    private GpuAutoAffinityProgressPlan(IReadOnlyList<GpuAffinityCandidate> physicalCandidates)
    {
        PhysicalCandidateCount = physicalCandidates.Count;
    }

    public int PhysicalCandidateCount { get; }

    public int FinalistCandidateCount => Math.Min(MaximumFinalistCandidates, PhysicalCandidateCount);

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
        var physicalCandidates = GpuAffinityCandidatePlanner.Create(topology, pressure, cpuSets);
        return new GpuAutoAffinityProgressPlan(physicalCandidates);
    }

    private int GetBaseTotalUnits() =>
        ControlWarmupCount +
        ScoredOriginalUnits +
        PostScreeningControlUnits +
        PostFinalistControlUnits +
        (PhysicalCandidateCount * ScreeningUnitsPerCandidate) +
        (FinalistCandidateCount * FinalistUnitsPerCandidate) +
        FinalVerificationUnits;

}

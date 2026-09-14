using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Protocol;

namespace LatencyPilot.Service;

internal static class GpuAffinityCandidateSource
{
    public static IReadOnlyList<GpuInterruptAffinityCandidate> Create(
        ProcessorTopologySnapshot topology,
        ProcessorCpuSetSnapshot? cpuSets,
        IReadOnlyList<KernelLatencyCaptureResponse> baselineCaptures,
        int maximumCandidates = GpuAffinityCandidatePlanner.DefaultMaximumCandidates)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(baselineCaptures);

        if (topology.ProcessorGroupCount != 1)
        {
            throw new NotSupportedException(
                "GPU affinity candidate evidence v1 requires exactly one processor group because protocol v6 exposes processor numbers without group IDs.");
        }

        if (baselineCaptures.Count == 0)
        {
            throw new ArgumentException("At least one baseline capture is required.", nameof(baselineCaptures));
        }

        var windows = new List<IReadOnlyList<ProcessorInterruptCountEvidence>>(baselineCaptures.Count);
        foreach (var capture in baselineCaptures)
        {
            ArgumentNullException.ThrowIfNull(capture);
            if (capture.Processors is null)
            {
                throw new InvalidDataException("Baseline capture is missing per-processor evidence.");
            }

            var evidence = new List<ProcessorInterruptCountEvidence>(capture.Processors.Count);
            foreach (var processor in capture.Processors)
            {
                if (processor.ProcessorNumber is < 0 or >= 64)
                {
                    throw new InvalidDataException(
                        $"Protocol processor number {processor.ProcessorNumber} is outside the Phase 3 v1 KAFFINITY boundary.");
                }

                evidence.Add(new ProcessorInterruptCountEvidence(
                    new LogicalProcessorId(0, checked((byte)processor.ProcessorNumber)),
                    processor.Dpc.Count,
                    processor.Isr.Count));
            }

            windows.Add(evidence);
        }

        var pressureEvidence = ProcessorPressureEvidenceBuilder.Create(topology, windows);
        var planned = GpuAffinityCandidatePlanner.Create(
            topology,
            pressureEvidence,
            cpuSets,
            maximumCandidates);

        return planned
            .Select(candidate => GpuInterruptAffinityCandidate.Create(topology, candidate.Processor))
            .ToArray();
    }
}

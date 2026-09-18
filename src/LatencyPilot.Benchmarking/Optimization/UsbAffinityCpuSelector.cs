using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed record UsbAffinityCpuCandidate(
    int PhysicalCoreIndex,
    LogicalProcessorId Processor,
    double DpcTotalDurationMicroseconds,
    double IsrTotalDurationMicroseconds,
    double InterruptTailP99Microseconds,
    int DpcCount,
    int IsrCount)
{
    public double TotalInterruptDurationMicroseconds =>
        DpcTotalDurationMicroseconds + IsrTotalDurationMicroseconds;

    public int InterruptCount => DpcCount + IsrCount;
}

public static class UsbAffinityCpuSelector
{
    public static IReadOnlyList<UsbAffinityCpuCandidate> Rank(
        ProcessorTopologySnapshot topology,
        KernelLatencyCaptureResult capture,
        LogicalProcessorId gpuWinner)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(capture);

        if (topology.ProcessorGroupCount != 1 || gpuWinner.Group != 0)
        {
            throw new NotSupportedException(
                "Automatic USB/xHCI affinity v1 currently requires one processor group.");
        }

        var gpuCore = topology.Cores.SingleOrDefault(core =>
            core.LogicalProcessors.Contains(gpuWinner))
            ?? throw new ArgumentException(
                "The GPU winner does not exist in the supplied processor topology.",
                nameof(gpuWinner));

        var ranked = new List<UsbAffinityCpuCandidate>();
        foreach (var core in topology.Cores)
        {
            // Do not place xHCI on the same physical core as the GPU winner,
            // including its SMT sibling.
            if (core.Index == gpuCore.Index)
            {
                continue;
            }

            foreach (var processor in core.LogicalProcessors)
            {
                if (processor.Group != 0)
                {
                    continue;
                }

                var events = capture.Events
                    .Where(item => item.ProcessorNumber == processor.Number)
                    .Where(static item => item.Kind is KernelLatencyEventKind.Dpc or KernelLatencyEventKind.Isr)
                    .Where(static item => double.IsFinite(item.DurationMicroseconds) && item.DurationMicroseconds >= 0d)
                    .ToArray();

                var dpc = events
                    .Where(static item => item.Kind == KernelLatencyEventKind.Dpc)
                    .Select(static item => item.DurationMicroseconds)
                    .ToArray();
                var isr = events
                    .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
                    .Select(static item => item.DurationMicroseconds)
                    .ToArray();
                var allDurations = events
                    .Select(static item => item.DurationMicroseconds)
                    .Order()
                    .ToArray();

                ranked.Add(new UsbAffinityCpuCandidate(
                    core.Index,
                    processor,
                    dpc.Sum(),
                    isr.Sum(),
                    Percentile99(allDurations),
                    dpc.Length,
                    isr.Length));
            }
        }

        return ranked
            .OrderBy(static item => item.TotalInterruptDurationMicroseconds)
            .ThenBy(static item => item.InterruptTailP99Microseconds)
            .ThenBy(static item => item.InterruptCount)
            .ThenBy(static item => item.Processor.Number)
            .ToArray();
    }

    public static UsbAffinityCpuCandidate Select(
        ProcessorTopologySnapshot topology,
        KernelLatencyCaptureResult capture,
        LogicalProcessorId gpuWinner)
    {
        var ranked = Rank(topology, capture, gpuWinner);
        return ranked.Count > 0
            ? ranked[0]
            : throw new InvalidOperationException(
                "No logical processor remains for USB/xHCI affinity after excluding the GPU winner physical core.");
    }

    private static double Percentile99(double[] sortedAscending)
    {
        if (sortedAscending.Length == 0)
        {
            return 0d;
        }

        var position = (sortedAscending.Length - 1) * 0.99d;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper
            ? sortedAscending[lower]
            : sortedAscending[lower] +
              ((sortedAscending[upper] - sortedAscending[lower]) * (position - lower));
    }
}

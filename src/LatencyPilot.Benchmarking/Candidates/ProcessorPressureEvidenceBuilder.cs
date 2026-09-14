using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Candidates;

public sealed record ProcessorInterruptCountEvidence
{
    public ProcessorInterruptCountEvidence(
        LogicalProcessorId processor,
        int dpcCount,
        int isrCount)
    {
        if (dpcCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpcCount));
        }

        if (isrCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(isrCount));
        }

        Processor = processor;
        DpcCount = dpcCount;
        IsrCount = isrCount;
    }

    public LogicalProcessorId Processor { get; }

    public int DpcCount { get; }

    public int IsrCount { get; }

    public long TotalInterruptEventCount => (long)DpcCount + IsrCount;
}

public static class ProcessorPressureEvidenceBuilder
{
    public static IReadOnlyList<ProcessorPressureEvidence> Create(
        ProcessorTopologySnapshot topology,
        IReadOnlyList<IReadOnlyList<ProcessorInterruptCountEvidence>> windows)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(windows);

        if (windows.Count == 0)
        {
            throw new ArgumentException("At least one measurement window is required.", nameof(windows));
        }

        var processors = topology.Cores
            .SelectMany(static core => core.LogicalProcessors)
            .Distinct()
            .OrderBy(static processor => processor.Group)
            .ThenBy(static processor => processor.Number)
            .ToArray();
        if (processors.Length == 0)
        {
            throw new ArgumentException("Processor topology contains no logical processors.", nameof(topology));
        }

        var knownProcessors = processors.ToHashSet();
        var accumulatedShares = processors.ToDictionary(
            static processor => processor,
            static _ => 0d);

        foreach (var window in windows)
        {
            ArgumentNullException.ThrowIfNull(window);

            var counts = new Dictionary<LogicalProcessorId, long>();
            long totalEvents = 0;
            foreach (var item in window)
            {
                ArgumentNullException.ThrowIfNull(item);
                if (!knownProcessors.Contains(item.Processor))
                {
                    throw new ArgumentException(
                        $"Measurement window contains processor {item.Processor}, which is absent from the captured topology.",
                        nameof(windows));
                }

                if (!counts.TryAdd(item.Processor, item.TotalInterruptEventCount))
                {
                    throw new ArgumentException(
                        $"Measurement window contains duplicate evidence for processor {item.Processor}.",
                        nameof(windows));
                }

                totalEvents = checked(totalEvents + item.TotalInterruptEventCount);
            }

            if (totalEvents <= 0)
            {
                throw new ArgumentException(
                    "Every measurement window must contain at least one observed DPC or ISR event.",
                    nameof(windows));
            }

            foreach (var processor in processors)
            {
                var count = counts.TryGetValue(processor, out var observedCount)
                    ? observedCount
                    : 0L;
                accumulatedShares[processor] += count / (double)totalEvents;
            }
        }

        // The score is deliberately only an observed interrupt-activity share.
        // It does not mix unrelated units or claim that event count alone is latency.
        // Averaging per-window shares prevents a busier window from dominating the
        // candidate ranking merely because it contained more total events.
        return processors
            .Select(processor => new ProcessorPressureEvidence(
                processor,
                accumulatedShares[processor] / windows.Count))
            .ToArray();
    }
}

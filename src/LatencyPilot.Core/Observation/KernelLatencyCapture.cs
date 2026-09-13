namespace LatencyPilot.Core.Observation;

public enum KernelLatencyEventKind
{
    Dpc,
    Isr,
}

public readonly record struct KernelLatencyEvent(
    KernelLatencyEventKind Kind,
    int ProcessorNumber,
    double TimeStampRelativeMilliseconds,
    double DurationMicroseconds,
    ulong RoutineAddress,
    int? InterruptVector,
    int? MessageNumber,
    string? ModulePath = null);

public sealed record KernelLatencyCaptureResult(
    DateTimeOffset StartedAtUtc,
    TimeSpan RequestedDuration,
    TimeSpan ActualDuration,
    IReadOnlyList<KernelLatencyEvent> Events,
    int EventsLost,
    int InvalidEventCount,
    int InvalidImageEventCount,
    bool EventLimitReached)
{
    public bool IsValid =>
        EventsLost == 0 &&
        InvalidEventCount == 0 &&
        InvalidImageEventCount == 0 &&
        !EventLimitReached;

    public int DpcCount => Events.Count(static item => item.Kind == KernelLatencyEventKind.Dpc);

    public int IsrCount => Events.Count(static item => item.Kind == KernelLatencyEventKind.Isr);

    public int ResolvedModuleEventCount => Events.Count(static item => item.ModulePath is not null);

    public int UnresolvedModuleEventCount => Events.Count - ResolvedModuleEventCount;
}

public sealed record KernelLatencyCaptureOptions(TimeSpan Duration, int MaximumEvents = 500_000)
{
    public void Validate()
    {
        if (Duration <= TimeSpan.Zero || Duration > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(Duration), "Capture duration must be greater than zero and at most five minutes.");
        }

        if (MaximumEvents is < 1_000 or > 5_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumEvents), "MaximumEvents must be between 1,000 and 5,000,000.");
        }
    }
}

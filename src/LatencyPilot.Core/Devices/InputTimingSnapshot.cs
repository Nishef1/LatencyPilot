namespace LatencyPilot.Core.Devices;

public enum InputTimingAnalysisStatus
{
    Available = 0,
    InsufficientSamples = 1,
}

public sealed record InputReportTimestampSeries(
    string DeviceIdentity,
    long TimestampFrequency,
    IReadOnlyList<long> TimestampTicks)
{
    public int ReportCount => TimestampTicks.Count;
}

public sealed record InputTimingSnapshot(
    InputTimingAnalysisStatus Status,
    string DeviceIdentity,
    int ReportCount,
    int IntervalCount,
    IReadOnlyList<double> IntervalsMilliseconds,
    double? MedianIntervalMilliseconds,
    double? P95IntervalMilliseconds,
    double? P99IntervalMilliseconds,
    double? ObservedReportRateHz,
    double? TailJitterMilliseconds,
    int LongGapCount,
    int BurstIntervalCount,
    bool HasBurstOrCoalescingEvidence,
    string MeasurementScope,
    string? Reason)
{
    public const string HostObservableScope = "host-observable-raw-input-dispatch-timing";
}

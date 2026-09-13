using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private void LogObservationDiagnosticSummary(
        KernelLatencyCaptureResponse capture,
        MeasurementScenario scenario,
        RuntimeMeasurementContextInterval? runtimeContext)
    {
        var attributedEventCount = capture.ResolvedModuleEventCount + capture.UnresolvedModuleEventCount;
        var attributionCoveragePercent = Percentage(capture.ResolvedModuleEventCount, attributedEventCount);
        var dpcGuidanceRatePercent = Percentage(
            capture.DpcThresholds.GuidanceExceedanceCount,
            capture.Dpc.Count);
        var isrGuidanceRatePercent = Percentage(
            capture.IsrThresholds.GuidanceExceedanceCount,
            capture.Isr.Count);

        var topDpcProcessor = capture.Processors
            .OrderByDescending(static processor => processor.Dpc.Count)
            .ThenBy(static processor => processor.ProcessorNumber)
            .FirstOrDefault();
        var topIsrProcessor = capture.Processors
            .OrderByDescending(static processor => processor.Isr.Count)
            .ThenBy(static processor => processor.ProcessorNumber)
            .FirstOrDefault();

        var topDpcGuidanceModule = capture.Modules
            .Where(static module => module.DpcThresholds.GuidanceExceedanceCount > 0)
            .OrderByDescending(static module => module.DpcThresholds.GuidanceExceedanceCount)
            .ThenByDescending(static module => module.Dpc.P99Microseconds ?? 0d)
            .ThenBy(static module => module.ModuleName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        var topIsrGuidanceModule = capture.Modules
            .Where(static module => module.IsrThresholds.GuidanceExceedanceCount > 0)
            .OrderByDescending(static module => module.IsrThresholds.GuidanceExceedanceCount)
            .ThenByDescending(static module => module.Isr.P99Microseconds ?? 0d)
            .ThenBy(static module => module.ModuleName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        Logger.Information(
            "Observation summary {RequestId}: scenario {Scenario}; DPC count {DpcCount}, p99 {DpcP99Microseconds} us, p99.9 {DpcP999Microseconds} us, max {DpcMaximumMicroseconds} us, guidance exceedances {DpcGuidanceExceedanceCount} ({DpcGuidanceExceedanceRatePercent}%); ISR count {IsrCount}, p99 {IsrP99Microseconds} us, p99.9 {IsrP999Microseconds} us, max {IsrMaximumMicroseconds} us, guidance exceedances {IsrGuidanceExceedanceCount} ({IsrGuidanceExceedanceRatePercent}%); ETW lost {EventsLost}, invalid events {InvalidEventCount}, invalid image events {InvalidImageEventCount}, event limit reached {EventLimitReached}.",
            capture.RequestId,
            scenario,
            capture.Dpc.Count,
            capture.Dpc.P99Microseconds,
            capture.Dpc.P999Microseconds,
            capture.Dpc.MaximumMicroseconds,
            capture.DpcThresholds.GuidanceExceedanceCount,
            dpcGuidanceRatePercent,
            capture.Isr.Count,
            capture.Isr.P99Microseconds,
            capture.Isr.P999Microseconds,
            capture.Isr.MaximumMicroseconds,
            capture.IsrThresholds.GuidanceExceedanceCount,
            isrGuidanceRatePercent,
            capture.EventsLost,
            capture.InvalidEventCount,
            capture.InvalidImageEventCount,
            capture.EventLimitReached);

        Logger.Information(
            "Observation attribution {RequestId}: module coverage {AttributionCoveragePercent}% ({ResolvedModuleEventCount} resolved, {UnresolvedModuleEventCount} unresolved); top DPC processor {TopDpcProcessorNumber} handled {TopDpcProcessorSharePercent}% of DPC events; top ISR processor {TopIsrProcessorNumber} handled {TopIsrProcessorSharePercent}% of ISR events; leading DPC guidance contributor {TopDpcGuidanceModule} ({TopDpcGuidanceExceedances}); leading ISR guidance contributor {TopIsrGuidanceModule} ({TopIsrGuidanceExceedances}); system CPU busy {SystemCpuBusyPercent}%; power {PowerLineState}, configured mode {ConfiguredPowerMode}, power context changed {PowerContextChanged}.",
            capture.RequestId,
            attributionCoveragePercent,
            capture.ResolvedModuleEventCount,
            capture.UnresolvedModuleEventCount,
            topDpcProcessor?.ProcessorNumber,
            Percentage(topDpcProcessor?.Dpc.Count ?? 0, capture.Dpc.Count),
            topIsrProcessor?.ProcessorNumber,
            Percentage(topIsrProcessor?.Isr.Count ?? 0, capture.Isr.Count),
            topDpcGuidanceModule?.ModuleName,
            topDpcGuidanceModule?.DpcThresholds.GuidanceExceedanceCount ?? 0,
            topIsrGuidanceModule?.ModuleName,
            topIsrGuidanceModule?.IsrThresholds.GuidanceExceedanceCount ?? 0,
            runtimeContext?.SystemCpuBusyPercent,
            runtimeContext?.StartPower.LineState,
            runtimeContext?.StartPower.UserConfiguredPowerMode,
            runtimeContext?.PowerContextChanged);
    }

    private void LogBaselineDiagnosticSummary(
        IReadOnlyList<KernelLatencyCaptureResponse> captures,
        IReadOnlyList<MeasurementRuntimeWindow> runtimeWindows,
        BaselineQualityResult quality,
        MeasurementScenario scenario,
        bool isPartial)
    {
        var cpuValues = runtimeWindows
            .Where(static window => window.Context?.SystemCpuBusyPercent is not null)
            .Select(static window => window.Context!.SystemCpuBusyPercent!.Value)
            .ToArray();
        var powerContextChanged = runtimeWindows
            .Where(static window => window.Context is not null)
            .Any(static window => window.Context!.PowerContextChanged);
        var requestIds = string.Join(
            ",",
            captures.Select(static capture => capture.RequestId.ToString("D")));

        Logger.Information(
            "Baseline summary: scenario {Scenario}; partial {IsPartial}; method {MethodVersion}; status {Status}; valid for comparison {IsValidForComparison}; windows {TotalWindowCount}, valid captures {ValidCaptureWindowCount}; DPC p99 median {DpcMedianMicroseconds} us, noise floor {DpcRelativeNoiseFloor}, drift {DpcRelativeDrift}; ISR p99 median {IsrMedianMicroseconds} us, noise floor {IsrRelativeNoiseFloor}, drift {IsrRelativeDrift}; runtime CPU avg {RuntimeCpuAveragePercent}% min {RuntimeCpuMinimumPercent}% max {RuntimeCpuMaximumPercent}%; power context changed {PowerContextChanged}; reasons {Reasons}; request IDs {RequestIds}.",
            scenario,
            isPartial,
            quality.MethodVersion,
            quality.Status,
            quality.IsValidForComparison,
            quality.TotalWindowCount,
            quality.ValidCaptureWindowCount,
            quality.DpcP99.MedianMicroseconds,
            quality.DpcP99.RelativeNoiseFloor,
            quality.DpcP99.RelativeDrift,
            quality.IsrP99.MedianMicroseconds,
            quality.IsrP99.RelativeNoiseFloor,
            quality.IsrP99.RelativeDrift,
            cpuValues.Length == 0 ? null : cpuValues.Average(),
            cpuValues.Length == 0 ? null : cpuValues.Min(),
            cpuValues.Length == 0 ? null : cpuValues.Max(),
            powerContextChanged,
            quality.Reasons.Count == 0 ? "none" : string.Join(" | ", quality.Reasons),
            requestIds);
    }

    private static double? Percentage(int numerator, int denominator) =>
        denominator <= 0
            ? null
            : numerator * 100d / denominator;
}

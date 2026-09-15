using System.ComponentModel;
using System.Globalization;
using LatencyPilot.App.Services;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private IReadOnlyList<GpuAffinityCandidate> _latestGpuAffinityCandidates = [];

    private void PrepareGpuAffinityCandidatePlan(
        IReadOnlyList<KernelLatencyCaptureResponse> captures,
        IReadOnlyList<BaselineWindowEvidence> windows,
        IReadOnlyList<MeasurementRuntimeWindow> runtimeWindows,
        BaselineQualityResult quality,
        bool isPartial,
        MeasurementScenario scenario)
    {
        ApplyBaselineTransientSignals(captures);
        _latestGpuAffinityCandidates = [];

        if (isPartial || scenario != MeasurementScenario.RealWorld)
        {
            return;
        }

        if (captures.Count != windows.Count || captures.Count != runtimeWindows.Count)
        {
            AppendGpuOptimizationReadiness(
                "Not ready. Baseline latency, window, and runtime-context evidence are misaligned; capture a new baseline.");
            Logger.Warning(
                "GPU affinity candidate preparation skipped because baseline evidence is misaligned. Captures={CaptureCount}, Windows={WindowCount}, RuntimeWindows={RuntimeWindowCount}.",
                captures.Count,
                windows.Count,
                runtimeWindows.Count);
            return;
        }

        var workloadStability = WorkloadStabilityAnalyzer.Analyze(
            windows,
            runtimeWindows.Select(static window => window.Context?.SystemCpuBusyPercent).ToArray());
        var experimentReady = GpuOptimizationBaselineReadiness.IsEligible(quality, workloadStability);
        AppendGpuOptimizationReadiness(FormatGpuOptimizationReadiness(quality, workloadStability, experimentReady));
        if (!experimentReady)
        {
            Logger.Information(
                "GPU affinity candidate preparation skipped because the repeated baseline is not experiment-ready. BaselineValid={BaselineValid}, WorkloadStatus={WorkloadStatus}, Reasons={Reasons}.",
                quality.IsValidForComparison,
                workloadStability.Status,
                workloadStability.Reasons.Count == 0 ? "none" : string.Join(" | ", workloadStability.Reasons));
            return;
        }

        try
        {
            // Phase 3 candidate preparation stays read-only. Re-capture topology at
            // the point the baseline becomes authoritative so a stale startup view
            // cannot silently drive a later affinity experiment.
            var topology = ProcessorTopologyReader.Capture();
            if (topology.ProcessorGroupCount != 1)
            {
                AppendGpuOptimizationReadiness(
                    $"Candidate planning stopped: this system exposes {topology.ProcessorGroupCount} processor groups; GPU affinity v1 supports exactly one.");
                Logger.Information(
                    "GPU affinity candidate preparation skipped because topology has {ProcessorGroupCount} processor groups; v1 supports exactly one.",
                    topology.ProcessorGroupCount);
                return;
            }

            ProcessorCpuSetSnapshot? cpuSets = null;
            try
            {
                cpuSets = ProcessorCpuSetReader.Capture();
            }
            catch (Exception exception) when (exception is
                Win32Exception or
                InvalidDataException or
                OverflowException)
            {
                Logger.Warning(
                    exception,
                    "CPU-set metadata was unavailable while preparing GPU affinity candidates; topology and baseline pressure remain usable.");
            }

            var pressureWindows = captures
                .Select(capture => (IReadOnlyList<ProcessorInterruptCountEvidence>)capture.Processors
                    .Select(CreateProcessorInterruptCountEvidence)
                    .ToArray())
                .ToArray();
            var pressure = ProcessorPressureEvidenceBuilder.Create(topology, pressureWindows);
            _latestGpuAffinityCandidates = GpuAffinityCandidatePlanner.Create(
                topology,
                pressure,
                cpuSets);

            var summary = string.Join(
                ", ",
                _latestGpuAffinityCandidates.Select(candidate => string.Create(
                    CultureInfo.InvariantCulture,
                    $"core {candidate.PhysicalCoreIndex}/CPU {candidate.Processor.Number}: {candidate.ObservedPressureScore:P2}")));
            Logger.Information(
                "Prepared {CandidateCount} read-only GPU affinity candidate(s) from the valid stable Real-world baseline using mean per-window DPC/ISR event share. Candidates={Candidates}.",
                _latestGpuAffinityCandidates.Count,
                summary);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidDataException or
            NotSupportedException or
            OverflowException or
            Win32Exception)
        {
            _latestGpuAffinityCandidates = [];
            AppendGpuOptimizationReadiness(
                "Candidate planning could not produce a trustworthy bounded plan. The baseline remains available for diagnosis, but no optimization should be attempted from it.");
            Logger.Warning(
                exception,
                "Valid baseline was retained, but automatic GPU affinity candidate preparation could not produce a trustworthy plan.");
        }
    }

    private static string FormatGpuOptimizationReadiness(
        BaselineQualityResult quality,
        WorkloadStabilityResult workloadStability,
        bool experimentReady)
    {
        if (experimentReady)
        {
            return $"Ready for bounded read-only GPU candidate planning. Latency repeatability passed {BaselineQualityAnalyzer.MethodVersion} and workload activity is Stable under {WorkloadStabilityAnalyzer.MethodVersion}.";
        }

        if (!quality.IsValidForComparison)
        {
            return $"Not ready. Latency repeatability did not pass {BaselineQualityAnalyzer.MethodVersion}; capture a new baseline before optimization.";
        }

        var workloadReason = workloadStability.Reasons.Count == 0
            ? $"{WorkloadStabilityAnalyzer.MethodVersion} could not establish stable workload activity."
            : string.Join(" ", workloadStability.Reasons);
        return $"Not ready. Latency repeatability is valid, but workload activity is {workloadStability.Status} under {WorkloadStabilityAnalyzer.MethodVersion}. {workloadReason} Re-run after the workload is fully warmed and repeatable.";
    }

    private void AppendGpuOptimizationReadiness(string detail)
    {
        BaselineReasonsText.Text = string.IsNullOrWhiteSpace(BaselineReasonsText.Text)
            ? $"Optimization readiness: {detail}"
            : $"{BaselineReasonsText.Text}{Environment.NewLine}Optimization readiness: {detail}";
    }

    private static ProcessorInterruptCountEvidence CreateProcessorInterruptCountEvidence(
        ProcessorLatencyDistribution processor)
    {
        if (processor.ProcessorNumber is < 0 or > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"Processor number {processor.ProcessorNumber} cannot be mapped to the single-group candidate planner.");
        }

        return new ProcessorInterruptCountEvidence(
            new LogicalProcessorId(0, checked((byte)processor.ProcessorNumber)),
            processor.Dpc.Count,
            processor.Isr.Count);
    }
}

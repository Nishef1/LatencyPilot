using System.ComponentModel;
using System.Globalization;
using LatencyPilot.App.Services;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Benchmarking.Candidates;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private IReadOnlyList<GpuAffinityCandidate> _latestGpuAffinityCandidates = [];
    private GpuOptimizationDashboardState? _latestGpuOptimizationDashboardState;
    private bool _gpuOptimizationDashboardHooksRegistered;

    private void PrepareGpuAffinityCandidatePlan(
        List<KernelLatencyCaptureResponse> captures,
        List<BaselineWindowEvidence> windows,
        List<MeasurementRuntimeWindow> runtimeWindows,
        BaselineQualityResult quality,
        bool isPartial,
        MeasurementScenario scenario)
    {
        ApplyBaselineTransientSignals(captures);
        EnsureGpuOptimizationDashboardHooks();
        _latestGpuAffinityCandidates = [];
        _latestGpuOptimizationDashboardState = null;
        ClearGpuOptimizationDashboardAnnotations();

        if (isPartial || scenario != MeasurementScenario.RealWorld)
        {
            return;
        }

        if (captures.Count != windows.Count || captures.Count != runtimeWindows.Count)
        {
            const string reason =
                "Baseline latency, window, and runtime-context evidence are misaligned; capture a new baseline.";
            AppendGpuOptimizationReadiness($"Not ready. {reason}");
            SetGpuOptimizationDashboardState(
                quality,
                false,
                "Evidence alignment failed",
                reason);
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
        var baselineEligibility = GpuOptimizationBaselineReadiness.Evaluate(quality, workloadStability);
        if (!baselineEligibility.IsEligible)
        {
            AppendGpuOptimizationReadiness(FormatGpuOptimizationBlockReason(quality, workloadStability));
            SetGpuOptimizationDashboardState(
                quality,
                false,
                FormatWorkloadDashboardDetail(workloadStability),
                baselineEligibility.Reason);
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
                var reason =
                    $"Baseline evidence passed, but this system exposes {topology.ProcessorGroupCount} processor groups and GPU affinity v1 supports exactly one.";
                AppendGpuOptimizationReadiness($"Not ready. {reason}");
                SetGpuOptimizationDashboardState(
                    quality,
                    false,
                    $"Workload: Stable · {topology.ProcessorGroupCount} processor groups unsupported",
                    reason);
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
            if (_latestGpuAffinityCandidates.Count == 0)
            {
                const string reason =
                    "Baseline evidence passed, but no bounded GPU affinity candidate could be derived from the current topology and pressure evidence.";
                AppendGpuOptimizationReadiness($"Not ready. {reason}");
                SetGpuOptimizationDashboardState(
                    quality,
                    false,
                    "Workload: Stable · no bounded GPU candidate",
                    reason);
                Logger.Information("GPU affinity candidate preparation produced no bounded candidates.");
                return;
            }

            var readyReason =
                $"Ready for bounded read-only GPU candidate planning. Latency repeatability passed {BaselineQualityAnalyzer.MethodVersion}, steady workload activity is Stable under {WorkloadStabilityAnalyzer.MethodVersion}, and {_latestGpuAffinityCandidates.Count} candidate(s) were prepared.";
            AppendGpuOptimizationReadiness(readyReason);
            SetGpuOptimizationDashboardState(
                quality,
                true,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Workload: Stable · {_latestGpuAffinityCandidates.Count:N0} GPU candidate(s) prepared"),
                $"{baselineEligibility.Reason} {_latestGpuAffinityCandidates.Count} bounded candidate(s) were prepared.");

            var summary = string.Join(
                ", ",
                _latestGpuAffinityCandidates.Select(candidate => string.Create(
                    CultureInfo.InvariantCulture,
                    $"core {candidate.PhysicalCoreIndex}/CPU {candidate.Processor.Number}: {candidate.ObservedPressureScore:P2}")));
            Logger.Information(
                "Prepared {CandidateCount} read-only GPU affinity candidate(s) from the valid stable steady-state Real-world baseline using mean per-window DPC/ISR event share. Candidates={Candidates}.",
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
            const string reason =
                "Candidate planning could not produce a trustworthy bounded plan. The baseline remains available for diagnosis, but no optimization should be attempted from it.";
            AppendGpuOptimizationReadiness($"Not ready. {reason}");
            SetGpuOptimizationDashboardState(
                quality,
                false,
                "Workload: Stable · candidate planning unavailable",
                reason);
            Logger.Warning(
                exception,
                "Valid baseline was retained, but automatic GPU affinity candidate preparation could not produce a trustworthy plan.");
        }
    }

    private static string FormatGpuOptimizationBlockReason(
        BaselineQualityResult quality,
        WorkloadStabilityResult workloadStability)
    {
        if (!quality.IsValidForComparison)
        {
            return $"Not ready. Latency repeatability did not pass {BaselineQualityAnalyzer.MethodVersion}; capture a new steady-state baseline before optimization. A phase-changing built-in benchmark is not eligible for this five-window baseline method.";
        }

        var eligibility = GpuOptimizationBaselineReadiness.Evaluate(quality, workloadStability);
        return $"Not ready. Latency repeatability is valid, but workload activity is {workloadStability.Status} under {WorkloadStabilityAnalyzer.MethodVersion}. {eligibility.Reason} Re-run on one warmed steady scene/action loop. If the workload is a phase-changing built-in benchmark, compare repeated whole benchmark runs instead; do not use its internal phases as GPU candidate evidence.";
    }

    private static string FormatWorkloadDashboardDetail(WorkloadStabilityResult workloadStability)
    {
        if (workloadStability.Status == WorkloadStabilityStatus.Stable)
        {
            return "Workload: Stable";
        }

        var material = workloadStability.Signals.FirstOrDefault(static signal =>
            signal.HasMaterialDrift || signal.HasExtremeWindow);
        if (material is null)
        {
            return $"Workload: {workloadStability.Status}";
        }

        var signalName = material.SignalName switch
        {
            "System CPU busy" => "CPU",
            "DPC event rate" => "DPC rate",
            "ISR event rate" => "ISR rate",
            _ => material.SignalName,
        };

        if (material.HasMaterialDrift)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Workload: {workloadStability.Status} · {signalName} drift {material.RelativeDrift * 100d:0.0}% > {WorkloadStabilityAnalyzer.MaximumRelativeActivityDrift * 100d:0}%");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Workload: {workloadStability.Status} · {signalName} deviation {material.MaximumRelativeDeviation * 100d:0.0}% > {WorkloadStabilityAnalyzer.MaximumExtremeWindowRelativeDeviation * 100d:0}%");
    }

    private void AppendGpuOptimizationReadiness(string detail)
    {
        BaselineReasonsText.Text = string.IsNullOrWhiteSpace(BaselineReasonsText.Text)
            ? $"Optimization readiness: {detail}"
            : $"{BaselineReasonsText.Text}{Environment.NewLine}Optimization readiness: {detail}";
    }

    private void EnsureGpuOptimizationDashboardHooks()
    {
        if (_gpuOptimizationDashboardHooksRegistered)
        {
            return;
        }

        _gpuOptimizationDashboardHooksRegistered = true;
        RootGrid.ActualThemeChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(ApplyGpuOptimizationDashboardState);
        TryRegisterHighContrastChanged(() =>
            DispatcherQueue.TryEnqueue(ApplyGpuOptimizationDashboardState));
        BaselineVerdictText.RegisterPropertyChangedCallback(
            TextBlock.TextProperty,
            (_, _) => ApplyGpuOptimizationDashboardState());
    }

    private void SetGpuOptimizationDashboardState(
        BaselineQualityResult quality,
        bool isEligible,
        string detail,
        string reason)
    {
        if (!quality.IsValidForComparison)
        {
            _latestGpuOptimizationDashboardState = null;
            return;
        }

        _latestGpuOptimizationDashboardState = new GpuOptimizationDashboardState(
            isEligible,
            detail,
            reason);
        ApplyGpuOptimizationDashboardState();
    }

    private void ApplyGpuOptimizationDashboardState()
    {
        var state = _latestGpuOptimizationDashboardState;
        if (state is null ||
            !string.Equals(BaselineVerdictText.Text, "Valid", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var statusText = state.IsEligible
            ? "Valid · optimization-ready"
            : "Valid · not optimization-ready";
        BaselineSummaryText.Text = statusText;
        BaselineSummaryIcon.Glyph = state.IsEligible ? "\uE73E" : "\uE7BA";
        BaselineSummaryText.Foreground = ThemeBrush(state.IsEligible ? "SuccessBrush" : "WarningBrush");
        BaselineSummaryDetailText.TextWrapping = TextWrapping.Wrap;
        BaselineSummaryDetailText.MaxLines = 3;

        var transientDetail = _latestBaselineTransientSignals is { HasOverOneMillisecondSignal: true } transient
            ? $"{Environment.NewLine}Tail: {FormatTransientDashboardDetail(transient).Replace("p99 repeatable · ", string.Empty, StringComparison.Ordinal)}"
            : string.Empty;
        BaselineSummaryDetailText.Text = $"{state.Detail}{transientDetail}";

        var transientHelp = _latestBaselineTransientSignals is { HasOverOneMillisecondSignal: true } transientSummary
            ? $" {FormatTransientExactEvidence(transientSummary)}"
            : string.Empty;
        var helpText =
            $"Baseline quality: Valid. Optimizer eligibility: {(state.IsEligible ? "Eligible" : "Not eligible")}. {state.Reason}{transientHelp}";
        ToolTipService.SetToolTip(BaselineSummaryText, helpText);
        ToolTipService.SetToolTip(BaselineSummaryDetailText, helpText);
        AutomationProperties.SetName(BaselineSummaryText, statusText);
        AutomationProperties.SetHelpText(BaselineSummaryText, helpText);
        AutomationProperties.SetHelpText(BaselineSummaryDetailText, helpText);
    }

    private void ClearGpuOptimizationDashboardAnnotations()
    {
        ToolTipService.SetToolTip(BaselineSummaryText, null);
        ToolTipService.SetToolTip(BaselineSummaryDetailText, null);
        AutomationProperties.SetHelpText(BaselineSummaryText, string.Empty);
        AutomationProperties.SetHelpText(BaselineSummaryDetailText, string.Empty);
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

    private sealed record GpuOptimizationDashboardState(
        bool IsEligible,
        string Detail,
        string Reason);
}

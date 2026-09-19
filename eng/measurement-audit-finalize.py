from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one anchor, found {count}")
    return text.replace(old, new, 1)


def replace_between(text: str, start: str, end: str, replacement: str, label: str) -> str:
    first = text.find(start)
    if first < 0:
        raise RuntimeError(f"{label}: start anchor missing")
    last = text.find(end, first)
    if last < 0:
        raise RuntimeError(f"{label}: end anchor missing")
    return text[:first] + replacement + text[last:]


def main() -> None:
    runner_path = "tools/LatencyPilot.GateAValidation/GpuAutoAffinityGateARunner.cs"
    runner = read(runner_path)
    runner = replace_once(
        runner,
        '''    private static readonly Guid DisplayDeviceClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
''',
        '''    private static readonly Guid DisplayDeviceClass = new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private static readonly TimeSpan ControlPollInterval = TimeSpan.FromSeconds(1);
''',
        "Gate A control poll interval")
    runner = replace_once(
        runner,
        '''            await progress.ReportTerminalAsync(
                result.Recommendation.ToString(),
                result.Finalist,
                finalReport.FinalStateVerified,
''',
        '''            await progress.ReportTerminalAsync(
                result.Recommendation.ToString(),
                result.Recommendation == GpuOptimizationRecommendation.KeepCandidate
                    ? result.Finalist
                    : null,
                finalReport.FinalStateVerified,
''',
        "terminal retained candidate")
    runner = replace_once(
        runner,
        '''            Console.WriteLine($"final-processor={result.Finalist?.Processor.ToString() ?? "original"}");
''',
        '''            Console.WriteLine($"final-processor={finalReport.FinalProcessor?.ToString() ?? "original"}");
''',
        "terminal processor output")
    runner = replace_once(
        runner,
        '''            await Task.Delay(TimeSpan.FromMilliseconds(250), shutdownToken).ConfigureAwait(false);
''',
        '''            await Task.Delay(ControlPollInterval, shutdownToken).ConfigureAwait(false);
''',
        "stop watcher polling")
    write(runner_path, runner)

    experience_path = "src/LatencyPilot.App/GateAValidationExperience.cs"
    experience = read(experience_path)
    experience = replace_once(
        experience,
        '''    private static readonly JsonSerializerOptions GateAJsonOptions = new(JsonSerializerDefaults.Web);
''',
        '''    private static readonly JsonSerializerOptions GateAJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan GateAHelperPollInterval = TimeSpan.FromSeconds(1);
''',
        "App helper poll interval")
    experience = replace_once(
        experience,
        '''            await Task.Delay(TimeSpan.FromMilliseconds(250));
''',
        '''            await Task.Delay(GateAHelperPollInterval);
''',
        "App helper polling")
    summary = '''    private static string BuildFinalSummary(GpuAutoAffinityReport report)
    {
        var headline = string.Equals(report.FinalRecommendation, "KeepCandidate", StringComparison.Ordinal)
            ? $"Verified winner: CPU {report.FinalProcessor?.Number.ToString(CultureInfo.InvariantCulture) ?? "—"}."
            : report.OriginalStateRestored
                ? "Original GPU affinity state is verified/restored."
                : $"Final recommendation: {report.FinalRecommendation}.";

        var metrics = string.Empty;
        var decisionProcessor = report.FinalProcessor ?? report.BestMeasuredProcessor;
        if (decisionProcessor is { } processor)
        {
            var decision = report.Candidates.LastOrDefault(candidate =>
                candidate.Processor.Equals(processor) &&
                candidate.MedianOnePercentLowFps is { } low &&
                double.IsFinite(low) && low > 0);
            if (decision is not null)
            {
                var prefix = report.FinalProcessor is not null
                    ? " Kept decision medians:"
                    : $" Best measured forced candidate CPU {processor.Number} decision medians:";
                metrics = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{prefix} 1% {FormatMetric(decision.MedianOnePercentLowFps, "F1")} FPS · 0.1% {FormatMetric(decision.MedianLow01PctFps, "F1")} FPS · AVG {FormatMetric(decision.MedianAvgFps, "F1")} FPS · p99 {FormatMetric(decision.MedianFrameP99Milliseconds, "F2")} ms.");
            }
        }

        var finalVerification = report.Trials.LastOrDefault(static trial =>
            string.Equals(trial.Phase, "final-verification", StringComparison.Ordinal));
        var placement = finalVerification?.Placement is { ConfirmsRequestedPlacement: true } proof
            ? $" Final ISR placement verified on CPU {proof.TargetProcessor.Number}."
            : string.Equals(report.FinalRecommendation, "KeepCandidate", StringComparison.Ordinal)
                ? " Final ISR placement evidence is missing from the report."
                : string.Empty;
        var source = report.GateAClosureEligible
            ? " Source evidence is eligible for physical Gate A closure."
            : string.Equals(report.SourceState, GpuOptimizationSourceState.DevelopmentOnly.ToString(), StringComparison.Ordinal)
                ? " Development-only source: this report cannot close physical Gate A."
                : " Source evidence is not eligible for physical Gate A closure.";
        var reasons = string.Join(" ", report.Reasons.Take(2));
        return $"{headline}{metrics}{placement}{source} {reasons}".TrimEnd();
    }

'''
    experience = replace_between(
        experience,
        "    private static string BuildFinalSummary(GpuAutoAffinityReport report)\n",
        "    private static string FormatMetric(double? value, string format) =>\n",
        summary,
        "final summary decision source")
    write(experience_path, experience)

    progress_path = "src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml.cs"
    progress = read(progress_path)
    progress = replace_once(
        progress,
        '''            StatusText.Text = $"{StatusText.Text}\\n{summary}\\nReport: {reportPath}";
''',
        '''            StatusText.Text = $"{summary}\\nReport: {reportPath}";
''',
        "terminal status de-duplication")
    write(progress_path, progress)

    xaml_path = "src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml"
    xaml = read(xaml_path)
    xaml = replace_once(
        xaml,
        '''                        <TextBlock Text="Last completed candidate"
''',
        '''                        <TextBlock Text="Latest benchmark outcome"
''',
        "latest outcome label")
    write(xaml_path, xaml)

    workload_path = "src/LatencyPilot.GpuBenchmark/BenchmarkWorkload.cs"
    workload = read(workload_path)
    workload = replace_once(
        workload,
        '''    private const int MaximumSimulationIterations = 4_000_000;
''',
        '''    private const int MaximumSimulationIterations = 4_000_000;
    private const int PreallocatedFramesPerSecond = 1_000;
''',
        "frame preallocation constant")
    workload = replace_once(
        workload,
        '''            Math.Ceiling(duration.TotalSeconds * 240d),
''',
        '''            Math.Ceiling(duration.TotalSeconds * PreallocatedFramesPerSecond),
''',
        "frame list preallocation")
    write(workload_path, workload)

    audit_path = "tests/LatencyPilot.CriticalTests/AuditClosureIntegrationTests.cs"
    audit = read(audit_path)
    anchor = '''        Assert.AreEqual(0, obsoleteWorkflows.Length,
            "Temporary audit-closure workflows must not remain after their source scripts are removed.");
'''
    addition = anchor + '''
        var gateARunner = File.ReadAllText(Path.Combine(root, "tools", "LatencyPilot.GateAValidation", "GpuAutoAffinityGateARunner.cs"));
        Assert.IsFalse(gateARunner.Contains("Task.Delay(TimeSpan.FromMilliseconds(250)", StringComparison.Ordinal));
        StringAssert.Contains(gateARunner, "finalReport.FinalProcessor");
        var gateAExperience = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.App", "GateAValidationExperience.cs"));
        Assert.IsFalse(gateAExperience.Contains("Task.Delay(TimeSpan.FromMilliseconds(250)", StringComparison.Ordinal));
        Assert.IsFalse(gateAExperience.Contains("MedianTrialMetric(", StringComparison.Ordinal),
            "The App must consume decision-engine medians instead of recomputing a competing result from raw trials.");
'''
    audit = replace_once(audit, anchor, addition, "final observer and summary regressions")
    write(audit_path, audit)


if __name__ == "__main__":
    main()

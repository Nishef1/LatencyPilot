from __future__ import annotations

from pathlib import Path
import sys

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


def replace_all(text: str, old: str, new: str, expected: int, label: str) -> str:
    count = text.count(old)
    if count != expected:
        raise RuntimeError(f"{label}: expected {expected} anchors, found {count}")
    return text.replace(old, new)


def replace_between(text: str, start: str, end: str, replacement: str, label: str) -> str:
    first = text.find(start)
    if first < 0:
        raise RuntimeError(f"{label}: start anchor missing")
    last = text.find(end, first)
    if last < 0:
        raise RuntimeError(f"{label}: end anchor missing")
    return text[:first] + replacement + text[last:]


def add_regressions() -> None:
    audit_path = "tests/LatencyPilot.CriticalTests/AuditClosureIntegrationTests.cs"
    audit = read(audit_path)
    anchor = '''        StringAssert.Contains(reportContract, "GateAClosureEligible");
        StringAssert.Contains(reportContract, "SourceState");
'''
    addition = anchor + '''
        var kernelCapture = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.Platform.Windows", "Etw", "KernelLatencyCapture.cs"));
        StringAssert.Contains(kernelCapture, "onSessionReady?.Invoke()");
        var gateABackend = File.ReadAllText(Path.Combine(root, "tools", "LatencyPilot.GateAValidation", "GpuAutoAffinityGateABackend.cs"));
        StringAssert.Contains(gateABackend, "TaskCompletionSource<bool>");
        Assert.IsFalse(gateABackend.Contains("Task.Delay(TimeSpan.FromMilliseconds(150)", StringComparison.Ordinal),
            "Scored benchmark startup must wait for an explicit ETW-ready signal, not an arbitrary sleep.");

        var progressWindow = File.ReadAllText(Path.Combine(root, "src", "LatencyPilot.App", "GpuOptimizationProgressWindow.xaml.cs"));
        StringAssert.Contains(progressWindow, "ProgressPollInterval = TimeSpan.FromSeconds(1)");
        StringAssert.Contains(progressWindow, "lastProgressWriteUtc");
        Assert.IsFalse(progressWindow.Contains("Task.Delay(TimeSpan.FromMilliseconds(250)", StringComparison.Ordinal),
            "The observer UI must not poll and redraw four times per second during scored measurements.");

        var obsoleteWorkflows = Directory.GetFiles(
            Path.Combine(root, ".github", "workflows"),
            "audit-closure-*.yml",
            SearchOption.TopDirectoryOnly);
        Assert.AreEqual(0, obsoleteWorkflows.Length,
            "Temporary audit-closure workflows must not remain after their source scripts are removed.");
'''
    audit = replace_once(audit, anchor, addition, "audit integration regressions")
    write(audit_path, audit)

    session_test_path = "tests/LatencyPilot.CriticalTests/GpuAutoAffinitySessionTests.cs"
    tests = read(session_test_path)
    tests = replace_once(
        tests,
        '''        Assert.IsTrue(result.Report.FinalStateVerified);
        Assert.IsFalse(result.Report.OriginalStateRestored);
''',
        '''        Assert.IsTrue(result.Report.FinalStateVerified);
        Assert.IsFalse(result.Report.OriginalStateRestored);
        Assert.AreEqual(new LogicalProcessorId(0, 2), result.Report.FinalProcessor);
        Assert.AreEqual(result.Finalist?.Processor, result.Report.BestMeasuredProcessor);
''',
        "kept final processor regression")
    tests = replace_once(
        tests,
        '''        Assert.IsTrue(finalistReports.All(static report => report.TrialCount == 2));
''',
        '''        Assert.IsTrue(finalistReports.All(static report => report.TrialCount == 2));
        Assert.IsTrue(finalistReports.All(static report =>
            report.MedianOnePercentLowFps is > 0 &&
            report.MedianAvgFps is > 0 &&
            report.MedianFrameP99Milliseconds is > 0 &&
            report.ValidObservationCount is >= 3));
''',
        "decision metrics regression")
    tests = replace_once(
        tests,
        '''        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, originalWins.Recommendation);
        Assert.IsTrue(originalWins.Report.OriginalStateRestored);
''',
        '''        Assert.AreEqual(GpuOptimizationRecommendation.RestoreOriginal, originalWins.Recommendation);
        Assert.IsTrue(originalWins.Report.OriginalStateRestored);
        Assert.IsNull(originalWins.Report.FinalProcessor,
            "FinalProcessor is the retained active candidate and must be null when Original is restored.");
        Assert.IsNotNull(originalWins.Report.BestMeasuredProcessor,
            "The best measured forced candidate remains useful diagnostic context even when Original wins.");
''',
        "restore-original report semantics regression")
    tests = replace_once(
        tests,
        '''        Assert.AreEqual(new LogicalProcessorId(0, 2), missingFinalResult.Finalist?.Processor);
        Assert.IsTrue(missingFinalResult.Report.OriginalStateRestored);
''',
        '''        Assert.AreEqual(new LogicalProcessorId(0, 2), missingFinalResult.Finalist?.Processor);
        Assert.IsNull(missingFinalResult.Report.FinalProcessor,
            "A failed final placement proof restores Original, so the report must not claim the attempted finalist is active.");
        Assert.AreEqual(new LogicalProcessorId(0, 2), missingFinalResult.Report.BestMeasuredProcessor);
        Assert.IsTrue(missingFinalResult.Report.OriginalStateRestored);
''',
        "failed keep report semantics regression")
    tests = replace_once(
        tests,
        '''        Assert.AreEqual(result.Report.FinalProcessor, roundTrip.FinalProcessor);
''',
        '''        Assert.AreEqual(result.Report.FinalProcessor, roundTrip.FinalProcessor);
        Assert.AreEqual(result.Report.BestMeasuredProcessor, roundTrip.BestMeasuredProcessor);
''',
        "report roundtrip regression")
    write(session_test_path, tests)


def implement() -> None:
    kernel_path = "src/LatencyPilot.Platform.Windows/Etw/KernelLatencyCapture.cs"
    kernel = read(kernel_path)
    kernel = replace_once(
        kernel,
        '''    public static KernelLatencyCaptureResult Capture(
        KernelLatencyCaptureOptions options,
        CancellationToken cancellationToken = default)
''',
        '''    public static KernelLatencyCaptureResult Capture(
        KernelLatencyCaptureOptions options,
        CancellationToken cancellationToken = default,
        Action? onSessionReady = null)
''',
        "kernel readiness signature")
    kernel = replace_once(
        kernel,
        '''        session.Source.Process();
''',
        '''        // EnableKernelProvider has already started kernel collection. Signal only
        // after handlers, cancellation, and timeout ownership are installed so a
        // synchronized workload never relies on an arbitrary startup sleep.
        onSessionReady?.Invoke();
        session.Source.Process();
''',
        "kernel readiness signal")
    write(kernel_path, kernel)

    backend_path = "tools/LatencyPilot.GateAValidation/GpuAutoAffinityGateABackend.cs"
    backend = read(backend_path)
    old_kernel_start = '''            var kernelDuration = request.Duration + CollectorTailSlack;
            var kernelTask = Task.Run(
                () => KernelLatencyCapture.Capture(
                    new KernelLatencyCaptureOptions(kernelDuration, KernelMaximumEvents),
                    deadline.Token),
                deadline.Token);

            await Task.Delay(TimeSpan.FromMilliseconds(150), deadline.Token).ConfigureAwait(false);
            var artifactPathTask = benchmark.RunTrialAsync(
'''
    new_kernel_start = '''            var kernelDuration = request.Duration + CollectorTailSlack;
            var kernelReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var kernelTask = Task.Run(
                () => KernelLatencyCapture.Capture(
                    new KernelLatencyCaptureOptions(kernelDuration, KernelMaximumEvents),
                    deadline.Token,
                    () => kernelReady.TrySetResult(true)),
                deadline.Token);

            var firstKernelState = await Task.WhenAny(kernelReady.Task, kernelTask).ConfigureAwait(false);
            if (ReferenceEquals(firstKernelState, kernelTask))
            {
                await kernelTask.ConfigureAwait(false);
                throw new InvalidOperationException("Kernel ETW capture completed before signaling readiness.");
            }

            await kernelReady.Task.ConfigureAwait(false);
            var artifactPathTask = benchmark.RunTrialAsync(
'''
    backend = replace_once(backend, old_kernel_start, new_kernel_start, "Gate A ETW synchronization")
    write(backend_path, backend)

    cluster_path = "src/LatencyPilot.Benchmarking/Optimization/GpuRepeatabilityClusterSelector.cs"
    cluster = read(cluster_path)
    cluster = replace_once(
        cluster,
        '''    internal static GpuRepeatabilityClusterSelection? Select(double[] values)
    {
''',
        '''    internal static GpuRepeatabilityClusterSelection? Select(double[] values) =>
        SelectCore(values, enforceTolerance: true);

    internal static GpuRepeatabilityClusterSelection? SelectClosest(double[] values) =>
        SelectCore(values, enforceTolerance: false);

    private static GpuRepeatabilityClusterSelection? SelectCore(double[] values, bool enforceTolerance)
    {
''',
        "repeatability closest-cluster helper")
    cluster = replace_once(
        cluster,
        '''                    if (maximum > RelativeTolerance)
''',
        '''                    if (enforceTolerance && maximum > RelativeTolerance)
''',
        "repeatability tolerance switch")
    write(cluster_path, cluster)

    report_path = "src/LatencyPilot.Core/Benchmarking/GpuAutoAffinityReport.cs"
    report = read(report_path)
    report = replace_once(
        report,
        '''    IReadOnlyList<string> RegressedGuardrails,
    string? Reason = null);
''',
        '''    IReadOnlyList<string> RegressedGuardrails,
    string? Reason = null,
    double? MedianOnePercentLowFps = null,
    double? MedianLow01PctFps = null,
    double? MedianAvgFps = null,
    double? MedianFrameP99Milliseconds = null,
    double? PrimaryRelativeNoise = null,
    int? ValidObservationCount = null,
    int? TotalObservationCount = null);
''',
        "candidate decision metrics contract")
    report = replace_once(
        report,
        '''    public bool GateAClosureEligible { get; init; }
''',
        '''    public bool GateAClosureEligible { get; init; }

    // BestMeasuredProcessor is diagnostic ranking context. FinalProcessor is only
    // populated when a candidate is actually retained as the final active state.
    public LogicalProcessorId? BestMeasuredProcessor { get; init; }
''',
        "report best-measured semantics")
    write(report_path, report)

    session_path = "src/LatencyPilot.Benchmarking/Optimization/GpuAutoAffinitySession.cs"
    session = read(session_path)
    old_cluster_call = '''            var cluster = GpuRepeatabilityClusterSelector.Select(values.Select(static item => item.Low1PctFps).ToArray());
'''
    new_cluster_call = '''            var low1Values = values.Select(static item => item.Low1PctFps).ToArray();
            var cluster = GpuRepeatabilityClusterSelector.Select(low1Values);
'''
    session = replace_all(session, old_cluster_call, new_cluster_call, 1, "candidate repeatability values")
    old_original_cluster = '''        var cluster = GpuRepeatabilityClusterSelector.Select(values.Select(static item => item.Low1PctFps).ToArray());
'''
    new_original_cluster = '''        var low1Values = values.Select(static item => item.Low1PctFps).ToArray();
        var cluster = GpuRepeatabilityClusterSelector.Select(low1Values);
'''
    session = replace_all(session, old_original_cluster, new_original_cluster, 1, "original repeatability values")
    session = replace_all(
        session,
        "BuildNoStableClusterReason(observations.Length)",
        "BuildNoStableClusterReason(low1Values)",
        2,
        "repeatability diagnostic calls")
    old_reason = '''    private static string BuildNoStableClusterReason(int observationCount) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"No stable {GpuRepeatabilityClusterSelector.RequiredRunCount}-run 1% low cluster exists within ±{GpuRepeatabilityClusterSelector.RelativeTolerance:P0} after {observationCount} scored observation(s)." );
'''
    new_reason = '''    private static string BuildNoStableClusterReason(double[] values)
    {
        var closest = GpuRepeatabilityClusterSelector.SelectClosest(values);
        var observed = string.Join(", ", values.Select(static value =>
            value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)));
        if (closest is null)
        {
            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"No stable {GpuRepeatabilityClusterSelector.RequiredRunCount}-run 1% low cluster exists within ±{GpuRepeatabilityClusterSelector.RelativeTolerance:P0} after {values.Length} scored observation(s). Observed 1% lows: [{observed}] FPS.");
        }

        var tightest = string.Join(", ", closest.Indexes.Select(index =>
            values[index].ToString("F1", System.Globalization.CultureInfo.InvariantCulture)));
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"No stable {GpuRepeatabilityClusterSelector.RequiredRunCount}-run 1% low cluster exists within ±{GpuRepeatabilityClusterSelector.RelativeTolerance:P0} after {values.Length} scored observation(s). Observed 1% lows: [{observed}] FPS. Tightest {GpuRepeatabilityClusterSelector.RequiredRunCount}-run triplet [{tightest}] has max median-centered deviation {closest.MaximumRelativeDeviation:P2}.");
    }
'''
    session = replace_once(session, old_reason, new_reason, "repeatability diagnostics")
    old_report_tail = '''                : evaluation.Reason);
'''
    new_report_tail = '''                : evaluation.Reason,
            MedianOnePercentLowFps: evaluation.IsRankable ? evaluation.MedianLow1Fps : null,
            MedianLow01PctFps: evaluation.IsRankable ? evaluation.MedianLow01Fps : null,
            MedianAvgFps: evaluation.IsRankable ? evaluation.MedianAvgFps : null,
            MedianFrameP99Milliseconds: evaluation.IsRankable ? evaluation.MedianFrameP99Milliseconds : null,
            PrimaryRelativeNoise: evaluation.IsRankable ? evaluation.PrimaryRelativeNoise : null,
            ValidObservationCount: evaluation.ValidObservationCount,
            TotalObservationCount: evaluation.TotalObservationCount);
'''
    session = replace_once(session, old_report_tail, new_report_tail, "candidate report decision metrics")
    session = replace_once(
        session,
        '''            recommendation.ToString(),
            finalist?.Processor,
''',
        '''            recommendation.ToString(),
            recommendation == GpuOptimizationRecommendation.KeepCandidate ? finalist?.Processor : null,
''',
        "retained final processor semantics")
    session = replace_once(
        session,
        '''            originalStateRestored,
            reasons.AsReadOnly());
''',
        '''            originalStateRestored,
            reasons.AsReadOnly())
        {
            BestMeasuredProcessor = finalist?.Processor,
        };
''',
        "best measured processor report")
    write(session_path, session)

    progress_path = "src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml.cs"
    progress = read(progress_path)
    progress = replace_once(
        progress,
        '''    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
''',
        '''    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ProgressPollInterval = TimeSpan.FromSeconds(1);
''',
        "progress poll interval")
    progress = replace_once(
        progress,
        '''    private bool terminalSnapshotReceived;
''',
        '''    private bool terminalSnapshotReceived;
    private DateTime lastProgressWriteUtc;
    private long lastProgressLength = -1;
''',
        "progress file stamp")
    progress = replace_once(
        progress,
        '''                await Task.Delay(TimeSpan.FromMilliseconds(250));
''',
        '''                await Task.Delay(ProgressPollInterval);
''',
        "progress polling cadence")
    progress = replace_once(
        progress,
        '''            if (!File.Exists(progressPath))
            {
                return false;
            }

            await using var stream = new FileStream(
''',
        '''            var progressFile = new FileInfo(progressPath);
            if (!progressFile.Exists)
            {
                return false;
            }

            progressFile.Refresh();
            if (progressFile.LastWriteTimeUtc == lastProgressWriteUtc &&
                progressFile.Length == lastProgressLength)
            {
                return false;
            }
            var observedWriteUtc = progressFile.LastWriteTimeUtc;
            var observedLength = progressFile.Length;

            await using var stream = new FileStream(
''',
        "progress unchanged fast path")
    progress = replace_once(
        progress,
        '''            ApplySnapshot(snapshot);
''',
        '''            lastProgressWriteUtc = observedWriteUtc;
            lastProgressLength = observedLength;
            ApplySnapshot(snapshot);
''',
        "progress stamp after valid parse")

    new_show_ranked = '''    private void ShowRankedResults(GpuAutoAffinityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        RankedCandidatesPanel.Children.Clear();

        // Candidate reports carry the exact medians selected by the decision engine.
        // Use the latest report per processor (finalist re-test when present) instead
        // of recomputing a different median from raw trials in the UI.
        var rows = report.Candidates
            .GroupBy(static item => item.Processor)
            .Select(static group => group.Last())
            .Where(static item =>
                string.Equals(item.Verdict, "Ranked", StringComparison.Ordinal) &&
                item.MedianOnePercentLowFps is { } low && double.IsFinite(low) && low > 0)
            .OrderByDescending(item =>
                report.BestMeasuredProcessor is not null &&
                report.BestMeasuredProcessor.Equals(item.Processor))
            .ThenByDescending(static item => item.MedianOnePercentLowFps)
            .ThenByDescending(static item => item.MedianAvgFps)
            .ThenBy(static item => item.MedianFrameP99Milliseconds)
            .ThenByDescending(static item => item.MedianLow01PctFps)
            .ToArray();

        if (rows.Length == 0)
        {
            RankedSummaryText.Text = "No decision-grade candidate result. Original/default remains active; open the JSON report for the exact repeatability or trial reason.";
            AutomationProperties.SetName(RankedSummaryText, "Candidate results: none decision-grade");
            return;
        }

        var inconclusive = report.Candidates
            .Where(static item => string.Equals(item.Verdict, "Inconclusive", StringComparison.Ordinal))
            .Select(static item => item.Processor.Number)
            .Distinct()
            .Count();
        var best = rows[0];
        var kept = report.FinalProcessor is not null && report.FinalProcessor.Equals(best.Processor);
        var outcome = kept
            ? "Kept"
            : "Best measured forced candidate";
        var finalState = kept
            ? string.Empty
            : " Original/default remains active.";
        RankedSummaryText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "{0}: CPU {1} (decision median 1% low {2:F1} FPS, {3} decision-grade{4}).{5} Practical-tie bands are applied by the decision engine before this summary.",
            outcome,
            best.Processor.Number,
            best.MedianOnePercentLowFps!.Value,
            rows.Length,
            inconclusive > 0 ? $", {inconclusive} inconclusive" : string.Empty,
            finalState);
        AutomationProperties.SetName(RankedSummaryText, $"Candidate results. {RankedSummaryText.Text}");

        var minimumLow = rows.Min(static item => item.MedianOnePercentLowFps!.Value);
        var maximumLow = rows.Max(static item => item.MedianOnePercentLowFps!.Value);
        var span = maximumLow - minimumLow;
        foreach (var row in rows)
        {
            var isBestMeasured = report.BestMeasuredProcessor is not null &&
                report.BestMeasuredProcessor.Equals(row.Processor);
            var isKept = report.FinalProcessor is not null && report.FinalProcessor.Equals(row.Processor);
            var suffix = isKept ? " · kept" : isBestMeasured ? " · best measured" : string.Empty;
            var label = new TextBlock
            {
                Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "CPU {0} · core {1} · 1% {2} · 0.1% {3} · AVG {4} FPS · p99 {5} ms · {6}{7}",
                    row.Processor.Number,
                    row.PhysicalCoreIndex,
                    FormatFps(row.MedianOnePercentLowFps),
                    FormatFps(row.MedianLow01PctFps),
                    FormatFps(row.MedianAvgFps),
                    row.MedianFrameP99Milliseconds is { } p99 ? p99.ToString("F2", CultureInfo.InvariantCulture) : "—",
                    row.Verdict,
                    suffix),
                Style = (Style)Application.Current.Resources["BodyTextStyle"],
                TextWrapping = TextWrapping.Wrap,
            };
            AutomationProperties.SetName(label, label.Text);
            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = span > 0
                    ? (row.MedianOnePercentLowFps!.Value - minimumLow) / span * 100d
                    : 100d,
                Height = 8,
            };
            AutomationProperties.SetName(
                bar,
                string.Create(CultureInfo.InvariantCulture, $"CPU {row.Processor.Number} relative 1 percent low bar"));
            var container = new StackPanel { Spacing = 2 };
            container.Children.Add(label);
            container.Children.Add(bar);
            RankedCandidatesPanel.Children.Add(container);
        }
    }

'''
    progress = replace_between(
        progress,
        "    private void ShowRankedResults(GpuAutoAffinityReport report)\n",
        "    internal void ShowStartupFailure(string message)\n",
        new_show_ranked,
        "ranked-result UI")
    write(progress_path, progress)

    xaml_path = "src/LatencyPilot.App/GpuOptimizationProgressWindow.xaml"
    xaml = read(xaml_path)
    xaml = replace_once(
        xaml,
        '''                        <TextBlock Text="Ranked candidates (higher 1% low is better)"
                                   Style="{StaticResource CaptionTextStyle}"
                                   AutomationProperties.Name="Ranked candidates by 1 percent low" />
''',
        '''                        <TextBlock Text="Candidate results (decision-grade medians)"
                                   Style="{StaticResource CaptionTextStyle}"
                                   AutomationProperties.Name="Decision-grade GPU affinity candidate results" />
''',
        "candidate results heading")
    write(xaml_path, xaml)

    for workflow in (
        ".github/workflows/audit-closure-device-mutations.yml",
        ".github/workflows/audit-closure-final.yml",
        ".github/workflows/audit-closure-tranche1.yml",
        ".github/workflows/audit-closure-usb.yml",
    ):
        path = ROOT / workflow
        if not path.exists():
            raise RuntimeError(f"obsolete workflow expected but missing: {workflow}")
        path.unlink()


def main() -> None:
    if len(sys.argv) != 2 or sys.argv[1] not in {"tests", "implementation"}:
        raise SystemExit("usage: measurement-audit-cleanup.py tests|implementation")
    if sys.argv[1] == "tests":
        add_regressions()
    else:
        implement()


if __name__ == "__main__":
    main()

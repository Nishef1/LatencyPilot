using System.Globalization;
using System.Reflection;
using LatencyPilot.App.Services;
using LatencyPilot.App.ViewModels;
using LatencyPilot.Benchmarking.Baselines;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;
using Microsoft.UI.Xaml;
using Serilog;

namespace LatencyPilot.App;

public sealed partial class MainWindow : Window
{
    private static readonly Serilog.ILogger Logger = Log.ForContext<MainWindow>();
    private static readonly TimeSpan ObservationDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BaselineInterWindowDelay = TimeSpan.FromMilliseconds(750);
    private static readonly BaselineQualityPolicy BaselinePolicy = new();
    private const int ObservationMaximumEvents = 200_000;
    private const int BaselineWindowCount = 5;
    private const string MeasurementContextGuidance =
        "Measurement context: for a diagnostic capture, keep the apps and workload that reproduce the issue open. " +
        "For a controlled idle baseline, close unnecessary apps. For before/after comparisons, keep the same apps, workload, power state and background activity on both sides.";

    private bool _observationServiceReady;
    private bool _initialLoadStarted;
    private string? _latestEvidenceJson;
    private string? _latestEvidenceSuggestedFileName;

    public MainWindow()
    {
        InitializeComponent();
        Title = "LatencyPilot";
        VersionText.Text = $"v{GetProductVersion()}";
        BaselineProgressBar.Maximum = BaselineWindowCount;
        ObservationQualityText.Text = MeasurementContextGuidance;
        InitializePremiumObservationUi();
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialLoadStarted)
        {
            return;
        }

        _initialLoadStarted = true;
        await Task.Yield();
        await Task.WhenAll(
            CaptureSystemInventoryAsync(),
            CaptureProcessorTopologyAsync(),
            CaptureDeviceInventoryAsync());
        await RefreshObservationServiceStatusAsync();
    }

    private async void RefreshServiceButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshObservationServiceStatusAsync();
    }

    private async Task RefreshObservationServiceStatusAsync()
    {
        CaptureObservationButton.IsEnabled = false;
        CaptureBaselineButton.IsEnabled = false;
        RefreshServiceButton.IsEnabled = false;
        ServiceStatusBadgeText.Text = "Checking service";
        ServiceStatusText.Text = "Checking the local observation service…";

        try
        {
            var status = await ObservationServiceClient.GetStatusAsync();
            if (!status.PrivilegedObservationHostImplemented || status.MutationAvailable)
            {
                Logger.Warning(
                    "Observation service contract mismatch. HostImplemented={HostImplemented}, MutationAvailable={MutationAvailable}.",
                    status.PrivilegedObservationHostImplemented,
                    status.MutationAvailable);
                _observationServiceReady = false;
                ServiceStatusBadgeText.Text = "Contract mismatch";
                ServiceStatusText.Text = "Service contract mismatch. Read-only kernel capture is disabled.";
                return;
            }

            _observationServiceReady = true;
            CaptureObservationButton.IsEnabled = true;
            CaptureBaselineButton.IsEnabled = true;
            ServiceStatusBadgeText.Text = "Service connected";
            ServiceStatusText.Text = "Connected to the privileged read-only observation service. Mutation remains disabled.";
        }
        catch (TimeoutException exception)
        {
            Logger.Warning(exception, "Observation service status request timed out.");
            SetServiceUnavailable("Observation service is not running or did not respond in time.");
        }
        catch (IOException exception)
        {
            Logger.Warning(exception, "Observation service status connection failed.");
            SetServiceUnavailable("Observation service connection failed.");
        }
        catch (InvalidDataException exception)
        {
            Logger.Error(exception, "Observation service returned an invalid status response.");
            SetServiceUnavailable("Observation service returned an invalid protocol response.");
        }
        catch (InvalidOperationException exception)
        {
            Logger.Warning(exception, "Observation service rejected the status request.");
            SetServiceUnavailable(exception.Message);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Unexpected failure while refreshing observation service status.");
            SetServiceUnavailable("Unexpected service error. See the diagnostics log for details.");
        }
        finally
        {
            RefreshServiceButton.IsEnabled = true;
        }
    }

    private async void CaptureObservationButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureObservationServiceReadyAsync())
        {
            return;
        }

        ClearExportEvidence("Capture in progress. Evidence export becomes available after completion.");
        SetObservationControlsBusy(true);
        KernelCaptureStatusText.Text = "Capturing DPC/ISR activity for 5 seconds…";
        ObservationQualityText.Text =
            "Capture in progress. Keep the apps/workload you are trying to diagnose open; they are part of the measurement context. No interpretation is made until capture completes.";

        try
        {
            var capture = await ObservationServiceClient.CaptureKernelLatencyAsync(
                ObservationDuration,
                ObservationMaximumEvents);

            RenderCapture(capture);
            TryPrepareObservationEvidence(capture);
        }
        catch (Exception exception)
        {
            HandleCaptureFailure(exception, "Kernel observation");
        }
        finally
        {
            SetObservationControlsBusy(false);
        }
    }

    private async void CaptureBaselineButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureObservationServiceReadyAsync())
        {
            return;
        }

        ClearExportEvidence("Baseline capture in progress. Export is prepared only after the capture sequence stops or completes.");
        SetObservationControlsBusy(true);
        BaselineProgressBar.Value = 0;
        BaselineVerdictText.Text = "Capturing";
        BaselineStatusText.Text = $"Preparing {BaselineWindowCount} repeated five-second windows…";
        BaselineMetricsText.Text = "Noise and drift will be computed after all required windows complete.";
        BaselineReasonsText.Text =
            "Keep the test context consistent across all five windows. For an idle baseline, close unnecessary apps; for a real-world baseline, keep the same workload active. No window is silently discarded.";
        BaselineWindowsList.ItemsSource = null;

        var windows = new List<BaselineWindowEvidence>(BaselineWindowCount);
        var captures = new List<KernelLatencyCaptureResponse>(BaselineWindowCount);

        try
        {
            for (var index = 1; index <= BaselineWindowCount; index++)
            {
                BaselineStatusText.Text = $"Capturing baseline window {index} of {BaselineWindowCount}…";
                var capture = await ObservationServiceClient.CaptureKernelLatencyAsync(
                    ObservationDuration,
                    ObservationMaximumEvents);

                captures.Add(capture);
                RenderCapture(capture);
                var integrityIssue = GetCaptureIntegrityIssue(capture);
                windows.Add(new BaselineWindowEvidence(
                    index,
                    capture.StartedAtUtc,
                    integrityIssue is null,
                    integrityIssue,
                    capture.Dpc.Count,
                    capture.Dpc.P99Microseconds,
                    capture.Isr.Count,
                    capture.Isr.P99Microseconds));

                BaselineProgressBar.Value = index;
                BaselineWindowsList.ItemsSource = CreateBaselineWindowRows(windows);

                if (index < BaselineWindowCount)
                {
                    await Task.Delay(BaselineInterWindowDelay);
                }
            }

            var quality = BaselineQualityAnalyzer.Analyze(windows, BaselinePolicy);
            RenderBaselineQuality(quality);
            TryPrepareBaselineEvidence(captures, windows, quality, isPartial: false);
        }
        catch (Exception exception)
        {
            HandleCaptureFailure(exception, "Repeated baseline capture");
            BaselineVerdictText.Text = "Inconclusive";
            BaselineStatusText.Text = $"Baseline capture stopped after {windows.Count} of {BaselineWindowCount} windows.";

            if (windows.Count > 0)
            {
                var partialQuality = BaselineQualityAnalyzer.Analyze(windows, BaselinePolicy);
                RenderBaselineQuality(partialQuality, preserveStatusText: true);
                TryPrepareBaselineEvidence(captures, windows, partialQuality, isPartial: true);
            }
            else
            {
                BaselineMetricsText.Text = "No baseline metric evidence was produced.";
                BaselineReasonsText.Text = "The repeated capture must complete before a baseline can be used for comparison.";
            }
        }
        finally
        {
            SetObservationControlsBusy(false);
        }
    }

    private async void ExportEvidenceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latestEvidenceJson is null || _latestEvidenceSuggestedFileName is null)
        {
            EvidenceExportStatusText.Text = "No completed observation or baseline evidence is available to export.";
            return;
        }

        ExportEvidenceButton.IsEnabled = false;
        try
        {
            var savedPath = await EvidenceExportService.SaveAsync(
                this,
                _latestEvidenceJson,
                _latestEvidenceSuggestedFileName);

            if (savedPath is not null)
            {
                EvidenceExportStatusText.Text = $"Evidence exported to {savedPath}";
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Evidence export failed.");
            EvidenceExportStatusText.Text = "Evidence export failed. The capture result remains valid; see the diagnostics log for export details.";
        }
        finally
        {
            ExportEvidenceButton.IsEnabled = _latestEvidenceJson is not null;
        }
    }

    private async Task<bool> EnsureObservationServiceReadyAsync()
    {
        if (_observationServiceReady)
        {
            return true;
        }

        await RefreshObservationServiceStatusAsync();
        return _observationServiceReady;
    }

    private void SetObservationControlsBusy(bool busy)
    {
        CaptureObservationButton.IsEnabled = !busy && _observationServiceReady;
        CaptureBaselineButton.IsEnabled = !busy && _observationServiceReady;
        RefreshServiceButton.IsEnabled = !busy;
        ExportEvidenceButton.IsEnabled = !busy && _latestEvidenceJson is not null;
    }

    private void RenderCapture(KernelLatencyCaptureResponse capture)
    {
        DpcCountText.Text = capture.Dpc.Count.ToString("N0", CultureInfo.InvariantCulture);
        IsrCountText.Text = capture.Isr.Count.ToString("N0", CultureInfo.InvariantCulture);
        DpcP99Text.Text = $"p99 {FormatMicroseconds(capture.Dpc.P99Microseconds)} · max {FormatMicroseconds(capture.Dpc.MaximumMicroseconds)}";
        DpcP999Text.Text = FormatMicroseconds(capture.Dpc.P999Microseconds);
        IsrP99Text.Text = $"p99 {FormatMicroseconds(capture.Isr.P99Microseconds)} · max {FormatMicroseconds(capture.Isr.MaximumMicroseconds)}";
        IsrP999Text.Text = FormatMicroseconds(capture.Isr.P999Microseconds);
        ObservedProcessorCountText.Text = capture.Processors.Count.ToString(CultureInfo.InvariantCulture);

        var totalAttributedEvents = capture.ResolvedModuleEventCount + capture.UnresolvedModuleEventCount;
        var resolvedPercent = totalAttributedEvents == 0
            ? 0d
            : capture.ResolvedModuleEventCount * 100d / totalAttributedEvents;
        ModuleCoverageBar.Value = Math.Clamp(resolvedPercent, 0d, 100d);

        var truncationSuffix = capture.ModuleContributorListTruncated || capture.UnresolvedRoutineListTruncated
            ? " Contributor lists reached protocol bounds."
            : string.Empty;

        ModuleAttributionCoverageText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{capture.ResolvedModuleEventCount:N0} resolved · {capture.UnresolvedModuleEventCount:N0} unresolved · {resolvedPercent:F1}% coverage.{truncationSuffix}");

        TopModulesList.ItemsSource = capture.Modules
            .Take(8)
            .Select(module => new ModuleObservationRow(
                module.ModuleName,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"DPC {module.Dpc.Count:N0} ({module.DpcThresholds.GuidanceExceedanceCount:N0} >{module.DpcThresholds.GuidanceThresholdMicroseconds:F0} µs) · ISR {module.Isr.Count:N0} ({module.IsrThresholds.GuidanceExceedanceCount:N0} >{module.IsrThresholds.GuidanceThresholdMicroseconds:F0} µs)"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"total {module.TotalDurationMicroseconds:F1} µs · p99.9 {FormatLargestP999(module.Dpc, module.Isr)} · max {FormatLargestMaximum(module.Dpc, module.Isr)}")))
            .ToArray();

        var topModule = capture.Modules.Count == 0 ? null : capture.Modules[0];
        TopModuleText.Text = topModule is null
            ? "No routine address was resolved to an authoritative image range."
            : $"Dominant resolved module by accumulated DPC/ISR duration: {topModule.ModuleName}";

        TopProcessorsList.ItemsSource = capture.Processors
            .OrderByDescending(processor => processor.Dpc.Count + processor.Isr.Count)
            .Take(8)
            .Select(processor => new ProcessorObservationRow(
                $"CPU {processor.ProcessorNumber}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{processor.Dpc.Count + processor.Isr.Count:N0} events · DPC {processor.Dpc.Count:N0} / ISR {processor.Isr.Count:N0}"),
                $"p99 {FormatLargestP99(processor)} · max {FormatLargestMaximum(processor.Dpc, processor.Isr)}"))
            .ToArray();

        var integrityIssue = GetCaptureIntegrityIssue(capture);
        if (integrityIssue is null)
        {
            KernelCaptureStatusText.Text = $"Observation complete in {capture.ActualDurationMilliseconds:F0} ms with no ETW loss detected.";
            ObservationQualityText.Text = $"Capture integrity looks clean. {FormatCaptureInterpretation(capture)} {MeasurementContextGuidance}";
        }
        else
        {
            KernelCaptureStatusText.Text = $"Observation completed with quality warning: {integrityIssue}";
            ObservationQualityText.Text = $"Treat this observation as incomplete evidence. {FormatCaptureInterpretation(capture)} {MeasurementContextGuidance}";
        }

        RenderPremiumCapture(capture);
    }

    private void RenderBaselineQuality(BaselineQualityResult quality, bool preserveStatusText = false)
    {
        BaselineVerdictText.Text = quality.IsValidForComparison ? "Valid" : "Inconclusive";
        if (!preserveStatusText)
        {
            BaselineStatusText.Text = quality.IsValidForComparison
                ? $"{quality.ValidCaptureWindowCount}/{quality.TotalWindowCount} clean windows passed {quality.MethodVersion}."
                : $"Baseline quality gate failed under {quality.MethodVersion}.";
        }

        BaselineMetricsText.Text =
            $"{FormatBaselineMetric(quality.DpcP99)}\n{FormatBaselineMetric(quality.IsrP99)}";
        BaselineReasonsText.Text = quality.Reasons.Count == 0
            ? "This baseline is stable enough for later comparisons. Valid means repeatable under this test context; it does not mean the latency values are automatically good. Keep the same workload and background-app state when comparing a candidate."
            : $"Inconclusive means the run was not stable or complete enough for a fair comparison; it does not by itself mean the system latency is bad. {string.Join(" ", quality.Reasons)}";
    }

    private static string FormatBaselineMetric(BaselineMetricQuality metric)
    {
        if (metric.MedianMicroseconds is null)
        {
            return $"{metric.MetricName}: unavailable across {metric.EligibleWindowCount} analyzable window(s).";
        }

        var noise = metric.RelativeNoiseFloor is null
            ? "—"
            : metric.RelativeNoiseFloor.Value.ToString("P1", CultureInfo.InvariantCulture);
        var drift = metric.RelativeDrift is null
            ? "—"
            : metric.RelativeDrift.Value.ToString("P1", CultureInfo.InvariantCulture);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{metric.MetricName}: median {metric.MedianMicroseconds:F1} µs · P10-P90 spread {noise} · drift {drift} · {metric.EligibleWindowCount} windows");
    }

    private static BaselineWindowRow[] CreateBaselineWindowRows(IEnumerable<BaselineWindowEvidence> windows) =>
        windows
            .Select(window => new BaselineWindowRow(
                $"Window {window.WindowNumber}",
                window.CaptureIntegrityValid ? "clean capture" : window.CaptureIntegrityIssue ?? "capture warning",
                $"DPC {window.DpcEventCount:N0} · p99 {FormatMicroseconds(window.DpcP99Microseconds)}",
                $"ISR {window.IsrEventCount:N0} · p99 {FormatMicroseconds(window.IsrP99Microseconds)}"))
            .ToArray();

    private static string? GetCaptureIntegrityIssue(KernelLatencyCaptureResponse capture)
    {
        var issues = new List<string>();

        if (capture.EventsLost < 0)
        {
            issues.Add("ETW loss count is unavailable.");
        }
        else if (capture.EventsLost > 0)
        {
            issues.Add($"ETW lost {capture.EventsLost:N0} event(s).");
        }

        if (capture.InvalidEventCount > 0)
        {
            issues.Add($"{capture.InvalidEventCount:N0} latency event(s) were invalid.");
        }

        if (capture.InvalidImageEventCount > 0)
        {
            issues.Add($"{capture.InvalidImageEventCount:N0} image event(s) were invalid.");
        }

        if (capture.EventLimitReached)
        {
            issues.Add("The event safety limit was reached.");
        }

        return issues.Count == 0 ? null : string.Join(" ", issues);
    }

    private void TryPrepareObservationEvidence(KernelLatencyCaptureResponse capture)
    {
        try
        {
            SetExportEvidence(
                EvidenceExportService.CreateObservationJson(GetProductVersion(), capture),
                EvidenceExportService.CreateSuggestedFileName("observation", capture.StartedAtUtc),
                "Full bounded observation aggregates are ready for JSON export.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Observation evidence preparation failed.");
            ClearExportEvidence("Observation completed, but evidence export preparation failed. See the diagnostics log for details.");
        }
    }

    private void TryPrepareBaselineEvidence(
        List<KernelLatencyCaptureResponse> captures,
        IReadOnlyList<BaselineWindowEvidence> windows,
        BaselineQualityResult quality,
        bool isPartial)
    {
        try
        {
            var evidenceType = isPartial ? "baseline-partial" : "baseline";
            SetExportEvidence(
                EvidenceExportService.CreateBaselineJson(GetProductVersion(), captures, windows, quality),
                EvidenceExportService.CreateSuggestedFileName(evidenceType, captures[0].StartedAtUtc),
                isPartial
                    ? "Partial baseline aggregates and quality reasons are ready for JSON export."
                    : "Full baseline aggregates, windows and quality reasons are ready for JSON export.");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Baseline evidence preparation failed.");
            ClearExportEvidence("Baseline result remains available, but evidence export preparation failed. See the diagnostics log for details.");
        }
    }

    private void SetExportEvidence(string json, string suggestedFileName, string status)
    {
        _latestEvidenceJson = json;
        _latestEvidenceSuggestedFileName = suggestedFileName;
        ExportEvidenceButton.IsEnabled = true;
        EvidenceExportStatusText.Text = status;
    }

    private void ClearExportEvidence(string status)
    {
        _latestEvidenceJson = null;
        _latestEvidenceSuggestedFileName = null;
        ExportEvidenceButton.IsEnabled = false;
        EvidenceExportStatusText.Text = status;
    }

    private void HandleCaptureFailure(Exception exception, string operationName)
    {
        ClearCaptureMetrics();

        switch (exception)
        {
            case TimeoutException:
                Logger.Warning(exception, "{OperationName} timed out.", operationName);
                SetServiceUnavailable("Observation service is not running or did not respond in time.");
                KernelCaptureStatusText.Text = $"{operationName} did not start or exceeded its deadline.";
                break;
            case IOException:
                Logger.Warning(exception, "{OperationName} pipe connection failed.", operationName);
                SetServiceUnavailable("Observation service connection failed.");
                KernelCaptureStatusText.Text = $"{operationName} did not complete.";
                break;
            case InvalidDataException:
                Logger.Error(exception, "{OperationName} returned an invalid protocol response.", operationName);
                KernelCaptureStatusText.Text = "Observation service returned an invalid protocol response.";
                break;
            case InvalidOperationException:
                Logger.Warning(exception, "{OperationName} request was rejected.", operationName);
                KernelCaptureStatusText.Text = exception.Message;
                break;
            default:
                Logger.Error(exception, "Unexpected {OperationName} failure.", operationName);
                KernelCaptureStatusText.Text = "Unexpected observation error. See the diagnostics log for details.";
                break;
        }
    }

    private void SetServiceUnavailable(string message)
    {
        _observationServiceReady = false;
        CaptureObservationButton.IsEnabled = false;
        CaptureBaselineButton.IsEnabled = false;
        ServiceStatusBadgeText.Text = "Service unavailable";
        ServiceStatusText.Text = message;
    }

    private void ClearCaptureMetrics()
    {
        DpcCountText.Text = "—";
        IsrCountText.Text = "—";
        DpcP99Text.Text = "—";
        DpcP999Text.Text = "—";
        IsrP99Text.Text = "—";
        IsrP999Text.Text = "—";
        ObservedProcessorCountText.Text = "—";
        ModuleAttributionCoverageText.Text = "—";
        ModuleCoverageBar.Value = 0;
        TopModuleText.Text = "No observation yet.";
        TopModulesList.ItemsSource = null;
        TopProcessorsList.ItemsSource = null;
        ObservationQualityText.Text = MeasurementContextGuidance;
        ClearPremiumCapture();
    }

    private static string GetProductVersion()
    {
        var assembly = typeof(MainWindow).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "unknown";
    }

    private static string FormatLargestP99(ProcessorLatencyDistribution processor) =>
        FormatLargestValue(processor.Dpc.P99Microseconds, processor.Isr.P99Microseconds);

    private static string FormatLargestP999(LatencyDistribution dpc, LatencyDistribution isr) =>
        FormatLargestValue(dpc.P999Microseconds, isr.P999Microseconds);

    private static string FormatLargestMaximum(LatencyDistribution dpc, LatencyDistribution isr) =>
        FormatLargestValue(dpc.MaximumMicroseconds, isr.MaximumMicroseconds);

    private static string FormatLargestValue(double? first, double? second)
    {
        if (first is null)
        {
            return FormatMicroseconds(second);
        }

        if (second is null)
        {
            return FormatMicroseconds(first);
        }

        return FormatMicroseconds(Math.Max(first.Value, second.Value));
    }

    private static string FormatCaptureInterpretation(KernelLatencyCaptureResponse capture)
    {
        var guidanceExceedances =
            capture.DpcThresholds.GuidanceExceedanceCount + capture.IsrThresholds.GuidanceExceedanceCount;
        var overOneMillisecond =
            capture.DpcThresholds.OverOneMillisecondCount + capture.IsrThresholds.OverOneMillisecondCount;
        var overThreeMilliseconds =
            capture.DpcThresholds.OverThreeMillisecondsCount + capture.IsrThresholds.OverThreeMillisecondsCount;
        var oneToThreeMilliseconds = Math.Max(0, overOneMillisecond - overThreeMilliseconds);

        string assessment;
        if (overThreeMilliseconds > 0)
        {
            assessment = string.Create(
                CultureInfo.InvariantCulture,
                $"Severe media-impact range observed: {overThreeMilliseconds:N0} DPC/ISR event(s) exceeded 3 ms. Microsoft's streaming-media assessment treats >3 ms as error-level in that media scenario. This is a strong investigation signal, not a universal system-fail verdict.");
        }
        else if (overOneMillisecond > 0)
        {
            assessment = string.Create(
                CultureInfo.InvariantCulture,
                $"Potential real-time impact range observed: {oneToThreeMilliseconds:N0} DPC/ISR event(s) were between 1 and 3 ms. Microsoft's streaming-media assessment warns on long-running DPC/ISR in this range.");
        }
        else if (guidanceExceedances > 0)
        {
            assessment = string.Create(
                CultureInfo.InvariantCulture,
                $"Driver guidance was exceeded, but no millisecond-scale spike was observed. {capture.DpcThresholds.GuidanceExceedanceCount:N0} DPC event(s) exceeded {capture.DpcThresholds.GuidanceThresholdMicroseconds:F0} µs and {capture.IsrThresholds.GuidanceExceedanceCount:N0} ISR event(s) exceeded {capture.IsrThresholds.GuidanceThresholdMicroseconds:F0} µs. This is diagnostic context, not proof of a user-visible problem.");
        }
        else
        {
            assessment = "Within driver guidance in this capture: no DPC exceeded 100 µs, no ISR exceeded 25 µs, and no millisecond-scale spike was observed. This is encouraging for this workload, but it is not proof that every workload is clean.";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{assessment} Max DPC {FormatMicroseconds(capture.Dpc.MaximumMicroseconds)}; max ISR {FormatMicroseconds(capture.Isr.MaximumMicroseconds)}; >1 ms DPC/ISR {capture.DpcThresholds.OverOneMillisecondCount:N0}/{capture.IsrThresholds.OverOneMillisecondCount:N0}; >3 ms {capture.DpcThresholds.OverThreeMillisecondsCount:N0}/{capture.IsrThresholds.OverThreeMillisecondsCount:N0}.");
    }

    private static string FormatMicroseconds(double? value) =>
        value is null
            ? "—"
            : value.Value.ToString("F1", CultureInfo.InvariantCulture) + " µs";

    private async Task CaptureSystemInventoryAsync()
    {
        try
        {
            var system = await Task.Run(() => SystemInventoryReader.Capture());
            OperatingSystemText.Text = system.OperatingSystem;
            OsArchitectureText.Text = system.OsArchitecture;
            ProcessArchitectureText.Text = system.ProcessArchitecture;
            ProcessAvailableProcessorCountText.Text = system.ProcessAvailableProcessorCount.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "System inventory capture failed.");
            OperatingSystemText.Text = "Unavailable";
            OsArchitectureText.Text = "—";
            ProcessArchitectureText.Text = "—";
            ProcessAvailableProcessorCountText.Text = "—";
            TopologyStatusText.Text = "System inventory failed. See the diagnostics log for details.";
        }
    }

    private async Task CaptureProcessorTopologyAsync()
    {
        try
        {
            var topology = await Task.Run(() => ProcessorTopologyReader.Capture());
            HardwareLogicalProcessorCountText.Text = topology.LogicalProcessorCount.ToString(CultureInfo.InvariantCulture);
            PhysicalCoreCountText.Text = topology.PhysicalCoreCount.ToString(CultureInfo.InvariantCulture);
            PackageCountText.Text = topology.Packages.Count.ToString(CultureInfo.InvariantCulture);
            ProcessorGroupCountText.Text = topology.ProcessorGroupCount.ToString(CultureInfo.InvariantCulture);
            SmtCoreCountText.Text = topology.SmtCoreCount.ToString(CultureInfo.InvariantCulture);
            TopologyStatusText.Text = "CPU topology captured through GetLogicalProcessorInformationEx.";
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Processor topology capture failed.");
            HardwareLogicalProcessorCountText.Text = "Unavailable";
            PhysicalCoreCountText.Text = "Unavailable";
            PackageCountText.Text = "Unavailable";
            ProcessorGroupCountText.Text = "Unavailable";
            SmtCoreCountText.Text = "Unavailable";
            TopologyStatusText.Text = "Topology capture failed. See the diagnostics log for details.";
        }
    }

    private async Task CaptureDeviceInventoryAsync()
    {
        try
        {
            var inventory = await Task.Run(() => DeviceInventoryReader.CapturePresentDevices());
            PresentDeviceCountText.Text = inventory.PresentDeviceCount.ToString(CultureInfo.InvariantCulture);
            DeviceInventoryStatusText.Text = "Present devices captured through SetupAPI using stable device instance IDs.";
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Device inventory capture failed.");
            PresentDeviceCountText.Text = "Unavailable";
            DeviceInventoryStatusText.Text = "Device inventory failed. See the diagnostics log for details.";
        }
    }
}

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LatencyPilot.Core.Benchmarking;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.System;
using LatencyPilot.Persistence;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LatencyPilot.App;

public sealed partial class MainWindow
{
    private bool _usbSubsystemRunning;

    private async Task RunUsbXhciWorkflowAsync()
    {
        if (_usbSubsystemRunning || _gateAValidationRunning || _measurementBusy || _manualDeviceAffinityBusy)
        {
            UsbEvidenceText.Text =
                "USB/xHCI is unavailable while another measurement or mutation session owns the hardware.";
            return;
        }

        _usbSubsystemRunning = true;
        UsbSubsystemButton.IsEnabled = false;
        NetworkSubsystemButton.IsEnabled = false;
        ManualDeviceAffinityButton.IsEnabled = false;
        SetObservationControlsBusy(_measurementBusy);
        if (_gateASourceAssessment is not null)
        {
            ApplyGateASourceAssessmentUi(_gateASourceAssessment);
        }
        UpdateScenarioSelectionEnabledState();

        try
        {
            UsbEvidenceText.Text = "Resolving the primary input route…";
            var inspection = await Task.Run(CaptureDeviceEvidenceInspection);
            var primaryRoute = await SelectPrimaryUsbRouteAsync(inspection.InputRoutes);
            if (primaryRoute is null)
            {
                UsbEvidenceText.Text = "USB/xHCI run cancelled or no exact primary USB mouse route is available.";
                return;
            }

            var controllerId = primaryRoute.UsbHostControllerInstanceId!;
            var unresolved = MutationJournalReadOnlyInspector.GetUnresolved(
                MutationJournal.GetDefaultDatabasePath());
            var controllerOwned = unresolved
                .Where(entry =>
                    string.Equals(
                        entry.TargetId,
                        controllerId,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (controllerOwned.Length > 1)
            {
                UsbEvidenceText.Text =
                    "USB/xHCI is blocked because more than one unresolved journal entry owns the routed controller. Recover the journal before continuing.";
                await ShowSimpleUsbMessageAsync(
                    "USB / xHCI recovery required",
                    UsbEvidenceText.Text);
                return;
            }

            if (controllerOwned.Length == 1)
            {
                var pending = controllerOwned[0];
                var pendingMask = TryGetPendingAffinityMask(pending);
                if (pending.State == MutationJournalState.ApplyRebootPending &&
                    pendingMask is { } resumableMask)
                {
                    var resume = await ConfirmUsbResumeAsync(
                        primaryRoute,
                        resumableMask);
                    if (resume)
                    {
                        UsbEvidenceText.Text =
                            $"Resuming journal-owned xHCI candidate {FormatMask(resumableMask)}…";
                        var resumed = await RunManualAffinityHelperAsync(
                            "Apply",
                            "Xhci",
                            controllerId,
                            resumableMask,
                            restartDeviceOnly: false);
                        await ShowUsbApplyResultAsync(resumed);
                    }

                    return;
                }

                UsbEvidenceText.Text =
                    $"USB/xHCI is blocked by journal-owned state {pending.State}. Use Interrupt Policy Lab to recover that exact state before a new automatic USB run.";
                await ShowSimpleUsbMessageAsync(
                    "USB / xHCI recovery required",
                    UsbEvidenceText.Text);
                return;
            }

            var mutationBlockedByOtherTarget = unresolved.Count != 0;
            var blockerSummary = mutationBlockedByOtherTarget
                ? string.Join(
                    Environment.NewLine,
                    unresolved.Take(4).Select(static entry =>
                        $"• {FormatMutationKind(entry.Kind)} · {entry.State} · {entry.TargetId}"))
                : null;

            var gpuReservation = mutationBlockedByOtherTarget
                ? null
                : await Task.Run(TryResolveCurrentVerifiedGpuReservation);
            UsbEvidenceText.Text = mutationBlockedByOtherTarget
                ? "Capturing USB/xHCI diagnostics. Another target still owns an unresolved mutation, so this run cannot Apply a new xHCI policy."
                : gpuReservation is null
                    ? "Capturing USB/xHCI interrupt headroom. No verified GPU reservation is available, so the result will be diagnostic-only."
                    : $"Capturing USB/xHCI interrupt headroom with GPU CPU {gpuReservation.Processor.Number} reserved…";

            var report = await RunUsbXhciReadinessHelperAsync(
                primaryRoute.RawInputDevice.PnPInstanceId!,
                gpuReservation?.Processor);
            await ShowUsbXhciReadinessResultAsync(
                report,
                gpuReservation,
                mutationBlockedByOtherTarget,
                blockerSummary);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            UsbEvidenceText.Text =
                "USB/xHCI run was cancelled at the Windows elevation prompt; no mutation was requested.";
        }
        catch (Exception exception) when (
            IsRecoverableDeviceEvidenceException(exception) ||
            exception is JsonException or NotSupportedException)
        {
            Logger.Error(exception, "Independent USB/xHCI workflow failed.");
            UsbEvidenceText.Text = $"USB/xHCI could not complete: {exception.Message}";
        }
        finally
        {
            _usbSubsystemRunning = false;
            UsbSubsystemButton.IsEnabled = true;
            NetworkSubsystemButton.IsEnabled = true;
            ManualDeviceAffinityButton.IsEnabled = true;
            SetObservationControlsBusy(_measurementBusy);
            UpdateScenarioSelectionEnabledState();
            if (_gateASourceAssessment is not null)
            {
                ApplyGateASourceAssessmentUi(_gateASourceAssessment);
            }
        }
    }

    private async Task<InputDeviceRouteSnapshot?> SelectPrimaryUsbRouteAsync(
        UserInputRouteInventory inputRoutes)
    {
        var candidates = inputRoutes.Routes
            .Where(static route =>
                route.RawInputDevice.Kind == RawInputDeviceKind.Mouse &&
                route.RawInputDevice.ResolutionStatus == RawInputRouteResolutionStatus.Available &&
                !string.IsNullOrWhiteSpace(route.RawInputDevice.PnPInstanceId) &&
                route.IsUsbBacked &&
                route.UsbPortRoute?.IsAvailable == true &&
                !string.IsNullOrWhiteSpace(route.UsbHostControllerInstanceId))
            .GroupBy(
                static route => route.RawInputDevice.PnPInstanceId!,
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(
                static route => route.PnPDisplayName ?? route.RawInputDevice.PnPInstanceId,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            await ShowSimpleUsbMessageAsync(
                "USB / xHCI not ready",
                "No mouse currently has one exact Raw Input → USB hub/port → xHCI route. LatencyPilot will not guess a controller.");
            return null;
        }

        if (candidates.Length == 1)
        {
            return candidates[0];
        }

        var picker = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 420d,
        };
        foreach (var route in candidates)
        {
            picker.Items.Add(new ComboBoxItem
            {
                Content = $"{route.PnPDisplayName ?? "Mouse"} · {route.RawInputDevice.PnPInstanceId}",
                Tag = route.RawInputDevice.PnPInstanceId,
            });
        }
        picker.SelectedIndex = 0;

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Select primary USB mouse",
            Content = new StackPanel
            {
                Spacing = 10d,
                Children =
                {
                    new TextBlock
                    {
                        Text = "USB/xHCI owns this selection independently. Choose the mouse you actively use so LatencyPilot can bind the run to one exact interrupt-owning controller.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    picker,
                },
            },
            PrimaryButtonText = "Use this mouse",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            picker.SelectedItem is not ComboBoxItem selected ||
            selected.Tag is not string selectedInstanceId)
        {
            return null;
        }

        return candidates.Single(route =>
            string.Equals(
                route.RawInputDevice.PnPInstanceId,
                selectedInstanceId,
                StringComparison.OrdinalIgnoreCase));
    }

    private VerifiedGpuReservation? TryResolveCurrentVerifiedGpuReservation()
    {
        var validationRoot = GetValidationDirectory();
        if (!Directory.Exists(validationRoot))
        {
            return null;
        }

        ProcessorTopologySnapshot topology;
        try
        {
            topology = ProcessorTopologyReader.Capture();
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidDataException or NotSupportedException)
        {
            Logger.Warning(exception, "USB/xHCI could not capture topology while resolving the current GPU reservation.");
            return null;
        }

        foreach (var directory in Directory
                     .EnumerateDirectories(validationRoot, "gpu-auto-affinity-*", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(static path => Directory.GetLastWriteTimeUtc(path)))
        {
            var reportPath = Path.Combine(directory, "gpu-auto-affinity-report.json");
            if (!File.Exists(reportPath))
            {
                continue;
            }

            try
            {
                var report = JsonSerializer.Deserialize<GpuAutoAffinityReport>(
                    File.ReadAllText(reportPath),
                    GateAJsonOptions);
                if (report is null ||
                    !string.Equals(report.Schema, GpuAutoAffinityReport.SchemaId, StringComparison.Ordinal) ||
                    report.SearchScope != GpuAutoAffinitySearchScope.Full ||
                    report.FinalProcessor is not { } processor ||
                    processor.Group != 0 ||
                    !report.FinalStateVerified ||
                    !report.GateAClosureEligible ||
                    report.OriginalStateRestored ||
                    !string.Equals(
                        report.FinalRecommendation,
                        "KeepCandidate",
                        StringComparison.Ordinal) ||
                    report.FinalStoredState is null)
                {
                    continue;
                }

                var candidate = GpuInterruptAffinityCandidate.Create(topology, processor);
                var currentGpuState = GpuInterruptAffinityPolicyStore.Capture(
                    report.FinalStoredState.DeviceInstanceId);
                if (!string.Equals(
                        currentGpuState.DriverVersion,
                        report.FinalStoredState.DriverVersion,
                        StringComparison.OrdinalIgnoreCase) ||
                    !GpuInterruptAffinityPolicyStore.IsCandidateStored(
                        report.FinalStoredState.DeviceInstanceId,
                        candidate))
                {
                    continue;
                }

                return new VerifiedGpuReservation(
                    processor,
                    report.FinalStoredState.DeviceInstanceId,
                    reportPath,
                    report.EndedAtUtc);
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                JsonException or
                InvalidOperationException or
                NotSupportedException or
                ArgumentException or
                System.Security.SecurityException)
            {
                Logger.Debug(
                    exception,
                    "Ignoring unusable historical GPU report {GpuReportPath} while resolving USB reservation.",
                    reportPath);
            }
        }

        return null;
    }

    private async Task<UsbXhciReadinessUiReport> RunUsbXhciReadinessHelperAsync(
        string primaryInputDeviceInstanceId,
        LogicalProcessorId? reservedGpuProcessor)
    {
        if (string.IsNullOrWhiteSpace(_gateARepositoryRoot))
        {
            throw new InvalidOperationException(
                "USB/xHCI automatic readiness is available only from a development checkout until the product mutation boundary is armed.");
        }

        var helperProject = Path.Combine(
            _gateARepositoryRoot,
            "tools",
            "LatencyPilot.GateAValidation",
            "LatencyPilot.GateAValidation.csproj");
        if (!File.Exists(helperProject))
        {
            throw new FileNotFoundException(
                "The elevated development helper project was not found.",
                helperProject);
        }

        var outputDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LatencyPilot",
            "UsbXhci");
        Directory.CreateDirectory(outputDirectory);
        var reportPath = Path.Combine(
            outputDirectory,
            $"usb-xhci-readiness-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveDotnetExecutable(),
            WorkingDirectory = _gateARepositoryRoot,
            UseShellExecute = true,
            Verb = "runas",
        };
        foreach (var argument in new[]
                 {
                     "run",
                     "--project", helperProject,
                     "--configuration", "Release",
                     "--",
                     "--usb-xhci-readiness",
                     "--primary-input", primaryInputDeviceInstanceId,
                     "--output", reportPath,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (reservedGpuProcessor is { } gpuProcessor)
        {
            startInfo.ArgumentList.Add("--gpu-processor");
            startInfo.ArgumentList.Add(
                gpuProcessor.Number.ToString(CultureInfo.InvariantCulture));
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "The elevated USB/xHCI readiness helper could not be started.");
        await process.WaitForExitAsync();

        if (!File.Exists(reportPath))
        {
            throw new InvalidOperationException(
                $"USB/xHCI readiness helper exited with code {process.ExitCode.ToString(CultureInfo.InvariantCulture)} without producing its report.");
        }

        var report = JsonSerializer.Deserialize<UsbXhciReadinessUiReport>(
            await File.ReadAllTextAsync(reportPath),
            GateAJsonOptions)
            ?? throw new InvalidDataException(
                "USB/xHCI readiness helper returned an empty report.");
        if (!string.Equals(
                report.Schema,
                "latencypilot-usb-xhci-readiness-v1",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported USB/xHCI readiness schema '{report.Schema}'.");
        }

        return report;
    }

    private async Task ShowUsbXhciReadinessResultAsync(
        UsbXhciReadinessUiReport report,
        VerifiedGpuReservation? gpuReservation,
        bool mutationBlockedByOtherTarget,
        string? mutationBlockerSummary)
    {
        var hasCandidate =
            report.ControllerInstanceId is not null &&
            report.Processor is not null;
        var readyToApply =
            !mutationBlockedByOtherTarget &&
            string.Equals(report.Status, "Ready", StringComparison.Ordinal) &&
            hasCandidate &&
            gpuReservation is not null;

        var content = new StackPanel { Spacing = 8d };
        content.Children.Add(new TextBlock
        {
            Text = report.Status,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeBrush(
                readyToApply
                    ? "SemanticGoodBrush"
                    : string.Equals(report.Status, "NotReady", StringComparison.Ordinal)
                        ? "SemanticFailureBrush"
                        : "SemanticAttentionBrush"),
        });
        content.Children.Add(CreateMutedText(report.Reason));

        if (hasCandidate)
        {
            content.Children.Add(CreateEvidenceLine(
                "Controller",
                report.ControllerInstanceId!));
            content.Children.Add(CreateEvidenceLine(
                "Recommended CPU",
                $"CPU {report.Processor!.Value.Number}"));
            content.Children.Add(CreateEvidenceLine(
                "Observed DPC + ISR duration",
                report.TotalInterruptDurationMicroseconds is { } total
                    ? $"{total:F1} us"
                    : "—"));
            content.Children.Add(CreateEvidenceLine(
                "Interrupt p99 tail",
                report.InterruptTailP99Microseconds is { } p99
                    ? $"{p99:F1} us"
                    : "—"));
        }

        content.Children.Add(CreateEvidenceLine(
            "GPU reservation",
            gpuReservation is null
                ? "Unavailable · diagnostic-only ranking"
                : $"CPU {gpuReservation.Processor.Number} · latest verified GPU Keep still matches stored policy"));

        if (mutationBlockedByOtherTarget)
        {
            content.Children.Add(CreateEvidenceLine(
                "Mutation gate",
                "Blocked for Apply · another target has unresolved journal ownership"));
            if (!string.IsNullOrWhiteSpace(mutationBlockerSummary))
            {
                content.Children.Add(CreateMutedText(
                    $"USB diagnostics are still valid, but xHCI Apply is disabled until this recovery is closed:{Environment.NewLine}{mutationBlockerSummary}"));
            }
        }

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "USB / xHCI result",
            Content = content,
            PrimaryButtonText = readyToApply ? "Apply & verify xHCI" : null,
            CloseButtonText = "Close",
            DefaultButton = readyToApply
                ? ContentDialogButton.Primary
                : ContentDialogButton.Close,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary ||
            !readyToApply ||
            report.ControllerInstanceId is null ||
            report.Processor is not { } processor)
        {
            UsbEvidenceText.Text = report.Reason;
            return;
        }

        var confirm = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = $"Apply xHCI to CPU {processor.Number}?",
            Content = new TextBlock
            {
                Text = "LatencyPilot will journal the exact original controller policy, apply only the recommended xHCI affinity, restart/activate the controller when Windows permits it, then require translated-allocation plus controller-attributed ISR verification. Keep moving the selected USB mouse during the verification window. Any contradictory readable evidence triggers exact rollback.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Apply & verify",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            UsbEvidenceText.Text = "USB/xHCI recommendation was not applied.";
            return;
        }

        var mask = 1UL << processor.Number;
        UsbEvidenceText.Text =
            $"Applying CPU {processor.Number} to {report.ControllerInstanceId}; keep using the selected USB mouse during runtime verification…";
        var applyReport = await RunManualAffinityHelperAsync(
            "Apply",
            "Xhci",
            report.ControllerInstanceId,
            mask,
            restartDeviceOnly: false);
        await ShowUsbApplyResultAsync(applyReport);
    }

    private async Task<bool> ConfirmUsbResumeAsync(
        InputDeviceRouteSnapshot primaryRoute,
        ulong pendingMask)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Resume pending USB / xHCI verification?",
            Content = new TextBlock
            {
                Text = $"The selected mouse routes to {primaryRoute.UsbHostControllerInstanceId}. LatencyPilot already owns a reboot-pending xHCI candidate {FormatMask(pendingMask)} for that controller. Resume that exact candidate instead of planning a new one.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Resume & verify",
            CloseButtonText = "Not now",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task ShowUsbApplyResultAsync(ManualAffinityHelperReport report)
    {
        UsbEvidenceText.Text = report.Message;
        var content = new StackPanel { Spacing = 8d };
        content.Children.Add(CreateEvidenceLine("Status", report.Status));
        content.Children.Add(CreateMutedText(report.Message));
        if (!string.IsNullOrWhiteSpace(report.Verification))
        {
            content.Children.Add(CreateEvidenceLine(
                "Verification",
                report.Verification));
        }

        await ShowFocusedDeviceEvidenceDialogAsync(
            report.RestartRequired
                ? "USB / xHCI · reboot required"
                : report.Succeeded
                    ? "USB / xHCI · verified"
                    : "USB / xHCI · not kept",
            content);
    }

    private static string FormatMutationKind(string kind) =>
        kind switch
        {
            "gpu-interrupt-affinity" => "GPU interrupt affinity",
            "xhci-interrupt-affinity" => "USB/xHCI interrupt affinity",
            "device-interrupt-affinity" => "Device interrupt affinity",
            "device-msi-enable" => "Device MSI policy",
            _ => kind,
        };

    private async Task ShowSimpleUsbMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private sealed record VerifiedGpuReservation(
        LogicalProcessorId Processor,
        string DeviceInstanceId,
        string ReportPath,
        DateTimeOffset VerifiedAtUtc);

    private sealed record UsbXhciReadinessUiReport(
        string Schema,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc,
        string Status,
        string PrimaryInputDeviceInstanceId,
        string? ControllerInstanceId,
        LogicalProcessorId? Processor,
        LogicalProcessorId? ReservedGpuProcessor,
        double? TotalInterruptDurationMicroseconds,
        double? InterruptTailP99Microseconds,
        int? DpcCount,
        int? IsrCount,
        bool CaptureValid,
        int? EventsLost,
        int? InvalidEventCount,
        bool? EventLimitReached,
        string Reason);
}

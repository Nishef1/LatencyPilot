using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
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

            UsbEvidenceText.Text = mutationBlockedByOtherTarget
                ? "Capturing USB/xHCI benchmark evidence. Existing fixed CPU policies are reserved automatically; Apply stays disabled until the unrelated recovery closes."
                : "Capturing USB/xHCI interrupt headroom. CPUs already fixed by explicit device affinity policies are excluded automatically.";

            var report = await RunUsbXhciReadinessHelperAsync(
                primaryRoute.RawInputDevice.PnPInstanceId!);
            await ShowUsbXhciReadinessResultAsync(
                report,
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

    private async Task<UsbXhciReadinessUiReport> RunUsbXhciReadinessHelperAsync(
        string primaryInputDeviceInstanceId)
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
                "latencypilot-usb-xhci-readiness-v2",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported USB/xHCI readiness schema '{report.Schema}'.");
        }

        return report;
    }

    private async Task ShowUsbXhciReadinessResultAsync(
        UsbXhciReadinessUiReport report,
        bool mutationBlockedByOtherTarget,
        string? mutationBlockerSummary)
    {
        var hasCandidate =
            report.ControllerInstanceId is not null &&
            report.Processor is not null;
        var benchmarkReady =
            string.Equals(report.Status, "Ready", StringComparison.Ordinal) &&
            hasCandidate;
        var readyToApply =
            !mutationBlockedByOtherTarget &&
            report.ApplyEligible &&
            benchmarkReady;
        var headline = !benchmarkReady
            ? "Benchmark not ready"
            : readyToApply
                ? "Benchmark ready · apply ready"
                : "Benchmark ready · apply gated";

        UsbEvidenceText.Text = benchmarkReady
            ? readyToApply
                ? $"USB/xHCI benchmark ready · CPU {report.Processor!.Value.Number} · apply ready."
                : $"USB/xHCI benchmark ready · CPU {report.Processor!.Value.Number} · apply gated."
            : "USB/xHCI benchmark not ready.";

        var content = new StackPanel { Spacing = 10d };
        content.Children.Add(new TextBlock
        {
            Text = headline,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = ThemeBrush(
                readyToApply
                    ? "SemanticGoodBrush"
                    : benchmarkReady
                        ? "SemanticAttentionBrush"
                        : "SemanticFailureBrush"),
        });

        if (hasCandidate)
        {
            var candidateSummary = report.WinningPhysicalCoreIndex is { } coreIndex
                ? $"CPU {report.Processor!.Value.Number} · physical core {coreIndex}"
                : $"CPU {report.Processor!.Value.Number}";
            content.Children.Add(CreateEvidenceLine("Recommended", candidateSummary));
            content.Children.Add(CreateEvidenceLine(
                "Selection stability",
                report.StabilityWindowCount > 0
                    ? $"{report.WinningCoreVotes}/{report.StabilityWindowCount} windows"
                    : "—"));
            content.Children.Add(CreateEvidenceLine(
                "Median DPC + ISR",
                report.TotalInterruptDurationMicroseconds is { } total
                    ? $"{total:F1} us"
                    : "—"));
            content.Children.Add(CreateEvidenceLine(
                "Median interrupt p99",
                report.InterruptTailP99Microseconds is { } p99
                    ? $"{p99:F1} us"
                    : "—"));
        }

        content.Children.Add(CreateEvidenceLine(
            "Reserved CPUs",
            report.ReservedProcessors.Count == 0
                ? "None"
                : string.Join(", ", report.ReservedProcessors.Select(static processor => $"CPU {processor.Number}"))));

        var verificationSummary = !report.ApplyEligible
            ? "Apply gated"
            : report.ControllerSpecificAttributionAvailable
                ? "Target allocation + controller ETW"
                : "Target allocation";
        content.Children.Add(CreateEvidenceLine("Verification", verificationSummary));

        if (mutationBlockedByOtherTarget)
        {
            content.Children.Add(CreateEvidenceLine(
                "Mutation gate",
                "Blocked · another target has unresolved journal ownership"));
        }

        var technical = new StackPanel { Spacing = 7d };
        technical.Children.Add(CreateEvidenceLine(
            "Controller",
            report.ControllerInstanceId ?? "Unavailable"));
        technical.Children.Add(CreateEvidenceLine(
            "Verification mode",
            report.VerificationMode));
        technical.Children.Add(CreateMutedText(report.ApplyEligibilityReason));
        technical.Children.Add(CreateMutedText(report.Reason));

        if (report.Reservations.Count != 0)
        {
            technical.Children.Add(CreateMutedText(
                "Reserved by current explicit device policies:" + Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    report.Reservations.Select(static reservation =>
                        $"• {reservation.DisplayName} · mask 0x{reservation.AffinityMask:X}"))));
        }

        if (mutationBlockedByOtherTarget &&
            !string.IsNullOrWhiteSpace(mutationBlockerSummary))
        {
            technical.Children.Add(CreateMutedText(
                "Unresolved mutation ownership:" + Environment.NewLine +
                mutationBlockerSummary));
        }

        content.Children.Add(new Expander
        {
            Header = "Technical details",
            Content = technical,
            IsExpanded = !benchmarkReady,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        });

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
            UsbEvidenceText.Text = benchmarkReady
                ? readyToApply
                    ? $"USB/xHCI benchmark ready · CPU {report.Processor!.Value.Number} · apply ready."
                    : $"USB/xHCI benchmark ready · CPU {report.Processor!.Value.Number} · apply gated."
                : "USB/xHCI benchmark not ready.";
            return;
        }

        var verificationCopy = report.ControllerSpecificAttributionAvailable
            ? "After restart, LatencyPilot will require the target controller's translated allocation to match the requested CPU and will also use controller-specific ETW as an independent runtime check."
            : "After restart, LatencyPilot will require the target controller's own translated allocation to match the requested CPU. Controller-specific ETW attribution is unavailable on this hardware, so LatencyPilot will not claim per-controller ISR proof unless that stronger evidence becomes available.";
        var confirm = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = $"Apply xHCI to CPU {processor.Number}?",
            Content = new TextBlock
            {
                Text =
                    $"LatencyPilot will journal the exact original xHCI policy before changing anything. {verificationCopy} " +
                    "If the authoritative target allocation does not match, or controller-specific ETW produces contradictory evidence, the exact original state is restored. Keep using the selected USB mouse during verification.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Apply & verify",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            UsbEvidenceText.Text =
                $"USB/xHCI benchmark ready · CPU {processor.Number} · not applied.";
            return;
        }

        var mask = 1UL << processor.Number;
        UsbEvidenceText.Text =
            $"Applying CPU {processor.Number} to the routed xHCI controller · verification in progress…";
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
        var allocationVerified = report.Status is
            "AppliedAllocationVerified" or
            "AlreadyConfiguredAllocationVerified";
        var runtimeVerified = report.Status is
            "AppliedAndKept" or
            "AlreadyConfigured";
        var summary = report.RestartRequired
            ? "USB/xHCI requires a reboot before verification can finish."
            : runtimeVerified
                ? "USB/xHCI applied · target allocation and controller ETW verified."
                : allocationVerified
                    ? "USB/xHCI applied · target allocation verified."
                    : report.Succeeded
                        ? "USB/xHCI operation completed."
                        : "USB/xHCI change was not kept.";

        UsbEvidenceText.Text = summary;

        var content = new StackPanel { Spacing = 8d };
        content.Children.Add(CreateEvidenceLine("Status", report.Status));
        content.Children.Add(CreateMutedText(summary));
        if (!string.IsNullOrWhiteSpace(report.Verification))
        {
            var details = new StackPanel { Spacing = 7d };
            details.Children.Add(CreateMutedText(report.Message));
            details.Children.Add(CreateMutedText(report.Verification));
            content.Children.Add(new Expander
            {
                Header = "Verification details",
                Content = details,
                IsExpanded = !report.Succeeded,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            });
        }
        else if (!string.IsNullOrWhiteSpace(report.Message))
        {
            content.Children.Add(CreateMutedText(report.Message));
        }

        await ShowFocusedDeviceEvidenceDialogAsync(
            report.RestartRequired
                ? "USB / xHCI · reboot required"
                : runtimeVerified
                    ? "USB / xHCI · runtime verified"
                    : allocationVerified
                        ? "USB / xHCI · allocation verified"
                        : report.Succeeded
                            ? "USB / xHCI · complete"
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

    private sealed record UsbXhciReservationUiReport(
        string DeviceInstanceId,
        string DisplayName,
        ulong AffinityMask,
        IReadOnlyList<LogicalProcessorId> Processors);

    private sealed record UsbXhciReadinessUiReport(
        string Schema,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc,
        string Status,
        string PrimaryInputDeviceInstanceId,
        string? ControllerInstanceId,
        LogicalProcessorId? Processor,
        IReadOnlyList<LogicalProcessorId> ReservedProcessors,
        IReadOnlyList<UsbXhciReservationUiReport> Reservations,
        bool ApplyEligible,
        string ApplyEligibilityReason,
        bool ControllerSpecificAttributionAvailable,
        string VerificationMode,
        int? WinningPhysicalCoreIndex,
        int StabilityWindowCount,
        int WinningCoreVotes,
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

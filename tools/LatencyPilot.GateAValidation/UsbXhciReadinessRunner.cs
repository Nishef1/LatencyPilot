using System.ComponentModel;
using System.Globalization;
using System.Security.Principal;
using System.Text.Json;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.Etw;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Protocol;

namespace LatencyPilot.GateAValidation;

internal static class UsbXhciReadinessRunner
{
    internal const string ModeFlag = "--usb-xhci-readiness";
    private const int CaptureWindowCount = 3;
    private static readonly TimeSpan CaptureWindowDuration = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    internal static async Task<int> RunAsync(string[] args)
    {
        UsbXhciReadinessOptions? options = null;
        UsbXhciReadinessReport report;
        try
        {
            options = UsbXhciReadinessOptions.Parse(args);
            EnsureAdministrator();

            var startedAtUtc = DateTimeOffset.UtcNow;
            var topology = ProcessorTopologyReader.Capture();
            if (topology.ProcessorGroupCount != 1)
            {
                throw new NotSupportedException(
                    "USB/xHCI readiness currently requires one Windows processor group.");
            }

            var inventory = DeviceInventoryReader.CapturePresentDevices();
            var routes = InputDeviceRouteReader.Capture(inventory);
            var routedControllerId = TryResolvePrimaryControllerId(
                routes,
                options.PrimaryInputDeviceInstanceId);

            var reservations = InterruptCpuReservationPlanner.Capture(
                inventory,
                routedControllerId);

            var captures = new KernelLatencyCaptureResult[CaptureWindowCount];
            for (var window = 0; window < captures.Length; window++)
            {
                captures[window] = KernelLatencyCapture.Capture(
                    new KernelLatencyCaptureOptions(
                        CaptureWindowDuration,
                        ObservationProtocol.MaximumCaptureEvents));
            }

            var recommendation = UsbAffinityRecommendationPlanner.CreateWithReservations(
                topology,
                captures,
                routes,
                options.PrimaryInputDeviceInstanceId,
                reservations.Processors);

            XhciInterruptVerificationPreflight applyPreflight;
            if (recommendation.ControllerInstanceId is { } controllerId &&
                recommendation.Processor is { } processor)
            {
                applyPreflight = XhciInterruptRuntimePlacementVerifier.AssessApplyPreflight(
                    controllerId,
                    inventory.Devices,
                    new DeviceInterruptAffinityCandidate(
                        processor.Group,
                        processor.Number,
                        1UL << processor.Number));
            }
            else
            {
                applyPreflight = new(
                    false,
                    "USB/xHCI Apply is unavailable because the benchmark did not produce one exact controller/CPU candidate.");
            }

            var evidence = recommendation.CpuEvidence;
            report = new UsbXhciReadinessReport(
                Schema: "latencypilot-usb-xhci-readiness-v2",
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                Status: recommendation.Status.ToString(),
                PrimaryInputDeviceInstanceId: options.PrimaryInputDeviceInstanceId,
                ControllerInstanceId: recommendation.ControllerInstanceId,
                Processor: recommendation.Processor,
                ReservedProcessors: reservations.Processors,
                Reservations: reservations.Reservations
                    .Select(static reservation => new UsbXhciReservationReport(
                        reservation.DeviceInstanceId,
                        reservation.DisplayName,
                        reservation.AffinityMask,
                        reservation.Processors))
                    .ToArray(),
                ApplyEligible: applyPreflight.CanAttemptApply,
                ApplyEligibilityReason: applyPreflight.Reason,
                ControllerSpecificAttributionAvailable: applyPreflight.ControllerSpecificAttributionAvailable,
                VerificationMode: applyPreflight.VerificationMode,
                WinningPhysicalCoreIndex: recommendation.WinningPhysicalCoreIndex,
                StabilityWindowCount: recommendation.StabilityWindowCount,
                WinningCoreVotes: recommendation.WinningCoreVotes,
                TotalInterruptDurationMicroseconds: evidence?.TotalInterruptDurationMicroseconds,
                InterruptTailP99Microseconds: evidence?.InterruptTailP99Microseconds,
                DpcCount: evidence?.DpcCount,
                IsrCount: evidence?.IsrCount,
                CaptureValid: captures.All(static capture => capture.IsValid),
                EventsLost: captures.Sum(static capture => capture.EventsLost),
                InvalidEventCount: captures.Sum(static capture => capture.InvalidEventCount),
                EventLimitReached: captures.Any(static capture => capture.EventLimitReached),
                Reason: recommendation.Reason);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            report = new UsbXhciReadinessReport(
                Schema: "latencypilot-usb-xhci-readiness-v2",
                StartedAtUtc: DateTimeOffset.UtcNow,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                Status: UsbAffinityRecommendationStatus.NotReady.ToString(),
                PrimaryInputDeviceInstanceId: options?.PrimaryInputDeviceInstanceId ?? string.Empty,
                ControllerInstanceId: null,
                Processor: null,
                ReservedProcessors: [],
                Reservations: [],
                ApplyEligible: false,
                ApplyEligibilityReason: "USB/xHCI Apply is unavailable because readiness capture failed.",
                ControllerSpecificAttributionAvailable: false,
                VerificationMode: "Unavailable",
                WinningPhysicalCoreIndex: null,
                StabilityWindowCount: 0,
                WinningCoreVotes: 0,
                TotalInterruptDurationMicroseconds: null,
                InterruptTailP99Microseconds: null,
                DpcCount: null,
                IsrCount: null,
                CaptureValid: false,
                EventsLost: null,
                InvalidEventCount: null,
                EventLimitReached: null,
                Reason: $"{exception.GetType().Name}: {exception.Message}");
        }

        if (options is null)
        {
            Console.Error.WriteLine(report.Reason);
            return 2;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)
            ?? throw new InvalidOperationException("USB/xHCI readiness output path has no parent directory."));
        await File.WriteAllTextAsync(
            options.OutputPath,
            JsonSerializer.Serialize(report, JsonOptions));

        Console.WriteLine($"usb-xhci-status={report.Status}");
        Console.WriteLine($"usb-xhci-controller={report.ControllerInstanceId ?? "unavailable"}");
        Console.WriteLine($"usb-xhci-processor={report.Processor?.Number.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}");
        Console.WriteLine($"usb-xhci-reservations={report.ReservedProcessors.Count.ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"usb-xhci-apply-eligible={report.ApplyEligible}");
        Console.WriteLine($"usb-xhci-verification-mode={report.VerificationMode}");
        Console.WriteLine($"usb-xhci-core-votes={report.WinningCoreVotes}/{report.StabilityWindowCount}");
        Console.WriteLine($"usb-xhci-report={options.OutputPath}");

        return string.Equals(report.Status, UsbAffinityRecommendationStatus.NotReady.ToString(), StringComparison.Ordinal)
            ? 3
            : 0;
    }

    private static string? TryResolvePrimaryControllerId(
        UserInputRouteInventory routes,
        string primaryInputDeviceInstanceId)
    {
        var controllers = routes.Routes
            .Where(route =>
                route.RawInputDevice.Kind == RawInputDeviceKind.Mouse &&
                route.RawInputDevice.ResolutionStatus == RawInputRouteResolutionStatus.Available &&
                string.Equals(
                    route.RawInputDevice.PnPInstanceId,
                    primaryInputDeviceInstanceId,
                    StringComparison.OrdinalIgnoreCase) &&
                route.UsbPortRoute?.IsAvailable == true &&
                !string.IsNullOrWhiteSpace(route.UsbHostControllerInstanceId) &&
                string.Equals(
                    route.UsbPortRoute.Port?.HostControllerInstanceId,
                    route.UsbHostControllerInstanceId,
                    StringComparison.OrdinalIgnoreCase))
            .Select(static route => route.UsbHostControllerInstanceId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return controllers.Length == 1 ? controllers[0] : null;
    }

    private static void EnsureAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "USB/xHCI readiness is supported only on Windows.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new UnauthorizedAccessException(
                "USB/xHCI readiness requires the elevated development helper for kernel ETW capture.");
        }
    }

    private sealed record UsbXhciReadinessOptions(
        string PrimaryInputDeviceInstanceId,
        string OutputPath)
    {
        internal static UsbXhciReadinessOptions Parse(string[] args)
        {
            ArgumentNullException.ThrowIfNull(args);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index++)
            {
                var token = args[index];
                if (string.Equals(token, ModeFlag, StringComparison.Ordinal))
                {
                    continue;
                }

                if (token is not ("--primary-input" or "--output"))
                {
                    throw new ArgumentException(
                        $"Unknown USB/xHCI readiness option '{token}'.");
                }

                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    throw new ArgumentException(
                        $"USB/xHCI readiness option '{token}' requires a value.");
                }

                if (!values.TryAdd(token, args[++index]))
                {
                    throw new ArgumentException(
                        $"Duplicate USB/xHCI readiness option '{token}'.");
                }
            }

            string Required(string key) =>
                values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                    ? value.Trim()
                    : throw new ArgumentException(
                        $"Required USB/xHCI readiness option '{key}' is missing.");

            return new UsbXhciReadinessOptions(
                Required("--primary-input"),
                Path.GetFullPath(Required("--output")));
        }
    }
}

internal sealed record UsbXhciReservationReport(
    string DeviceInstanceId,
    string DisplayName,
    ulong AffinityMask,
    IReadOnlyList<LogicalProcessorId> Processors);

internal sealed record UsbXhciReadinessReport(
    string Schema,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string Status,
    string PrimaryInputDeviceInstanceId,
    string? ControllerInstanceId,
    LogicalProcessorId? Processor,
    IReadOnlyList<LogicalProcessorId> ReservedProcessors,
    IReadOnlyList<UsbXhciReservationReport> Reservations,
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

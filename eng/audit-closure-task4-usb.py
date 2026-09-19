from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

def write(path: str, text: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8", newline="\n")

def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")

def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    if text.count(old) != 1:
        raise RuntimeError(f"{path}: expected one anchor, found {text.count(old)}")
    write(path, text.replace(old, new, 1))

def apply_tests() -> None:
    write("tests/LatencyPilot.CriticalTests/UsbOptimizationTests.cs", r'''using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class UsbOptimizationTests
{
    [TestMethod]
    public void XhciReadinessRequiresPrimaryIdentityCompositeRouteQualityAndUnambiguousAttribution()
    {
        var controller = new PnPDeviceSnapshot(
            "PCI\\VEN_TEST&DEV_XHCI", Guid.NewGuid(), "Test xHCI Controller", "Test Vendor", "PCI", "USBXHCI",
            new DriverMetadataSnapshot("1.0", "Test Vendor", "usbxhci.inf"),
            InterruptConfigurationSnapshot.Available(null, null, null, null), InterruptResourceSnapshot.Available([]));
        var raw = new RawInputDeviceSnapshot(
            RawInputDeviceKind.Mouse, "\\\\?\\HID#VID_TEST", "HID\\VID_TEST", 1, 2, 1, null, null,
            RawInputRouteResolutionStatus.Available, null, null);
        var port = new UsbHubPortSnapshot(
            "\\\\?\\USB#ROOT_HUB30#TEST", "USB\\ROOT_HUB30\\TEST", controller.InstanceId, 3, "{driver-key}",
            UsbPortConnectionStatus.Connected, UsbDeviceSpeed.Super, false);
        var route = new InputDeviceRouteSnapshot(raw, "Test Mouse", controller.InstanceId, null,
            ["USB\\VID_TEST", controller.InstanceId])
        {
            UsbDeviceInstanceId = "USB\\VID_TEST",
            UsbPortRoute = new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.Available, port, null),
        };
        var capture = new KernelLatencyCaptureResult(
            DateTimeOffset.UnixEpoch, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20),
            [
                new KernelLatencyEvent(KernelLatencyEventKind.Dpc, 2, 1, 40, 0x1000, null, null, "C:\\Windows\\System32\\drivers\\USBXHCI.SYS"),
                new KernelLatencyEvent(KernelLatencyEventKind.Isr, 2, 2, 8, 0x1001, 44, 0, "C:\\Windows\\System32\\drivers\\usbxhci.sys"),
                new KernelLatencyEvent(KernelLatencyEventKind.Dpc, 4, 3, 12, 0x2000, null, null, "C:\\Windows\\System32\\drivers\\ndis.sys"),
            ], 0, 0, 0, false);
        var attribution = UsbInterruptAttribution.Analyze(capture, controller, [controller]);
        Assert.IsTrue(attribution.ControllerOwnershipUnambiguous);
        Assert.AreEqual(1, attribution.MatchingDpcEventCount);
        Assert.AreEqual(1, attribution.MatchingIsrEventCount);

        var topology = new ProcessorTopologySnapshot(
            [new ProcessorPackageSnapshot(0,
                [new LogicalProcessorId(0, 0), new LogicalProcessorId(0, 1), new LogicalProcessorId(0, 2),
                 new LogicalProcessorId(0, 3), new LogicalProcessorId(0, 4), new LogicalProcessorId(0, 5)])],
            [
                new ProcessorCoreSnapshot(0, 0, [new LogicalProcessorId(0, 0), new LogicalProcessorId(0, 1)]),
                new ProcessorCoreSnapshot(1, 0, [new LogicalProcessorId(0, 2), new LogicalProcessorId(0, 3)]),
                new ProcessorCoreSnapshot(2, 0, [new LogicalProcessorId(0, 4), new LogicalProcessorId(0, 5)]),
            ], DateTimeOffset.UnixEpoch);
        var rankedCpuHeadroom = UsbAffinityCpuSelector.Rank(topology, capture, new LogicalProcessorId(0, 0));
        Assert.IsFalse(rankedCpuHeadroom.Any(static candidate => candidate.Processor.Number is 0 or 1));
        Assert.AreEqual(new LogicalProcessorId(0, 3), rankedCpuHeadroom[0].Processor);

        var inventory = new UserInputRouteInventory([route], DateTimeOffset.UnixEpoch);
        Assert.AreEqual(UsbAffinityRecommendationStatus.NotReady,
            UsbAffinityRecommendationPlanner.Create(topology, capture, inventory, new LogicalProcessorId(0, 0)).Status,
            "Automatic v1 must not substitute an arbitrary resolved mouse for the primary input device.");
        var recommendation = UsbAffinityRecommendationPlanner.Create(
            topology, capture, inventory, new LogicalProcessorId(0, 0), raw.PnPInstanceId!);
        Assert.IsTrue(recommendation.IsReady);
        Assert.AreEqual(controller.InstanceId, recommendation.ControllerInstanceId);
        Assert.AreEqual(new LogicalProcessorId(0, 3), recommendation.Processor);

        var otherRaw = raw with { DeviceInterfacePath = "\\\\?\\HID#VID_OTHER", PnPInstanceId = "HID\\VID_OTHER" };
        var otherControllerId = "PCI\\VEN_TEST&DEV_OTHER_XHCI";
        var otherRoute = route with
        {
            RawInputDevice = otherRaw,
            UsbHostControllerInstanceId = otherControllerId,
            UsbPortRoute = new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.Available,
                port with { HostControllerInstanceId = otherControllerId, ConnectionIndex = 4 }, null),
        };
        var multiMouse = new UserInputRouteInventory([route, otherRoute], DateTimeOffset.UnixEpoch);
        Assert.IsTrue(UsbAffinityRecommendationPlanner.Create(
            topology, capture, multiMouse, new LogicalProcessorId(0, 0), raw.PnPInstanceId!).IsReady,
            "A secondary resolved mouse must not replace or make the explicit primary identity ambiguous.");
        Assert.AreEqual(UsbAffinityRecommendationStatus.NotReady,
            UsbAffinityRecommendationPlanner.Create(
                topology, capture, multiMouse, new LogicalProcessorId(0, 0), "HID\\UNKNOWN").Status);
        var shortCapture = capture with { RequestedDuration = TimeSpan.FromSeconds(2), ActualDuration = TimeSpan.FromSeconds(2) };
        Assert.AreEqual(UsbAffinityRecommendationStatus.NotReady,
            UsbAffinityRecommendationPlanner.Create(
                topology, shortCapture, inventory, new LogicalProcessorId(0, 0), raw.PnPInstanceId!).Status);

        var ticks = Enumerable.Range(0, 101).Select(index => index * 1_000_000L).ToArray();
        var timing = InputTimingAnalyzer.Analyze(new InputReportTimestampSeries(raw.PnPInstanceId!, 1_000_000_000L, ticks));
        Assert.AreEqual(UsbOptimizationReadinessStatus.Ready,
            UsbOptimizationReadiness.Evaluate(route, timing, attribution).Status);
        var sharedController = controller with { InstanceId = otherControllerId, DisplayName = "Other xHCI" };
        var driverWide = UsbInterruptAttribution.Analyze(capture, controller, [controller, sharedController]);
        Assert.IsFalse(driverWide.ControllerOwnershipUnambiguous);
        Assert.AreEqual(UsbOptimizationReadinessStatus.Inconclusive,
            UsbOptimizationReadiness.Evaluate(route, timing, driverWide).Status,
            "A shared USBXHCI module is driver-wide evidence, not controller-specific ownership.");

        var composite = UsbPortRouteCorrelator.ResolveFromCandidates(
            [
                new UsbDriverKeyCandidate("HID\\VID_TEST", null, "HID child has no usable port driver key"),
                new UsbDriverKeyCandidate("USB\\VID_TEST", "{driver-key}", null),
            ], controller.InstanceId, [port]);
        Assert.AreEqual(UsbPortRouteResolutionStatus.Available, composite.Evidence.Status);
        Assert.AreEqual("USB\\VID_TEST", composite.MatchedDeviceInstanceId);
        Assert.AreEqual(3u, composite.Evidence.Port?.ConnectionIndex);

        var ambiguousRoute = route with
        {
            UsbPortRoute = new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.Ambiguous, null, "duplicate driver-key"),
        };
        Assert.AreEqual(UsbOptimizationReadinessStatus.NotReady,
            UsbOptimizationReadiness.Evaluate(ambiguousRoute, timing, attribution).Status);
        var wrongController = attribution with { ControllerInstanceId = "PCI\\VEN_OTHER&DEV_XHCI" };
        Assert.AreEqual(UsbOptimizationReadinessStatus.NotReady,
            UsbOptimizationReadiness.Evaluate(route, timing, wrongController).Status);
    }
}
''')

def apply_implementation() -> None:
    write("src/LatencyPilot.Benchmarking/Optimization/UsbAffinityRecommendationPlanner.cs", r'''using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;

namespace LatencyPilot.Benchmarking.Optimization;

public enum UsbAffinityRecommendationStatus { Ready = 0, NotReady = 1 }

public sealed record UsbAffinityRecommendation(
    UsbAffinityRecommendationStatus Status, string? ControllerInstanceId, LogicalProcessorId? Processor,
    IReadOnlyList<string> InputDeviceInstanceIds, UsbAffinityCpuCandidate? CpuEvidence, string Reason)
{
    public bool IsReady => Status == UsbAffinityRecommendationStatus.Ready && ControllerInstanceId is not null && Processor is not null && CpuEvidence is not null;
}

public static class UsbAffinityRecommendationPlanner
{
    private static readonly TimeSpan MinimumCaptureDuration = TimeSpan.FromSeconds(5);

    public static UsbAffinityRecommendation Create(
        ProcessorTopologySnapshot topology, KernelLatencyCaptureResult quietCapture,
        UserInputRouteInventory inputRoutes, LogicalProcessorId gpuWinner) =>
        NotReady("Primary Raw Input PnP identity is required before automatic USB/xHCI affinity selection; LatencyPilot will not substitute another resolved mouse.");

    public static UsbAffinityRecommendation Create(
        ProcessorTopologySnapshot topology, KernelLatencyCaptureResult quietCapture,
        UserInputRouteInventory inputRoutes, LogicalProcessorId gpuWinner, string primaryInputDeviceInstanceId)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(quietCapture);
        ArgumentNullException.ThrowIfNull(inputRoutes);
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryInputDeviceInstanceId);

        if (!TryValidateCapture(quietCapture, out var captureReason)) return NotReady(captureReason);

        var primaryRoutes = inputRoutes.Routes.Where(route =>
            route.RawInputDevice.Kind == RawInputDeviceKind.Mouse &&
            route.RawInputDevice.ResolutionStatus == RawInputRouteResolutionStatus.Available &&
            string.Equals(route.RawInputDevice.PnPInstanceId, primaryInputDeviceInstanceId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (primaryRoutes.Length != 1)
            return NotReady(primaryRoutes.Length == 0
                ? "The selected primary Raw Input mouse is not present in the resolved route inventory."
                : "The selected primary Raw Input identity maps to more than one route; automatic mutation requires one exact route.");

        var route = primaryRoutes[0];
        if (route.UsbPortRoute?.IsAvailable != true || string.IsNullOrWhiteSpace(route.UsbHostControllerInstanceId) ||
            !string.Equals(route.UsbPortRoute.Port?.HostControllerInstanceId, route.UsbHostControllerInstanceId, StringComparison.OrdinalIgnoreCase))
            return NotReady("The selected primary mouse does not have one exact USB hub/port/xHCI route.");

        UsbAffinityCpuCandidate selected;
        try { selected = UsbAffinityCpuSelector.Select(topology, quietCapture, gpuWinner); }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        { return NotReady(exception.Message); }

        var controller = route.UsbHostControllerInstanceId!;
        var reason = $"Selected CPU {selected.Processor.Number} for primary input {primaryInputDeviceInstanceId} on xHCI {controller}: " +
            $"{selected.TotalInterruptDurationMicroseconds:F1} us observed DPC+ISR time and {selected.InterruptTailP99Microseconds:F1} us p99 tail. " +
            $"The physical core containing GPU CPU {gpuWinner.Number} was excluded.";
        return new UsbAffinityRecommendation(UsbAffinityRecommendationStatus.Ready, controller, selected.Processor,
            [primaryInputDeviceInstanceId], selected, reason);
    }

    private static bool TryValidateCapture(KernelLatencyCaptureResult capture, out string reason)
    {
        if (!capture.IsValid) { reason = "The post-GPU ETW capture is not clean enough for USB CPU-headroom selection."; return false; }
        if (capture.RequestedDuration < MinimumCaptureDuration || capture.ActualDuration < MinimumCaptureDuration ||
            capture.ActualDuration < TimeSpan.FromTicks((long)(capture.RequestedDuration.Ticks * 0.90)))
        { reason = "The post-GPU quiet ETW capture is too short or incomplete for USB CPU-headroom selection."; return false; }
        if (capture.DpcCount + capture.IsrCount == 0)
        { reason = "The post-GPU quiet ETW capture contains no DPC/ISR evidence; CPU headroom is unknown."; return false; }
        reason = string.Empty; return true;
    }

    private static UsbAffinityRecommendation NotReady(string reason) =>
        new(UsbAffinityRecommendationStatus.NotReady, null, null, [], null, reason);
}
''')

    write("src/LatencyPilot.Platform.Windows/Devices/UsbInterruptAttribution.cs", r'''using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;

namespace LatencyPilot.Platform.Windows.Devices;

public enum UsbInterruptAttributionScope { DriverWide = 0 }

public sealed record UsbInterruptAttributionEvidence(
    string ControllerInstanceId, string ControllerServiceName, string TargetModuleStem,
    int MatchingDpcEventCount, int MatchingIsrEventCount, IReadOnlyList<double> DpcDurationsMicroseconds,
    IReadOnlyList<double> IsrDurationsMicroseconds, int UnresolvedDpcIsrEventCount, bool CaptureIntegrityValid)
{
    public bool HasTargetEvidence => MatchingDpcEventCount > 0 || MatchingIsrEventCount > 0;
    public UsbInterruptAttributionScope Scope { get; init; } = UsbInterruptAttributionScope.DriverWide;
    public int SameServiceControllerCount { get; init; } = -1;
    public bool ControllerOwnershipUnambiguous => SameServiceControllerCount == 1;
}

public static class UsbInterruptAttribution
{
    public static UsbInterruptAttributionEvidence Analyze(KernelLatencyCaptureResult capture, PnPDeviceSnapshot controller) =>
        Analyze(capture, controller, null);

    public static UsbInterruptAttributionEvidence Analyze(
        KernelLatencyCaptureResult capture, PnPDeviceSnapshot controller, IReadOnlyList<PnPDeviceSnapshot>? presentXhciControllers)
    {
        ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(controller);
        ArgumentException.ThrowIfNullOrWhiteSpace(controller.InstanceId);
        if (string.IsNullOrWhiteSpace(controller.ServiceName))
            throw new InvalidOperationException("The xHCI controller does not expose a driver service name, so module ownership cannot be attributed.");

        var targetModule = NormalizeModuleStem(controller.ServiceName);
        var matching = capture.Events.Where(static item => item.Kind is KernelLatencyEventKind.Dpc or KernelLatencyEventKind.Isr)
            .Where(item => ModuleMatchesService(item.ModulePath, targetModule)).ToArray();
        var dpc = matching.Where(static item => item.Kind == KernelLatencyEventKind.Dpc).Select(static item => item.DurationMicroseconds)
            .Where(static value => double.IsFinite(value) && value >= 0).ToArray();
        var isr = matching.Where(static item => item.Kind == KernelLatencyEventKind.Isr).Select(static item => item.DurationMicroseconds)
            .Where(static value => double.IsFinite(value) && value >= 0).ToArray();
        var unresolved = capture.Events.Count(static item =>
            item.Kind is KernelLatencyEventKind.Dpc or KernelLatencyEventKind.Isr && string.IsNullOrWhiteSpace(item.ModulePath));
        var sameServiceCount = presentXhciControllers is null ? -1 : presentXhciControllers.Count(candidate =>
            string.Equals(candidate.ServiceName, controller.ServiceName, StringComparison.OrdinalIgnoreCase));
        return new UsbInterruptAttributionEvidence(controller.InstanceId, controller.ServiceName, targetModule,
            dpc.Length, isr.Length, dpc, isr, unresolved, capture.IsValid) { SameServiceControllerCount = sameServiceCount };
    }

    private static bool ModuleMatchesService(string? modulePath, string serviceName) =>
        !string.IsNullOrWhiteSpace(modulePath) && string.Equals(NormalizeModuleStem(modulePath), serviceName, StringComparison.OrdinalIgnoreCase);
    private static string NormalizeModuleStem(string value) => Path.GetFileNameWithoutExtension(Path.GetFileName(value.Trim()));
}
''')

    write("src/LatencyPilot.Platform.Windows/Devices/UsbPortRouteCorrelator.cs", r'''using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

public sealed record UsbDriverKeyCandidate(string DeviceInstanceId, string? DriverKeyName, string? Error);
public sealed record UsbCompositePortRouteResolution(UsbPortRouteEvidence Evidence, string? MatchedDeviceInstanceId);

public static class UsbPortRouteCorrelator
{
    public static UsbCompositePortRouteResolution ResolveFromCandidates(
        IReadOnlyList<UsbDriverKeyCandidate> candidates, string? expectedHostControllerInstanceId,
        IReadOnlyList<UsbHubPortSnapshot> ports)
    {
        ArgumentNullException.ThrowIfNull(candidates); ArgumentNullException.ThrowIfNull(ports);
        var available = candidates.Where(static c => !string.IsNullOrWhiteSpace(c.DriverKeyName))
            .Select(c => (Candidate: c, Evidence: Resolve(c.DriverKeyName, expectedHostControllerInstanceId, ports)))
            .Where(static item => item.Evidence.IsAvailable).ToArray();
        var distinct = available.GroupBy(item =>
            $"{item.Evidence.Port!.HubDevicePath}|{item.Evidence.Port.ConnectionIndex}|{item.Evidence.Port.HostControllerInstanceId}",
            StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length == 1)
        {
            var match = distinct[0].First();
            return new UsbCompositePortRouteResolution(match.Evidence, match.Candidate.DeviceInstanceId);
        }
        if (distinct.Length > 1)
            return new UsbCompositePortRouteResolution(new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.Ambiguous, null,
                "More than one HID/USB ancestor resolves to a different connected USB port; composite transport ownership is ambiguous."), null);
        var detail = candidates.FirstOrDefault(static c => !string.IsNullOrWhiteSpace(c.Error))?.Error;
        return new UsbCompositePortRouteResolution(new UsbPortRouteEvidence(
            candidates.Any(static c => !string.IsNullOrWhiteSpace(c.DriverKeyName)) ? UsbPortRouteResolutionStatus.NotFound : UsbPortRouteResolutionStatus.DriverKeyUnavailable,
            null, detail ?? "No HID/USB ancestor exposes a unique driver-key match to a connected USB port."), null);
    }

    public static UsbPortRouteEvidence Resolve(string? driverKeyName, string? expectedHostControllerInstanceId, IReadOnlyList<UsbHubPortSnapshot> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        if (string.IsNullOrWhiteSpace(driverKeyName))
            return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.DriverKeyUnavailable, null,
                "The PnP device does not expose the exact driver-key identity needed for USB hub-port correlation.");
        var matches = ports.Where(port => port.ConnectionStatus == UsbPortConnectionStatus.Connected &&
            !string.IsNullOrWhiteSpace(port.DriverKeyName) && string.Equals(port.DriverKeyName, driverKeyName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.NotFound, null,
            "No connected USB hub port exposes the same exact driver-key identity.");
        if (matches.Length != 1) return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.Ambiguous, null,
            $"{matches.Length} connected USB ports expose the same driver-key identity; the route is ambiguous.");
        var match = matches[0];
        if (!string.IsNullOrWhiteSpace(expectedHostControllerInstanceId))
        {
            if (string.IsNullOrWhiteSpace(match.HostControllerInstanceId))
                return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.HostControllerUnavailable, null,
                    "The matching USB port does not have an authoritative host-controller identity.");
            if (!string.Equals(match.HostControllerInstanceId, expectedHostControllerInstanceId, StringComparison.OrdinalIgnoreCase))
                return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.HostControllerMismatch, null,
                    "The exact driver-key match belongs to a different USB host controller than the known PnP ancestry.");
        }
        return new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.Available, match, null);
    }
}
''')

    readiness = "src/LatencyPilot.Service/UsbOptimizationReadiness.cs"
    replace_once(readiness,
'''        if (!attribution.CaptureIntegrityValid)\n        {\n''',
'''        if (!attribution.ControllerOwnershipUnambiguous)\n        {\n            return Result(\n                UsbOptimizationReadinessStatus.Inconclusive,\n                routeController,\n                "The xHCI service/module attribution is driver-wide and is shared by multiple or unknown controllers; controller-specific ownership is not proven.");\n        }\n\n        if (!attribution.CaptureIntegrityValid)\n        {\n''')

    route_reader = "src/LatencyPilot.Platform.Windows/Devices/InputDeviceRouteReader.cs"
    old = '''    private static InputDeviceRouteSnapshot EnrichUsbPortRoute(\n        InputDeviceRouteSnapshot route,\n        DeviceRelationshipGraph graph,\n        UsbTopologySnapshot usbTopology)\n    {\n        if (!route.IsUsbBacked || route.RawInputDevice.PnPInstanceId is null)\n        {\n            return route;\n        }\n\n        var device = graph.TryGetDevice(route.RawInputDevice.PnPInstanceId);\n        if (device is null)\n        {\n            return route;\n        }\n\n        var chain = new[] { device }.Concat(graph.GetKnownAncestors(device.InstanceId)).ToArray();\n        var usbDevice = chain.FirstOrDefault(static candidate =>\n            candidate.InstanceId.StartsWith("USB\\\\", StringComparison.OrdinalIgnoreCase) &&\n            !string.Equals(candidate.ServiceName, "USBXHCI", StringComparison.OrdinalIgnoreCase) &&\n            !candidate.InstanceId.StartsWith("USB\\\\ROOT_HUB", StringComparison.OrdinalIgnoreCase));\n        var driverKeyTarget = usbDevice ?? device;\n\n        try\n        {\n            var driverKey = UsbTopologyReader.TryReadDriverKeyName(driverKeyTarget.InstanceId, out var driverKeyError);\n            var portRoute = driverKey is null && driverKeyError is not null\n                ? new UsbPortRouteEvidence(\n                    UsbPortRouteResolutionStatus.DriverKeyUnavailable,\n                    null,\n                    driverKeyError)\n                : UsbPortRouteCorrelator.Resolve(\n                    driverKey,\n                    route.UsbHostControllerInstanceId,\n                    usbTopology.Ports);\n\n            return route with\n            {\n                UsbDeviceInstanceId = usbDevice?.InstanceId,\n                UsbPortRoute = portRoute,\n            };\n        }\n        catch (Exception exception) when (IsRecoverableUsbMetadataException(exception))\n        {\n            return route with\n            {\n                UsbDeviceInstanceId = usbDevice?.InstanceId,\n                UsbPortRoute = new UsbPortRouteEvidence(\n                    UsbPortRouteResolutionStatus.DriverKeyUnavailable,\n                    null,\n                    $"Unable to read the exact USB transport driver-key identity: {exception.Message}"),\n            };\n        }\n    }\n'''
    new = '''    private static InputDeviceRouteSnapshot EnrichUsbPortRoute(\n        InputDeviceRouteSnapshot route,\n        DeviceRelationshipGraph graph,\n        UsbTopologySnapshot usbTopology)\n    {\n        if (!route.IsUsbBacked || route.RawInputDevice.PnPInstanceId is null) return route;\n        var device = graph.TryGetDevice(route.RawInputDevice.PnPInstanceId);\n        if (device is null) return route;\n\n        var chain = new[] { device }.Concat(graph.GetKnownAncestors(device.InstanceId))\n            .TakeWhile(static candidate => !string.Equals(candidate.ServiceName, "USBXHCI", StringComparison.OrdinalIgnoreCase))\n            .Where(static candidate => !candidate.InstanceId.StartsWith("USB\\\\ROOT_HUB", StringComparison.OrdinalIgnoreCase))\n            .ToArray();\n        var candidates = new List<UsbDriverKeyCandidate>(chain.Length);\n        foreach (var candidate in chain)\n        {\n            try\n            {\n                var key = UsbTopologyReader.TryReadDriverKeyName(candidate.InstanceId, out var error);\n                candidates.Add(new UsbDriverKeyCandidate(candidate.InstanceId, key, error));\n            }\n            catch (Exception exception) when (IsRecoverableUsbMetadataException(exception))\n            {\n                candidates.Add(new UsbDriverKeyCandidate(candidate.InstanceId, null, exception.Message));\n            }\n        }\n\n        var resolved = UsbPortRouteCorrelator.ResolveFromCandidates(\n            candidates, route.UsbHostControllerInstanceId, usbTopology.Ports);\n        return route with\n        {\n            UsbDeviceInstanceId = resolved.MatchedDeviceInstanceId,\n            UsbPortRoute = resolved.Evidence,\n        };\n    }\n'''
    replace_once(route_reader, old, new)

if __name__ == "__main__":
    if len(sys.argv) != 2 or sys.argv[1] not in {"tests", "implementation"}:
        raise SystemExit("usage: audit-closure-task4-usb.py tests|implementation")
    (apply_tests if sys.argv[1] == "tests" else apply_implementation)()

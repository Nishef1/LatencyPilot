using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class UsbOptimizationTests
{
    [TestMethod]
    public void XhciReadinessRequiresExactRouteTimingAndModuleAttribution()
    {
        var controller = new PnPDeviceSnapshot(
            "PCI\\VEN_TEST&DEV_XHCI",
            Guid.NewGuid(),
            "Test xHCI Controller",
            "Test Vendor",
            "PCI",
            "USBXHCI",
            new DriverMetadataSnapshot("1.0", "Test Vendor", "usbxhci.inf"),
            InterruptConfigurationSnapshot.Available(null, null, null, null),
            InterruptResourceSnapshot.Available([]));

        var raw = new RawInputDeviceSnapshot(
            RawInputDeviceKind.Mouse,
            "\\\\?\\HID#VID_TEST",
            "HID\\VID_TEST",
            1,
            2,
            1,
            null,
            null,
            RawInputRouteResolutionStatus.Available,
            null,
            null);
        var port = new UsbHubPortSnapshot(
            "\\\\?\\USB#ROOT_HUB30#TEST",
            "USB\\ROOT_HUB30\\TEST",
            controller.InstanceId,
            3,
            "{driver-key}",
            UsbPortConnectionStatus.Connected,
            UsbDeviceSpeed.Super,
            false);
        var route = new InputDeviceRouteSnapshot(
            raw,
            "Test Mouse",
            controller.InstanceId,
            null,
            ["USB\\VID_TEST", controller.InstanceId])
        {
            UsbDeviceInstanceId = "USB\\VID_TEST",
            UsbPortRoute = new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.Available, port, null),
        };

        var capture = new KernelLatencyCaptureResult(
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20),
            [
                new KernelLatencyEvent(KernelLatencyEventKind.Dpc, 2, 1, 40, 0x1000, null, null, "C:\\Windows\\System32\\drivers\\USBXHCI.SYS"),
                new KernelLatencyEvent(KernelLatencyEventKind.Isr, 2, 2, 8, 0x1001, 44, 0, "C:\\Windows\\System32\\drivers\\usbxhci.sys"),
                new KernelLatencyEvent(KernelLatencyEventKind.Dpc, 4, 3, 12, 0x2000, null, null, "C:\\Windows\\System32\\drivers\\ndis.sys"),
            ],
            0,
            0,
            0,
            false);
        var attribution = UsbInterruptAttribution.Analyze(capture, controller);
        Assert.AreEqual(1, attribution.MatchingDpcEventCount);
        Assert.AreEqual(1, attribution.MatchingIsrEventCount);
        Assert.IsTrue(attribution.HasTargetEvidence);

        var ticks = Enumerable.Range(0, 101).Select(index => index * 1_000_000L).ToArray();
        var timing = InputTimingAnalyzer.Analyze(new InputReportTimestampSeries("HID\\VID_TEST", 1_000_000_000L, ticks));
        var ready = UsbOptimizationReadiness.Evaluate(route, timing, attribution);
        Assert.AreEqual(UsbOptimizationReadinessStatus.Ready, ready.Status);
        Assert.AreEqual(controller.InstanceId, ready.ControllerInstanceId);
        Assert.IsTrue(ready.TargetMetrics.ContainsKey(UsbOptimizationMetricNames.XhciDpcDuration));
        Assert.IsTrue(ready.TargetMetrics.ContainsKey(UsbOptimizationMetricNames.XhciIsrDuration));
        Assert.IsTrue(ready.TargetMetrics.ContainsKey(UsbOptimizationMetricNames.InputReportInterval));
        CollectionAssert.Contains(ready.RequiredGuardrails.ToList(), UsbOptimizationGuardrailNames.NetworkLatencyJitter);
        CollectionAssert.Contains(ready.RequiredGuardrails.ToList(), UsbOptimizationGuardrailNames.AudioStability);
        CollectionAssert.Contains(ready.RequiredGuardrails.ToList(), UsbOptimizationGuardrailNames.GraphicsFrameTime);

        var ambiguousRoute = route with
        {
            UsbPortRoute = new UsbPortRouteEvidence(UsbPortRouteResolutionStatus.Ambiguous, null, "duplicate driver-key"),
        };
        Assert.AreEqual(
            UsbOptimizationReadinessStatus.NotReady,
            UsbOptimizationReadiness.Evaluate(ambiguousRoute, timing, attribution).Status);

        var insufficientTiming = timing with { Status = InputTimingAnalysisStatus.InsufficientSamples };
        Assert.AreEqual(
            UsbOptimizationReadinessStatus.Inconclusive,
            UsbOptimizationReadiness.Evaluate(route, insufficientTiming, attribution).Status);

        var unattributed = attribution with
        {
            MatchingDpcEventCount = 0,
            MatchingIsrEventCount = 0,
            DpcDurationsMicroseconds = [],
            IsrDurationsMicroseconds = [],
        };
        Assert.AreEqual(
            UsbOptimizationReadinessStatus.Inconclusive,
            UsbOptimizationReadiness.Evaluate(route, timing, unattributed).Status);

        var wrongController = attribution with { ControllerInstanceId = "PCI\\VEN_OTHER&DEV_XHCI" };
        Assert.AreEqual(
            UsbOptimizationReadinessStatus.NotReady,
            UsbOptimizationReadiness.Evaluate(route, timing, wrongController).Status);
    }
}

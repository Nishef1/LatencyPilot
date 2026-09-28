#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Core.System;
using LatencyPilot.Platform.Windows.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class UsbOptimizationTests
{
    [AuditCase]
    public void XhciSelectionAndRuntimeVerificationRequireExactRouteAndUnambiguousPlacement()
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

        var sharedController = controller with { InstanceId = otherControllerId, DisplayName = "Other xHCI" };

        var targetWithAllocation = controller with
        {
            InterruptResources = InterruptResourceSnapshot.Available(
            [
                new AllocatedInterruptResourceSnapshot(44, 0, 1UL << 2, 0),
            ]),
        };
        var peerWithDisjointAllocation = sharedController with
        {
            InterruptResources = InterruptResourceSnapshot.Available(
            [
                new AllocatedInterruptResourceSnapshot(45, 0, 1UL << 4, 0),
            ]),
        };
        var sharedDriverCapture = capture with
        {
            Events =
            [
                new KernelLatencyEvent(KernelLatencyEventKind.Isr, 2, 1, 8, 0x1001, 44, 0, "C:\\Windows\\System32\\drivers\\USBXHCI.SYS"),
                new KernelLatencyEvent(KernelLatencyEventKind.Isr, 4, 2, 7, 0x1002, 45, 0, "C:\\Windows\\System32\\drivers\\USBXHCI.SYS"),
            ],
        };
        var routedInventory = new DeviceInventorySnapshot(
            [targetWithAllocation, peerWithDisjointAllocation],
            DateTimeOffset.UnixEpoch);
        var peerAwareRecommendation = UsbAffinityRecommendationPlanner.Create(
            topology,
            capture,
            inventory,
            new LogicalProcessorId(0, 0),
            raw.PnPInstanceId!,
            routedInventory);
        Assert.IsTrue(peerAwareRecommendation.IsReady);
        Assert.AreNotEqual(new LogicalProcessorId(0, 4), peerAwareRecommendation.Processor,
            "The planner must not recommend a CPU already present in same-service peer xHCI translated allocation.");

        var allPeerAllocated = peerWithDisjointAllocation with
        {
            InterruptResources = InterruptResourceSnapshot.Available(
            [
                new AllocatedInterruptResourceSnapshot(
                    45,
                    0,
                    (1UL << 2) | (1UL << 3) | (1UL << 4) | (1UL << 5),
                    0),
            ]),
        };
        var blockedRecommendation = UsbAffinityRecommendationPlanner.Create(
            topology,
            capture,
            inventory,
            new LogicalProcessorId(0, 0),
            raw.PnPInstanceId!,
            new DeviceInventorySnapshot(
                [targetWithAllocation, allPeerAllocated],
                DateTimeOffset.UnixEpoch));
        Assert.AreEqual(
            UsbAffinityRecommendationStatus.NotReady,
            blockedRecommendation.Status,
            "Automatic xHCI selection must fail closed when every eligible CPU overlaps a same-service peer controller allocation.");

        var unknownPeer = peerWithDisjointAllocation with
        {
            InterruptResources = InterruptResourceSnapshot.ReadFailed(),
        };
        var unknownPeerRecommendation = UsbAffinityRecommendationPlanner.Create(
            topology,
            capture,
            inventory,
            new LogicalProcessorId(0, 0),
            raw.PnPInstanceId!,
            new DeviceInventorySnapshot(
                [targetWithAllocation, unknownPeer],
                DateTimeOffset.UnixEpoch));
        Assert.AreEqual(
            UsbAffinityRecommendationStatus.NotReady,
            unknownPeerRecommendation.Status,
            "Automatic xHCI selection must not promise controller-specific verification when peer allocation is unreadable.");

        var xhciCandidate = new DeviceInterruptAffinityCandidate(0, 2, 1UL << 2);
        var sharedDriverPlacement = XhciInterruptRuntimePlacementVerifier.Analyze(
            sharedDriverCapture,
            targetWithAllocation.InstanceId,
            [targetWithAllocation, peerWithDisjointAllocation],
            xhciCandidate);
        Assert.IsTrue(sharedDriverPlacement.ConfirmsRequestedPlacement,
            "When every same-service peer has readable translated allocation disjoint from the requested CPU, USBXHCI ISR execution on the requested CPU is uniquely attributable to the target controller.");

        var multiMaskAttribution = new XhciInterruptIsrAttribution(
            targetWithAllocation.InstanceId,
            "USBXHCI",
            [
                new KernelLatencyEvent(KernelLatencyEventKind.Isr, 2, 1, 8, 0x1001, 44, 0, "C:\\Windows\\System32\\drivers\\USBXHCI.SYS"),
                new KernelLatencyEvent(KernelLatencyEventKind.Isr, 3, 2, 7, 0x1002, 44, 0, "C:\\Windows\\System32\\drivers\\USBXHCI.SYS"),
            ],
            0,
            XhciInterruptIsrAttributionMode.SingleServiceInstance);
        var multiMaskPlacement = XhciInterruptRuntimePlacementVerifier.Analyze(
            multiMaskAttribution,
            new DeviceInterruptAffinityCandidate(0, 2, (1UL << 2) | (1UL << 3)));
        Assert.IsTrue(multiMaskPlacement.ConfirmsRequestedPlacement,
            "Manual multi-CPU KAFFINITY must accept ISR execution on any selected processor, not only the primary/lowest bit.");
        Assert.AreEqual(2, multiMaskPlacement.InRequestedMaskIsrEventCount);
        Assert.AreEqual(0, multiMaskPlacement.OffTargetIsrEventCount);

        var peerWithOverlappingAllocation = peerWithDisjointAllocation with
        {
            InterruptResources = InterruptResourceSnapshot.Available(
            [
                new AllocatedInterruptResourceSnapshot(45, 0, 1UL << 2, 0),
            ]),
        };
        Assert.ThrowsExactly<NotSupportedException>(() =>
            XhciInterruptRuntimePlacementVerifier.Analyze(
                sharedDriverCapture,
                targetWithAllocation.InstanceId,
                [targetWithAllocation, peerWithOverlappingAllocation],
                xhciCandidate),
            "Shared-driver attribution must stay fail-closed when another xHCI controller can service interrupts on the requested CPU.");

        var composite = UsbPortRouteCorrelator.ResolveFromCandidates(
            [
                new UsbDriverKeyCandidate("HID\\VID_TEST", null, "HID child has no usable port driver key"),
                new UsbDriverKeyCandidate("USB\\VID_TEST", "{driver-key}", null),
            ], controller.InstanceId, [port]);
        Assert.AreEqual(UsbPortRouteResolutionStatus.Available, composite.Evidence.Status);
        Assert.AreEqual("USB\\VID_TEST", composite.MatchedDeviceInstanceId);
        Assert.AreEqual(3u, composite.Evidence.Port?.ConnectionIndex);

    }
}

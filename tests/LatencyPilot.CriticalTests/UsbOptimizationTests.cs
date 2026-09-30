#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using System.Buffers.Binary;
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
        Assert.AreEqual(new LogicalProcessorId(0, 5), recommendation.Processor,
            "Recommendation should rank physical-core interrupt pressure first, then select the quieter SMT sibling.");

        var independentWithoutGpuReservation = UsbAffinityRecommendationPlanner.CreateIndependent(
            topology,
            capture,
            inventory,
            raw.PnPInstanceId!,
            reservedGpuProcessor: null);
        Assert.AreEqual(
            UsbAffinityRecommendationStatus.Ready,
            independentWithoutGpuReservation.Status,
            "USB benchmarking is independent of GPU benchmark provenance; no GPU result is required to rank its own candidates.");
        Assert.IsTrue(independentWithoutGpuReservation.HasCandidate);
        Assert.IsTrue(independentWithoutGpuReservation.IsReady);

        var noReservationRanking = UsbAffinityCpuSelector.Rank(
            topology,
            capture,
            reservedGpuProcessor: null);
        Assert.IsTrue(
            noReservationRanking.Any(static candidate => candidate.Processor.Number is 0 or 1),
            "Independent USB diagnostics must not silently invent a GPU-core exclusion when no verified GPU reservation was supplied.");

        var stableSelection = UsbAffinityCpuSelector.SelectStable(
            topology,
            [capture, capture, capture],
            [new LogicalProcessorId(0, 0)]);
        Assert.IsTrue(stableSelection.IsStable);
        Assert.AreEqual(2, stableSelection.PhysicalCoreIndex);
        Assert.AreEqual(3, stableSelection.WinningCoreVotes);
        Assert.AreEqual(3, stableSelection.WindowCount);
        Assert.AreEqual(new LogicalProcessorId(0, 5), stableSelection.Candidate!.Processor);

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
        var overlappingPeerPreflight = XhciInterruptRuntimePlacementVerifier.AssessApplyPreflight(
            targetWithAllocation.InstanceId,
            [targetWithAllocation, allPeerAllocated],
            new DeviceInterruptAffinityCandidate(0, 2, 1UL << 2));
        Assert.IsTrue(
            overlappingPeerPreflight.CanAttemptApply,
            "A journaled xHCI Apply may be attempted even when peer attribution is ambiguous; target translated allocation remains the post-Apply authority.");
        Assert.IsFalse(
            overlappingPeerPreflight.ControllerSpecificAttributionAvailable,
            "Peer overlap must still disable the stronger controller-specific ETW claim.");

        var unknownPeer = peerWithDisjointAllocation with
        {
            InterruptResources = InterruptResourceSnapshot.ReadFailed(),
        };
        var unknownPeerPreflight = XhciInterruptRuntimePlacementVerifier.AssessApplyPreflight(
            targetWithAllocation.InstanceId,
            [targetWithAllocation, unknownPeer],
            new DeviceInterruptAffinityCandidate(0, 3, 1UL << 3));
        Assert.IsTrue(
            unknownPeerPreflight.CanAttemptApply,
            "Unreadable peer allocation must not permanently gate a rollback-safe Apply attempt.");
        Assert.IsFalse(
            unknownPeerPreflight.ControllerSpecificAttributionAvailable,
            "Unreadable peer allocation still prevents the stronger controller-specific ETW attribution before Apply.");

        var vectorSnapshots = new Dictionary<string, DeviceInterruptVectorSnapshot>(
            StringComparer.OrdinalIgnoreCase)
        {
            [targetWithAllocation.InstanceId] = new(
                targetWithAllocation.InstanceId,
                DeviceInterruptVectorReadStatus.Available,
                [44],
                null),
            [unknownPeer.InstanceId] = new(
                unknownPeer.InstanceId,
                DeviceInterruptVectorReadStatus.Available,
                [45],
                null),
        };

        var vectorPreflight = XhciInterruptRuntimePlacementVerifier.AssessApplyPreflight(
            targetWithAllocation.InstanceId,
            [targetWithAllocation, unknownPeer],
            new DeviceInterruptAffinityCandidate(0, 2, 1UL << 2),
            vectorSnapshots);
        Assert.IsTrue(vectorPreflight.CanAttemptApply);
        Assert.IsTrue(
            vectorPreflight.ControllerSpecificAttributionAvailable,
            "Disjoint device-associated IRQ vectors should enable the stronger controller-specific ETW path.");

        var vectorPlacement = XhciInterruptRuntimePlacementVerifier.Analyze(
            sharedDriverCapture,
            targetWithAllocation.InstanceId,
            [targetWithAllocation, unknownPeer],
            new DeviceInterruptAffinityCandidate(0, 2, 1UL << 2),
            vectorSnapshots);
        Assert.IsTrue(vectorPlacement.ConfirmsRequestedPlacement);
        var vectorAttribution = XhciInterruptRuntimePlacementVerifier.ResolveIsrAttribution(
            sharedDriverCapture,
            targetWithAllocation.InstanceId,
            [targetWithAllocation, unknownPeer],
            new DeviceInterruptAffinityCandidate(0, 2, 1UL << 2),
            vectorSnapshots);
        Assert.AreEqual(
            XhciInterruptIsrAttributionMode.DeviceInterruptVector,
            vectorAttribution.AttributionMode);
        Assert.AreEqual(1, vectorAttribution.Events.Count);
        Assert.AreEqual(44, vectorAttribution.Events[0].InterruptVector);

        var overlappingVectorSnapshots = new Dictionary<string, DeviceInterruptVectorSnapshot>(
            vectorSnapshots,
            StringComparer.OrdinalIgnoreCase)
        {
            [unknownPeer.InstanceId] = new(
                unknownPeer.InstanceId,
                DeviceInterruptVectorReadStatus.Available,
                [44],
                null),
        };
        var overlappingVectorPreflight = XhciInterruptRuntimePlacementVerifier.AssessApplyPreflight(
            targetWithAllocation.InstanceId,
            [targetWithAllocation, unknownPeer],
            new DeviceInterruptAffinityCandidate(0, 2, 1UL << 2),
            overlappingVectorSnapshots);
        Assert.IsTrue(
            overlappingVectorPreflight.CanAttemptApply,
            "Shared IRQ-vector ownership does not make a journaled Apply unsafe by itself; target translated allocation is checked after Apply.");
        Assert.IsFalse(
            overlappingVectorPreflight.ControllerSpecificAttributionAvailable,
            "Shared vector ownership must still prevent a controller-specific ETW claim.");

        var manuallyFixedGpu = new PnPDeviceSnapshot(
            "PCI\\VEN_TEST&DEV_GPU",
            new Guid("4D36E968-E325-11CE-BFC1-08002BE10318"),
            "Fixed GPU",
            "Test Vendor",
            "PCI",
            "nvlddmkm",
            new DriverMetadataSnapshot("1.0", "Test Vendor", "gpu.inf"),
            InterruptConfigurationSnapshot.Available(null, null, 4, (1UL << 0) | (1UL << 1)),
            InterruptResourceSnapshot.ReadFailed());
        var reservations = InterruptCpuReservationPlanner.Capture(
            new DeviceInventorySnapshot(
                [manuallyFixedGpu, targetWithAllocation, peerWithDisjointAllocation],
                DateTimeOffset.UnixEpoch),
            targetWithAllocation.InstanceId);
        CollectionAssert.AreEquivalent(
            new[] { new LogicalProcessorId(0, 0), new LogicalProcessorId(0, 1) },
            reservations.Processors.ToArray(),
            "A manually fixed GPU policy must reserve its selected CPUs regardless of which benchmark created that policy.");

        var rankedWithReservations = UsbAffinityCpuSelector.Rank(
            topology,
            capture,
            reservations.Processors);
        Assert.IsFalse(
            rankedWithReservations.Any(static candidate => candidate.Processor.Number is 0 or 1),
            "USB benchmarking must skip the entire physical core already reserved by another fixed device policy.");

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

        // Keep this fixture independent from the parser constants: a field-offset or
        // type-value drift in the implementation must make the documented layout fail.
        var irqDescriptor = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(irqDescriptor.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(irqDescriptor.AsSpan(4, 4), 12u);
        BinaryPrimitives.WriteUInt16LittleEndian(irqDescriptor.AsSpan(8, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(irqDescriptor.AsSpan(10, 2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(irqDescriptor.AsSpan(12, 4), 44);
        BinaryPrimitives.WriteUInt64LittleEndian(irqDescriptor.AsSpan(16, 8), 1UL << 7);
        Assert.IsTrue(
            AllocatedIrqDescriptorParser.TryParseResourceList(irqDescriptor, out var parsedIrq),
            "The documented 64-bit IRQ_DES resource-list layout must decode the processor group and KAFFINITY mask without field drift.");
        Assert.AreEqual((ushort)0, parsedIrq.ProcessorGroup);
        Assert.AreEqual(1UL << 7, parsedIrq.AffinityMask);
        Assert.AreEqual(44u, parsedIrq.Irq);

        BinaryPrimitives.WriteUInt64LittleEndian(irqDescriptor.AsSpan(16, 8), 0);
        Assert.IsFalse(
            AllocatedIrqDescriptorParser.TryParseResourceList(irqDescriptor, out _),
            "A zero affinity names no processor and must not be promoted to runtime-placement evidence.");
    }
}

#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using System.Net.NetworkInformation;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class NetworkRssTests
{
    private static readonly string[] ExpectedRssProcessors = ["0:2", "0:4", "0:6", "0:8"];

    [AuditCase]
    public void RssReadOnlyEvidencePreservesAuthoritativeFieldsAttributionAndContinuity()
    {
        var mapped = NetworkRssPropertyMapper.Map(new Dictionary<string, object?>
        {
            ["Name"] = "Ethernet",
            ["InterfaceDescription"] = "Test 10GbE Adapter",
            ["Enabled"] = true,
            ["MsiSupported"] = true,
            ["MsiXSupported"] = true,
            ["MsiXEnabled"] = true,
            ["NumberOfInterruptMessages"] = 8u,
            ["NumberOfReceiveQueues"] = 4u,
            ["Profile"] = 4u,
            ["BaseProcessorGroup"] = (ushort)0,
            ["BaseProcessorNumber"] = (byte)2,
            ["MaxProcessorGroup"] = (ushort)0,
            ["MaxProcessorNumber"] = (byte)14,
            ["MaxProcessors"] = 8u,
            ["NumaNode"] = (ushort)0,
            ["IndirectionTable"] = ExpectedRssProcessors,
            ["RssProcessorArray"] = ExpectedRssProcessors,
        });

        Assert.AreEqual("Ethernet", mapped.Name);
        Assert.AreEqual("Test 10GbE Adapter", mapped.InterfaceDescription);
        Assert.AreEqual(true, mapped.Enabled);
        Assert.AreEqual(true, mapped.MsiSupported);
        Assert.AreEqual(true, mapped.MsiXSupported);
        Assert.AreEqual(true, mapped.MsiXEnabled);
        Assert.AreEqual(8u, mapped.NumberOfInterruptMessages);
        Assert.AreEqual(4u, mapped.NumberOfReceiveQueues);
        Assert.AreEqual((byte)2, mapped.BaseProcessorNumber);
        CollectionAssert.AreEqual(ExpectedRssProcessors, mapped.IndirectionTable.ToArray());
        CollectionAssert.AreEqual(ExpectedRssProcessors, mapped.RssProcessorArray.ToArray());

        var correlated = NetworkRssPnpCorrelator.Resolve(
            mapped.InterfaceDescription,
            [new NetworkAdapterPnpIdentity("Test 10GbE Adapter", "PCI\\VEN_TEST&DEV_NIC", true, true)]);
        Assert.AreEqual(NetworkRssPnpCorrelationStatus.Available, correlated.Status);
        Assert.AreEqual("PCI\\VEN_TEST&DEV_NIC", correlated.PnpInstanceId);

        var ambiguous = NetworkRssPnpCorrelator.Resolve(
            mapped.InterfaceDescription,
            [
                new NetworkAdapterPnpIdentity("Test 10GbE Adapter", "PCI\\VEN_A", true, true),
                new NetworkAdapterPnpIdentity("Test 10GbE Adapter", "PCI\\VEN_B", true, true),
            ]);
        Assert.AreEqual(NetworkRssPnpCorrelationStatus.Ambiguous, ambiguous.Status);

        var adapter = new PnPDeviceSnapshot(
            "PCI\\VEN_TEST&DEV_NIC",
            Guid.NewGuid(),
            "Test 10GbE Adapter",
            "Test Vendor",
            "PCI",
            "e2fexpress",
            new DriverMetadataSnapshot("1.0", "Test Vendor", "e2fexpress.inf"),
            InterruptConfigurationSnapshot.Available(null, null, null, null),
            InterruptResourceSnapshot.Available([]));
        var capture = new KernelLatencyCaptureResult(
            DateTimeOffset.UnixEpoch,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20),
            [
                new KernelLatencyEvent(KernelLatencyEventKind.Dpc, 2, 1, 30, 0x1000, null, null, "C:\\Windows\\System32\\drivers\\e2fexpress.sys"),
                new KernelLatencyEvent(KernelLatencyEventKind.Isr, 2, 2, 6, 0x1001, 44, 0, "C:\\Windows\\System32\\drivers\\E2FEXPRESS.SYS"),
                new KernelLatencyEvent(KernelLatencyEventKind.Dpc, 4, 3, 12, 0x2000, null, null, "C:\\Windows\\System32\\drivers\\ndis.sys"),
            ],
            0,
            0,
            0,
            false);
        var attribution = NetworkInterruptAttribution.Analyze(capture, adapter);
        Assert.AreEqual(1, attribution.MatchingDpcEventCount);
        Assert.AreEqual(1, attribution.MatchingIsrEventCount);
        Assert.AreEqual(1, attribution.GenericNdisEventCount);
        Assert.IsTrue(attribution.HasTargetEvidence);

        var rss = mapped with
        {
            PnpCorrelation = new NetworkRssPnpCorrelation(
                NetworkRssPnpCorrelationStatus.Available,
                adapter.InstanceId,
                null),
        };
        var physicalRss = rss with
        {
            HardwareInterface = true,
            ConnectorPresent = true,
        };
        var virtualRss = rss with
        {
            Name = "vEthernet",
            HardwareInterface = false,
            ConnectorPresent = false,
        };
        var physicalSelection = NetworkRssPhysicalAdapterSelector.Select(new NetworkRssSnapshot(
            NetworkRssReadStatus.Available,
            [virtualRss, physicalRss],
            DateTimeOffset.UnixEpoch,
            null));
        Assert.AreEqual(1, physicalSelection.Count);
        Assert.AreEqual("Ethernet", physicalSelection[0].Name);

        var physicalWithoutRss = physicalRss with
        {
            Name = "Realtek PCIe GbE Family Controller",
            InterfaceDescription = "Realtek PCIe GbE Family Controller",
            RssSettingsAvailable = false,
            HardwareInterface = false,
            ConnectorPresent = null,
            Enabled = null,
            NumberOfReceiveQueues = null,
            RssProcessorArray = [],
            PnpCorrelation = new NetworkRssPnpCorrelation(
                NetworkRssPnpCorrelationStatus.Available,
                "PCI\\VEN_10EC&DEV_8168",
                null),
        };
        var physicalWithoutRssSelection = NetworkRssPhysicalAdapterSelector.Select(
            new NetworkRssSnapshot(
                NetworkRssReadStatus.Available,
                [virtualRss, physicalWithoutRss],
                DateTimeOffset.UnixEpoch,
                null));
        Assert.AreEqual(1, physicalWithoutRssSelection.Count);
        Assert.IsFalse(physicalWithoutRssSelection[0].RssSettingsAvailable,
            "A physical NIC must remain analyzable when Windows/driver does not expose MSFT_NetAdapterRssSettingData.");
        Assert.AreEqual(
            "PCI\\VEN_10EC&DEV_8168",
            physicalWithoutRssSelection[0].PnpCorrelation.PnpInstanceId,
            "Authoritative PCI/USB PnP identity is sufficient physical evidence even if a provider flag is missing or wrong.");

        var usableCoverage = NetworkRssInspectionCoverage.Evaluate(new NetworkRssSnapshot(
            NetworkRssReadStatus.Available,
            [rss],
            DateTimeOffset.UnixEpoch,
            null));
        Assert.IsTrue(usableCoverage.IsUsable);
        Assert.IsFalse(NetworkRssInspectionCoverage.Evaluate(new NetworkRssSnapshot(
            NetworkRssReadStatus.Available,
            [],
            DateTimeOffset.UnixEpoch,
            null)).IsUsable);
        var noRssCoverage = NetworkRssInspectionCoverage.Evaluate(new NetworkRssSnapshot(
            NetworkRssReadStatus.Available,
            [physicalWithoutRss],
            DateTimeOffset.UnixEpoch,
            null));
        Assert.IsFalse(noRssCoverage.IsUsable,
            "RSS provider coverage remains unavailable even though the physical NIC itself is still analyzable.");

        var targetInterface = new NetworkInterfaceContinuitySnapshot(
            "interface-1",
            rss.InterfaceDescription!,
            NetworkInterfaceType.Ethernet,
            OperationalStatus.Up,
            12,
            12,
            ["192.0.2.10"],
            ["192.0.2.1"],
            WifiConnectionContinuityStatus.NotApplicable,
            null,
            null);
        var before = new NetworkEnvironmentContinuitySnapshot(
            adapter.InstanceId,
            rss,
            targetInterface,
            [targetInterface.InterfaceId],
            new SystemAwakeTimeSnapshot(1_000, 10_000_000));
        var after = before with { AwakeTime = new SystemAwakeTimeSnapshot(21_000, 210_000_000) };
        Assert.IsTrue(NetworkEnvironmentContinuity.Evaluate(before, after).IsStable);
        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(
            before,
            after with { UpInterfaceIds = [targetInterface.InterfaceId, "vpn-interface"] }).IsStable);
        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(
            before,
            after with { Rss = rss with { IndirectionTable = ["0:2", "0:4", "0:6", "0:10"] } }).IsStable);
    }
}

using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class NetworkRssTests
{
    private static readonly string[] ExpectedRssProcessors = ["0:2", "0:4", "0:6", "0:8"];

    [TestMethod]
    public void RssProviderMappingPreservesAuthoritativeFieldsAndPnpCorrelation()
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
        Assert.AreEqual(4u, mapped.Profile);
        Assert.AreEqual((ushort)0, mapped.BaseProcessorGroup);
        Assert.AreEqual((byte)2, mapped.BaseProcessorNumber);
        Assert.AreEqual((ushort)0, mapped.MaxProcessorGroup);
        Assert.AreEqual((byte)14, mapped.MaxProcessorNumber);
        Assert.AreEqual(8u, mapped.MaxProcessors);
        Assert.AreEqual((ushort)0, mapped.NumaNode);
        CollectionAssert.AreEqual(ExpectedRssProcessors, mapped.IndirectionTable.ToArray());
        CollectionAssert.AreEqual(ExpectedRssProcessors, mapped.RssProcessorArray.ToArray());

        var correlated = NetworkRssPnpCorrelator.Resolve(
            mapped.InterfaceDescription,
            [new NetworkAdapterPnpIdentity("Test 10GbE Adapter", "PCI\\VEN_TEST&DEV_NIC")]);
        Assert.AreEqual(NetworkRssPnpCorrelationStatus.Available, correlated.Status);
        Assert.AreEqual("PCI\\VEN_TEST&DEV_NIC", correlated.PnpInstanceId);

        var ambiguous = NetworkRssPnpCorrelator.Resolve(
            mapped.InterfaceDescription,
            [
                new NetworkAdapterPnpIdentity("Test 10GbE Adapter", "PCI\\VEN_A"),
                new NetworkAdapterPnpIdentity("Test 10GbE Adapter", "PCI\\VEN_B"),
            ]);
        Assert.AreEqual(NetworkRssPnpCorrelationStatus.Ambiguous, ambiguous.Status);
        Assert.IsNull(ambiguous.PnpInstanceId);

        var missing = NetworkRssPropertyMapper.Map(new Dictionary<string, object?>
        {
            ["Name"] = "Ethernet 2",
        });
        Assert.IsNull(missing.InterfaceDescription);
        Assert.IsNull(missing.Enabled);
        Assert.IsNull(missing.NumberOfReceiveQueues);
        Assert.AreEqual(0, missing.IndirectionTable.Count);
        Assert.AreEqual(0, missing.RssProcessorArray.Count);
        Assert.AreEqual(
            NetworkRssPnpCorrelationStatus.MissingIdentity,
            NetworkRssPnpCorrelator.Resolve(missing.InterfaceDescription, []).Status);
    }
}

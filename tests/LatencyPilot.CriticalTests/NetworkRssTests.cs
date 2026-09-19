#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using System.Net.NetworkInformation;
using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Devices;
using LatencyPilot.Core.Observation;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class NetworkRssTests
{
    private static readonly string[] ExpectedRssProcessors = ["0:2", "0:4", "0:6", "0:8"];

    [AuditCase]
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

        var observations = Enumerable.Range(0, 25)
            .Select(index => new NetworkProbeObservation(
                index * 10d,
                true,
                1d + ((index % 5) * 0.1d),
                940d + (index % 3),
                5d + (index % 2)))
            .ToArray();
        var benchmark = LocalNetworkBenchmark.Analyze(NetworkBenchmarkScope.LocalAuthoritative, observations);
        Assert.AreEqual(NetworkBenchmarkStatus.Available, benchmark.Status);
        Assert.IsTrue(benchmark.IsAuthoritative);
        Assert.AreEqual(0d, benchmark.LossRatio, 0.000001);
        Assert.AreEqual(25, benchmark.Metrics[NetworkBenchmarkMetricNames.RoundTripTime].Samples.Count);
        Assert.AreEqual(24, benchmark.Metrics[NetworkBenchmarkMetricNames.Jitter].Samples.Count);
        Assert.AreEqual(25, benchmark.Metrics[NetworkBenchmarkMetricNames.LossIndicator].Samples.Count);
        Assert.AreEqual(25, benchmark.Metrics[NetworkBenchmarkMetricNames.Throughput].Samples.Count);
        Assert.AreEqual(25, benchmark.Metrics[NetworkBenchmarkMetricNames.CpuUtilization].Samples.Count);

        var withLoss = observations.ToArray();
        withLoss[12] = withLoss[12] with { Success = false, RoundTripMilliseconds = null };
        var lossResult = LocalNetworkBenchmark.Analyze(NetworkBenchmarkScope.LocalAuthoritative, withLoss);
        Assert.AreEqual(NetworkBenchmarkStatus.Available, lossResult.Status);
        Assert.AreEqual(1d / 25d, lossResult.LossRatio, 0.000001);
        Assert.AreEqual(24, lossResult.Metrics[NetworkBenchmarkMetricNames.RoundTripTime].Samples.Count);
        Assert.ThrowsExactly<ArgumentException>(() => LocalNetworkBenchmark.Analyze(
            NetworkBenchmarkScope.LocalAuthoritative,
            withLoss.Select((item, index) => index == 12 ? item with { RoundTripMilliseconds = 0d } : item).ToArray()));

        var supplemental = LocalNetworkBenchmark.Analyze(NetworkBenchmarkScope.InternetSupplemental, observations);
        Assert.AreEqual(NetworkBenchmarkStatus.SupplementalOnly, supplemental.Status);
        Assert.IsFalse(supplemental.IsAuthoritative);
        Assert.AreEqual(
            NetworkBenchmarkStatus.InsufficientSamples,
            LocalNetworkBenchmark.Analyze(NetworkBenchmarkScope.LocalAuthoritative, observations.Take(10).ToArray()).Status);

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

        var usableCoverage = NetworkRssInspectionCoverage.Evaluate(new NetworkRssSnapshot(
            NetworkRssReadStatus.Available,
            [rss],
            DateTimeOffset.UnixEpoch,
            null));
        Assert.IsTrue(usableCoverage.IsUsable);
        Assert.AreEqual(1, usableCoverage.ProviderRowCount);
        Assert.AreEqual(1, usableCoverage.PnpCorrelatedRowCount);

        Assert.IsFalse(NetworkRssInspectionCoverage.Evaluate(new NetworkRssSnapshot(
            NetworkRssReadStatus.Available,
            [],
            DateTimeOffset.UnixEpoch,
            null)).IsUsable);
        Assert.IsFalse(NetworkRssInspectionCoverage.Evaluate(new NetworkRssSnapshot(
            NetworkRssReadStatus.Available,
            [mapped],
            DateTimeOffset.UnixEpoch,
            null)).IsUsable);
        Assert.IsFalse(NetworkRssInspectionCoverage.Evaluate(new NetworkRssSnapshot(
            NetworkRssReadStatus.ProviderUnavailable,
            [rss],
            DateTimeOffset.UnixEpoch,
            "provider missing")).IsUsable);

        var ready = NetworkOptimizationReadiness.Evaluate(rss, adapter, benchmark, attribution);
        Assert.AreEqual(NetworkOptimizationReadinessStatus.Ready, ready.Status);
        Assert.AreEqual(adapter.InstanceId, ready.AdapterInstanceId);
        Assert.IsTrue(ready.TargetMetrics.ContainsKey(NetworkBenchmarkMetricNames.RoundTripTime));
        Assert.IsTrue(ready.TargetMetrics.ContainsKey(NetworkBenchmarkMetricNames.Jitter));
        CollectionAssert.Contains(ready.RequiredGuardrails.ToList(), NetworkBenchmarkMetricNames.LossIndicator);
        CollectionAssert.Contains(ready.RequiredGuardrails.ToList(), NetworkBenchmarkMetricNames.Throughput);
        CollectionAssert.Contains(ready.RequiredGuardrails.ToList(), NetworkBenchmarkMetricNames.CpuUtilization);

        Assert.AreEqual(
            NetworkOptimizationReadinessStatus.NotReady,
            NetworkOptimizationReadiness.Evaluate(
                rss with
                {
                    PnpCorrelation = new NetworkRssPnpCorrelation(
                        NetworkRssPnpCorrelationStatus.Available,
                        "PCI\\VEN_OTHER",
                        null),
                },
                adapter,
                benchmark,
                attribution).Status);
        Assert.AreEqual(
            NetworkOptimizationReadinessStatus.Inconclusive,
            NetworkOptimizationReadiness.Evaluate(rss, adapter, supplemental, attribution).Status);
        Assert.AreEqual(
            NetworkOptimizationReadinessStatus.Inconclusive,
            NetworkOptimizationReadiness.Evaluate(
                rss,
                adapter,
                benchmark,
                attribution with
                {
                    MatchingDpcEventCount = 0,
                    MatchingIsrEventCount = 0,
                    DpcDurationsMicroseconds = [],
                    IsrDurationsMicroseconds = [],
                }).Status);

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
        var continuityBefore = new NetworkEnvironmentContinuitySnapshot(
            adapter.InstanceId,
            rss,
            targetInterface,
            [targetInterface.InterfaceId],
            new SystemAwakeTimeSnapshot(1_000, 10_000_000));
        var continuityAfter = continuityBefore with
        {
            AwakeTime = new SystemAwakeTimeSnapshot(21_000, 210_000_000),
        };
        var stableContinuity = NetworkEnvironmentContinuity.Evaluate(continuityBefore, continuityAfter);
        Assert.IsTrue(stableContinuity.IsStable);
        Assert.AreEqual(
            NetworkOptimizationReadinessStatus.Ready,
            NetworkOptimizationReadiness.EvaluateForExperiment(
                rss,
                adapter,
                benchmark,
                attribution,
                stableContinuity).Status);

        var vpnChurn = NetworkEnvironmentContinuity.Evaluate(
            continuityBefore,
            continuityAfter with { UpInterfaceIds = [targetInterface.InterfaceId, "vpn-interface"] });
        Assert.IsFalse(vpnChurn.IsStable);
        Assert.AreEqual(
            NetworkOptimizationReadinessStatus.Inconclusive,
            NetworkOptimizationReadiness.EvaluateForExperiment(
                rss,
                adapter,
                benchmark,
                attribution,
                vpnChurn).Status);

        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(
            continuityBefore,
            continuityAfter with
            {
                Rss = rss with { IndirectionTable = ["0:2", "0:4", "0:6", "0:10"] },
            }).IsStable);

        var wifiTarget = targetInterface with
        {
            InterfaceType = NetworkInterfaceType.Wireless80211,
            WifiStatus = WifiConnectionContinuityStatus.Available,
            WifiSsid = "TestWifi",
            WifiBssid = "00:11:22:33:44:55",
        };
        var wifiBefore = continuityBefore with { TargetInterface = wifiTarget };
        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(
            wifiBefore,
            continuityAfter with
            {
                TargetInterface = wifiTarget with { WifiBssid = "00:11:22:33:44:66" },
            }).IsStable);
        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(
            wifiBefore,
            continuityAfter with
            {
                TargetInterface = wifiTarget with
                {
                    WifiStatus = WifiConnectionContinuityStatus.PermissionDenied,
                    WifiBssid = null,
                },
            }).IsStable);
    }
}

using System.Net.NetworkInformation;
using LatencyPilot.Core.Devices;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class RuntimeContinuityEdgeCaseTests
{
    [TestMethod]
    public void GpuCaptureRejectsSleepAndHybridTargetAmbiguity()
    {
        var target = new PnPDeviceSnapshot(
            "PCI\\VEN_10DE&DEV_2484&SUBSYS_147A10DE&REV_A1\\4&TEST",
            new Guid("4D36E968-E325-11CE-BFC1-08002BE10318"),
            "Test NVIDIA GPU",
            "NVIDIA",
            "PCI",
            "nvlddmkm",
            new DriverMetadataSnapshot("1.0", "NVIDIA", "display.inf"),
            InterruptConfigurationSnapshot.Available(1, null, null, null),
            InterruptResourceSnapshot.Available([]));
        var luid = new GraphicsAdapterLuid(0x12345678, 0x10203040);
        var dxgi = new GraphicsAdapterSnapshot(
            0,
            "Test NVIDIA GPU",
            0x10DE,
            0x2484,
            0x147A10DE,
            0xA1,
            8UL * 1024 * 1024 * 1024,
            0,
            16UL * 1024 * 1024 * 1024,
            luid,
            0);
        var presentMon = new PresentMonDeviceInventory(
            PresentMonDiscoveryStatus.Available,
            [new PresentMonGraphicsDeviceSnapshot(7, 0, "Test NVIDIA GPU", luid)],
            "PresentMonAPI2.dll",
            0,
            null,
            DateTimeOffset.UnixEpoch);

        var resolved = GpuGraphicsTargetIdentityResolver.Resolve(
            target,
            new GraphicsAdapterInventory([dxgi], DateTimeOffset.UnixEpoch),
            presentMon);
        Assert.IsTrue(resolved.IsUsable);
        Assert.IsNotNull(resolved.Identity);
        Assert.AreEqual((uint)7, resolved.Identity.PresentMonDeviceId);

        var secondAdapter = new GraphicsAdapterSnapshot(
            1,
            "Integrated GPU",
            0x8086,
            0x1234,
            0,
            1,
            0,
            0,
            8UL * 1024 * 1024 * 1024,
            new GraphicsAdapterLuid(0x87654321, 0x01020304),
            0);
        Assert.IsFalse(GpuGraphicsTargetIdentityResolver.Resolve(
            target,
            new GraphicsAdapterInventory([dxgi, secondAdapter], DateTimeOffset.UnixEpoch),
            presentMon).IsUsable);
        Assert.IsFalse(GpuGraphicsTargetIdentityResolver.Resolve(
            target,
            new GraphicsAdapterInventory([dxgi], DateTimeOffset.UnixEpoch),
            presentMon with
            {
                GraphicsDevices = [new PresentMonGraphicsDeviceSnapshot(
                    7,
                    0,
                    "Wrong GPU",
                    new GraphicsAdapterLuid(1, 2))],
            }).IsUsable);

        var balancedScheme = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        var power = new SystemPowerSnapshot(
            SystemPowerLineState.Online,
            BatteryPresent: false,
            Charging: null,
            BatteryPercent: null,
            BatterySaverEnabled: false,
            ActiveSchemeId: balancedScheme,
            ActiveSchemeName: "Balanced",
            UserConfiguredPowerModeId: Guid.Empty,
            UserConfiguredPowerMode: UserConfiguredPowerMode.Balanced);
        var before = new GpuOptimizationCaptureContinuitySnapshot(
            42,
            DateTimeOffset.UnixEpoch,
            "game",
            1,
            1,
            power,
            new SystemAwakeTimeSnapshot(1_000, 10_000_000),
            resolved.Identity);
        var after = before with
        {
            AwakeTime = new SystemAwakeTimeSnapshot(31_000, 310_000_000),
        };
        Assert.IsTrue(GpuOptimizationCaptureContinuity.Evaluate(before, after).IsStable);

        var slept = after with
        {
            AwakeTime = new SystemAwakeTimeSnapshot(36_000, 310_000_000),
        };
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(before, slept).IsStable);
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(
            before,
            after with
            {
                GraphicsTarget = after.GraphicsTarget with { PresentMonDeviceId = 8 },
            }).IsStable);
    }

    [TestMethod]
    public void NetworkBenchmarkRejectsRouteVpnWifiAndRssChurn()
    {
        var rss = new NetworkRssAdapterSnapshot(
            "Ethernet",
            "Test 10GbE Adapter",
            true,
            true,
            true,
            true,
            8,
            4,
            4,
            0,
            2,
            0,
            14,
            8,
            0,
            ["0:2", "0:4", "0:6", "0:8"],
            ["0:2", "0:4", "0:6", "0:8"])
        {
            PnpCorrelation = new NetworkRssPnpCorrelation(
                NetworkRssPnpCorrelationStatus.Available,
                "PCI\\VEN_TEST&DEV_NIC",
                null),
        };
        var target = new NetworkInterfaceContinuitySnapshot(
            "interface-1",
            "Test 10GbE Adapter",
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
            "PCI\\VEN_TEST&DEV_NIC",
            rss,
            target,
            ["interface-1"],
            new SystemAwakeTimeSnapshot(1_000, 10_000_000));
        var after = before with
        {
            AwakeTime = new SystemAwakeTimeSnapshot(21_000, 210_000_000),
        };

        Assert.IsTrue(NetworkEnvironmentContinuity.Evaluate(before, after).IsStable);
        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(
            before,
            after with { UpInterfaceIds = ["interface-1", "vpn-interface"] }).IsStable);
        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(
            before,
            after with
            {
                Rss = rss with { IndirectionTable = ["0:2", "0:4", "0:6", "0:10"] },
            }).IsStable);

        var wifiTarget = target with
        {
            InterfaceType = NetworkInterfaceType.Wireless80211,
            WifiStatus = WifiConnectionContinuityStatus.Available,
            WifiSsid = "TestWifi",
            WifiBssid = "00:11:22:33:44:55",
        };
        var wifiBefore = before with { TargetInterface = wifiTarget };
        var wifiAfter = after with
        {
            TargetInterface = wifiTarget with { WifiBssid = "00:11:22:33:44:66" },
        };
        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(wifiBefore, wifiAfter).IsStable);
        Assert.IsFalse(NetworkEnvironmentContinuity.Evaluate(
            wifiBefore,
            after with
            {
                TargetInterface = wifiTarget with
                {
                    WifiStatus = WifiConnectionContinuityStatus.PermissionDenied,
                    WifiBssid = null,
                },
            }).IsStable);
    }
}

using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Platform.Windows.Devices;
using LatencyPilot.Platform.Windows.System;
using LatencyPilot.Service;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuRuntimePlacementContractTests
{
    [TestMethod]
    public void GateAPlacementAndCaptureContinuityFailClosed()
    {
        var confirmed = new GpuInterruptRuntimePlacementEvidence(
            "PCI\\VEN_10DE&DEV_TEST",
            "nvlddmkm",
            3,
            200,
            200,
            0,
            5,
            [new ProcessorObservedInterruptCount(3, 200)]);

        Assert.IsTrue(confirmed.ConfirmsRequestedPlacement);
        Assert.IsTrue(
            GpuInterruptRuntimePlacementVerifier.ConfirmsGateAPlacement(
                storedCandidateBefore: true,
                storedCandidateAfter: true,
                captureIsValid: true,
                confirmed));

        Assert.IsFalse(
            GpuInterruptRuntimePlacementVerifier.ConfirmsGateAPlacement(
                storedCandidateBefore: false,
                storedCandidateAfter: true,
                captureIsValid: true,
                confirmed));
        Assert.IsFalse(
            GpuInterruptRuntimePlacementVerifier.ConfirmsGateAPlacement(
                storedCandidateBefore: true,
                storedCandidateAfter: false,
                captureIsValid: true,
                confirmed));
        Assert.IsFalse(
            GpuInterruptRuntimePlacementVerifier.ConfirmsGateAPlacement(
                storedCandidateBefore: true,
                storedCandidateAfter: true,
                captureIsValid: false,
                confirmed));

        var missingRuntimeEvidence = confirmed with
        {
            MatchingResolvedIsrEventCount = 0,
            TargetProcessorIsrEventCount = 0,
            ObservedProcessors = [],
        };
        Assert.IsFalse(missingRuntimeEvidence.ConfirmsRequestedPlacement);
        Assert.IsFalse(
            GpuInterruptRuntimePlacementVerifier.ConfirmsGateAPlacement(
                storedCandidateBefore: true,
                storedCandidateAfter: true,
                captureIsValid: true,
                missingRuntimeEvidence));

        var inconsistentResolvedCounts = confirmed with
        {
            TargetProcessorIsrEventCount = 199,
            OffTargetIsrEventCount = 0,
            ObservedProcessors = [new ProcessorObservedInterruptCount(3, 199)],
        };
        Assert.IsFalse(inconsistentResolvedCounts.ConfirmsRequestedPlacement);

        var offTargetEvidence = confirmed with
        {
            TargetProcessorIsrEventCount = 199,
            OffTargetIsrEventCount = 1,
            ObservedProcessors =
            [
                new ProcessorObservedInterruptCount(3, 199),
                new ProcessorObservedInterruptCount(4, 1),
            ],
        };
        Assert.IsFalse(offTargetEvidence.ConfirmsRequestedPlacement);

        var expectedRevision = new string('a', 40);
        Assert.IsTrue(GpuOptimizationSourceRevisionPolicy.IsExactMatch(expectedRevision, expectedRevision.ToUpperInvariant()));
        Assert.IsFalse(GpuOptimizationSourceRevisionPolicy.IsExactMatch(expectedRevision, new string('a', 39)));
        Assert.IsFalse(GpuOptimizationSourceRevisionPolicy.IsExactMatch(expectedRevision, new string('b', 40)));
        Assert.IsFalse(GpuOptimizationSourceRevisionPolicy.IsExactMatch(expectedRevision, null));
        Assert.IsFalse(GpuOptimizationSourceRevisionPolicy.IsExactMatch("dirty", expectedRevision));

        var balancedScheme = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
        var stablePower = new SystemPowerSnapshot(
            SystemPowerLineState.Online,
            BatteryPresent: false,
            Charging: null,
            BatteryPercent: null,
            BatterySaverEnabled: false,
            ActiveSchemeId: balancedScheme,
            ActiveSchemeName: "Balanced",
            UserConfiguredPowerModeId: Guid.Empty,
            UserConfiguredPowerMode: LatencyPilot.Platform.Windows.System.UserConfiguredPowerMode.Balanced);
        var stableContext = new GpuOptimizationCaptureContinuitySnapshot(
            ProcessId: 42,
            ProcessStartedAtUtc: DateTimeOffset.UnixEpoch,
            ProcessName: "game",
            ProcessSessionId: 1,
            ActiveConsoleSessionId: 1,
            Power: stablePower);

        Assert.IsTrue(GpuOptimizationCaptureContinuity.Evaluate(stableContext, stableContext).IsStable);
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(
            stableContext,
            stableContext with { ProcessStartedAtUtc = stableContext.ProcessStartedAtUtc.AddSeconds(1) }).IsStable);
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(
            stableContext,
            stableContext with { ActiveConsoleSessionId = 2 }).IsStable);
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(
            stableContext with { ProcessSessionId = 2 },
            stableContext with { ProcessSessionId = 2 }).IsStable);
        Assert.IsFalse(GpuOptimizationCaptureContinuity.Evaluate(
            stableContext,
            stableContext with
            {
                Power = stablePower with { BatterySaverEnabled = true },
            }).IsStable);
    }
}

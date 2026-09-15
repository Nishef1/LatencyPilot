using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Platform.Windows.Devices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GpuRuntimePlacementContractTests
{
    [TestMethod]
    public void GateAPlacementRequiresResolvedIsrEvidenceConfinedToTargetProcessor()
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
    }
}

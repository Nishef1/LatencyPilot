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

        var missingRuntimeEvidence = confirmed with
        {
            MatchingResolvedIsrEventCount = 0,
            TargetProcessorIsrEventCount = 0,
            ObservedProcessors = [],
        };
        Assert.IsFalse(missingRuntimeEvidence.ConfirmsRequestedPlacement);

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
    }
}

using LatencyPilot.Benchmarking.Optimization;
using LatencyPilot.Core.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class OptimizationProfileTests
{
    private static readonly string[] ExpectedDominatingMetricOrder =
    [
        "input-report-p99",
        "frame-time-p99",
        "network-jitter-p99",
    ];

    [TestMethod]
    public void ProfilesAndParetoPolicyRemainTransparentAndFailClosed()
    {
        var competitive = OptimizationProfiles.Get(OptimizationProfileKind.CompetitiveGaming);
        var general = OptimizationProfiles.Get(OptimizationProfileKind.General);
        var audio = OptimizationProfiles.Get(OptimizationProfileKind.AudioSensitive);

        Assert.AreEqual("competitive-gaming-v1", competitive.Id);
        Assert.AreEqual("general-v1", general.Id);
        Assert.AreEqual("audio-sensitive-v1", audio.Id);
        Assert.AreEqual(1, competitive.Version);
        competitive.ComparisonPolicy.Validate();
        general.ComparisonPolicy.Validate();
        audio.ComparisonPolicy.Validate();
        Assert.IsTrue(competitive.IsSubsystemEnabled(OptimizationSubsystem.Gpu));
        Assert.IsTrue(competitive.IsSubsystemEnabled(OptimizationSubsystem.Usb));
        Assert.IsTrue(competitive.IsSubsystemEnabled(OptimizationSubsystem.Network));

        var networkOptOut = competitive.WithSubsystemEnabled(OptimizationSubsystem.Network, enabled: false);
        Assert.IsFalse(networkOptOut.IsSubsystemEnabled(OptimizationSubsystem.Network));
        Assert.IsTrue(networkOptOut.IsSubsystemEnabled(OptimizationSubsystem.Gpu));
        Assert.AreEqual(competitive.ComparisonPolicy, networkOptOut.ComparisonPolicy);
        Assert.AreEqual(competitive.Id, networkOptOut.Id);

        var dominates = ParetoDecisionPolicy.Evaluate(
        [
            new ParetoMetricOutcome("input-report-p99", ExperimentVerdict.Improved),
            new ParetoMetricOutcome("frame-time-p99", ExperimentVerdict.NoMeasurableDifference),
            new ParetoMetricOutcome("network-jitter-p99", ExperimentVerdict.Improved),
        ]);
        Assert.AreEqual(ParetoRelation.Dominates, dominates.Relation);
        CollectionAssert.AreEqual(
            ExpectedDominatingMetricOrder,
            dominates.Outcomes.Select(static outcome => outcome.MetricName).ToArray());

        Assert.AreEqual(
            ParetoRelation.Dominated,
            ParetoDecisionPolicy.Evaluate(
            [
                new ParetoMetricOutcome("input-report-p99", ExperimentVerdict.Regressed),
                new ParetoMetricOutcome("frame-time-p99", ExperimentVerdict.NoMeasurableDifference),
            ]).Relation);
        Assert.AreEqual(
            ParetoRelation.Equivalent,
            ParetoDecisionPolicy.Evaluate(
            [
                new ParetoMetricOutcome("input-report-p99", ExperimentVerdict.NoMeasurableDifference),
                new ParetoMetricOutcome("frame-time-p99", ExperimentVerdict.NoMeasurableDifference),
            ]).Relation);
        Assert.AreEqual(
            ParetoRelation.Tradeoff,
            ParetoDecisionPolicy.Evaluate(
            [
                new ParetoMetricOutcome("input-report-p99", ExperimentVerdict.Improved),
                new ParetoMetricOutcome("frame-time-p99", ExperimentVerdict.Regressed),
            ]).Relation);
        Assert.AreEqual(
            ParetoRelation.Tradeoff,
            ParetoDecisionPolicy.Evaluate(
            [
                new ParetoMetricOutcome("input-report-p99", ExperimentVerdict.Tradeoff),
            ]).Relation);
        Assert.AreEqual(
            ParetoRelation.Inconclusive,
            ParetoDecisionPolicy.Evaluate(
            [
                new ParetoMetricOutcome("input-report-p99", ExperimentVerdict.Improved),
                new ParetoMetricOutcome("frame-time-p99", ExperimentVerdict.Inconclusive),
            ]).Relation);
        Assert.AreEqual(ParetoRelation.Inconclusive, ParetoDecisionPolicy.Evaluate([]).Relation);
        Assert.ThrowsExactly<ArgumentException>(() => ParetoDecisionPolicy.Evaluate(
        [
            new ParetoMetricOutcome("same", ExperimentVerdict.Improved),
            new ParetoMetricOutcome("same", ExperimentVerdict.NoMeasurableDifference),
        ]));
        Assert.IsNull(typeof(ParetoDecisionResult).GetProperty("Score"));
    }
}

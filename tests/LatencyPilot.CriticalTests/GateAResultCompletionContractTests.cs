#pragma warning disable CA1822 // AuditCase methods are reflection-invoked by ConsolidatedCriticalTests.
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LatencyPilot.CriticalTests;

[TestClass]
public sealed class GateAResultCompletionContractTests
{
    [AuditCase]
    public void ValidatedGateACompletionPackagesEvidenceWithoutAutoOpeningRawJson()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "LatencyPilot.App",
            "GateAValidationExperience.cs"));

        var validationMarker = source.IndexOf(
            "report.SessionId != sessionId",
            StringComparison.Ordinal);
        var packagingMarker = source.IndexOf(
            "GateAEvidenceBundleExporter.TryCreateAsync(",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, validationMarker);
        Assert.IsGreaterThan(
            validationMarker,
            packagingMarker,
            "Gate A packaging must run only after the report schema/session identity has been validated.");
        Assert.IsFalse(
            source.Contains("TryRevealReport(reportPath)", StringComparison.Ordinal),
            "Successful Gate A completion must not automatically shell-open the raw JSON report.");
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            "Unable to locate repository file: " + Path.Combine(relativeParts));
    }
}

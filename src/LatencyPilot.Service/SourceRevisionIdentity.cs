namespace LatencyPilot.Service;

public static class SourceRevisionIdentity
{
    public static bool MatchesExpectedCommit(string? productVersion, string expectedCommit)
    {
        if (!IsFullRevision(expectedCommit))
        {
            throw new ArgumentException(
                "Expected commit must be an exact 40-character hexadecimal source revision.",
                nameof(expectedCommit));
        }

        if (string.IsNullOrWhiteSpace(productVersion))
        {
            return false;
        }

        var metadataSeparator = productVersion.IndexOf('+');
        if (metadataSeparator < 0 || metadataSeparator == productVersion.Length - 1)
        {
            return false;
        }

        return productVersion[(metadataSeparator + 1)..]
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(token => string.Equals(token, expectedCommit, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFullRevision(string value) =>
        value.Length == 40 && value.All(static character =>
            character is >= '0' and <= '9' or
            >= 'a' and <= 'f' or
            >= 'A' and <= 'F');
}

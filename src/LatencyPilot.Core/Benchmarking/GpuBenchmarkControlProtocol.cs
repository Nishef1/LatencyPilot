using System.Security.Cryptography;
using System.Text;

namespace LatencyPilot.Core.Benchmarking;

public enum GpuBenchmarkControlCommandKind
{
    RunTrial,
    RecreateRenderer,
    Stop,
}

public sealed record GpuBenchmarkControlCommand(
    string Schema,
    Guid SessionId,
    string Token,
    GpuBenchmarkControlCommandKind Kind,
    int RunNumber,
    int DurationMilliseconds)
{
    public const string SchemaId = "latencypilot-gpu-benchmark-control-v1";

    public static GpuBenchmarkControlCommand RunTrial(
        Guid sessionId,
        string token,
        int runNumber,
        TimeSpan duration) =>
        new(
            SchemaId,
            sessionId,
            token,
            GpuBenchmarkControlCommandKind.RunTrial,
            runNumber,
            checked((int)duration.TotalMilliseconds));

    public static GpuBenchmarkControlCommand RecreateRenderer(Guid sessionId, string token) =>
        new(
            SchemaId,
            sessionId,
            token,
            GpuBenchmarkControlCommandKind.RecreateRenderer,
            0,
            0);

    public static GpuBenchmarkControlCommand Stop(Guid sessionId, string token) =>
        new(
            SchemaId,
            sessionId,
            token,
            GpuBenchmarkControlCommandKind.Stop,
            0,
            0);
}

public enum GpuBenchmarkControlResponseStatus
{
    Ready,
    RendererReady,
    TrialCompleted,
    Stopped,
    Rejected,
    Failed,
}

public sealed record GpuBenchmarkControlResponse(
    string Schema,
    Guid SessionId,
    GpuBenchmarkControlResponseStatus Status,
    int RunNumber,
    uint ProcessId,
    string? ArtifactPath,
    string Message)
{
    public const string SchemaId = "latencypilot-gpu-benchmark-control-response-v1";
}

public static class GpuBenchmarkControlProtocol
{
    public const int TokenHexLength = 64;
    public const int MinimumTrialDurationMilliseconds = 5_000;
    public const int MaximumTrialDurationMilliseconds = 300_000;

    public static bool TryValidate(
        GpuBenchmarkControlCommand? command,
        Guid expectedSessionId,
        string expectedToken,
        out string? reason)
    {
        if (command is null)
        {
            reason = "Benchmark control command is missing.";
            return false;
        }

        if (!string.Equals(command.Schema, GpuBenchmarkControlCommand.SchemaId, StringComparison.Ordinal))
        {
            reason = "Benchmark control command schema is unsupported.";
            return false;
        }

        if (expectedSessionId == Guid.Empty || command.SessionId != expectedSessionId)
        {
            reason = "Benchmark control command belongs to a different or invalid session.";
            return false;
        }

        if (!TokenMatches(command.Token, expectedToken))
        {
            reason = "Benchmark control command authentication failed.";
            return false;
        }

        switch (command.Kind)
        {
            case GpuBenchmarkControlCommandKind.RunTrial:
                if (command.RunNumber <= 0 ||
                    command.DurationMilliseconds is < MinimumTrialDurationMilliseconds or > MaximumTrialDurationMilliseconds)
                {
                    reason = "Benchmark trial command has an invalid run number or duration.";
                    return false;
                }

                break;
            case GpuBenchmarkControlCommandKind.RecreateRenderer:
                if (command.RunNumber != 0 || command.DurationMilliseconds != 0)
                {
                    reason = "Benchmark renderer-recreation command must not carry trial state.";
                    return false;
                }

                break;
            case GpuBenchmarkControlCommandKind.Stop:
                if (command.RunNumber != 0 || command.DurationMilliseconds != 0)
                {
                    reason = "Benchmark stop command must not carry trial state.";
                    return false;
                }

                break;
            default:
                reason = "Benchmark control command kind is unsupported.";
                return false;
        }

        reason = null;
        return true;
    }

    public static bool IsValidToken(string? token) =>
        token is { Length: TokenHexLength } && token.All(Uri.IsHexDigit);

    private static bool TokenMatches(string? actual, string expected)
    {
        if (!IsValidToken(actual) || !IsValidToken(expected))
        {
            return false;
        }

        var actualBytes = Encoding.ASCII.GetBytes(actual!);
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }
}

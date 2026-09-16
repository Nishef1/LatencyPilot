using System.Text.Json;

namespace LatencyPilot.GpuBenchmark;

internal static class BenchmarkProtocol
{
    internal const string ProgressSchema = "latencypilot-gpu-benchmark-progress-v1";
    internal const string FailureSchema = "latencypilot-gpu-benchmark-failure-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static void WriteProgress(
        TextWriter writer,
        Guid sessionId,
        string stage,
        double fraction,
        string message)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!double.IsFinite(fraction) || fraction < 0d || fraction > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction));
        }

        WriteLine(writer, new BenchmarkProgress(
            ProgressSchema,
            sessionId,
            stage,
            fraction,
            message));
    }

    internal static void WriteFailure(TextWriter writer, Guid sessionId, string code, string message)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        WriteLine(writer, new BenchmarkFailure(FailureSchema, sessionId, code, message));
    }

    private static void WriteLine<T>(TextWriter writer, T payload)
    {
        writer.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
        writer.Flush();
    }
}

internal sealed record BenchmarkProgress(
    string Schema,
    Guid SessionId,
    string Stage,
    double Fraction,
    string Message);

internal sealed record BenchmarkFailure(
    string Schema,
    Guid SessionId,
    string Code,
    string Message);

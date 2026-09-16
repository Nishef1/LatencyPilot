using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using LatencyPilot.Core.Benchmarking;

namespace LatencyPilot.GateAValidation;

internal sealed class GpuBenchmarkControlClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Guid sessionId;
    private readonly string token;
    private readonly NamedPipeClientStream pipe;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private uint processId;
    private bool stopped;

    private GpuBenchmarkControlClient(
        Guid sessionId,
        string token,
        NamedPipeClientStream pipe)
    {
        this.sessionId = sessionId;
        this.token = token;
        this.pipe = pipe;
        reader = new StreamReader(
            pipe,
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        writer = new StreamWriter(
            pipe,
            new UTF8Encoding(false),
            bufferSize: 4096,
            leaveOpen: true)
        {
            AutoFlush = true,
        };
    }

    internal uint ProcessId => processId != 0
        ? processId
        : throw new InvalidOperationException("Benchmark control handshake has not established a process identity yet.");

    internal static async Task<GpuBenchmarkControlClient> ConnectAsync(
        string pipeName,
        Guid sessionId,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (sessionId == Guid.Empty || !GpuBenchmarkControlProtocol.IsValidToken(token))
        {
            throw new ArgumentException("Benchmark control identity is incomplete or invalid.");
        }

        var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        var client = new GpuBenchmarkControlClient(sessionId, token, pipe);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            var ready = await client.ReadResponseAsync(deadline.Token, allowProcessBinding: true).ConfigureAwait(false);
            if (ready.Status != GpuBenchmarkControlResponseStatus.Ready || ready.RunNumber != 0)
            {
                throw new InvalidDataException(
                    $"Benchmark control handshake returned unexpected status {ready.Status}.");
            }

            return client;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<string> RunTrialAsync(
        int runNumber,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(stopped, this);
        var command = GpuBenchmarkControlCommand.RunTrial(sessionId, token, runNumber, duration);
        if (!GpuBenchmarkControlProtocol.TryValidate(command, sessionId, token, out var reason))
        {
            throw new ArgumentException(reason, nameof(duration));
        }

        await WriteCommandAsync(command, cancellationToken).ConfigureAwait(false);
        var response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        if (response.Status != GpuBenchmarkControlResponseStatus.TrialCompleted ||
            response.RunNumber != runNumber ||
            string.IsNullOrWhiteSpace(response.ArtifactPath))
        {
            throw new InvalidDataException(
                $"Benchmark trial {runNumber} returned {response.Status}: {response.Message}");
        }

        return Path.GetFullPath(response.ArtifactPath);
    }

    internal async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (stopped)
        {
            return;
        }

        await WriteCommandAsync(
            GpuBenchmarkControlCommand.Stop(sessionId, token),
            cancellationToken).ConfigureAwait(false);
        var response = await ReadResponseAsync(cancellationToken).ConfigureAwait(false);
        if (response.Status != GpuBenchmarkControlResponseStatus.Stopped)
        {
            throw new InvalidDataException(
                $"Benchmark stop returned {response.Status}: {response.Message}");
        }

        stopped = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!stopped && pipe.IsConnected)
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await StopAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                !System.Diagnostics.Debugger.IsAttached &&
                exception is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
            {
                // Closing the authenticated control pipe is the final fallback;
                // the benchmark treats disconnect-before-Stop as a failed session.
            }
        }

        await writer.DisposeAsync().ConfigureAwait(false);
        reader.Dispose();
        pipe.Dispose();
    }

    private async Task WriteCommandAsync(
        GpuBenchmarkControlCommand command,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(command, JsonOptions);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<GpuBenchmarkControlResponse> ReadResponseAsync(
        CancellationToken cancellationToken,
        bool allowProcessBinding = false)
    {
        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new EndOfStreamException("Benchmark control pipe closed unexpectedly.");
        var response = JsonSerializer.Deserialize<GpuBenchmarkControlResponse>(line, JsonOptions)
            ?? throw new InvalidDataException("Benchmark control response is empty.");
        if (!string.Equals(response.Schema, GpuBenchmarkControlResponse.SchemaId, StringComparison.Ordinal) ||
            response.SessionId != sessionId || response.ProcessId == 0)
        {
            throw new InvalidDataException(
                "Benchmark control response failed schema/session/process identity validation.");
        }

        if (processId == 0 && allowProcessBinding)
        {
            processId = response.ProcessId;
        }
        else if (response.ProcessId != processId)
        {
            throw new InvalidDataException(
                "Benchmark control response process identity changed after the authenticated handshake.");
        }

        if (response.Status is GpuBenchmarkControlResponseStatus.Rejected or
            GpuBenchmarkControlResponseStatus.Failed)
        {
            throw new InvalidDataException(response.Message);
        }

        return response;
    }
}

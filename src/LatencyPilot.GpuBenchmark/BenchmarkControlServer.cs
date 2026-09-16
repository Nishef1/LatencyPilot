using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LatencyPilot.Core.Benchmarking;

namespace LatencyPilot.GpuBenchmark;

internal sealed class BenchmarkControlServer(
    BenchmarkOptions options,
    BenchmarkWorkload benchmark,
    D3D12BenchmarkRenderer renderer,
    FrozenBenchmarkWorkload frozenWorkload)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HashSet<int> completedRuns = [];

    internal async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!options.IsControlledSession ||
            options.ControlPipeName is not { Length: > 0 } pipeName ||
            options.ControlToken is not { Length: > 0 } token ||
            options.OutputDirectory is not { Length: > 0 } outputDirectory)
        {
            throw new InvalidOperationException("Benchmark control server requires controlled-session options.");
        }

        Directory.CreateDirectory(outputDirectory);
        using var pipe = CreatePipe(pipeName);
        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(
            pipe,
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(false),
            bufferSize: 4096,
            leaveOpen: true)
        {
            AutoFlush = true,
        };

        await WriteResponseAsync(
            writer,
            new GpuBenchmarkControlResponse(
                GpuBenchmarkControlResponse.SchemaId,
                options.SessionId,
                GpuBenchmarkControlResponseStatus.Ready,
                0,
                checked((uint)Environment.ProcessId),
                null,
                "Benchmark calibrated once; frozen workload is ready for controlled trials."),
            cancellationToken).ConfigureAwait(false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                throw new IOException("Benchmark control client disconnected before sending Stop.");
            }

            GpuBenchmarkControlCommand? command;
            try
            {
                command = JsonSerializer.Deserialize<GpuBenchmarkControlCommand>(line, JsonOptions);
            }
            catch (JsonException exception)
            {
                await WriteRejectedAsync(writer, 0, $"Invalid benchmark control JSON: {exception.Message}", cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (!GpuBenchmarkControlProtocol.TryValidate(command, options.SessionId, token, out var rejection))
            {
                await WriteRejectedAsync(writer, command?.RunNumber ?? 0, rejection!, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            if (command!.Kind == GpuBenchmarkControlCommandKind.Stop)
            {
                await WriteResponseAsync(
                    writer,
                    new GpuBenchmarkControlResponse(
                        GpuBenchmarkControlResponse.SchemaId,
                        options.SessionId,
                        GpuBenchmarkControlResponseStatus.Stopped,
                        0,
                        checked((uint)Environment.ProcessId),
                        null,
                        "Controlled benchmark session stopped cleanly."),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!completedRuns.Add(command.RunNumber))
            {
                await WriteRejectedAsync(
                    writer,
                    command.RunNumber,
                    "Benchmark trial run number was already completed in this session.",
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            var artifactPath = Path.Combine(
                outputDirectory,
                $"gpu-benchmark-{options.SessionId:N}-trial-{command.RunNumber:D4}.json");
            if (File.Exists(artifactPath))
            {
                completedRuns.Remove(command.RunNumber);
                await WriteRejectedAsync(
                    writer,
                    command.RunNumber,
                    "Benchmark trial artifact already exists; refusing to overwrite evidence.",
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                var artifact = await benchmark.RunTrialAsync(
                    renderer,
                    frozenWorkload,
                    TimeSpan.FromMilliseconds(command.DurationMilliseconds),
                    cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    artifactPath,
                    JsonSerializer.Serialize(artifact, JsonOptions),
                    new UTF8Encoding(false),
                    cancellationToken).ConfigureAwait(false);
                await WriteResponseAsync(
                    writer,
                    new GpuBenchmarkControlResponse(
                        GpuBenchmarkControlResponse.SchemaId,
                        options.SessionId,
                        GpuBenchmarkControlResponseStatus.TrialCompleted,
                        command.RunNumber,
                        checked((uint)Environment.ProcessId),
                        artifactPath,
                        "Frozen benchmark trial completed."),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                completedRuns.Remove(command.RunNumber);
                await WriteResponseAsync(
                    writer,
                    new GpuBenchmarkControlResponse(
                        GpuBenchmarkControlResponse.SchemaId,
                        options.SessionId,
                        GpuBenchmarkControlResponseStatus.Failed,
                        command.RunNumber,
                        checked((uint)Environment.ProcessId),
                        null,
                        $"Benchmark trial failed: {exception.GetType().Name}: {exception.Message}"),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static NamedPipeServerStream CreatePipe(string pipeName)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Controlled GPU benchmark sessions require Windows named-pipe ACLs.");
        }

        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        var userSid = identity.User
            ?? throw new InvalidOperationException("The benchmark process has no Windows user SID.");
        var security = new PipeSecurity();
        security.SetOwner(userSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            userSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 4096,
            outBufferSize: 4096,
            security);
    }

    private Task WriteRejectedAsync(
        TextWriter writer,
        int runNumber,
        string message,
        CancellationToken cancellationToken) =>
        WriteResponseAsync(
            writer,
            new GpuBenchmarkControlResponse(
                GpuBenchmarkControlResponse.SchemaId,
                options.SessionId,
                GpuBenchmarkControlResponseStatus.Rejected,
                runNumber,
                checked((uint)Environment.ProcessId),
                null,
                message),
            cancellationToken);

    private static async Task WriteResponseAsync(
        TextWriter writer,
        GpuBenchmarkControlResponse response,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

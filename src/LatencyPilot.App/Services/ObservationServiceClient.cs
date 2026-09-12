using System.IO.Pipes;
using LatencyPilot.Protocol;

namespace LatencyPilot.App.Services;

internal static class ObservationServiceClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    public static async Task<ObservationServiceStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            new ObservationRequest(
                ProtocolVersion.Current,
                Guid.NewGuid(),
                ObservationCommand.GetStatus,
                null),
            cancellationToken).ConfigureAwait(false);

        return response.ServiceStatus
            ?? throw new InvalidDataException("Observation service returned no status payload.");
    }

    public static async Task<KernelLatencyCaptureResponse> CaptureKernelLatencyAsync(
        TimeSpan duration,
        int maximumEvents,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            new ObservationRequest(
                ProtocolVersion.Current,
                Guid.NewGuid(),
                ObservationCommand.CaptureKernelLatency,
                new KernelLatencyCaptureRequest(
                    checked((int)duration.TotalMilliseconds),
                    maximumEvents)),
            cancellationToken).ConfigureAwait(false);

        return response.KernelLatencyCapture
            ?? throw new InvalidDataException("Observation service returned no kernel-latency payload.");
    }

    private static async Task<ObservationResponse> SendAsync(
        ObservationRequest request,
        CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(
            ".",
            ObservationProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await pipe.ConnectAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);

        await PipeMessageFraming.WriteAsync(
            pipe,
            request,
            ObservationProtocol.MaximumRequestBytes,
            cancellationToken).ConfigureAwait(false);

        var response = await PipeMessageFraming.ReadAsync<ObservationResponse>(
            pipe,
            ObservationProtocol.MaximumResponseBytes,
            cancellationToken).ConfigureAwait(false);

        if (response.ProtocolVersion != ProtocolVersion.Current)
        {
            throw new InvalidDataException("Observation service protocol version mismatch.");
        }

        if (response.RequestId != request.RequestId)
        {
            throw new InvalidDataException("Observation service response RequestId mismatch.");
        }

        if (response.Status == ObservationResponseStatus.Error)
        {
            throw new InvalidOperationException(
                response.ErrorMessage ?? "Observation service rejected the request.");
        }

        return response;
    }
}

using System.IO.Pipes;
using LatencyPilot.Protocol;

namespace LatencyPilot.App.Services;

internal static class ObservationServiceClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CaptureCompletionMargin = TimeSpan.FromSeconds(5);

    public static async Task<ObservationServiceStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            new ObservationRequest(
                ProtocolVersion.Current,
                Guid.NewGuid(),
                ObservationCommand.GetStatus,
                null),
            StatusTimeout,
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
            duration + CaptureCompletionMargin,
            cancellationToken).ConfigureAwait(false);

        return response.KernelLatencyCapture
            ?? throw new InvalidDataException("Observation service returned no kernel-latency payload.");
    }

    private static async Task<ObservationResponse> SendAsync(
        ObservationRequest request,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(operationTimeout);
        var operationToken = timeoutSource.Token;

        using var pipe = new NamedPipeClientStream(
            ".",
            ObservationProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(ConnectTimeout, operationToken).ConfigureAwait(false);

            await PipeMessageFraming.WriteAsync(
                pipe,
                request,
                ObservationProtocol.MaximumRequestBytes,
                operationToken).ConfigureAwait(false);

            var response = await PipeMessageFraming.ReadAsync<ObservationResponse>(
                pipe,
                ObservationProtocol.MaximumResponseBytes,
                operationToken).ConfigureAwait(false);

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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutSource.IsCancellationRequested)
        {
            throw new TimeoutException("Observation service operation exceeded its deadline.");
        }
    }
}

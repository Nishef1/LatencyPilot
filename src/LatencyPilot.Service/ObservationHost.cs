using System.ComponentModel;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LatencyPilot.Core.Observation;
using LatencyPilot.Platform.Windows.Etw;
using LatencyPilot.Protocol;
using Microsoft.Extensions.Hosting;

namespace LatencyPilot.Service;

internal sealed class ObservationHost : BackgroundService
{
    private static readonly TimeSpan PipeIoTimeout = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var server = CreateServer();

            try
            {
                await server.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                await HandleClientAsync(server, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (EndOfStreamException)
            {
                // Client disconnected before completing a framed request.
            }
            catch (IOException)
            {
                // Broken pipe or client disconnect. The next loop creates a clean server instance.
            }
            catch (InvalidDataException)
            {
                // Malformed or oversized protocol frame: fail closed by dropping the connection.
            }
            catch (JsonException)
            {
                // Malformed JSON: fail closed by dropping the connection.
            }
        }
    }

    private static NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        AddServiceAccountRule(security, WellKnownSidType.LocalSystemSid);
        AddServiceAccountRule(security, WellKnownSidType.LocalServiceSid);
        AddServiceAccountRule(security, WellKnownSidType.NetworkServiceSid);

        return NamedPipeServerStreamAcl.Create(
            ObservationProtocol.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            inBufferSize: 64 * 1024,
            outBufferSize: 64 * 1024,
            security);
    }

    private static void AddServiceAccountRule(PipeSecurity security, WellKnownSidType sidType) =>
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(sidType, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

    private static async Task HandleClientAsync(
        NamedPipeServerStream server,
        CancellationToken stoppingToken)
    {
        ObservationRequest request;
        using (var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
        {
            requestDeadline.CancelAfter(PipeIoTimeout);
            try
            {
                request = await PipeMessageFraming.ReadAsync<ObservationRequest>(
                    server,
                    ObservationProtocol.MaximumRequestBytes,
                    requestDeadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        var response = HandleRequest(request, stoppingToken);

        using var responseDeadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        responseDeadline.CancelAfter(PipeIoTimeout);
        try
        {
            await PipeMessageFraming.WriteAsync(
                server,
                response,
                ObservationProtocol.MaximumResponseBytes,
                responseDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Client stopped reading. Drop the connection and accept the next request.
        }
    }

    private static ObservationResponse HandleRequest(
        ObservationRequest request,
        CancellationToken stoppingToken)
    {
        if (request.ProtocolVersion != ProtocolVersion.Current)
        {
            return Error(
                request.RequestId,
                ObservationErrorCode.ProtocolVersionMismatch,
                "The application and observation service use different protocol versions.");
        }

        if (request.RequestId == Guid.Empty)
        {
            return Error(
                request.RequestId,
                ObservationErrorCode.InvalidRequest,
                "RequestId must not be empty.");
        }

        return request.Command switch
        {
            ObservationCommand.GetStatus when request.KernelLatencyCapture is null =>
                Ok(
                    request.RequestId,
                    serviceStatus: new ObservationServiceStatus(
                        ServiceBoundary.PrivilegedObservationHostImplemented,
                        ServiceBoundary.MutationAvailable)),

            ObservationCommand.CaptureKernelLatency =>
                CaptureKernelLatency(request, stoppingToken),

            _ => Error(
                request.RequestId,
                ObservationErrorCode.InvalidRequest,
                "The request shape does not match an allowed observation command."),
        };
    }

    private static ObservationResponse CaptureKernelLatency(
        ObservationRequest request,
        CancellationToken stoppingToken)
    {
        var capture = request.KernelLatencyCapture;
        if (capture is null ||
            capture.DurationMilliseconds is < 100 or > ObservationProtocol.MaximumCaptureDurationMilliseconds ||
            capture.MaximumEvents is < 1_000 or > ObservationProtocol.MaximumCaptureEvents)
        {
            return Error(
                request.RequestId,
                ObservationErrorCode.InvalidRequest,
                "Kernel latency capture parameters are outside the allowed bounds.");
        }

        try
        {
            var result = KernelLatencyCapture.Capture(
                new KernelLatencyCaptureOptions(
                    TimeSpan.FromMilliseconds(capture.DurationMilliseconds),
                    capture.MaximumEvents),
                stoppingToken);

            return Ok(
                request.RequestId,
                kernelLatencyCapture: Summarize(result));
        }
        catch (UnauthorizedAccessException)
        {
            return CaptureUnavailable(request.RequestId);
        }
        catch (Win32Exception)
        {
            return CaptureUnavailable(request.RequestId);
        }
        catch (InvalidOperationException)
        {
            return CaptureUnavailable(request.RequestId);
        }
    }

    private static KernelLatencyCaptureResponse Summarize(KernelLatencyCaptureResult result)
    {
        var dpc = CreateDistribution(result.Events
            .Where(static item => item.Kind == KernelLatencyEventKind.Dpc)
            .Select(static item => item.DurationMicroseconds));
        var isr = CreateDistribution(result.Events
            .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
            .Select(static item => item.DurationMicroseconds));

        var processors = result.Events
            .GroupBy(static item => item.ProcessorNumber)
            .OrderBy(static group => group.Key)
            .Select(static group => new ProcessorLatencyDistribution(
                group.Key,
                CreateDistribution(group
                    .Where(static item => item.Kind == KernelLatencyEventKind.Dpc)
                    .Select(static item => item.DurationMicroseconds)),
                CreateDistribution(group
                    .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
                    .Select(static item => item.DurationMicroseconds))))
            .ToArray();

        return new KernelLatencyCaptureResponse(
            result.StartedAtUtc,
            checked((int)result.RequestedDuration.TotalMilliseconds),
            result.ActualDuration.TotalMilliseconds,
            result.EventsLost,
            result.InvalidEventCount,
            result.EventLimitReached,
            dpc,
            isr,
            processors);
    }

    private static LatencyDistribution CreateDistribution(IEnumerable<double> durations)
    {
        var sorted = durations.Order().ToArray();
        if (sorted.Length == 0)
        {
            return new LatencyDistribution(0, null, null, null, null, null);
        }

        return new LatencyDistribution(
            sorted.Length,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            Percentile(sorted, 0.999),
            sorted[^1]);
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static ObservationResponse CaptureUnavailable(Guid requestId) =>
        Error(
            requestId,
            ObservationErrorCode.CaptureUnavailable,
            "The privileged observation service could not start the kernel latency capture.");

    private static ObservationResponse Ok(
        Guid requestId,
        ObservationServiceStatus? serviceStatus = null,
        KernelLatencyCaptureResponse? kernelLatencyCapture = null) =>
        new(
            ProtocolVersion.Current,
            requestId,
            ObservationResponseStatus.Ok,
            ObservationErrorCode.None,
            null,
            serviceStatus,
            kernelLatencyCapture);

    private static ObservationResponse Error(
        Guid requestId,
        ObservationErrorCode errorCode,
        string message) =>
        new(
            ProtocolVersion.Current,
            requestId,
            ObservationResponseStatus.Error,
            errorCode,
            message,
            null,
            null);
}

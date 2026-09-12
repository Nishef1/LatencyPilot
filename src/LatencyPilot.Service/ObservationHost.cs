using System.ComponentModel;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LatencyPilot.Benchmarking.Statistics;
using LatencyPilot.Core.Observation;
using LatencyPilot.Platform.Windows.Etw;
using LatencyPilot.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LatencyPilot.Service;

internal sealed class ObservationHost : BackgroundService
{
    private static readonly TimeSpan PipeIoTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CaptureCompletionMargin = TimeSpan.FromSeconds(5);

    private static readonly Action<ILogger, Exception?> KernelLatencyCaptureFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1001, nameof(KernelLatencyCaptureFailed)),
            "Kernel latency capture failed unexpectedly.");

    private static readonly Action<ILogger, Guid, int, int, Exception?> KernelLatencyCaptureStarted =
        LoggerMessage.Define<Guid, int, int>(
            LogLevel.Information,
            new EventId(1002, nameof(KernelLatencyCaptureStarted)),
            "Kernel latency capture {RequestId} started for {DurationMilliseconds} ms with a {MaximumEvents} event limit.");

    private static readonly Action<ILogger, Guid, double, int, int, Exception?> KernelLatencyCaptureCompleted =
        LoggerMessage.Define<Guid, double, int, int>(
            LogLevel.Information,
            new EventId(1003, nameof(KernelLatencyCaptureCompleted)),
            "Kernel latency capture {RequestId} completed in {ActualDurationMilliseconds} ms with {EventCount} events and {EventsLost} ETW events lost.");

    private static readonly Action<ILogger, Guid, Exception?> ClientDisconnectedDuringOperation =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1004, nameof(ClientDisconnectedDuringOperation)),
            "Observation client disconnected or violated framing while request {RequestId} was executing; the operation was cancelled.");

    private static readonly Action<ILogger, string, Exception?> PipeRequestRejected =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1005, nameof(PipeRequestRejected)),
            "Observation pipe request was rejected before execution: {Reason}.");

    private readonly ILogger<ObservationHost> logger;

    public ObservationHost(ILogger<ObservationHost> logger)
    {
        this.logger = logger;
    }

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
            catch (EndOfStreamException exception)
            {
                PipeRequestRejected(logger, "client disconnected before completing a framed request", exception);
            }
            catch (IOException exception)
            {
                PipeRequestRejected(logger, "broken pipe or client disconnect", exception);
            }
            catch (InvalidDataException exception)
            {
                PipeRequestRejected(logger, "malformed or oversized protocol frame", exception);
            }
            catch (JsonException exception)
            {
                PipeRequestRejected(logger, "malformed or incompatible JSON payload", exception);
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
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
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

    private async Task HandleClientAsync(
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

        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        operationCancellation.CancelAfter(
            TimeSpan.FromMilliseconds(ObservationProtocol.MaximumCaptureDurationMilliseconds) + CaptureCompletionMargin);

        using var disconnectMonitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var disconnectMonitor = MonitorClientDisconnectAsync(
            server,
            operationCancellation,
            disconnectMonitorCancellation.Token);

        ObservationResponse response;
        try
        {
            response = await Task.Run(
                () => HandleRequest(request, operationCancellation.Token),
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            disconnectMonitorCancellation.Cancel();
            await AwaitDisconnectMonitorAsync(disconnectMonitor).ConfigureAwait(false);
        }

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (operationCancellation.IsCancellationRequested || !server.IsConnected)
        {
            ClientDisconnectedDuringOperation(logger, request.RequestId, null);
            return;
        }

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

    private static async Task MonitorClientDisconnectAsync(
        NamedPipeServerStream server,
        CancellationTokenSource operationCancellation,
        CancellationToken cancellationToken)
    {
        var probe = new byte[1];

        try
        {
            _ = await server.ReadAsync(probe, cancellationToken).ConfigureAwait(false);
            operationCancellation.Cancel();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The operation completed normally; stop the disconnect probe.
        }
        catch (IOException)
        {
            operationCancellation.Cancel();
        }
    }

    private static async Task AwaitDisconnectMonitorAsync(Task monitor)
    {
        try
        {
            await monitor.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the normal way to stop the disconnect probe after a response is ready.
        }
    }

    private ObservationResponse HandleRequest(
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

    private ObservationResponse CaptureKernelLatency(
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
            KernelLatencyCaptureStarted(
                logger,
                request.RequestId,
                capture.DurationMilliseconds,
                capture.MaximumEvents,
                null);

            var result = KernelLatencyCapture.Capture(
                new KernelLatencyCaptureOptions(
                    TimeSpan.FromMilliseconds(capture.DurationMilliseconds),
                    capture.MaximumEvents),
                stoppingToken);

            KernelLatencyCaptureCompleted(
                logger,
                request.RequestId,
                result.ActualDuration.TotalMilliseconds,
                result.Events.Count,
                result.EventsLost,
                null);

            return Ok(
                request.RequestId,
                kernelLatencyCapture: Summarize(result));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return CaptureUnavailable(request.RequestId);
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
        catch (Exception exception)
        {
            // A failed capture is an unavailable observation, not a reason to stop the
            // long-lived service host and strand subsequent status/recovery requests.
            KernelLatencyCaptureFailed(logger, exception);
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

        var allModules = result.Events
            .Where(static item => item.ModulePath is not null)
            .GroupBy(static item => item.ModulePath!, StringComparer.OrdinalIgnoreCase)
            .Select(static group => CreateModuleDistribution(group.Key, group))
            .OrderByDescending(static item => item.TotalDurationMicroseconds)
            .ThenBy(static item => item.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var allUnresolvedRoutines = result.Events
            .Where(static item => item.ModulePath is null)
            .GroupBy(static item => item.RoutineAddress)
            .Select(static group => CreateUnresolvedRoutineDistribution(group.Key, group))
            .OrderByDescending(static item => item.TotalDurationMicroseconds)
            .ThenBy(static item => item.RoutineAddress)
            .ToArray();

        return new KernelLatencyCaptureResponse(
            result.StartedAtUtc,
            checked((int)result.RequestedDuration.TotalMilliseconds),
            result.ActualDuration.TotalMilliseconds,
            result.EventsLost,
            result.InvalidEventCount,
            result.InvalidImageEventCount,
            result.EventLimitReached,
            result.ResolvedModuleEventCount,
            result.UnresolvedModuleEventCount,
            allModules.Length > ObservationProtocol.MaximumModuleContributors,
            allUnresolvedRoutines.Length > ObservationProtocol.MaximumUnresolvedRoutineContributors,
            dpc,
            isr,
            processors,
            allModules.Take(ObservationProtocol.MaximumModuleContributors).ToArray(),
            allUnresolvedRoutines.Take(ObservationProtocol.MaximumUnresolvedRoutineContributors).ToArray());
    }

    private static ModuleLatencyDistribution CreateModuleDistribution(
        string path,
        IEnumerable<KernelLatencyEvent> events)
    {
        var materialized = events.ToArray();
        var moduleName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            moduleName = path;
        }

        return new ModuleLatencyDistribution(
            moduleName,
            path,
            materialized.Sum(static item => item.DurationMicroseconds),
            CreateDistribution(materialized
                .Where(static item => item.Kind == KernelLatencyEventKind.Dpc)
                .Select(static item => item.DurationMicroseconds)),
            CreateDistribution(materialized
                .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
                .Select(static item => item.DurationMicroseconds)));
    }

    private static UnresolvedRoutineLatencyDistribution CreateUnresolvedRoutineDistribution(
        ulong routineAddress,
        IEnumerable<KernelLatencyEvent> events)
    {
        var materialized = events.ToArray();
        return new UnresolvedRoutineLatencyDistribution(
            routineAddress,
            materialized.Sum(static item => item.DurationMicroseconds),
            CreateDistribution(materialized
                .Where(static item => item.Kind == KernelLatencyEventKind.Dpc)
                .Select(static item => item.DurationMicroseconds)),
            CreateDistribution(materialized
                .Where(static item => item.Kind == KernelLatencyEventKind.Isr)
                .Select(static item => item.DurationMicroseconds)));
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
            Percentiles.CalculateSorted(sorted, 0.50),
            Percentiles.CalculateSorted(sorted, 0.95),
            Percentiles.CalculateSorted(sorted, 0.99),
            Percentiles.CalculateSorted(sorted, 0.999),
            sorted[^1]);
    }

    private static ObservationResponse CaptureUnavailable(Guid requestId) =>
        Error(
            requestId,
            ObservationErrorCode.CaptureUnavailable,
            "The privileged observation service could not start or complete the kernel latency capture.");

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

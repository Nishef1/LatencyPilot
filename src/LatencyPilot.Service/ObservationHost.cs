using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LatencyPilot.Benchmarking.Statistics;
using LatencyPilot.Core.Observation;
using LatencyPilot.Platform.Windows.Etw;
using LatencyPilot.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace LatencyPilot.Service;

internal sealed class ObservationHost : BackgroundService
{
    private static readonly TimeSpan PipeIoTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CaptureCompletionMargin = TimeSpan.FromSeconds(5);
    private const double DpcGuidanceThresholdMicroseconds = 100d;
    private const double IsrGuidanceThresholdMicroseconds = 25d;
    private const double OneMillisecondMicroseconds = 1_000d;
    private const double ThreeMillisecondsMicroseconds = 3_000d;
    private const uint NoActiveConsoleSession = 0xFFFFFFFF;

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

    private static readonly Action<ILogger, Guid, string, int, Exception?> KernelLatencyCaptureUnavailable =
        LoggerMessage.Define<Guid, string, int>(
            LogLevel.Warning,
            new EventId(1006, nameof(KernelLatencyCaptureUnavailable)),
            "Kernel latency capture {RequestId} was unavailable because of {FailureKind} (native error {NativeErrorCode}).");

    private static readonly Action<ILogger, uint, uint, int, Exception?> PipeClientSessionRejected =
        LoggerMessage.Define<uint, uint, int>(
            LogLevel.Warning,
            new EventId(1007, nameof(PipeClientSessionRejected)),
            "Observation pipe client session {ClientSessionId} was rejected; active console session is {ActiveConsoleSessionId}. Native error: {NativeErrorCode}.");

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
                if (!IsAuthorizedClientSession(server))
                {
                    continue;
                }

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

    private bool IsAuthorizedClientSession(NamedPipeServerStream server)
    {
        var activeConsoleSessionId = WTSGetActiveConsoleSessionId();
        if (!GetNamedPipeClientSessionId(server.SafePipeHandle, out var clientSessionId))
        {
            var nativeError = Marshal.GetLastPInvokeError();
            PipeClientSessionRejected(
                logger,
                NoActiveConsoleSession,
                activeConsoleSessionId,
                nativeError,
                null);
            return false;
        }

        if (activeConsoleSessionId == NoActiveConsoleSession || clientSessionId != activeConsoleSessionId)
        {
            PipeClientSessionRejected(
                logger,
                clientSessionId,
                activeConsoleSessionId,
                0,
                null);
            return false;
        }

        return true;
    }

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
        bool clientDisconnected;
        try
        {
            response = await Task.Run(
                () => HandleRequest(request, operationCancellation.Token),
                CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            disconnectMonitorCancellation.Cancel();
            clientDisconnected = await AwaitDisconnectMonitorAsync(disconnectMonitor).ConfigureAwait(false);
        }

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (clientDisconnected || !server.IsConnected)
        {
            ClientDisconnectedDuringOperation(logger, request.RequestId, null);
            return;
        }

        if (operationCancellation.IsCancellationRequested)
        {
            KernelLatencyCaptureUnavailable(logger, request.RequestId, "OperationDeadline", 0, null);
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

    private static async Task<bool> MonitorClientDisconnectAsync(
        NamedPipeServerStream server,
        CancellationTokenSource operationCancellation,
        CancellationToken cancellationToken)
    {
        var probe = new byte[1];

        try
        {
            _ = await server.ReadAsync(probe, cancellationToken).ConfigureAwait(false);
            operationCancellation.Cancel();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The operation completed normally; stop the disconnect probe.
            return false;
        }
        catch (IOException)
        {
            operationCancellation.Cancel();
            return true;
        }
    }

    private static async Task<bool> AwaitDisconnectMonitorAsync(Task<bool> monitor)
    {
        try
        {
            return await monitor.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the normal way to stop the disconnect probe after a response is ready.
            return false;
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
                Ok(request.RequestId, serviceStatus: CreateServiceStatus()),

            ObservationCommand.CaptureKernelLatency =>
                CaptureKernelLatency(request, stoppingToken),

            _ => Error(
                request.RequestId,
                ObservationErrorCode.InvalidRequest,
                "The request shape does not match an allowed observation command."),
        };
    }

    private static ObservationServiceStatus CreateServiceStatus() =>
        new(
            ServiceBoundary.PrivilegedObservationHostImplemented,
            ServiceBoundary.MutationAvailable,
            WindowsServiceHelpers.IsWindowsService(),
            HasExpectedKernelCapturePrivilege());

    private static bool HasExpectedKernelCapturePrivilege()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User;
        if (user is not null &&
            (user.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
             user.IsWellKnown(WellKnownSidType.LocalServiceSid) ||
             user.IsWellKnown(WellKnownSidType.NetworkServiceSid)))
        {
            return true;
        }

        var principal = new WindowsPrincipal(identity);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            return true;
        }

        var performanceLogUsers = new SecurityIdentifier(
            WellKnownSidType.BuiltinPerformanceLoggingUsersSid,
            null);
        return principal.IsInRole(performanceLogUsers);
    }

    private ObservationResponse CaptureKernelLatency(
        ObservationRequest request,
        CancellationToken stoppingToken)
    {
        if (!WindowsServiceHelpers.IsWindowsService() || !HasExpectedKernelCapturePrivilege())
        {
            return Error(
                request.RequestId,
                ObservationErrorCode.CaptureUnavailable,
                "Kernel observation requires the protected privileged Windows Service host.");
        }

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
                kernelLatencyCapture: Summarize(request.RequestId, result));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return CaptureUnavailable(request.RequestId);
        }
        catch (UnauthorizedAccessException exception)
        {
            KernelLatencyCaptureUnavailable(logger, request.RequestId, nameof(UnauthorizedAccessException), 0, exception);
            return CaptureUnavailable(request.RequestId);
        }
        catch (Win32Exception exception)
        {
            KernelLatencyCaptureUnavailable(logger, request.RequestId, nameof(Win32Exception), exception.NativeErrorCode, exception);
            return CaptureUnavailable(request.RequestId);
        }
        catch (InvalidOperationException exception)
        {
            KernelLatencyCaptureUnavailable(logger, request.RequestId, nameof(InvalidOperationException), 0, exception);
            return CaptureUnavailable(request.RequestId);
        }
        catch (Exception exception)
        {
            KernelLatencyCaptureFailed(logger, exception);
            return CaptureUnavailable(request.RequestId);
        }
    }

    private static KernelLatencyCaptureResponse Summarize(Guid requestId, KernelLatencyCaptureResult result)
    {
        var dpcDurations = new List<double>();
        var isrDurations = new List<double>();
        SplitDurations(result.Events, dpcDurations, isrDurations, out _);
        var dpc = CreateLatencySummary(dpcDurations, DpcGuidanceThresholdMicroseconds);
        var isr = CreateLatencySummary(isrDurations, IsrGuidanceThresholdMicroseconds);

        var processors = result.Events
            .GroupBy(static item => item.ProcessorNumber)
            .OrderBy(static group => group.Key)
            .Select(static group => CreateProcessorDistribution(group.Key, group))
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
            requestId,
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
            dpc.Distribution,
            isr.Distribution,
            dpc.Thresholds,
            isr.Thresholds,
            processors,
            allModules.Take(ObservationProtocol.MaximumModuleContributors).ToArray(),
            allUnresolvedRoutines.Take(ObservationProtocol.MaximumUnresolvedRoutineContributors).ToArray());
    }

    private static ProcessorLatencyDistribution CreateProcessorDistribution(
        int processorNumber,
        IEnumerable<KernelLatencyEvent> events)
    {
        var dpcDurations = new List<double>();
        var isrDurations = new List<double>();
        SplitDurations(events, dpcDurations, isrDurations, out _);
        var dpc = CreateLatencySummary(dpcDurations, DpcGuidanceThresholdMicroseconds);
        var isr = CreateLatencySummary(isrDurations, IsrGuidanceThresholdMicroseconds);

        return new ProcessorLatencyDistribution(
            processorNumber,
            dpc.Distribution,
            isr.Distribution,
            dpc.Thresholds,
            isr.Thresholds);
    }

    private static ModuleLatencyDistribution CreateModuleDistribution(
        string path,
        IEnumerable<KernelLatencyEvent> events)
    {
        var dpcDurations = new List<double>();
        var isrDurations = new List<double>();
        SplitDurations(events, dpcDurations, isrDurations, out var totalDurationMicroseconds);
        var dpc = CreateLatencySummary(dpcDurations, DpcGuidanceThresholdMicroseconds);
        var isr = CreateLatencySummary(isrDurations, IsrGuidanceThresholdMicroseconds);

        var moduleName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(moduleName))
        {
            moduleName = path;
        }

        return new ModuleLatencyDistribution(
            moduleName,
            path,
            totalDurationMicroseconds,
            dpc.Distribution,
            isr.Distribution,
            dpc.Thresholds,
            isr.Thresholds);
    }

    private static UnresolvedRoutineLatencyDistribution CreateUnresolvedRoutineDistribution(
        ulong routineAddress,
        IEnumerable<KernelLatencyEvent> events)
    {
        var dpcDurations = new List<double>();
        var isrDurations = new List<double>();
        SplitDurations(events, dpcDurations, isrDurations, out var totalDurationMicroseconds);
        var dpc = CreateLatencySummary(dpcDurations, DpcGuidanceThresholdMicroseconds);
        var isr = CreateLatencySummary(isrDurations, IsrGuidanceThresholdMicroseconds);

        return new UnresolvedRoutineLatencyDistribution(
            routineAddress,
            totalDurationMicroseconds,
            dpc.Distribution,
            isr.Distribution,
            dpc.Thresholds,
            isr.Thresholds);
    }

    private static void SplitDurations(
        IEnumerable<KernelLatencyEvent> events,
        List<double> dpcDurations,
        List<double> isrDurations,
        out double totalDurationMicroseconds)
    {
        totalDurationMicroseconds = 0d;

        foreach (var item in events)
        {
            totalDurationMicroseconds += item.DurationMicroseconds;
            if (item.Kind == KernelLatencyEventKind.Dpc)
            {
                dpcDurations.Add(item.DurationMicroseconds);
            }
            else
            {
                isrDurations.Add(item.DurationMicroseconds);
            }
        }
    }

    private static (LatencyDistribution Distribution, LatencyThresholdSummary Thresholds) CreateLatencySummary(
        List<double> durations,
        double guidanceThresholdMicroseconds)
    {
        var thresholds = CreateThresholdSummary(durations, guidanceThresholdMicroseconds);
        return (CreateDistribution(durations), thresholds);
    }

    private static LatencyThresholdSummary CreateThresholdSummary(
        List<double> durations,
        double guidanceThresholdMicroseconds)
    {
        var guidanceExceedanceCount = 0;
        var overOneMillisecondCount = 0;
        var overThreeMillisecondsCount = 0;

        foreach (var duration in durations)
        {
            if (duration > guidanceThresholdMicroseconds)
            {
                guidanceExceedanceCount++;
            }

            if (duration > OneMillisecondMicroseconds)
            {
                overOneMillisecondCount++;
            }

            if (duration > ThreeMillisecondsMicroseconds)
            {
                overThreeMillisecondsCount++;
            }
        }

        return new LatencyThresholdSummary(
            guidanceThresholdMicroseconds,
            guidanceExceedanceCount,
            overOneMillisecondCount,
            overThreeMillisecondsCount);
    }

    private static LatencyDistribution CreateDistribution(List<double> sorted)
    {
        if (sorted.Count == 0)
        {
            return new LatencyDistribution(0, null, null, null, null, null);
        }

        sorted.Sort();
        var p999 = sorted.Count >= ObservationProtocol.MinimumSamplesForP999
            ? Percentiles.CalculateSorted(sorted, 0.999)
            : null;

        return new LatencyDistribution(
            sorted.Count,
            Percentiles.CalculateSorted(sorted, 0.50),
            Percentiles.CalculateSorted(sorted, 0.95),
            Percentiles.CalculateSorted(sorted, 0.99),
            p999,
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientSessionId(
        SafePipeHandle pipe,
        out uint clientSessionId);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();
}

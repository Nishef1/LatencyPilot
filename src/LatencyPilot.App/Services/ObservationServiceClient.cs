using System.Diagnostics;
using System.IO.Pipes;
using LatencyPilot.Protocol;
using Serilog;

namespace LatencyPilot.App.Services;

internal static class ObservationServiceClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CaptureCompletionMargin = TimeSpan.FromSeconds(5);
    private static readonly Serilog.ILogger Logger =
        Log.ForContext("SourceContext", nameof(ObservationServiceClient));

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

        if (response.KernelLatencyCapture is not null)
        {
            throw new InvalidDataException("Observation service returned a capture payload for a status request.");
        }

        var status = response.ServiceStatus
            ?? throw new InvalidDataException("Observation service returned no status payload.");

        if (!status.RunningAsWindowsService)
        {
            throw new InvalidOperationException(
                "Observation host is reachable, but it is not running under the Windows Service Control Manager. Kernel capture remains disabled.");
        }

        if (!status.KernelCapturePrivilegeExpected)
        {
            throw new InvalidOperationException(
                "Observation service is running, but its process identity does not have the expected kernel-trace authority. Kernel capture remains disabled.");
        }

        return status;
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

        if (response.ServiceStatus is not null)
        {
            throw new InvalidDataException("Observation service returned a status payload for a capture request.");
        }

        var capture = response.KernelLatencyCapture
            ?? throw new InvalidDataException("Observation service returned no kernel-latency payload.");
        ValidateCapture(capture);
        return capture;
    }

    private static async Task<ObservationResponse> SendAsync(
        ObservationRequest request,
        TimeSpan operationTimeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(operationTimeout);
        var operationToken = timeoutSource.Token;
        var stopwatch = Stopwatch.StartNew();

        using var pipe = new NamedPipeClientStream(
            ".",
            ObservationProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        Logger.Information(
            "Observation request {RequestId} {Command} started with a {TimeoutMilliseconds} ms deadline.",
            request.RequestId,
            request.Command,
            operationTimeout.TotalMilliseconds);

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

            ValidateResponse(request, response);

            Logger.Information(
                "Observation request {RequestId} {Command} completed with status {Status} in {ElapsedMilliseconds} ms.",
                request.RequestId,
                request.Command,
                response.Status,
                stopwatch.Elapsed.TotalMilliseconds);

            return response;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            timeoutSource.IsCancellationRequested)
        {
            Logger.Warning(
                "Observation request {RequestId} {Command} timed out after {ElapsedMilliseconds} ms.",
                request.RequestId,
                request.Command,
                stopwatch.Elapsed.TotalMilliseconds);
            throw new TimeoutException("Observation service operation exceeded its deadline.");
        }
        catch (OperationCanceledException)
        {
            Logger.Information(
                "Observation request {RequestId} {Command} was cancelled after {ElapsedMilliseconds} ms.",
                request.RequestId,
                request.Command,
                stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            Logger.Warning(
                exception,
                "Observation request {RequestId} {Command} failed after {ElapsedMilliseconds} ms.",
                request.RequestId,
                request.Command,
                stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    private static void ValidateResponse(ObservationRequest request, ObservationResponse response)
    {
        if (response.ProtocolVersion != ProtocolVersion.Current)
        {
            throw new InvalidDataException("Observation service protocol version mismatch.");
        }

        if (response.RequestId != request.RequestId)
        {
            throw new InvalidDataException("Observation service response RequestId mismatch.");
        }

        if (!Enum.IsDefined(response.Status))
        {
            throw new InvalidDataException("Observation service returned an unknown response status.");
        }

        if (!Enum.IsDefined(response.ErrorCode))
        {
            throw new InvalidDataException("Observation service returned an unknown error code.");
        }

        if (response.Status == ObservationResponseStatus.Error)
        {
            if (response.ErrorCode == ObservationErrorCode.None)
            {
                throw new InvalidDataException("Observation service returned an error response without an error code.");
            }

            throw new InvalidOperationException(
                response.ErrorMessage ?? "Observation service rejected the request.");
        }

        if (response.Status != ObservationResponseStatus.Ok ||
            response.ErrorCode != ObservationErrorCode.None ||
            response.ErrorMessage is not null)
        {
            throw new InvalidDataException("Observation service returned an inconsistent success response.");
        }
    }

    private static void ValidateCapture(KernelLatencyCaptureResponse capture)
    {
        ValidateThresholdSummary("DPC", capture.Dpc, capture.DpcThresholds);
        ValidateThresholdSummary("ISR", capture.Isr, capture.IsrThresholds);

        foreach (var processor in capture.Processors)
        {
            ValidateThresholdSummary("processor DPC", processor.Dpc, processor.DpcThresholds);
            ValidateThresholdSummary("processor ISR", processor.Isr, processor.IsrThresholds);
        }

        foreach (var module in capture.Modules)
        {
            ValidateThresholdSummary("module DPC", module.Dpc, module.DpcThresholds);
            ValidateThresholdSummary("module ISR", module.Isr, module.IsrThresholds);
        }

        foreach (var routine in capture.UnresolvedRoutines)
        {
            ValidateThresholdSummary("unresolved DPC", routine.Dpc, routine.DpcThresholds);
            ValidateThresholdSummary("unresolved ISR", routine.Isr, routine.IsrThresholds);
        }
    }

    private static void ValidateThresholdSummary(
        string context,
        LatencyDistribution distribution,
        LatencyThresholdSummary thresholds)
    {
        if (!double.IsFinite(thresholds.GuidanceThresholdMicroseconds) ||
            thresholds.GuidanceThresholdMicroseconds <= 0d ||
            thresholds.GuidanceExceedanceCount < 0 ||
            thresholds.OverOneMillisecondCount < 0 ||
            thresholds.OverThreeMillisecondsCount < 0 ||
            thresholds.GuidanceExceedanceCount > distribution.Count ||
            thresholds.OverOneMillisecondCount > distribution.Count ||
            thresholds.OverThreeMillisecondsCount > thresholds.OverOneMillisecondCount)
        {
            throw new InvalidDataException($"Observation service returned inconsistent {context} threshold evidence.");
        }
    }
}

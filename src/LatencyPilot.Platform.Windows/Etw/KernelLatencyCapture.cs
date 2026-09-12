using System.Diagnostics;
using LatencyPilot.Core.Observation;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace LatencyPilot.Platform.Windows.Etw;

public static class KernelLatencyCapture
{
    public static KernelLatencyCaptureResult Capture(
        KernelLatencyCaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var sessionName = $"LatencyPilot-Kernel-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var events = new List<KernelLatencyEvent>(Math.Min(options.MaximumEvents, 32_768));
        var imageTracker = new KernelImageTracker();
        var invalidEventCount = 0;
        var eventLimitReached = false;

        using var session = new TraceEventSession(sessionName)
        {
            StopOnDispose = true,
        };

        void StopProcessing() => session.Source.StopProcessing();
        var stopProcessing = new Action(StopProcessing);

        void StopSession()
        {
            // Kernel DCEnd rundown is emitted as part of session stop. Stopping the
            // session, rather than only stopping the consumer, lets already-loaded
            // image mappings reach the parser before Process() returns.
            session.Stop(noThrow: true);
        }

        var stopSession = new Action(StopSession);

        void Append(
            KernelLatencyEventKind kind,
            int processorNumber,
            double timeStampRelativeMilliseconds,
            double elapsedMilliseconds,
            ulong routineAddress,
            int? interruptVector,
            int? messageNumber)
        {
            var durationMicroseconds = elapsedMilliseconds * 1_000d;
            if (!double.IsFinite(durationMicroseconds) || durationMicroseconds < 0 ||
                !double.IsFinite(timeStampRelativeMilliseconds) || timeStampRelativeMilliseconds < 0)
            {
                invalidEventCount++;
                return;
            }

            if (events.Count >= options.MaximumEvents)
            {
                eventLimitReached = true;
                stopProcessing();
                return;
            }

            events.Add(new KernelLatencyEvent(
                kind,
                processorNumber,
                timeStampRelativeMilliseconds,
                durationMicroseconds,
                routineAddress,
                interruptVector,
                messageNumber));
        }

        session.Source.Kernel.PerfInfoDPC += data =>
            Append(
                KernelLatencyEventKind.Dpc,
                data.ProcessorNumber,
                data.TimeStampRelativeMSec,
                data.ElapsedTimeMSec,
                data.Routine,
                null,
                null);

        session.Source.Kernel.PerfInfoISR += data =>
            Append(
                KernelLatencyEventKind.Isr,
                data.ProcessorNumber,
                data.TimeStampRelativeMSec,
                data.ElapsedTimeMSec,
                data.Routine,
                data.Vector,
                data.Message);

        session.Source.Kernel.ImageLoad += imageTracker.ObserveLoad;
        session.Source.Kernel.ImageUnload += imageTracker.ObserveUnload;
        session.Source.Kernel.ImageDCEnd += imageTracker.ObserveRundownEnd;

        var keywords =
            KernelTraceEventParser.Keywords.DeferedProcedureCalls |
            KernelTraceEventParser.Keywords.Interrupt |
            KernelTraceEventParser.Keywords.ImageLoad;

        session.EnableKernelProvider(keywords);

        using var cancellationRegistration = cancellationToken.Register(stopSession);
        using var timeoutTimer = new Timer(
            static state => ((Action)state!).Invoke(),
            stopSession,
            options.Duration,
            Timeout.InfiniteTimeSpan);

        session.Source.Process();
        stopwatch.Stop();

        cancellationToken.ThrowIfCancellationRequested();

        var attributedEvents = events
            .Select(item => item with
            {
                ModulePath = imageTracker.ResolvePath(
                    item.RoutineAddress,
                    item.TimeStampRelativeMilliseconds),
            })
            .ToArray();

        return new KernelLatencyCaptureResult(
            startedAtUtc,
            options.Duration,
            stopwatch.Elapsed,
            attributedEvents,
            session.EventsLost,
            invalidEventCount,
            imageTracker.InvalidEventCount,
            eventLimitReached);
    }
}

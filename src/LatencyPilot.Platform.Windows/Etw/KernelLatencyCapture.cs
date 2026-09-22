using System.Diagnostics;
using System.Runtime.InteropServices;
using LatencyPilot.Core.Observation;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace LatencyPilot.Platform.Windows.Etw;

public static class KernelLatencyCapture
{
    private const int UnknownEventsLost = -1;

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
        var eventLimitStopRequested = 0;
        var sessionStopRequested = 0;
        var eventsLost = UnknownEventsLost;

        using var session = new TraceEventSession(sessionName)
        {
            StopOnDispose = true,
        };

        void StopSession()
        {
            if (Interlocked.Exchange(ref sessionStopRequested, 1) != 0)
            {
                return;
            }

            // Kernel DCStop rundown is emitted as part of session stop. Stopping the
            // session, rather than only stopping the consumer, lets already-loaded
            // image mappings reach the parser before Process() returns.
            try
            {
                // TraceEventSession.EventsLost queries the live ETW session. Once Stop
                // removes the session, Windows may reject that query with a WMI
                // instance-name error, so snapshot it first. Any failure here must
                // still reach session.Stop: sessionStopRequested is already set, so a
                // re-entry would no-op and Process() could hang forever.
                eventsLost = session.EventsLost;
            }
            catch (Exception exception) when (exception is not StackOverflowException and not OutOfMemoryException)
            {
                // Preserve the observation, but keep the loss count explicitly unknown
                // so the UI cannot present an unverified zero as a clean capture.
                eventsLost = UnknownEventsLost;
            }

            session.Stop(noThrow: true);
        }

        var stopSession = new Action(StopSession);

        void RequestEventLimitStop()
        {
            eventLimitReached = true;
            if (Interlocked.Exchange(ref eventLimitStopRequested, 1) != 0)
            {
                return;
            }

            // TraceEventSession.Stop is documented as safe while Process() runs on
            // another thread. Queue the stop so the parser thread can keep consuming
            // the final kernel rundown instead of terminating the consumer early.
            ThreadPool.QueueUserWorkItem(
                static state => ((Action)state!).Invoke(),
                stopSession);
        }

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
                RequestEventLimitStop();
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

            if (events.Count >= options.MaximumEvents)
            {
                RequestEventLimitStop();
            }
        }

        var keywords =
            KernelTraceEventParser.Keywords.DeferedProcedureCalls |
            KernelTraceEventParser.Keywords.Interrupt |
            KernelTraceEventParser.Keywords.ImageLoad;

        // TraceEvent 3.2.6 starts the real-time session when Source is first accessed.
        // Enable the special kernel provider before touching Source so the provider is
        // configured on the session before it becomes active.
        session.EnableKernelProvider(keywords);

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
        session.Source.Kernel.ImageDCStop += imageTracker.ObserveRundownStop;

        using var cancellationRegistration = cancellationToken.Register(stopSession);
        using var timeoutTimer = new Timer(
            static state => ((Action)state!).Invoke(),
            stopSession,
            options.Duration,
            Timeout.InfiniteTimeSpan);

        session.Source.Process();
        stopwatch.Stop();

        cancellationToken.ThrowIfCancellationRequested();

        for (var index = 0; index < events.Count; index++)
        {
            var item = events[index];
            events[index] = item with
            {
                ModulePath = imageTracker.ResolvePath(
                    item.RoutineAddress,
                    item.TimeStampRelativeMilliseconds),
            };
        }

        return new KernelLatencyCaptureResult(
            startedAtUtc,
            options.Duration,
            stopwatch.Elapsed,
            events.AsReadOnly(),
            eventsLost,
            invalidEventCount,
            imageTracker.InvalidEventCount,
            eventLimitReached);
    }
}

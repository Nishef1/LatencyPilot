from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one anchor, found {count}: {old[:120]!r}")
    write(path, text.replace(old, new, 1))


def apply_tests() -> None:
    path = "tests/LatencyPilot.CriticalTests/SourceRevisionIdentityTests.cs"
    anchor = '''        var benchmarkControlClientSource = File.ReadAllText(Path.Combine(\n'''
    addition = '''        var collectorBackendSource = File.ReadAllText(Path.Combine(\n            repositoryRoot,\n            "tools",\n            "LatencyPilot.GateAValidation",\n            "GpuAutoAffinityGateABackend.cs"));\n        StringAssert.Contains(collectorBackendSource, "TryStartOptionalPresentMonAsync");\n        StringAssert.Contains(collectorBackendSource, "CollectorTailSlack");\n        StringAssert.Contains(collectorBackendSource, "PresentMon startup unavailable");\n        Assert.IsFalse(\n            collectorBackendSource.Contains("request.Duration + TimeSpan.FromSeconds(8)", StringComparison.Ordinal),\n            "Scored ETW capture must not add an unconditional eight-second tail to every benchmark block.");\n        StringAssert.Contains(collectorBackendSource, "GPU ISR attribution is unavailable for this trial");\n\n        var benchmarkControlClientSource = File.ReadAllText(Path.Combine(\n'''
    replace_once(path, anchor, addition)


def apply_implementation() -> None:
    path = "tools/LatencyPilot.GateAValidation/GpuAutoAffinityGateABackend.cs"
    replace_once(
        path,
        '''    private const int KernelMaximumEvents = 2_000_000;\n    private const double MinimumOverlapRatio = 0.95;\n''',
        '''    private const int KernelMaximumEvents = 2_000_000;\n    private const double MinimumOverlapRatio = 0.95;\n    private static readonly TimeSpan CollectorTailSlack = TimeSpan.FromSeconds(2);\n''')

    replace_once(
        path,
        '''            var presentMonWindow = request.Duration;\n            await using var presentMonSession = await PresentMonConsoleFrameMetricsReader.StartAsync(\n                benchmarkProcessId,\n                presentMonWindow,\n                cancellationToken: cancellationToken).ConfigureAwait(false);\n\n            var kernelDuration = request.Duration + TimeSpan.FromSeconds(8);\n            var kernelTask = Task.Run(\n                () => KernelLatencyCapture.Capture(\n                    new KernelLatencyCaptureOptions(kernelDuration, KernelMaximumEvents),\n                    deadline.Token),\n                deadline.Token);\n\n            await Task.Delay(TimeSpan.FromMilliseconds(150), deadline.Token).ConfigureAwait(false);\n            var artifactPathTask = benchmark.RunTrialAsync(\n                request.RunNumber,\n                request.Duration,\n                deadline.Token);\n\n            await Task.WhenAll(kernelTask, artifactPathTask).ConfigureAwait(false);\n            kernel = await kernelTask.ConfigureAwait(false);\n            var artifactPath = await artifactPathTask.ConfigureAwait(false);\n            artifact = await ReadArtifactAsync(artifactPath, deadline.Token).ConfigureAwait(false);\n            presentMon = await presentMonSession.CompleteAsync(\n                artifact.StartedAtUtc,\n                artifact.EndedAtUtc,\n                deadline.Token).ConfigureAwait(false);\n''',
        '''            var presentMonWindow = request.Duration;\n            var presentMonStart = await TryStartOptionalPresentMonAsync(\n                benchmarkProcessId,\n                presentMonWindow,\n                cancellationToken).ConfigureAwait(false);\n\n            var kernelDuration = request.Duration + CollectorTailSlack;\n            var kernelTask = Task.Run(\n                () => KernelLatencyCapture.Capture(\n                    new KernelLatencyCaptureOptions(kernelDuration, KernelMaximumEvents),\n                    deadline.Token),\n                deadline.Token);\n\n            await Task.Delay(TimeSpan.FromMilliseconds(150), deadline.Token).ConfigureAwait(false);\n            var artifactPathTask = benchmark.RunTrialAsync(\n                request.RunNumber,\n                request.Duration,\n                deadline.Token);\n\n            await Task.WhenAll(kernelTask, artifactPathTask).ConfigureAwait(false);\n            kernel = await kernelTask.ConfigureAwait(false);\n            var artifactPath = await artifactPathTask.ConfigureAwait(false);\n            artifact = await ReadArtifactAsync(artifactPath, deadline.Token).ConfigureAwait(false);\n\n            if (presentMonStart.Session is null)\n            {\n                presentMon = CreateUnavailablePresentMon(\n                    benchmarkProcessId,\n                    presentMonWindow,\n                    artifact.StartedAtUtc,\n                    artifact.EndedAtUtc,\n                    presentMonStart.Error ?? "PresentMon startup unavailable.");\n            }\n            else\n            {\n                await using var presentMonSession = presentMonStart.Session;\n                presentMon = await presentMonSession.CompleteAsync(\n                    artifact.StartedAtUtc,\n                    artifact.EndedAtUtc,\n                    deadline.Token).ConfigureAwait(false);\n            }\n''')

    replace_once(
        path,
        '''            catch (Exception exception) when (exception is\n                InvalidOperationException or\n                NotSupportedException or\n                System.ComponentModel.Win32Exception)\n            {\n                reasons.Add(\n                    $"GPU ISR attribution failed: {exception.GetType().Name}: {exception.Message}");\n            }\n''',
        '''            catch (Exception exception) when (exception is\n                InvalidOperationException or\n                NotSupportedException or\n                System.ComponentModel.Win32Exception)\n            {\n                softNotes.Add(\n                    $"GPU ISR attribution is unavailable for this trial: {exception.GetType().Name}: {exception.Message}. Placement remains Unknown; final Keep still requires positive target-only proof.");\n            }\n''')

    helper_anchor = '''    private static async Task<GpuBenchmarkTrialArtifact> ReadArtifactAsync(\n'''
    helpers = '''    private static async Task<(PresentMonConsoleFrameMetricsReader.PresentMonConsoleCaptureSession? Session, string? Error)> TryStartOptionalPresentMonAsync(\n        uint processId,\n        TimeSpan requestedWindow,\n        CancellationToken cancellationToken)\n    {\n        try\n        {\n            var session = await PresentMonConsoleFrameMetricsReader.StartAsync(\n                processId,\n                requestedWindow,\n                cancellationToken: cancellationToken).ConfigureAwait(false);\n            return (session, null);\n        }\n        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)\n        {\n            return (null, "PresentMon startup unavailable: provisioning/startup exceeded its own bounded deadline.");\n        }\n        catch (OperationCanceledException)\n        {\n            throw;\n        }\n        catch (InvalidDataException)\n        {\n            // A pinned-binary integrity failure is not optional evidence loss.\n            throw;\n        }\n        catch (UnauthorizedAccessException)\n        {\n            throw;\n        }\n        catch (System.Security.SecurityException)\n        {\n            throw;\n        }\n        catch (Exception exception) when (\n            exception is FileNotFoundException or\n            DirectoryNotFoundException or\n            System.Net.Http.HttpRequestException or\n            InvalidOperationException or\n            System.ComponentModel.Win32Exception ||\n            exception is IOException and not InvalidDataException)\n        {\n            return (null, $"PresentMon startup unavailable: {exception.GetType().Name}: {exception.Message}");\n        }\n    }\n\n    private static PresentMonFrameCaptureSnapshot CreateUnavailablePresentMon(\n        uint processId,\n        TimeSpan requestedWindow,\n        DateTimeOffset benchmarkStartedAtUtc,\n        DateTimeOffset benchmarkEndedAtUtc,\n        string error) =>\n        new(\n            PresentMonWorkloadCaptureStatus.TrackingFailed,\n            processId,\n            requestedWindow.TotalMilliseconds,\n            Math.Max(0d, (benchmarkEndedAtUtc - benchmarkStartedAtUtc).TotalMilliseconds),\n            ApiVersion: null,\n            SwapChains: [],\n            UnavailableMetrics: [],\n            ApiPath: null,\n            NativeStatusCode: null,\n            Error: error,\n            StartedAtUtc: benchmarkStartedAtUtc,\n            EndedAtUtc: benchmarkEndedAtUtc);\n\n    private static async Task<GpuBenchmarkTrialArtifact> ReadArtifactAsync(\n'''
    replace_once(path, helper_anchor, helpers)


if __name__ == "__main__":
    if len(sys.argv) != 2 or sys.argv[1] not in {"tests", "implementation"}:
        raise SystemExit("usage: audit-closure-task3-collectors.py tests|implementation")
    if sys.argv[1] == "tests":
        apply_tests()
    else:
        apply_implementation()

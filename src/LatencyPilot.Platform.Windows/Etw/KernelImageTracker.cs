using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace LatencyPilot.Platform.Windows.Etw;

internal sealed class KernelImageTracker
{
    private readonly List<ImageLifetime> _images = [];
    private readonly Dictionary<ulong, ImageLifetime[]> _candidateCache = [];

    public int InvalidEventCount { get; private set; }

    public void ObserveLoad(ImageLoadTraceData data)
    {
        if (!TryReadImage(data, out var image) || !IsValidTimestamp(data.TimeStampRelativeMSec))
        {
            InvalidEventCount++;
            return;
        }

        _images.Add(new ImageLifetime(
            image.BaseAddress,
            image.EndAddressExclusive,
            image.Path,
            data.TimeStampRelativeMSec,
            null));
    }

    public void ObserveUnload(ImageLoadTraceData data)
    {
        if (!TryReadImage(data, out var image) || !IsValidTimestamp(data.TimeStampRelativeMSec))
        {
            InvalidEventCount++;
            return;
        }

        for (var index = _images.Count - 1; index >= 0; index--)
        {
            var candidate = _images[index];
            if (candidate.UnloadedAtRelativeMilliseconds is null &&
                candidate.BaseAddress == image.BaseAddress &&
                string.Equals(candidate.Path, image.Path, StringComparison.OrdinalIgnoreCase))
            {
                candidate.UnloadedAtRelativeMilliseconds = data.TimeStampRelativeMSec;
                return;
            }
        }

        // An unload without a matching runtime load means the image predates this observation.
        _images.Add(new ImageLifetime(
            image.BaseAddress,
            image.EndAddressExclusive,
            image.Path,
            0,
            data.TimeStampRelativeMSec));
    }

    public void ObserveRundownStop(ImageLoadTraceData data)
    {
        if (!TryReadImage(data, out var image))
        {
            InvalidEventCount++;
            return;
        }

        // Runtime ImageLoad already gives the stronger start time. Avoid duplicating it when
        // the same still-loaded image appears in the kernel DCStop rundown at session stop.
        if (_images.Any(candidate =>
                candidate.UnloadedAtRelativeMilliseconds is null &&
                candidate.BaseAddress == image.BaseAddress &&
                string.Equals(candidate.Path, image.Path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _images.Add(new ImageLifetime(
            image.BaseAddress,
            image.EndAddressExclusive,
            image.Path,
            0,
            null));
    }

    public string? ResolvePath(ulong routineAddress, double timeStampRelativeMilliseconds)
    {
        if (routineAddress == 0 || !IsValidTimestamp(timeStampRelativeMilliseconds))
        {
            return null;
        }

        if (!_candidateCache.TryGetValue(routineAddress, out var candidates))
        {
            candidates = _images
                .Where(image => image.BaseAddress <= routineAddress && routineAddress < image.EndAddressExclusive)
                .ToArray();
            _candidateCache.Add(routineAddress, candidates);
        }

        string? resolvedPath = null;
        foreach (var candidate in candidates)
        {
            if (candidate.LoadedAtRelativeMilliseconds > timeStampRelativeMilliseconds ||
                candidate.UnloadedAtRelativeMilliseconds is { } unloadedAt &&
                timeStampRelativeMilliseconds >= unloadedAt)
            {
                continue;
            }

            if (resolvedPath is null)
            {
                resolvedPath = candidate.Path;
                continue;
            }

            if (!string.Equals(resolvedPath, candidate.Path, StringComparison.OrdinalIgnoreCase))
            {
                // Overlapping active ranges with different identities are ambiguous. Never guess.
                return null;
            }
        }

        return resolvedPath;
    }

    private static bool TryReadImage(ImageLoadTraceData data, out ImageDescriptor image)
    {
        var baseAddress = (ulong)data.ImageBase;
        var size = data.ImageSize;
        var path = data.FileName;

        if (baseAddress == 0 || size <= 0 || string.IsNullOrWhiteSpace(path))
        {
            image = default;
            return false;
        }

        ulong endAddressExclusive;
        try
        {
            endAddressExclusive = checked(baseAddress + (ulong)size);
        }
        catch (OverflowException)
        {
            image = default;
            return false;
        }

        image = new ImageDescriptor(
            baseAddress,
            endAddressExclusive,
            path.Trim());
        return true;
    }

    private static bool IsValidTimestamp(double value) =>
        double.IsFinite(value) && value >= 0;

    private readonly record struct ImageDescriptor(
        ulong BaseAddress,
        ulong EndAddressExclusive,
        string Path);

    private sealed class ImageLifetime(
        ulong baseAddress,
        ulong endAddressExclusive,
        string path,
        double loadedAtRelativeMilliseconds,
        double? unloadedAtRelativeMilliseconds)
    {
        public ulong BaseAddress { get; } = baseAddress;

        public ulong EndAddressExclusive { get; } = endAddressExclusive;

        public string Path { get; } = path;

        public double LoadedAtRelativeMilliseconds { get; } = loadedAtRelativeMilliseconds;

        public double? UnloadedAtRelativeMilliseconds { get; set; } = unloadedAtRelativeMilliseconds;
    }
}

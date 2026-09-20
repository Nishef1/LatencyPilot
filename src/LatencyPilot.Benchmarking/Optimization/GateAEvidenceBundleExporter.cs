using System.IO.Compression;

namespace LatencyPilot.Benchmarking.Optimization;

public sealed record GateAEvidenceBundleExportResult(string? ZipPath, string? Error)
{
    public bool Succeeded => !string.IsNullOrWhiteSpace(ZipPath);
}

public static class GateAEvidenceBundleExporter
{
    public static async Task<GateAEvidenceBundleExportResult> TryCreateAsync(
        string sessionDirectory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(
                () => Create(sessionDirectory, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException)
        {
            return new GateAEvidenceBundleExportResult(null, exception.Message);
        }
    }

    private static GateAEvidenceBundleExportResult Create(
        string sessionDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);

        var fullSessionDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(sessionDirectory));
        if (!Directory.Exists(fullSessionDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Gate A session directory was not found: {fullSessionDirectory}");
        }

        var sessionName = Path.GetFileName(fullSessionDirectory);
        if (string.IsNullOrWhiteSpace(sessionName))
        {
            throw new InvalidDataException("Gate A session directory has no usable folder name.");
        }

        var zipPath = fullSessionDirectory + ".zip";
        var temporaryZipPath = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{zipPath}.{Guid.NewGuid():N}.tmp");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = new FileStream(
                       temporaryZipPath,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var filePath in Directory.EnumerateFiles(
                             fullSessionDirectory,
                             "*",
                             SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(fullSessionDirectory, filePath)
                        .Replace('\\', '/');
                    var entryName = $"{sessionName}/{relativePath}";
                    archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryZipPath, zipPath, overwrite: true);
            return new GateAEvidenceBundleExportResult(zipPath, null);
        }
        catch
        {
            TryDeleteTemporaryArchive(temporaryZipPath);
            throw;
        }
    }

    private static void TryDeleteTemporaryArchive(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

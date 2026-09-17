using System.Security.Cryptography;

namespace LatencyPilot.Platform.Windows.Devices;

public static class PresentMonConsoleLocator
{
    public const string Version = "2.5.1";
    public const string PinnedVersion = Version;
    public const string FileName = "PresentMon-2.5.1-x64.exe";
    public const string PinnedFileName = FileName;
    public const string Sha256 = "9bec3083069f58f911e6a512f4806db51a27bd096103087bc1d05ef54c80a191";
    public const string DownloadUrl = "https://github.com/GameTechDev/PresentMon/releases/download/v2.5.1/PresentMon-2.5.1-x64.exe";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    public static async Task<string> ResolveAsync(
        string? explicitPath = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var fullPath = Path.GetFullPath(explicitPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("The configured PresentMon console binary was not found.", fullPath);
            }

            await VerifyPinnedBinaryAsync(fullPath, cancellationToken).ConfigureAwait(false);
            return fullPath;
        }

        var packagedPath = Path.Combine(
            AppContext.BaseDirectory,
            "ThirdParty",
            "PresentMon",
            FileName);
        if (File.Exists(packagedPath))
        {
            await VerifyPinnedBinaryAsync(packagedPath, cancellationToken).ConfigureAwait(false);
            return packagedPath;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("Windows LocalAppData is unavailable; PresentMon cannot be provisioned safely.");
        }

        var cacheDirectory = Path.Combine(
            localAppData,
            "LatencyPilot",
            "ThirdParty",
            "PresentMon",
            Version);
        Directory.CreateDirectory(cacheDirectory);
        var cachedPath = Path.Combine(cacheDirectory, FileName);
        if (File.Exists(cachedPath))
        {
            if (await HasExpectedHashAsync(cachedPath, cancellationToken).ConfigureAwait(false))
            {
                return cachedPath;
            }

            File.Delete(cachedPath);
        }

        var temporaryPath = cachedPath + ".download-" + Guid.NewGuid().ToString("N");
        try
        {
            using var response = await Http.GetAsync(
                DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            await VerifyPinnedBinaryAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, cachedPath, overwrite: true);
            return cachedPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task VerifyPinnedBinaryAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!await HasExpectedHashAsync(path, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException(
                $"PresentMon {Version} failed the pinned SHA-256 integrity check. Expected {Sha256}.");
        }
    }

    private static async Task<bool> HasExpectedHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(
            Convert.ToHexString(digest),
            Sha256,
            StringComparison.OrdinalIgnoreCase);
    }
}

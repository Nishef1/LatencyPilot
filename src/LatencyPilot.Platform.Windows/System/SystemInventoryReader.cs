using System.Runtime.InteropServices;
using LatencyPilot.Core.System;

namespace LatencyPilot.Platform.Windows.System;

public static class SystemInventoryReader
{
    private const byte VerNtWorkstation = 1;
    private const int StatusSuccess = 0;

    public static SystemInventorySnapshot Capture() => new(
        GetOperatingSystemDisplayName(),
        RuntimeInformation.OSArchitecture.ToString(),
        RuntimeInformation.ProcessArchitecture.ToString(),
        Environment.ProcessorCount,
        DateTimeOffset.UtcNow);

    private static string GetOperatingSystemDisplayName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSDescription;
        }

        if (TryGetWindowsVersion(out var version))
        {
            var productName = version.ProductType switch
            {
                VerNtWorkstation when version.MajorVersion == 10 &&
                    version.MinorVersion == 0 &&
                    version.BuildNumber >= 22000 => "Microsoft Windows 11",
                VerNtWorkstation when version.MajorVersion == 10 && version.MinorVersion == 0 =>
                    "Microsoft Windows 10",
                not VerNtWorkstation => "Microsoft Windows Server",
                _ => "Microsoft Windows",
            };

            return $"{productName} · build {version.BuildNumber} · NT {version.MajorVersion}.{version.MinorVersion}";
        }

        var fallback = Environment.OSVersion.Version;
        return $"Microsoft Windows · build {fallback.Build} · NT {fallback.Major}.{fallback.Minor}";
    }

    private static bool TryGetWindowsVersion(out RtlOsVersionInfoEx version)
    {
        version = new RtlOsVersionInfoEx
        {
            Size = (uint)Marshal.SizeOf<RtlOsVersionInfoEx>(),
            ServicePack = string.Empty,
        };

        try
        {
            return RtlGetVersion(ref version) == StatusSuccess;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
    private static extern int RtlGetVersion(ref RtlOsVersionInfoEx versionInformation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RtlOsVersionInfoEx
    {
        public uint Size;
        public uint MajorVersion;
        public uint MinorVersion;
        public uint BuildNumber;
        public uint PlatformId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string ServicePack;

        public ushort ServicePackMajor;
        public ushort ServicePackMinor;
        public ushort SuiteMask;
        public byte ProductType;
        public byte Reserved;
    }
}

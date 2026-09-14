using System.Buffers.Binary;
using System.Runtime.InteropServices;
using LatencyPilot.Core.Devices;

namespace LatencyPilot.Platform.Windows.Devices;

public static class PresentMonDeviceReader
{
    private const int PresentMonSuccess = 0;
    private const int GraphicsAdapterDeviceType = 1;
    private const int MaximumIntrospectionDevices = 256;

    public static PresentMonDeviceInventory Capture(
        string? apiPath = null,
        string? controlPipeName = null)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var resolvedPath = ResolveApiPath(apiPath);
        if (resolvedPath is null)
        {
            return new PresentMonDeviceInventory(
                PresentMonDiscoveryStatus.ApiUnavailable,
                [],
                null,
                null,
                "PresentMonAPI2.dll was not found in a trusted LatencyPilot or installed PresentMon location.",
                capturedAt);
        }

        nint library = 0;
        nint session = 0;
        nint root = 0;
        PresentMonCloseSession? closeSession = null;
        PresentMonFreeIntrospectionRoot? freeRoot = null;

        try
        {
            library = NativeLibrary.Load(resolvedPath);
            var openSession = GetDelegate<PresentMonOpenSession>(library, "pmOpenSession");
            var openSessionWithPipe = GetDelegate<PresentMonOpenSessionWithPipe>(library, "pmOpenSessionWithPipe");
            closeSession = GetDelegate<PresentMonCloseSession>(library, "pmCloseSession");
            var getRoot = GetDelegate<PresentMonGetIntrospectionRoot>(library, "pmGetIntrospectionRoot");
            freeRoot = GetDelegate<PresentMonFreeIntrospectionRoot>(library, "pmFreeIntrospectionRoot");

            var status = string.IsNullOrWhiteSpace(controlPipeName)
                ? openSession(out session)
                : openSessionWithPipe(out session, controlPipeName);
            if (status != PresentMonSuccess || session == 0)
            {
                return new PresentMonDeviceInventory(
                    PresentMonDiscoveryStatus.ServiceUnavailable,
                    [],
                    resolvedPath,
                    status,
                    "PresentMon API is present but a PresentMon service session could not be opened.",
                    capturedAt);
            }

            status = getRoot(session, out root);
            if (status != PresentMonSuccess || root == 0)
            {
                return new PresentMonDeviceInventory(
                    PresentMonDiscoveryStatus.IntrospectionFailed,
                    [],
                    resolvedPath,
                    status,
                    "PresentMon introspection root could not be acquired.",
                    capturedAt);
            }

            var graphicsDevices = ReadGraphicsDevices(root);
            return new PresentMonDeviceInventory(
                PresentMonDiscoveryStatus.Available,
                graphicsDevices,
                resolvedPath,
                status,
                null,
                capturedAt);
        }
        catch (Exception exception) when (exception is
            BadImageFormatException or
            DllNotFoundException or
            EntryPointNotFoundException or
            InvalidDataException)
        {
            return new PresentMonDeviceInventory(
                PresentMonDiscoveryStatus.InvalidData,
                [],
                resolvedPath,
                null,
                $"{exception.GetType().Name}: {exception.Message}",
                capturedAt);
        }
        finally
        {
            if (root != 0 && freeRoot is not null)
            {
                _ = freeRoot(root);
            }

            if (session != 0 && closeSession is not null)
            {
                _ = closeSession(session);
            }

            if (library != 0)
            {
                NativeLibrary.Free(library);
            }
        }
    }

    private static IReadOnlyList<PresentMonGraphicsDeviceSnapshot> ReadGraphicsDevices(nint rootPointer)
    {
        var root = Marshal.PtrToStructure<PresentMonIntrospectionRoot>(rootPointer);
        if (root.Devices == 0)
        {
            return [];
        }

        var devices = Marshal.PtrToStructure<PresentMonObjectArray>(root.Devices);
        if (devices.Size > MaximumIntrospectionDevices)
        {
            throw new InvalidDataException(
                $"PresentMon introspection reported {devices.Size} devices, above the safety limit.");
        }

        if (devices.Size != 0 && devices.Data == 0)
        {
            throw new InvalidDataException("PresentMon returned a non-empty device array with a null data pointer.");
        }

        var result = new List<PresentMonGraphicsDeviceSnapshot>(checked((int)devices.Size));
        for (nuint index = 0; index < devices.Size; index++)
        {
            var devicePointer = Marshal.ReadIntPtr(devices.Data, checked((int)(index * (nuint)IntPtr.Size)));
            if (devicePointer == 0)
            {
                continue;
            }

            var device = Marshal.PtrToStructure<PresentMonIntrospectionDevice>(devicePointer);
            if (device.Type != GraphicsAdapterDeviceType)
            {
                continue;
            }

            result.Add(new PresentMonGraphicsDeviceSnapshot(
                device.Id,
                device.Vendor,
                ReadIntrospectionString(device.Name),
                ReadLuid(device.Luid)));
        }

        return result.ToArray();
    }

    private static GraphicsAdapterLuid? ReadLuid(nint luidPointer)
    {
        if (luidPointer == 0)
        {
            return null;
        }

        var luid = Marshal.PtrToStructure<PresentMonIntrospectionDeviceLuid>(luidPointer);
        if (luid.Data == 0 || luid.Size != 8)
        {
            return null;
        }

        var bytes = new byte[8];
        Marshal.Copy(luid.Data, bytes, 0, bytes.Length);
        return new GraphicsAdapterLuid(
            BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
    }

    private static string? ReadIntrospectionString(nint stringPointer)
    {
        if (stringPointer == 0)
        {
            return null;
        }

        var value = Marshal.PtrToStructure<PresentMonIntrospectionString>(stringPointer);
        return value.Data == 0 ? null : Marshal.PtrToStringUTF8(value.Data);
    }

    private static T GetDelegate<T>(nint library, string exportName)
        where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, exportName));

    private static string? ResolveApiPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var fullPath = Path.GetFullPath(explicitPath);
            return File.Exists(fullPath) ? fullPath : null;
        }

        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "PresentMon", "PresentMonAPI2.dll"),
        };

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            candidates.Add(Path.Combine(programFiles, "Intel", "PresentMon", "PresentMonAPI2.dll"));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PresentMonIntrospectionRoot
    {
        internal readonly nint Metrics;
        internal readonly nint Enums;
        internal readonly nint Devices;
        internal readonly nint Units;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PresentMonObjectArray
    {
        internal readonly nint Data;
        internal readonly nuint Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PresentMonIntrospectionString
    {
        internal readonly nint Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PresentMonIntrospectionDeviceLuid
    {
        internal readonly nint Data;
        internal readonly uint Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PresentMonIntrospectionDevice
    {
        internal readonly uint Id;
        internal readonly int Type;
        internal readonly int Vendor;
        internal readonly nint Name;
        internal readonly nint Luid;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonOpenSession(out nint session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonOpenSessionWithPipe(
        out nint session,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string controlPipeName);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonCloseSession(nint session);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonGetIntrospectionRoot(nint session, out nint root);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PresentMonFreeIntrospectionRoot(nint root);
}

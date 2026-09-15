using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace LatencyPilot.Platform.Windows.Interop;

internal sealed class TransactionalRegistry : IDisposable
{
    private readonly SafeFileHandle transactionHandle;
    private bool completed;
    private bool disposed;

    private TransactionalRegistry(SafeFileHandle transactionHandle)
    {
        this.transactionHandle = transactionHandle;
    }

    internal static TransactionalRegistry Begin(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var handle = TransactionalRegistryNative.CreateTransaction(
            0,
            0,
            0,
            0,
            0,
            0,
            description);
        if (handle == new nint(-1))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to create a Kernel Transaction Manager transaction.");
        }

        return new TransactionalRegistry(new SafeFileHandle(handle, ownsHandle: true));
    }

    internal TransactionalRegistryKey CreateOrOpenKey(RegistryHive hive, string subKeyPath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureActive();
        ArgumentException.ThrowIfNullOrWhiteSpace(subKeyPath);
        if (subKeyPath.Contains('\0'))
        {
            throw new ArgumentException("Registry path contains an invalid NUL character.", nameof(subKeyPath));
        }

        var status = TransactionalRegistryNative.RegCreateKeyTransacted(
            RootHandle(hive),
            subKeyPath,
            0,
            null,
            0,
            TransactionalRegistryNative.KeyReadWrite64,
            0,
            out var keyHandle,
            out _,
            transactionHandle.DangerousGetHandle(),
            0);
        if (status != 0)
        {
            throw new Win32Exception(status, $"Unable to create/open transacted registry key '{subKeyPath}'.");
        }

        return new TransactionalRegistryKey(new SafeRegistryHandle(keyHandle, ownsHandle: true));
    }

    internal void DeleteKey(RegistryHive hive, string subKeyPath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureActive();
        ArgumentException.ThrowIfNullOrWhiteSpace(subKeyPath);
        if (subKeyPath.Contains('\0'))
        {
            throw new ArgumentException("Registry path contains an invalid NUL character.", nameof(subKeyPath));
        }

        var status = TransactionalRegistryNative.RegDeleteKeyTransacted(
            RootHandle(hive),
            subKeyPath,
            TransactionalRegistryNative.KeyWow6464,
            0,
            transactionHandle.DangerousGetHandle(),
            0);
        if (status != 0)
        {
            throw new Win32Exception(status, $"Unable to delete transacted registry key '{subKeyPath}'.");
        }
    }

    internal void Commit()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsureActive();

        if (!TransactionalRegistryNative.CommitTransaction(transactionHandle.DangerousGetHandle()))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Unable to commit the registry transaction.");
        }

        completed = true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (!completed && !transactionHandle.IsInvalid && !transactionHandle.IsClosed)
        {
            _ = TransactionalRegistryNative.RollbackTransaction(transactionHandle.DangerousGetHandle());
        }

        transactionHandle.Dispose();
        disposed = true;
    }

    private void EnsureActive()
    {
        if (completed)
        {
            throw new InvalidOperationException("The registry transaction is already committed.");
        }
    }

    private static nint RootHandle(RegistryHive hive) => hive switch
    {
        RegistryHive.CurrentUser => unchecked((nint)(int)0x80000001u),
        RegistryHive.LocalMachine => unchecked((nint)(int)0x80000002u),
        _ => throw new NotSupportedException($"Transactional registry root {hive} is not supported."),
    };
}

internal sealed class TransactionalRegistryKey : IDisposable
{
    private const int ErrorFileNotFound = 2;
    private readonly SafeRegistryHandle handle;
    private bool disposed;

    internal TransactionalRegistryKey(SafeRegistryHandle handle)
    {
        this.handle = handle ?? throw new ArgumentNullException(nameof(handle));
    }

    internal void SetValue(string name, int value, RegistryValueKind kind)
    {
        if (kind != RegistryValueKind.DWord)
        {
            throw new ArgumentException("An Int32 transacted registry value must use RegistryValueKind.DWord.", nameof(kind));
        }

        SetRawValue(name, kind, BitConverter.GetBytes(value));
    }

    internal void SetValue(string name, long value, RegistryValueKind kind)
    {
        if (kind != RegistryValueKind.QWord)
        {
            throw new ArgumentException("An Int64 transacted registry value must use RegistryValueKind.QWord.", nameof(kind));
        }

        SetRawValue(name, kind, BitConverter.GetBytes(value));
    }

    internal void SetValue(string name, byte[] value, RegistryValueKind kind)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (kind != RegistryValueKind.Binary)
        {
            throw new ArgumentException("A byte-array transacted registry value must use RegistryValueKind.Binary.", nameof(kind));
        }

        SetRawValue(name, kind, value);
    }

    internal void DeleteValue(string name)
    {
        EnsureOpen();
        ValidateValueName(name);

        var status = TransactionalRegistryNative.RegDeleteValue(handle.DangerousGetHandle(), name);
        if (status is 0 or ErrorFileNotFound)
        {
            return;
        }

        throw new Win32Exception(status, $"Unable to delete transacted registry value '{name}'.");
    }

    internal bool ValueExists(string name)
    {
        EnsureOpen();
        ValidateValueName(name);

        uint size = 0;
        var status = TransactionalRegistryNative.RegQueryValueEx(
            handle.DangerousGetHandle(),
            name,
            0,
            out _,
            0,
            ref size);
        return status switch
        {
            0 => true,
            ErrorFileNotFound => false,
            _ => throw new Win32Exception(status, $"Unable to inspect transacted registry value '{name}'."),
        };
    }

    internal (uint SubKeyCount, uint ValueCount) GetCounts()
    {
        EnsureOpen();
        var status = TransactionalRegistryNative.RegQueryInfoKey(
            handle.DangerousGetHandle(),
            0,
            0,
            0,
            out var subKeyCount,
            0,
            0,
            out var valueCount,
            0,
            0,
            0,
            0);
        if (status != 0)
        {
            throw new Win32Exception(status, "Unable to inspect transacted registry key contents.");
        }

        return (subKeyCount, valueCount);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        handle.Dispose();
        disposed = true;
    }

    private unsafe void SetRawValue(string name, RegistryValueKind kind, byte[] data)
    {
        EnsureOpen();
        ValidateValueName(name);

        fixed (byte* dataPointer = data)
        {
            var status = TransactionalRegistryNative.RegSetValueEx(
                handle.DangerousGetHandle(),
                name,
                0,
                unchecked((uint)kind),
                dataPointer,
                checked((uint)data.Length));
            if (status != 0)
            {
                throw new Win32Exception(status, $"Unable to write transacted registry value '{name}'.");
            }
        }
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(disposed, this);

    private static void ValidateValueName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('\0'))
        {
            throw new ArgumentException("Registry value name contains an invalid NUL character.", nameof(name));
        }
    }
}

internal static partial class TransactionalRegistryNative
{
    internal const uint KeyWow6464 = 0x0100;
    internal const uint KeyReadWrite64 = 0x0002011F;

    [LibraryImport("KtmW32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateTransaction(
        nint transactionAttributes,
        nint unitOfWork,
        uint createOptions,
        uint isolationLevel,
        uint isolationFlags,
        uint timeout,
        string? description);

    [LibraryImport("KtmW32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CommitTransaction(nint transactionHandle);

    [LibraryImport("KtmW32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RollbackTransaction(nint transactionHandle);

    [LibraryImport("advapi32.dll", EntryPoint = "RegCreateKeyTransactedW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegCreateKeyTransacted(
        nint key,
        string subKey,
        uint reserved,
        string? keyClass,
        uint options,
        uint desiredAccess,
        nint securityAttributes,
        out nint resultKey,
        out uint disposition,
        nint transactionHandle,
        nint extendedParameter);

    [LibraryImport("advapi32.dll", EntryPoint = "RegDeleteKeyTransactedW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegDeleteKeyTransacted(
        nint key,
        string subKey,
        uint desiredAccess,
        uint reserved,
        nint transactionHandle,
        nint extendedParameter);

    [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial int RegSetValueEx(
        nint key,
        string valueName,
        uint reserved,
        uint type,
        byte* data,
        uint dataLength);

    [LibraryImport("advapi32.dll", EntryPoint = "RegDeleteValueW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegDeleteValue(nint key, string valueName);

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegQueryValueEx(
        nint key,
        string valueName,
        nint reserved,
        out uint type,
        nint data,
        ref uint dataLength);

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW")]
    internal static partial int RegQueryInfoKey(
        nint key,
        nint keyClass,
        nint keyClassLength,
        nint reserved,
        out uint subKeyCount,
        nint maxSubKeyLength,
        nint maxClassLength,
        out uint valueCount,
        nint maxValueNameLength,
        nint maxValueLength,
        nint securityDescriptorLength,
        nint lastWriteTime);
}

namespace LatencyPilot.Service;

/// <summary>
/// Serializes machine mutation, recovery and retained-restore operations across
/// LatencyPilot processes. A Windows mutex is released by the kernel if its owner
/// process/thread dies; SQLite CAS remains the second concurrency layer.
/// </summary>
internal sealed class MutationOperationLock : IDisposable
{
    private const string MutexName = @"Global\LatencyPilot.MutationOperation.v1";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly Mutex mutex;
    private bool ownsMutex;
    private bool disposed;

    private MutationOperationLock(Mutex mutex)
    {
        this.mutex = mutex;
        ownsMutex = true;
    }

    internal static MutationOperationLock Acquire() => Acquire(DefaultTimeout);

    internal static MutationOperationLock Acquire(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var mutex = new Mutex(initiallyOwned: false, MutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(timeout);
            }
            catch (AbandonedMutexException)
            {
                // The previous owner died. Windows transfers ownership to this
                // thread; actual journal + device state is still re-read by the
                // mutation/recovery caller before any subsequent write.
                acquired = true;
            }

            if (!acquired)
            {
                throw new TimeoutException(
                    "Another LatencyPilot mutation/recovery operation owns the machine mutation lock. Retry only after that operation completes or recovery inspects its journal state.");
            }

            return new MutationOperationLock(mutex);
        }
        catch
        {
            if (!acquired)
            {
                mutex.Dispose();
            }
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ownsMutex)
        {
            ownsMutex = false;
            mutex.ReleaseMutex();
        }
        mutex.Dispose();
    }
}

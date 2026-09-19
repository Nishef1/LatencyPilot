using System.Collections.Concurrent;
using LatencyPilot.Core.Benchmarking;

namespace LatencyPilot.GpuBenchmark;

/// <summary>
/// Owns every HWND/D3D12 renderer operation on one dedicated Windows thread.
/// Async pipe continuations enqueue work here and never touch renderer/window state directly.
/// </summary>
internal sealed class BenchmarkRendererOwner : IAsyncDisposable
{
    private static readonly TimeSpan IdlePumpInterval = TimeSpan.FromMilliseconds(15);

    private readonly Func<D3D12BenchmarkRenderer> rendererFactory;
    private readonly BlockingCollection<Action> workQueue = new();
    private readonly Thread ownerThread;
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private D3D12BenchmarkRenderer? renderer;
    private Exception? terminalFailure;
    private int disposeRequested;

    private BenchmarkRendererOwner(Func<D3D12BenchmarkRenderer> rendererFactory)
    {
        this.rendererFactory = rendererFactory ?? throw new ArgumentNullException(nameof(rendererFactory));
        ownerThread = new Thread(ThreadMain)
        {
            IsBackground = false,
            Name = "LatencyPilot GPU benchmark renderer owner",
        };
        ownerThread.SetApartmentState(ApartmentState.STA);
        ownerThread.Start();
    }

    internal static async Task<BenchmarkRendererOwner> CreateAsync(
        Func<D3D12BenchmarkRenderer> rendererFactory)
    {
        var owner = new BenchmarkRendererOwner(rendererFactory);
        try
        {
            await owner.initialized.Task.ConfigureAwait(false);
            return owner;
        }
        catch
        {
            await owner.completed.Task.ConfigureAwait(false);
            owner.workQueue.Dispose();
            throw;
        }
    }

    internal Task<FrozenBenchmarkWorkload> CalibrateAsync(
        BenchmarkWorkload benchmark,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(benchmark);
        return InvokeAsync(
            active => benchmark.CalibrateAsync(active, cancellationToken).GetAwaiter().GetResult(),
            cancellationToken);
    }

    internal Task<GpuBenchmarkTrialArtifact> RunTrialAsync(
        BenchmarkWorkload benchmark,
        FrozenBenchmarkWorkload workload,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(benchmark);
        ArgumentNullException.ThrowIfNull(workload);
        return InvokeAsync(
            active => benchmark.RunTrialAsync(active, workload, duration, cancellationToken).GetAwaiter().GetResult(),
            cancellationToken);
    }

    internal Task RecreateRendererAsync(CancellationToken cancellationToken = default) =>
        InvokeOwnerAsync(
            () =>
            {
                // Microsoft documents that D3D12CreateDevice can fail when the
                // process still owns a removed device for the same adapter.
                // Release every old device-dependent resource first, then create
                // the replacement. Keep renderer null if creation fails so the
                // server's bounded recreation retry can try again cleanly.
                var active = renderer;
                renderer = null;
                active?.Dispose();
                renderer = rendererFactory();
                return true;
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeRequested, 1) == 0)
        {
            workQueue.CompleteAdding();
        }

        await completed.Task.ConfigureAwait(false);
        workQueue.Dispose();
    }

    private Task<T> InvokeOwnerAsync<T>(
        Func<T> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeRequested) != 0, this);

        if (terminalFailure is { } failure)
        {
            return Task.FromException<T>(new InvalidOperationException(
                "The benchmark renderer owner thread is no longer available.",
                failure));
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            workQueue.Add(
                () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                        return;
                    }

                    try
                    {
                        completion.TrySetResult(action());
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                },
                cancellationToken);
        }
        catch (InvalidOperationException) when (workQueue.IsAddingCompleted)
        {
            completion.TrySetException(new ObjectDisposedException(nameof(BenchmarkRendererOwner)));
        }

        return completion.Task;
    }

    private Task<T> InvokeAsync<T>(
        Func<D3D12BenchmarkRenderer, T> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeRequested) != 0, this);

        if (terminalFailure is { } failure)
        {
            return Task.FromException<T>(new InvalidOperationException(
                "The benchmark renderer owner thread is no longer available.",
                failure));
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            workQueue.Add(
                () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                        return;
                    }

                    try
                    {
                        var active = renderer
                            ?? throw new InvalidOperationException("Renderer owner has no active D3D12 renderer.");
                        completion.TrySetResult(action(active));
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(cancellationToken);
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                },
                cancellationToken);
        }
        catch (InvalidOperationException) when (workQueue.IsAddingCompleted)
        {
            completion.TrySetException(new ObjectDisposedException(nameof(BenchmarkRendererOwner)));
        }

        return completion.Task;
    }

    private void ThreadMain()
    {
        Exception? fatal = null;
        try
        {
            renderer = rendererFactory();
            initialized.TrySetResult();

            while (!workQueue.IsCompleted)
            {
                if (workQueue.TryTake(out var action, IdlePumpInterval))
                {
                    action();
                    continue;
                }

                if (renderer is { } active && !active.PumpMessages())
                {
                    throw new OperationCanceledException(
                        "The benchmark render window was closed on its owner thread.");
                }
            }
        }
        catch (Exception exception)
        {
            fatal = exception;
            terminalFailure = exception;
            initialized.TrySetException(exception);
        }
        finally
        {
            try
            {
                renderer?.Dispose();
            }
            catch (Exception disposeFailure)
            {
                fatal ??= disposeFailure;
                terminalFailure ??= disposeFailure;
            }
            finally
            {
                renderer = null;
            }

            while (workQueue.TryTake(out var pending))
            {
                try
                {
                    pending();
                }
                catch
                {
                    // Pending closures own their TaskCompletionSource and fail it themselves.
                }
            }

            if (fatal is null)
            {
                completed.TrySetResult();
            }
            else
            {
                // Shutdown remains awaitable without hiding the first renderer failure.
                completed.TrySetResult();
            }
        }
    }
}

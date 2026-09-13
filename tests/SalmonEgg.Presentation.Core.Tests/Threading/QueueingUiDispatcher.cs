using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.Tests.Threading;

/// <summary>
/// Queueing <see cref="IUiDispatcher"/> for tests that need to defer
/// dispatch until <see cref="RunAll"/> is called explicitly.
/// </summary>
public sealed class QueueingUiDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<Action> _callbacks = new();

    public bool HasThreadAccess => false;

    public int PendingCount => _callbacks.Count;

    public void Enqueue(Action action)
    {
        _callbacks.Enqueue(action);
    }

    public Task EnqueueAsync(Action action)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _callbacks.Enqueue(() =>
        {
            try
            {
                action();
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    public Task EnqueueAsync(Func<Task> function)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _callbacks.Enqueue(() =>
        {
            try
            {
                function().GetAwaiter().GetResult();
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>
    /// Dequeues and executes one queued callback.
    /// Returns true if a callback was executed, false if the queue was empty.
    /// </summary>
    public bool RunNext()
    {
        if (!_callbacks.TryDequeue(out var callback))
        {
            return false;
        }

        callback();
        return true;
    }

    public void RunAll()
    {
        while (RunNext())
        {
        }
    }
}

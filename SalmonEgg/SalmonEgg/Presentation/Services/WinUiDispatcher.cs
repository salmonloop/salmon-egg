using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Services;

public class WinUiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue;
    private readonly ILogger<WinUiDispatcher> _logger;

    public WinUiDispatcher(DispatcherQueue queue, ILogger<WinUiDispatcher> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue), "DispatcherQueue cannot be null. Ensure WinUiDispatcher is initialized on a thread with a dispatcher.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool HasThreadAccess => _queue.HasThreadAccess;

    public void Enqueue(Action action)
    {
        var success = _queue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in enqueued UI action.");
                throw;
            }
        });

        if (!success)
        {
            _logger.LogWarning("Failed to enqueue action to DispatcherQueue. The queue might be shutting down.");
        }
    }

    public Task EnqueueAsync(Action action)
    {
        if (HasThreadAccess)
        {
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        var tcs = new TaskCompletionSource<bool>();
        var success = _queue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            try
            {
                action();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in enqueued async Action.");
                tcs.TrySetException(ex);
            }
        });

        if (!success)
        {
            _logger.LogWarning("Failed to enqueue async action to DispatcherQueue.");
            return Task.FromException(new InvalidOperationException("Failed to enqueue action to DispatcherQueue. The queue might be shutting down."));
        }

        return tcs.Task;
    }

    public async Task EnqueueAsync(Func<Task> function)
    {
        if (HasThreadAccess)
        {
            await function();
            return;
        }

        var tcs = new TaskCompletionSource<bool>();
        var success = _queue.TryEnqueue(DispatcherQueuePriority.Normal, async () =>
        {
            try
            {
                await function();
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in enqueued async Func<Task>.");
                tcs.TrySetException(ex);
            }
        });

        if (!success)
        {
            _logger.LogWarning("Failed to enqueue async function to DispatcherQueue.");
            throw new InvalidOperationException("Failed to enqueue function to DispatcherQueue. The queue might be shutting down.");
        }

        await tcs.Task;
    }
}

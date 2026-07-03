using SalmonEgg.Presentation.Core.Services;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Threading;

public sealed class UiDispatcherProjectionBarrierTests
{
    [Fact]
    public async Task AwaitQueuedTurnAsync_WhenCalledFromUiThread_CompletesOnLaterQueuedTurn()
    {
        var dispatcher = new QueueingUiDispatcherWithInlineUiAsync();
        var barrier = new UiDispatcherProjectionBarrier(dispatcher);
        Task? barrierTask = null;
        var observedInlineCompletion = true;

        dispatcher.Enqueue(() =>
        {
            barrierTask = barrier.AwaitQueuedTurnAsync();
            observedInlineCompletion = barrierTask.IsCompleted;
        });

        dispatcher.RunNext();

        Assert.NotNull(barrierTask);
        Assert.False(observedInlineCompletion);
        Assert.False(barrierTask!.IsCompleted);

        dispatcher.RunNext();

        await barrierTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    private sealed class QueueingUiDispatcherWithInlineUiAsync : IUiDispatcher
    {
        private readonly Queue<Action> _work = new();
        private bool _hasThreadAccess;

        public bool HasThreadAccess => _hasThreadAccess;

        public void Enqueue(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            _work.Enqueue(action);
        }

        public Task EnqueueAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (_hasThreadAccess)
            {
                action();
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<object?>();
            Enqueue(() =>
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
            ArgumentNullException.ThrowIfNull(function);
            if (_hasThreadAccess)
            {
                return function();
            }

            var tcs = new TaskCompletionSource<object?>();
            Enqueue(() =>
            {
                try
                {
                    var task = function();
                    if (task.IsCompletedSuccessfully)
                    {
                        tcs.TrySetResult(null);
                    }
                    else if (task.IsFaulted)
                    {
                        tcs.TrySetException(task.Exception!.InnerException ?? task.Exception);
                    }
                    else if (task.IsCanceled)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        _ = task.ContinueWith(
                            static (completedTask, state) =>
                            {
                                var source = (TaskCompletionSource<object?>)state!;
                                if (completedTask.IsFaulted)
                                {
                                    source.TrySetException(
                                        completedTask.Exception!.InnerException ?? completedTask.Exception);
                                }
                                else if (completedTask.IsCanceled)
                                {
                                    source.TrySetCanceled();
                                }
                                else
                                {
                                    source.TrySetResult(null);
                                }
                            },
                            tcs,
                            TaskContinuationOptions.ExecuteSynchronously);
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
        }

        public bool RunNext()
        {
            if (_work.Count == 0)
            {
                return false;
            }

            var action = _work.Dequeue();
            _hasThreadAccess = true;
            try
            {
                action();
            }
            finally
            {
                _hasThreadAccess = false;
            }

            return true;
        }
    }
}

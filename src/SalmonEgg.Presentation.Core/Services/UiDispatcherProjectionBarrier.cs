using System;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class UiDispatcherProjectionBarrier
{
    private readonly IUiDispatcher _uiDispatcher;

    public UiDispatcherProjectionBarrier(IUiDispatcher uiDispatcher)
    {
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
    }

    public Task AwaitQueuedTurnAsync()
    {
        var completion = new TaskCompletionSource<object?>();
        try
        {
            _uiDispatcher.Enqueue(() => completion.TrySetResult(null));
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }

        return completion.Task;
    }
}

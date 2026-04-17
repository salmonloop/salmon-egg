using System;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Core.Tests.Threading;

public class ImmediateUiDispatcher : IUiDispatcher
{
    public bool HasThreadAccess => true;

    public void Enqueue(Action action)
    {
        action();
    }

    public Task EnqueueAsync(Action action)
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

    public async Task EnqueueAsync(Func<Task> function)
    {
        await function();
    }
}

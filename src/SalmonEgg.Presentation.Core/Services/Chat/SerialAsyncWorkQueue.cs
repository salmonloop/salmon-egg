using System;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class SerialAsyncWorkQueue
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;

    public Task Enqueue(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        lock (_gate)
        {
            _tail = RunAsync(_tail, work);
            return _tail;
        }
    }

    private static async Task RunAsync(Task previous, Func<Task> work)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch
        {
        }

        await work().ConfigureAwait(false);
    }
}

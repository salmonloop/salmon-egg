namespace SalmonEgg.Presentation.Core.Services;

public interface IUiDispatcher
{
    bool HasThreadAccess { get; }
    void Enqueue(Action action);
    Task EnqueueAsync(Action action);
    Task EnqueueAsync(Func<Task> function);
}

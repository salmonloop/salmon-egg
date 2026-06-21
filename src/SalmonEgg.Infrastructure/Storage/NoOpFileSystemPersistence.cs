using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class NoOpFileSystemPersistence : IFileSystemPersistence
{
    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

internal sealed class ThrowingConfigurationFileTransaction : IConfigurationFileTransaction
{
    private readonly Exception _exception;

    public ThrowingConfigurationFileTransaction(Exception exception)
    {
        _exception = exception;
    }

    public Task ApplyAndFlushAsync(CancellationToken cancellationToken = default)
        => Task.FromException(_exception);

    public Task RollbackAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Complete()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

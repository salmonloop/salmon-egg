using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class ApplicationShutdownWorkflow : IApplicationShutdownWorkflow
{
    private readonly IChatRuntimePersistence _chatRuntimePersistence;
    private readonly ILogger<ApplicationShutdownWorkflow> _logger;
    private readonly object _shutdownSync = new();
    private Task? _shutdownTask;

    public ApplicationShutdownWorkflow(
        IChatRuntimePersistence chatRuntimePersistence,
        ILogger<ApplicationShutdownWorkflow> logger)
    {
        _chatRuntimePersistence = chatRuntimePersistence ?? throw new ArgumentNullException(nameof(chatRuntimePersistence));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        lock (_shutdownSync)
        {
            // Several close paths can race to end the process; they all join the same run so state is
            // flushed once. The completed task is kept so late callers return immediately.
            _shutdownTask ??= ShutdownCoreAsync(cancellationToken);
            return _shutdownTask;
        }
    }

    private async Task ShutdownCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _chatRuntimePersistence.FlushPendingStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Application shutdown flush was canceled; pending state may not be persisted");
        }
        catch (Exception ex)
        {
            // Teardown must not throw into a platform close handler: a failed flush should not also
            // block the window from closing.
            _logger.LogError(ex, "Application shutdown flush failed");
        }
    }
}

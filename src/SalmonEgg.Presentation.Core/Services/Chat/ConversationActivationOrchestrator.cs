using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Serializes conversation activation while preserving latest-intent semantics.
/// </summary>
public sealed class ConversationActivationOrchestrator : IConversationActivationOrchestrator
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _foregroundGate = new(1, 1);
    private readonly ILogger<ConversationActivationOrchestrator> _logger;
    private long _activationVersion;
    private CancellationTokenSource? _currentActivationCts;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConversationActivationOrchestrator"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public ConversationActivationOrchestrator(ILogger<ConversationActivationOrchestrator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public long CurrentActivationVersion => Interlocked.Read(ref _activationVersion);

    /// <inheritdoc />
    public bool IsLatestActivationVersion(long activationVersion)
        => CurrentActivationVersion == activationVersion;

    /// <inheritdoc />
    public async Task<ConversationActivationOrchestratorResult> ActivateAsync(
        ConversationActivationOrchestratorRequest request,
        IConversationActivationOrchestratorSink sink,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sink);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.ConversationId))
        {
            return ConversationActivationOrchestratorResult.Failed();
        }

        if (await sink.CanReuseWarmCurrentConversationAsync(request, cancellationToken).ConfigureAwait(false))
        {
            await sink.PrepareActivationAsync(request, cancellationToken).ConfigureAwait(false);
            var warmReuseContext = BeginActivation(cancellationToken);
            ConversationActivationOrchestratorResult warmReuseResult;
            try
            {
                await sink
                    .SupersedePendingActivationForWarmConversationAsync(
                        request,
                        warmReuseContext,
                        warmReuseContext.CancellationToken)
                    .ConfigureAwait(false);
                warmReuseResult = ConversationActivationOrchestratorResult.Success(usedWarmReuse: true);
            }
            catch (OperationCanceledException) when (warmReuseContext.CancellationToken.IsCancellationRequested)
            {
                warmReuseResult = ConversationActivationOrchestratorResult.Superseded();
            }

            await FinalizeActivationAsync(request, warmReuseContext, sink, warmReuseResult, CancellationToken.None)
                .ConfigureAwait(false);
            return warmReuseResult;
        }

        if (await sink.CanReusePendingRemoteHydrationCurrentConversationAsync(request, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "Skipping duplicate activation because the requested conversation already owns the pending remote hydration path. ConversationId={ConversationId}",
                request.ConversationId);
            return ConversationActivationOrchestratorResult.Success();
        }

        await sink.PrepareActivationAsync(request, cancellationToken).ConfigureAwait(false);
        var context = BeginActivation(cancellationToken);
        try
        {
            await context.WaitForForegroundGateAsync().ConfigureAwait(false);
            var result = await sink.ExecuteActivationAsync(request, context, context.CancellationToken).ConfigureAwait(false);
            if (!result.CompletionOwnedByBackground)
            {
                await FinalizeActivationAsync(request, context, sink, result, CancellationToken.None).ConfigureAwait(false);
            }

            return result;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            var result = ConversationActivationOrchestratorResult.Superseded();
            await FinalizeActivationAsync(request, context, sink, result, CancellationToken.None).ConfigureAwait(false);
            return result;
        }
    }

    /// <inheritdoc />
    public Task CompleteDeferredActivationAsync(
        ConversationActivationOrchestratorRequest request,
        ConversationActivationContext context,
        IConversationActivationOrchestratorSink sink,
        ConversationActivationOrchestratorResult result,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sink);
        return FinalizeActivationAsync(request, context, sink, result, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancellationTokenSource? currentCts;
        lock (_sync)
        {
            currentCts = _currentActivationCts;
            _currentActivationCts = null;
        }

        try
        {
            currentCts?.Cancel();
        }
        finally
        {
            currentCts?.Dispose();
            _foregroundGate.Dispose();
        }
    }

    private ConversationActivationContext BeginActivation(CancellationToken cancellationToken)
    {
        var activationVersion = Interlocked.Increment(ref _activationVersion);
        var currentCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previousCts;

        lock (_sync)
        {
            previousCts = _currentActivationCts;
            _currentActivationCts = currentCts;
        }

        previousCts?.Cancel();

        return new ConversationActivationContext(activationVersion, currentCts, _foregroundGate);
    }

    private async Task FinalizeActivationAsync(
        ConversationActivationOrchestratorRequest request,
        ConversationActivationContext context,
        IConversationActivationOrchestratorSink sink,
        ConversationActivationOrchestratorResult result,
        CancellationToken cancellationToken)
    {
        var completedCurrentActivation = TryCompleteActivationCore(context);
        if (!completedCurrentActivation)
        {
            return;
        }

        await sink.OnActivationCompletedAsync(request, context, result, cancellationToken).ConfigureAwait(false);
    }

    private bool TryCompleteActivationCore(ConversationActivationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ReleaseForegroundGate();

        var completedCurrentActivation = false;
        lock (_sync)
        {
            if (ReferenceEquals(_currentActivationCts, context.CancellationTokenSource)
                && CurrentActivationVersion == context.ActivationVersion)
            {
                _currentActivationCts = null;
                completedCurrentActivation = true;
            }
        }

        context.CancellationTokenSource.Dispose();
        return completedCurrentActivation;
    }
}

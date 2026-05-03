using System;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Owns latest-intent conversation activation concurrency and serialization semantics.
/// </summary>
public interface IConversationActivationOrchestrator : IDisposable
{
    /// <summary>
    /// Gets the latest activation version observed by the orchestrator.
    /// </summary>
    long CurrentActivationVersion { get; }

    /// <summary>
    /// Determines whether the supplied activation version is still authoritative.
    /// </summary>
    /// <param name="activationVersion">The activation version to validate.</param>
    /// <returns><see langword="true"/> when the version is still current; otherwise <see langword="false"/>.</returns>
    bool IsLatestActivationVersion(long activationVersion);

    /// <summary>
    /// Runs the latest-intent activation pipeline for a conversation.
    /// </summary>
    /// <param name="request">The activation request.</param>
    /// <param name="sink">The view-model sink that performs activation side effects.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>The activation result.</returns>
    Task<ConversationActivationOrchestratorResult> ActivateAsync(
        ConversationActivationOrchestratorRequest request,
        IConversationActivationOrchestratorSink sink,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes an activation whose remote phase was intentionally deferred to background execution.
    /// </summary>
    /// <param name="request">The activation request.</param>
    /// <param name="context">The activation context returned to the sink.</param>
    /// <param name="sink">The sink that owns UI-side completion effects.</param>
    /// <param name="result">The terminal activation result.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    Task CompleteDeferredActivationAsync(
        ConversationActivationOrchestratorRequest request,
        ConversationActivationContext context,
        IConversationActivationOrchestratorSink sink,
        ConversationActivationOrchestratorResult result,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Describes a conversation activation request.
/// </summary>
/// <param name="ConversationId">The target local conversation identifier.</param>
/// <param name="AwaitRemoteHydration">Whether the caller waits for remote hydration to complete.</param>
public sealed record ConversationActivationOrchestratorRequest(
    string ConversationId,
    bool AwaitRemoteHydration);

/// <summary>
/// Describes the outcome of one activation attempt.
/// </summary>
/// <param name="Succeeded">Whether activation succeeded.</param>
/// <param name="UsedWarmReuse">Whether the activation completed via warm reuse.</param>
/// <param name="WasSuperseded">Whether the activation became stale because a newer intent won.</param>
/// <param name="CompletionOwnedByBackground">Whether terminal completion will be reported later via <see cref="IConversationActivationOrchestrator.CompleteDeferredActivationAsync(ConversationActivationOrchestratorRequest, ConversationActivationContext, IConversationActivationOrchestratorSink, ConversationActivationOrchestratorResult, CancellationToken)"/>.</param>
public sealed record ConversationActivationOrchestratorResult(
    bool Succeeded,
    bool UsedWarmReuse,
    bool WasSuperseded,
    bool CompletionOwnedByBackground)
{
    /// <summary>
    /// Creates a successful foreground activation result.
    /// </summary>
    /// <param name="usedWarmReuse">Whether warm reuse completed the activation.</param>
    /// <returns>A successful result.</returns>
    public static ConversationActivationOrchestratorResult Success(bool usedWarmReuse = false)
        => new(true, usedWarmReuse, false, false);

    /// <summary>
    /// Creates a successful activation result whose completion is deferred to background work.
    /// </summary>
    /// <returns>A successful deferred-completion result.</returns>
    public static ConversationActivationOrchestratorResult BackgroundOwnedSuccess()
        => new(true, false, false, true);

    /// <summary>
    /// Creates a result representing a stale activation that lost latest-intent ownership.
    /// </summary>
    /// <returns>A superseded result.</returns>
    public static ConversationActivationOrchestratorResult Superseded()
        => new(false, false, true, false);

    /// <summary>
    /// Creates a failed activation result.
    /// </summary>
    /// <returns>A failed result.</returns>
    public static ConversationActivationOrchestratorResult Failed()
        => new(false, false, false, false);
}

/// <summary>
/// Carries authoritative activation ownership state for one activation run.
/// </summary>
public sealed class ConversationActivationContext
{
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly SemaphoreSlim _foregroundGate;
    private int _foregroundGateHeld;

    internal ConversationActivationContext(
        long activationVersion,
        CancellationTokenSource cancellationTokenSource,
        SemaphoreSlim foregroundGate)
    {
        ActivationVersion = activationVersion;
        _cancellationTokenSource = cancellationTokenSource ?? throw new ArgumentNullException(nameof(cancellationTokenSource));
        _foregroundGate = foregroundGate ?? throw new ArgumentNullException(nameof(foregroundGate));
    }

    /// <summary>
    /// Gets the authoritative activation version assigned to this activation.
    /// </summary>
    public long ActivationVersion { get; }

    /// <summary>
    /// Gets the cancellation token that is canceled when a newer activation supersedes this one.
    /// </summary>
    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    internal CancellationTokenSource CancellationTokenSource => _cancellationTokenSource;

    internal bool ForegroundGateHeld => Volatile.Read(ref _foregroundGateHeld) == 1;

    /// <summary>
    /// Enters the serialized foreground activation section.
    /// </summary>
    public async Task WaitForForegroundGateAsync()
    {
        await _foregroundGate.WaitAsync(CancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _foregroundGateHeld, 1);
    }

    /// <summary>
    /// Releases the serialized foreground activation section.
    /// </summary>
    public void ReleaseForegroundGate()
    {
        if (Interlocked.Exchange(ref _foregroundGateHeld, 0) != 1)
        {
            return;
        }

        _foregroundGate.Release();
    }
}

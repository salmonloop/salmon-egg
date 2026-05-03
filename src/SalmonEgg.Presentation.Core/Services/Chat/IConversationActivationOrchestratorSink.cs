using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Defines the side-effect boundary required by <see cref="IConversationActivationOrchestrator"/>.
/// </summary>
public interface IConversationActivationOrchestratorSink
{
    /// <summary>
    /// Prepares the sink for a new authoritative activation scope.
    /// </summary>
    /// <param name="request">The activation request.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    Task PrepareActivationAsync(
        ConversationActivationOrchestratorRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether the requested conversation can immediately reuse the current warm activation.
    /// </summary>
    Task<bool> CanReuseWarmCurrentConversationAsync(
        ConversationActivationOrchestratorRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Supersedes a stale in-flight activation when the requested conversation is already warm.
    /// </summary>
    Task SupersedePendingActivationForWarmConversationAsync(
        ConversationActivationOrchestratorRequest request,
        ConversationActivationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether the requested conversation is already the current pending remote-hydration target.
    /// </summary>
    Task<bool> CanReusePendingRemoteHydrationCurrentConversationAsync(
        ConversationActivationOrchestratorRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the activation body for the request using the authoritative activation context.
    /// </summary>
    Task<ConversationActivationOrchestratorResult> ExecuteActivationAsync(
        ConversationActivationOrchestratorRequest request,
        ConversationActivationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies sink-specific completion behavior once the orchestrator has confirmed terminal ownership.
    /// </summary>
    Task OnActivationCompletedAsync(
        ConversationActivationOrchestratorRequest request,
        ConversationActivationContext context,
        ConversationActivationOrchestratorResult result,
        CancellationToken cancellationToken = default);
}

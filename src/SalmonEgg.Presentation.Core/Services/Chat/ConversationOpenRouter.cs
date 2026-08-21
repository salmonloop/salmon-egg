using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Routes an external "open this conversation" request into the authoritative navigation chain.
/// </summary>
public sealed class ConversationOpenRouter : IConversationOpenRouter
{
    private readonly IConversationCatalogDisplayReadModel _conversationCatalog;
    private readonly IConversationProjectAffinityResolver _affinityResolver;
    private readonly IConversationActivationEntryPoint _activationEntryPoint;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<ConversationOpenRouter> _logger;

    public ConversationOpenRouter(
        IConversationCatalogDisplayReadModel conversationCatalog,
        IConversationProjectAffinityResolver affinityResolver,
        IConversationActivationEntryPoint activationEntryPoint,
        IUiDispatcher uiDispatcher,
        ILogger<ConversationOpenRouter> logger)
    {
        _conversationCatalog = conversationCatalog ?? throw new ArgumentNullException(nameof(conversationCatalog));
        _affinityResolver = affinityResolver ?? throw new ArgumentNullException(nameof(affinityResolver));
        _activationEntryPoint = activationEntryPoint ?? throw new ArgumentNullException(nameof(activationEntryPoint));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ConversationOpenResult> OpenConversationAsync(string conversationId)
    {
        var normalizedId = conversationId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            return ConversationOpenResult.Invalid;
        }

        var result = ConversationOpenResult.Failed;

        // The catalog and the navigation owner are both UI-bound state, so the lookup and the
        // activation happen together on the UI thread: reading the catalog off-thread could observe
        // a half-applied snapshot, and activating off-thread would mutate bound state.
        await _uiDispatcher.EnqueueAsync(async () =>
        {
            result = await OpenOnUiThreadAsync(normalizedId).ConfigureAwait(true);
        }).ConfigureAwait(false);

        return result;
    }

    private async Task<ConversationOpenResult> OpenOnUiThreadAsync(string conversationId)
    {
        var conversation = _conversationCatalog.Snapshot
            .FirstOrDefault(item => string.Equals(item.ConversationId, conversationId, StringComparison.Ordinal));
        if (conversation is null)
        {
            // A conversation the user deleted, or one belonging to another install. Do not guess.
            _logger.LogInformation(
                "Conversation open request referenced an unknown conversation. ConversationId={ConversationId}",
                conversationId);
            return ConversationOpenResult.NotFound;
        }

        var projectId = _affinityResolver.ResolveActivationProjectId(new ConversationProjectAffinityRequest(
            conversation.Cwd,
            conversation.BoundProfileId,
            conversation.RemoteSessionId,
            conversation.ProjectAffinityOverrideProjectId));

        try
        {
            var activated = await _activationEntryPoint
                .ActivateSessionAsync(conversationId, projectId)
                .ConfigureAwait(true);
            if (activated)
            {
                return ConversationOpenResult.Opened;
            }

            // Activation reports rejection, supersession and cancellation as false without throwing.
            // The navigation owner already surfaced whatever the user needs to see.
            _logger.LogInformation(
                "Conversation open request did not activate. ConversationId={ConversationId} ProjectId={ProjectId}",
                conversationId,
                projectId);
            return ConversationOpenResult.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Conversation open request failed. ConversationId={ConversationId} ProjectId={ProjectId}",
                conversationId,
                projectId);
            return ConversationOpenResult.Failed;
        }
    }
}

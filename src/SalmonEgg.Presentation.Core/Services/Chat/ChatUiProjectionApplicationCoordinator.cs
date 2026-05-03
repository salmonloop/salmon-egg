using System;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IChatUiProjectionApplicationCoordinator
{
    void ArmActivationSelectionProjection(string conversationId, long activationVersion);

    bool ShouldApply(ChatUiProjection projection, long activationVersion);
}

public sealed class ChatUiProjectionApplicationCoordinator : IChatUiProjectionApplicationCoordinator
{
    private readonly object _sync = new();
    private PendingSelectionProjection? _pendingSelectionProjection;

    public void ArmActivationSelectionProjection(string conversationId, long activationVersion)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return;
        }

        lock (_sync)
        {
            _pendingSelectionProjection = new PendingSelectionProjection(
                conversationId,
                activationVersion,
                FirstAppliedProjection: null);
        }
    }

    public bool ShouldApply(ChatUiProjection projection, long activationVersion)
    {
        ArgumentNullException.ThrowIfNull(projection);

        if (string.IsNullOrWhiteSpace(projection.HydratedConversationId))
        {
            return true;
        }

        lock (_sync)
        {
            if (_pendingSelectionProjection is null)
            {
                return true;
            }

            if (activationVersion > _pendingSelectionProjection.ActivationVersion)
            {
                _pendingSelectionProjection = null;
                return true;
            }

            if (activationVersion != _pendingSelectionProjection.ActivationVersion
                || !string.Equals(
                    projection.HydratedConversationId,
                    _pendingSelectionProjection.ConversationId,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (_pendingSelectionProjection.FirstAppliedProjection is null)
            {
                _pendingSelectionProjection = _pendingSelectionProjection with { FirstAppliedProjection = projection };
                return true;
            }

            var shouldApply = !_pendingSelectionProjection.FirstAppliedProjection.Equals(projection);
            _pendingSelectionProjection = null;
            return shouldApply;
        }
    }

    private sealed record PendingSelectionProjection(
        string ConversationId,
        long ActivationVersion,
        ChatUiProjection? FirstAppliedProjection);
}

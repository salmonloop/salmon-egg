using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed record ChatSessionToolingProjection(
    IReadOnlyList<ConversationModeOptionSnapshot> AvailableModes,
    string? SelectedModeId,
    IReadOnlyList<ConversationConfigOptionSnapshot> ConfigOptions,
    bool ShowConfigOptionsPanel,
    IReadOnlyList<ConversationAvailableCommandSnapshot> AvailableCommands);

public sealed class ChatSessionToolingProjector
{
    public ChatSessionToolingProjection Project(ChatState storeState, string? hydratedConversationId)
    {
        ArgumentNullException.ThrowIfNull(storeState);

        if (!string.IsNullOrWhiteSpace(hydratedConversationId))
        {
            var sessionStateSlice = storeState.ResolveSessionStateSlice(hydratedConversationId);
            if (sessionStateSlice is ConversationSessionStateSlice scopedTooling)
            {
                return new ChatSessionToolingProjection(
                    scopedTooling.AvailableModes,
                    scopedTooling.SelectedModeId,
                    scopedTooling.ConfigOptions,
                    scopedTooling.ShowConfigOptionsPanel,
                    scopedTooling.AvailableCommands);
            }

            return new ChatSessionToolingProjection(
                storeState.AvailableModes ?? ImmutableList<ConversationModeOptionSnapshot>.Empty,
                storeState.SelectedModeId,
                storeState.ConfigOptions ?? ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                storeState.ShowConfigOptionsPanel,
                storeState.AvailableCommands ?? ImmutableList<ConversationAvailableCommandSnapshot>.Empty);
        }

        return new ChatSessionToolingProjection(
            storeState.AvailableModes ?? ImmutableList<ConversationModeOptionSnapshot>.Empty,
            storeState.SelectedModeId,
            storeState.ConfigOptions ?? ImmutableList<ConversationConfigOptionSnapshot>.Empty,
            storeState.ShowConfigOptionsPanel,
            storeState.AvailableCommands ?? ImmutableList<ConversationAvailableCommandSnapshot>.Empty);
    }
}

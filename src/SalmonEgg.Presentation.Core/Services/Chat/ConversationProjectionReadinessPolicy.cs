using System;
using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public static class ConversationProjectionReadinessPolicy
{
    public static bool ShouldHydrateContent(ChatState state, string conversationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var content = state.ResolveContentSlice(conversationId)
            ?? new ConversationContentSlice(
                ImmutableList<ConversationMessageSnapshot>.Empty,
                ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                false,
                null);

        return !HasProjectedConversationContent(content);
    }

    public static bool ShouldHydratePrimarySessionState(ChatState state, string conversationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var sessionState = state.ResolveSessionStateSlice(conversationId)
            ?? new ConversationSessionStateSlice(
                ImmutableList<ConversationModeOptionSnapshot>.Empty,
                null,
                ImmutableList<ConversationConfigOptionSnapshot>.Empty,
                false,
                ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
                null,
                null);

        return !HasProjectedPrimarySessionState(sessionState);
    }

    public static bool HasProjectedConversationContent(ConversationContentSlice content)
        => content.Transcript.Count > 0
            || content.PlanEntries.Count > 0
            || content.ShowPlanPanel
            || !string.IsNullOrWhiteSpace(content.PlanTitle);

    public static bool HasProjectedPrimarySessionState(ConversationSessionStateSlice sessionState)
        => sessionState.AvailableModes.Count > 0
            || sessionState.ConfigOptions.Count > 0
            || sessionState.ShowConfigOptionsPanel
            || !string.IsNullOrWhiteSpace(sessionState.SelectedModeId);

    public static bool HasProjectedAuxiliarySessionState(ConversationSessionStateSlice sessionState)
        => sessionState.AvailableCommands.Count > 0
            || sessionState.SessionInfo is not null
            || sessionState.Usage is not null;

    public static bool ShouldHydrateAuxiliarySessionState(
        ConversationSessionStateSlice? sessionState,
        ConversationWorkspaceSnapshot? snapshot)
        => ShouldHydrateAvailableCommands(sessionState, snapshot)
            || ShouldHydrateSessionInfo(sessionState, snapshot)
            || ShouldHydrateUsage(sessionState, snapshot);

    public static bool ShouldHydrateAvailableCommands(
        ConversationSessionStateSlice? sessionState,
        ConversationWorkspaceSnapshot? snapshot)
        => (sessionState?.AvailableCommands.Count ?? 0) == 0
            && (snapshot?.AvailableCommands?.Count ?? 0) > 0;

    public static bool ShouldHydrateSessionInfo(
        ConversationSessionStateSlice? sessionState,
        ConversationWorkspaceSnapshot? snapshot)
        => sessionState?.SessionInfo is null
            && snapshot?.SessionInfo is not null;

    public static bool ShouldHydrateUsage(
        ConversationSessionStateSlice? sessionState,
        ConversationWorkspaceSnapshot? snapshot)
        => sessionState?.Usage is null
            && snapshot?.Usage is not null;

    public static bool HasReusableWarmProjection(
        ChatState state,
        string conversationId,
        ConversationWorkspaceSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return false;
        }

        if (snapshot is not null)
        {
            return true;
        }

        var contentSlice = state.ResolveContentSlice(conversationId);
        if (contentSlice is { } projectedContent
            && HasProjectedConversationContent(projectedContent))
        {
            return true;
        }

        var sessionStateSlice = state.ResolveSessionStateSlice(conversationId);
        if (sessionStateSlice is { } projectedSessionState
            && (HasProjectedPrimarySessionState(projectedSessionState)
                || HasProjectedAuxiliarySessionState(projectedSessionState)))
        {
            return true;
        }

        if (!string.Equals(state.HydratedConversationId, conversationId, StringComparison.Ordinal))
        {
            return false;
        }

        var rootContent = new ConversationContentSlice(
            state.Transcript ?? ImmutableList<ConversationMessageSnapshot>.Empty,
            state.PlanEntries ?? ImmutableList<ConversationPlanEntrySnapshot>.Empty,
            state.ShowPlanPanel,
            state.PlanTitle);
        if (HasProjectedConversationContent(rootContent))
        {
            return true;
        }

        var rootSessionState = new ConversationSessionStateSlice(
            state.AvailableModes ?? ImmutableList<ConversationModeOptionSnapshot>.Empty,
            state.SelectedModeId,
            state.ConfigOptions ?? ImmutableList<ConversationConfigOptionSnapshot>.Empty,
            state.ShowConfigOptionsPanel,
            state.AvailableCommands ?? ImmutableList<ConversationAvailableCommandSnapshot>.Empty,
            state.SessionInfo,
            state.Usage);
        return HasProjectedPrimarySessionState(rootSessionState)
            || HasProjectedAuxiliarySessionState(rootSessionState);
    }
}

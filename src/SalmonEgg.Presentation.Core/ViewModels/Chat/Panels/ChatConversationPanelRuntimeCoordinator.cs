using System;
using System.Threading.Tasks;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.ViewModels.Chat.Panels;

public sealed class ChatConversationPanelRuntimeCoordinator
{
    public ChatConversationPanelSelection SyncConversation(
        ChatConversationPanelStateCoordinator panelStateCoordinator,
        string? conversationId)
    {
        ArgumentNullException.ThrowIfNull(panelStateCoordinator);
        return panelStateCoordinator.SyncConversation(conversationId);
    }

    public async Task<LocalTerminalPanelSessionViewModel?> ActivateLocalTerminalSessionAsync(
        LocalTerminalPanelCoordinator? localTerminalPanelCoordinator,
        IChatStore chatStore,
        ISessionManager sessionManager,
        string? conversationId)
    {
        ArgumentNullException.ThrowIfNull(chatStore);
        ArgumentNullException.ThrowIfNull(sessionManager);

        if (localTerminalPanelCoordinator is null || string.IsNullOrWhiteSpace(conversationId))
        {
            return null;
        }

        var state = await chatStore.State ?? ChatState.Empty;
        var binding = state.ResolveBinding(conversationId);
        var isLocalSession = binding is null || string.IsNullOrWhiteSpace(binding.RemoteSessionId);
        var sessionInfoCwd = ResolveLocalTerminalSessionInfoCwd(sessionManager, conversationId, state);
        return await localTerminalPanelCoordinator
            .ActivateAsync(conversationId, isLocalSession, sessionInfoCwd)
            .ConfigureAwait(true);
    }

    public string? ResolveLocalTerminalSessionInfoCwd(
        ISessionManager sessionManager,
        string conversationId,
        ChatState state)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);

        var sessionCwd = sessionManager.GetSession(conversationId)?.Cwd?.Trim();
        if (!string.IsNullOrWhiteSpace(sessionCwd))
        {
            return sessionCwd;
        }

        return state.ResolveSessionStateSlice(conversationId)?.SessionInfo?.Cwd?.Trim();
    }

    public ChatConversationPanelSelection RemoveConversation(
        ChatConversationPanelStateCoordinator panelStateCoordinator,
        string conversationId,
        bool isCurrentConversation)
    {
        ArgumentNullException.ThrowIfNull(panelStateCoordinator);
        return panelStateCoordinator.RemoveConversation(conversationId, isCurrentConversation);
    }
}

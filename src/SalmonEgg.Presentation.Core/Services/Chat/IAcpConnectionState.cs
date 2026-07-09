using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Threading;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Presentation.Core.Services.Chat;

/// <summary>
/// Read-only ACP connection state consumed by the coordinator.
/// A future ChatViewModel adapter can implement this without exposing UI types.
/// </summary>
public interface IAcpConnectionState : INotifyPropertyChanged
{
    bool IsConnected { get; }

    bool IsConnecting { get; }

    bool IsInitializing { get; }

    bool IsSessionActive { get; }

    bool IsAuthenticationRequired { get; }

    string? ConnectionErrorMessage { get; }

    string? AuthenticationHintMessage { get; }

    string? AgentName { get; }

    string? AgentVersion { get; }

    string? CurrentSessionId { get; }

    string? ConnectionInstanceId { get; }

    bool IsHydrating { get; }
}

/// <summary>
/// Mutable sink implemented by the future ChatViewModel facade/adapter.
/// The coordinator pushes narrow state changes through this surface without owning UI collections.
/// </summary>
public interface IAcpChatCoordinatorSink : IAcpConnectionState
{
    IChatService? CurrentChatService { get; }

    bool IsInitialized { get; }

    string? CurrentRemoteSessionId { get; }

    string? SelectedProfileId { get; }

    ServerConfiguration? ResolveProfile(string? profileId);

    IReadOnlyList<McpServer> CurrentMcpServers { get; }

    void SetCurrentMcpServers(IReadOnlyList<McpServer> mcpServers);

    long ConnectionGeneration { get; }

    IUiDispatcher Dispatcher { get; }

    IConversationBindingCommands ConversationBindingCommands { get; }

    IReadOnlyList<AgentRemoteDirectory> GetAgentRemoteDirectories();

    ValueTask<ConversationRemoteBindingState?> GetCurrentRemoteBindingAsync(CancellationToken cancellationToken = default);

    ValueTask<ConversationRemoteBindingState?> GetConversationRemoteBindingAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    void SelectProfile(ServerConfiguration profile);

    Task SelectProfileAsync(ServerConfiguration profile, CancellationToken cancellationToken = default);

    void ReplaceChatService(IChatService? chatService);

    Task ReplaceChatServiceAsync(IChatService? chatService, CancellationToken cancellationToken = default);

    Task ReplaceChatServiceAsync(IChatService? chatService, ServiceReplaceIntent intent, CancellationToken cancellationToken = default);

    void UpdateConnectionState(bool isConnecting, bool isConnected, bool isInitialized, string? errorMessage);

    void UpdateInitializationState(bool isInitializing);

    void UpdateAuthenticationState(bool isRequired, string? hintMessage);

    void UpdateAgentIdentity(string? agentName, string? agentVersion);

    Task NotifyPromptRequestDispatchedAsync(CancellationToken cancellationToken = default);

    Task ResetHydratedConversationForResyncAsync(CancellationToken cancellationToken = default);

    Task ResetConversationForResyncAsync(string conversationId, CancellationToken cancellationToken = default);

    string? GetActiveSessionCwdOrDefault();

    string? GetSessionCwdOrDefault(string conversationId);

    Task SetIsHydratingAsync(bool isHydrating, CancellationToken cancellationToken = default);

    Task SetConversationHydratingAsync(
        string conversationId,
        bool isHydrating,
        CancellationToken cancellationToken = default);

    Task MarkActiveConversationRemoteHydratedAsync(CancellationToken cancellationToken = default);

    Task MarkConversationRemoteHydratedAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    Task ApplyConversationSessionLoadResponseAsync(
        string conversationId,
        SessionLoadResponse response,
        CancellationToken cancellationToken = default);
}

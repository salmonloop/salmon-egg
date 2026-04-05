using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Threading;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
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

    bool IsHydrating { get; }
}

/// <summary>
/// Mutable sink implemented by the future ChatViewModel facade/adapter.
/// The coordinator pushes narrow state changes through this surface without owning UI collections.
/// </summary>
public interface IAcpChatCoordinatorSink : IAcpConnectionState
{
    /// <summary>
    /// Optional richer bridge hooks for the ACP facade adapter.
    /// Default implementations keep the current ChatViewModel contract-compatible until that adapter lands.
    /// </summary>
    IChatService? CurrentChatService => null;

    bool IsInitialized => CurrentChatService?.IsInitialized ?? false;

    string? CurrentRemoteSessionId => null;

    string? SelectedProfileId => null;

    long ConnectionGeneration => 0;

    SynchronizationContext SessionUpdateSynchronizationContext
        => SynchronizationContext.Current ?? new SynchronizationContext();

    IConversationBindingCommands ConversationBindingCommands { get; }

    ValueTask<ConversationRemoteBindingState?> GetCurrentRemoteBindingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            string.IsNullOrWhiteSpace(CurrentSessionId)
                ? null
                : new ConversationRemoteBindingState(
                    CurrentSessionId!,
                    CurrentRemoteSessionId,
                    SelectedProfileId));
    }

    ValueTask<ConversationRemoteBindingState?> GetConversationRemoteBindingAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(conversationId)
            || !string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal))
        {
            return ValueTask.FromResult<ConversationRemoteBindingState?>(null);
        }

        return GetCurrentRemoteBindingAsync(cancellationToken);
    }

    void SelectProfile(ServerConfiguration profile)
    {
    }

    Task SelectProfileAsync(ServerConfiguration profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SelectProfile(profile);
        return Task.CompletedTask;
    }

    void ReplaceChatService(IChatService? chatService)
    {
    }

    Task ReplaceChatServiceAsync(IChatService? chatService, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReplaceChatService(chatService);
        return Task.CompletedTask;
    }

    void UpdateConnectionState(bool isConnecting, bool isConnected, bool isInitialized, string? errorMessage)
    {
    }

    void UpdateInitializationState(bool isInitializing)
    {
    }

    void UpdateAuthenticationState(bool isRequired, string? hintMessage)
    {
    }

    void UpdateAgentIdentity(string? agentName, string? agentVersion)
    {
    }

    Task ResetHydratedConversationForResyncAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    Task ResetConversationForResyncAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return string.IsNullOrWhiteSpace(conversationId)
            || !string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal)
            ? Task.CompletedTask
            : ResetHydratedConversationForResyncAsync(cancellationToken);
    }

    string GetActiveSessionCwdOrDefault() => Environment.CurrentDirectory;

    string GetSessionCwdOrDefault(string conversationId)
        => string.IsNullOrWhiteSpace(conversationId)
            || !string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal)
            ? Environment.CurrentDirectory
            : GetActiveSessionCwdOrDefault();

    Task SetIsHydratingAsync(bool isHydrating, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    Task SetConversationHydratingAsync(
        string conversationId,
        bool isHydrating,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return string.IsNullOrWhiteSpace(conversationId)
            || !string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal)
            ? Task.CompletedTask
            : SetIsHydratingAsync(isHydrating, cancellationToken);
    }

    Task MarkActiveConversationRemoteHydratedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    Task MarkConversationRemoteHydratedAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return string.IsNullOrWhiteSpace(conversationId)
            || !string.Equals(CurrentSessionId, conversationId, StringComparison.Ordinal)
            ? Task.CompletedTask
            : MarkActiveConversationRemoteHydratedAsync(cancellationToken);
    }

    Task ApplyConversationSessionLoadResponseAsync(
        string conversationId,
        SessionLoadResponse response,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

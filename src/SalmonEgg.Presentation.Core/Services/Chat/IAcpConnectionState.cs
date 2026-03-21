using System;
using System.ComponentModel;
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

    void SelectProfile(ServerConfiguration profile)
    {
    }

    void ReplaceChatService(IChatService? chatService)
    {
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

    string GetActiveSessionCwdOrDefault() => Environment.CurrentDirectory;
}

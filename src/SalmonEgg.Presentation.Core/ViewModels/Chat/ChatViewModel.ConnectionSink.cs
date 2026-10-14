using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;

namespace SalmonEgg.Presentation.ViewModels.Chat;

public partial class ChatViewModel
{
    private sealed class ScopedAcpChatCoordinatorSink : IAcpChatCoordinatorSink
    {
        private readonly ChatViewModel _owner;
        private readonly AcpConnectionContext _connectionContext;
        private readonly IConversationBindingCommands _bindingCommands;
        private readonly PromptOperation? _promptOperation;

        public ScopedAcpChatCoordinatorSink(ChatViewModel owner, AcpConnectionContext connectionContext, PromptOperation? promptOperation = null)
        {
            _owner = owner;
            _connectionContext = connectionContext;
            _promptOperation = promptOperation;
            _bindingCommands = promptOperation is null
                ? new ScopedBindingCommands(owner, connectionContext)
                : new PromptBindingCommands(owner, promptOperation);
        }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add => _owner.PropertyChanged += value;
            remove => _owner.PropertyChanged -= value;
        }

        public IChatService? CurrentChatService => _promptOperation?.Source.Service ?? _owner.CurrentChatService;
        public bool IsConnected => _promptOperation is null ? _owner.IsConnected : _promptOperation.Source.Service.IsConnected;
        public bool IsConnecting => _promptOperation is null && _owner.IsConnecting;
        public bool IsInitializing => _promptOperation is null && _owner.IsInitializing;
        public bool IsSessionActive => _promptOperation is not null || _owner.IsSessionActive;
        public bool IsAuthenticationRequired => (_promptOperation is null || CanMutate()) && _owner.IsAuthenticationRequired;
        public string? ConnectionErrorMessage => _owner.ConnectionErrorMessage;
        public string? AuthenticationHintMessage => _owner.AuthenticationHintMessage;
        public string? AgentName => _owner.AgentName;
        public string? AgentVersion => _owner.AgentVersion;
        public string? CurrentSessionId => _promptOperation?.Context.ConversationId ?? _owner.CurrentSessionId;
        public bool IsHydrating => (_promptOperation is null || CanMutate()) && _owner.IsHydrating;
        public bool IsInitialized => _promptOperation is null ? _owner.IsInitialized : _promptOperation.Source.Service.IsInitialized;
        public string? CurrentRemoteSessionId => _promptOperation is null ? _owner.CurrentRemoteSessionId : _promptOperation.RemoteSessionId;
        public string? SelectedProfileId => _promptOperation is null ? _owner.SelectedProfileId : _promptOperation.Source.ProfileId;
        public ServerConfiguration? ResolveProfile(string? profileId) => _owner.ResolveNewSessionDraftProfile(profileId);
        public IReadOnlyList<McpServer> CurrentMcpServers => _owner.CurrentMcpServers;
        public string? ConnectionInstanceId => _promptOperation is null ? _owner.ConnectionInstanceId : _promptOperation.Source.ConnectionInstanceId;
        public long ConnectionGeneration => _promptOperation?.Context.ConnectionGeneration ?? _owner.ConnectionGeneration;
        public IUiDispatcher Dispatcher => _owner.Dispatcher;
        public IConversationBindingCommands ConversationBindingCommands => _bindingCommands;
        public IReadOnlyList<AgentRemoteDirectory> GetAgentRemoteDirectories()
            => _owner._preferences.AgentRemoteDirectories;

        public void SetCurrentMcpServers(IReadOnlyList<McpServer> mcpServers)
        {
            if (CanMutate())
            {
                _owner.SetCurrentMcpServers(mcpServers);
            }
        }

        public ValueTask<ConversationRemoteBindingState?> GetCurrentRemoteBindingAsync(CancellationToken cancellationToken = default)
            => _promptOperation is null
                ? _owner.GetCurrentRemoteBindingAsync(cancellationToken)
                : _owner.GetConversationRemoteBindingAsync(_promptOperation.Context.ConversationId, cancellationToken);

        public ValueTask<ConversationRemoteBindingState?> GetConversationRemoteBindingAsync(
            string conversationId,
            CancellationToken cancellationToken = default)
            => _owner.GetConversationRemoteBindingAsync(conversationId, cancellationToken);

        public void SelectProfile(ServerConfiguration profile)
        {
            if (CanMutate())
            {
                _owner.SelectProfile(profile);
            }
        }

        public Task SelectProfileAsync(ServerConfiguration profile, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.SelectProfileAsync(profile, cancellationToken)
                : Task.CompletedTask;
        }

        public void ReplaceChatService(IChatService? chatService)
        {
            if (CanMutate())
            {
                _owner.ReplaceChatService(chatService);
            }
        }

        public Task ReplaceChatServiceAsync(IChatService? chatService, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.ReplaceChatServiceAsync(chatService, cancellationToken)
                : Task.CompletedTask;
        }

        public Task ReplaceChatServiceAsync(IChatService? chatService, ServiceReplaceIntent intent, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.ReplaceChatServiceWithIntentAsync(chatService, intent, cancellationToken)
                : Task.CompletedTask;
        }

        public void UpdateConnectionState(bool isConnecting, bool isConnected, bool isInitialized, string? errorMessage)
        {
            if (CanMutate())
            {
                _owner.UpdateConnectionState(isConnecting, isConnected, isInitialized, errorMessage);
            }
        }

        public void UpdateInitializationState(bool isInitializing)
        {
            if (CanMutate())
            {
                _owner.UpdateInitializationState(isInitializing);
            }
        }

        public void UpdateAuthenticationState(bool isRequired, string? hintMessage)
        {
            if (CanMutate())
            {
                _owner.UpdateAuthenticationState(isRequired, hintMessage);
            }
        }

        public void UpdateAgentIdentity(string? agentName, string? agentVersion)
        {
            if (CanMutate())
            {
                _owner.UpdateAgentIdentity(agentName, agentVersion);
            }
        }

        public Task NotifyPromptRequestDispatchedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_promptOperation is not null)
            {
                return _owner.OnPromptOperationDispatchedAsync(_promptOperation, cancellationToken);
            }

            return CanMutate()
                ? ((IAcpChatCoordinatorSink)_owner).NotifyPromptRequestDispatchedAsync(cancellationToken)
                : Task.CompletedTask;
        }

        public Task ResetHydratedConversationForResyncAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.ResetHydratedConversationForResyncAsync(cancellationToken)
                : Task.CompletedTask;
        }

        public Task ResetConversationForResyncAsync(string conversationId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.ResetConversationForResyncAsync(conversationId, cancellationToken)
                : Task.CompletedTask;
        }

        public string? GetActiveSessionCwdOrDefault()
            => _promptOperation is null ? _owner.GetActiveSessionCwdOrDefault() : _promptOperation.Context.Cwd;

        public string? GetSessionCwdOrDefault(string conversationId) => _owner.GetSessionCwdOrDefault(conversationId);

        public ValueTask<AcpRemoteSessionRecoveryFallback> GetSessionRecoveryFallbackAsync(
            string conversationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.GetSessionRecoveryFallbackAsync(conversationId, cancellationToken)
                : ValueTask.FromResult(default(AcpRemoteSessionRecoveryFallback));
        }

        public Task ApplyConversationRemoteSessionInfoAsync(
            string conversationId,
            AgentSessionInfo sessionInfo,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.ApplyConversationRemoteSessionInfoAsync(conversationId, sessionInfo, cancellationToken)
                : Task.CompletedTask;
        }

        public Task SetIsHydratingAsync(bool isHydrating, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.SetIsHydratingAsync(isHydrating, cancellationToken)
                : Task.CompletedTask;
        }

        public Task SetConversationHydratingAsync(
            string conversationId,
            bool isHydrating,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.SetConversationHydratingAsync(conversationId, isHydrating, cancellationToken)
                : Task.CompletedTask;
        }

        public Task MarkActiveConversationRemoteHydratedAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.MarkActiveConversationRemoteHydratedAsync(cancellationToken)
                : Task.CompletedTask;
        }

        public Task MarkConversationRemoteHydratedAsync(
            string conversationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.MarkConversationRemoteHydratedAsync(conversationId, cancellationToken)
                : Task.CompletedTask;
        }

        public Task ApplyConversationSessionLoadResponseAsync(
            string conversationId,
            SessionLoadResponse response,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CanMutate()
                ? _owner.ApplyConversationSessionLoadResponseAsync(conversationId, response, cancellationToken)
                : Task.CompletedTask;
        }

        private bool CanMutate() => _promptOperation is null
            ? _owner.IsCurrentConnectionContext(_connectionContext)
            : _owner.IsPromptForegroundCurrent(_promptOperation) && _owner.IsPromptSourceCurrent(_promptOperation.Source);
    }

    private sealed class ScopedBindingCommands : IConversationBindingCommands
    {
        private readonly ChatViewModel _owner;
        private readonly AcpConnectionContext _connectionContext;

        public ScopedBindingCommands(ChatViewModel owner, AcpConnectionContext connectionContext)
        {
            _owner = owner;
            _connectionContext = connectionContext;
        }

        public ValueTask<BindingUpdateResult> UpdateBindingAsync(string conversationId, string? remoteSessionId, string? boundProfileId)
            => _owner.IsCurrentConnectionContext(_connectionContext)
                ? _owner.ConversationBindingCommands.UpdateBindingAsync(conversationId, remoteSessionId, boundProfileId)
                : ValueTask.FromResult(BindingUpdateResult.Success());
    }

}

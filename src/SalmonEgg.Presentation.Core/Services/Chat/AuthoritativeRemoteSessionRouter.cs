using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IAuthoritativeRemoteSessionRouter
{
    ValueTask<string?> ResolveConversationIdAsync(string remoteSessionId, CancellationToken cancellationToken = default);

    ValueTask<string?> ResolveConversationIdAsync(string remoteSessionId, AcpSessionEventSource source, CancellationToken cancellationToken = default);

    string? ResolveConversationId(ChatState state, string remoteSessionId);

    string? ResolveConversationId(ChatState state, string remoteSessionId, AcpSessionEventSource source);
}

public sealed class AuthoritativeRemoteSessionRouter : IAuthoritativeRemoteSessionRouter
{
    private readonly IChatStore _chatStore;
    private readonly IAcpConnectionSessionRegistry? _sessionRegistry;

    public AuthoritativeRemoteSessionRouter(IChatStore chatStore, IAcpConnectionSessionRegistry? sessionRegistry = null)
    {
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
        _sessionRegistry = sessionRegistry;
    }

    public async ValueTask<string?> ResolveConversationIdAsync(string remoteSessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            return null;
        }

        var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
        return ResolveConversationId(state, remoteSessionId);
    }

    public async ValueTask<string?> ResolveConversationIdAsync(
        string remoteSessionId, AcpSessionEventSource source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await _chatStore.GetCurrentStateAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return ResolveConversationId(state, remoteSessionId, source);
    }

    public string? ResolveConversationId(ChatState state, string remoteSessionId)
    {
        if (string.IsNullOrWhiteSpace(remoteSessionId) || state.Bindings is null)
        {
            return null;
        }

        string? conversationId = null;
        foreach (var binding in state.Bindings)
        {
            if (string.Equals(binding.Value.RemoteSessionId, remoteSessionId, StringComparison.Ordinal))
            {
                if (conversationId is not null)
                {
                    return null;
                }

                conversationId = binding.Key;
            }
        }

        return conversationId;
    }

    public string? ResolveConversationId(ChatState state, string remoteSessionId, AcpSessionEventSource source)
    {
        if (string.IsNullOrWhiteSpace(remoteSessionId) || state.Bindings is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(source.ProfileId))
        {
            // An unprofiled direct connection has one active service owner. It may use the
            // legacy binding seam, but an ambiguous remote id still cannot identify a session.
            return ResolveConversationId(state, remoteSessionId);
        }

        if (_sessionRegistry is not null
            && (!_sessionRegistry.TryGetByProfile(source.ProfileId, out var session)
                || !source.Matches(session)))
        {
            return null;
        }

        var conversationId = ResolveProfileConversationId(state, remoteSessionId, source.ProfileId);
        var runtime = state.ResolveRuntimeState(conversationId);
        return runtime is { ConnectionInstanceId: not null }
            && !string.Equals(runtime.Value.ConnectionInstanceId, source.ConnectionInstanceId, StringComparison.Ordinal)
                ? null : conversationId;
    }

    private static string? ResolveProfileConversationId(ChatState state, string remoteSessionId, string profileId)
    {
        if (string.IsNullOrWhiteSpace(remoteSessionId) || string.IsNullOrWhiteSpace(profileId) || state.Bindings is null)
        {
            return null;
        }

        string? match = null;
        foreach (var binding in state.Bindings)
        {
            if (string.Equals(binding.Value.RemoteSessionId, remoteSessionId, StringComparison.Ordinal)
                && string.Equals(binding.Value.ProfileId, profileId, StringComparison.Ordinal))
            {
                if (match is not null) return null;
                match = binding.Key;
            }
        }

        return match;
    }
}

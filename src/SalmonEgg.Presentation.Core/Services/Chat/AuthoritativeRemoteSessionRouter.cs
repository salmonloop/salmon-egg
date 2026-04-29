using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IAuthoritativeRemoteSessionRouter
{
    ValueTask<string?> ResolveConversationIdAsync(string remoteSessionId, CancellationToken cancellationToken = default);

    string? ResolveConversationId(ChatState state, string remoteSessionId);
}

public sealed class AuthoritativeRemoteSessionRouter : IAuthoritativeRemoteSessionRouter
{
    private readonly IChatStore _chatStore;

    public AuthoritativeRemoteSessionRouter(IChatStore chatStore)
    {
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
    }

    public async ValueTask<string?> ResolveConversationIdAsync(string remoteSessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(remoteSessionId))
        {
            return null;
        }

        var state = await _chatStore.State ?? ChatState.Empty;
        return ResolveConversationId(state, remoteSessionId);
    }

    public string? ResolveConversationId(ChatState state, string remoteSessionId)
    {
        if (string.IsNullOrWhiteSpace(remoteSessionId) || state.Bindings is null)
        {
            return null;
        }

        foreach (var binding in state.Bindings)
        {
            if (string.Equals(binding.Value.RemoteSessionId, remoteSessionId, StringComparison.Ordinal))
            {
                return binding.Key;
            }
        }

        return null;
    }
}

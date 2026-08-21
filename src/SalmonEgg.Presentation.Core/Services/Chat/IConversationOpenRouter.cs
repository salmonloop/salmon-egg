using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public enum ConversationOpenResult
{
    /// <summary>The conversation was activated and is now the shell's current content.</summary>
    Opened,

    /// <summary>No conversation with the requested id exists in the authoritative catalog.</summary>
    NotFound,

    /// <summary>The request carried no usable conversation id.</summary>
    Invalid,

    /// <summary>Activation was rejected, superseded, cancelled or faulted.</summary>
    Failed
}

/// <summary>
/// Single entry point for "open this conversation" requests that originate outside the shell,
/// such as a system notification the user tapped.
/// </summary>
/// <remarks>
/// External callers know a conversation id and nothing else. This router owns the rest: catalog
/// lookup, project affinity resolution, UI-thread marshalling and routing through the one
/// authoritative navigation owner, so a platform activation handler never drives navigation itself.
/// </remarks>
public interface IConversationOpenRouter
{
    Task<ConversationOpenResult> OpenConversationAsync(string conversationId);
}

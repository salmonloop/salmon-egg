using System;
using System.Linq;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Mvux.Chat;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class BindingCoordinator : IConversationBindingCommands
{
    private readonly ChatConversationWorkspace _workspace;
    private readonly IChatStore _chatStore;

    public BindingCoordinator(ChatConversationWorkspace workspace, IChatStore chatStore)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
    }

    public async ValueTask<BindingUpdateResult> UpdateBindingAsync(string conversationId, string? remoteSessionId, string? boundProfileId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return BindingUpdateResult.Error("ConversationIdMissing");
        }

        try
        {
            var conversationExists = _workspace
                .GetKnownConversationIds()
                .Contains(conversationId, StringComparer.Ordinal);
            if (!conversationExists)
            {
                return BindingUpdateResult.NotFound();
            }

            var binding = new ConversationBindingSlice(conversationId, remoteSessionId, boundProfileId);
            await _chatStore.Dispatch(new SetBindingSliceAction(binding)).ConfigureAwait(false);
            _workspace.UpdateRemoteBinding(conversationId, remoteSessionId, boundProfileId);
            _workspace.ScheduleSave();
            return BindingUpdateResult.Success();
        }
        catch (Exception ex)
        {
            return BindingUpdateResult.Error(ex.Message);
        }
    }
}

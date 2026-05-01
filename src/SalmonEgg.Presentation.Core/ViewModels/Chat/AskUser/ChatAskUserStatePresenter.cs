using System;
using System.Collections.ObjectModel;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.AskUser;

public sealed class ChatAskUserStatePresenter
{
    public ChatAskUserState Present(
        AskUserRequestViewModel? pendingRequest,
        ObservableCollection<AskUserQuestionViewModel> emptyQuestions)
    {
        ArgumentNullException.ThrowIfNull(emptyQuestions);

        return new ChatAskUserState(
            HasPendingRequest: pendingRequest is not null,
            Prompt: pendingRequest?.Prompt ?? string.Empty,
            Questions: pendingRequest?.Questions ?? emptyQuestions,
            HasError: pendingRequest?.HasError ?? false,
            ErrorMessage: pendingRequest?.ErrorMessage ?? string.Empty,
            SubmitCommand: pendingRequest?.SubmitCommand);
    }
}

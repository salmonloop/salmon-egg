using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.ViewModels.Chat.AskUser;

public sealed record ChatAskUserState(
    bool HasPendingRequest,
    string Prompt,
    ObservableCollection<AskUserQuestionViewModel> Questions,
    bool HasError,
    string ErrorMessage,
    IAsyncRelayCommand? SubmitCommand);

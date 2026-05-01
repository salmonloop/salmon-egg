using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using SalmonEgg.Presentation.Core.ViewModels.Chat.AskUser;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.AskUser;

[Collection("NonParallel")]
public sealed class ChatAskUserStatePresenterTests
{
    private readonly ChatAskUserStatePresenter _sut = new();

    [Fact]
    public void Present_WithoutRequest_ReturnsEmptyState()
    {
        var emptyQuestions = new ObservableCollection<AskUserQuestionViewModel>();

        var state = _sut.Present(null, emptyQuestions);

        Assert.False(state.HasPendingRequest);
        Assert.Equal(string.Empty, state.Prompt);
        Assert.Same(emptyQuestions, state.Questions);
        Assert.False(state.HasError);
        Assert.Equal(string.Empty, state.ErrorMessage);
        Assert.Null(state.SubmitCommand);
    }

    [Fact]
    public void Present_WithRequest_ProjectsPromptQuestionsAndSubmit()
    {
        var question = new AskUserQuestionViewModel(
            "h",
            "p",
            isMultiSelect: false,
            options:
            [
                new AskUserOptionViewModel("one", "desc")
            ]);
        var request = new AskUserRequestViewModel(
            messageId: new object(),
            sessionId: "session-1",
            prompt: "Need input",
            questions: [question]);
        request.OnSubmit = _ => Task.FromResult(true);
        request.Questions[0].Options[0].ToggleSelectedCommand.Execute(null);
        _ = request.SubmitCommand.ExecuteAsync(null);
        request.ErrorMessage = "bad";

        var state = _sut.Present(request, new ObservableCollection<AskUserQuestionViewModel>());

        Assert.True(state.HasPendingRequest);
        Assert.Equal("Need input", state.Prompt);
        Assert.Same(request.Questions, state.Questions);
        Assert.True(state.HasError);
        Assert.Equal("bad", state.ErrorMessage);
        Assert.Same(request.SubmitCommand, state.SubmitCommand);
    }
}

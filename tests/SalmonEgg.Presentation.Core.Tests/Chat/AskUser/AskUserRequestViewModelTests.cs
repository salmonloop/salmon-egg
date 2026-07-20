using System.Threading.Tasks;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.AskUser;

public sealed class AskUserRequestViewModelTests
{
    [Fact]
    public async Task SubmitAsync_WhenHandlerMissing_SurfacesStableEnglishError()
    {
        var question = CreateAnsweredQuestion();
        var request = new AskUserRequestViewModel("id", "session", "prompt", [question]);

        await request.SubmitCommand.ExecuteAsync(null);

        Assert.True(request.HasError);
        Assert.Equal("Answers cannot be submitted right now.", request.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_WhenQuestionsUnanswered_SurfacesStableEnglishError()
    {
        var question = new AskUserQuestionViewModel(
            "Header",
            "Question?",
            isMultiSelect: false,
            [new AskUserOptionViewModel("Option A", "Desc")]);
        var request = new AskUserRequestViewModel("id", "session", "prompt", [question])
        {
            OnSubmit = _ => Task.FromResult(true)
        };

        await request.SubmitCommand.ExecuteAsync(null);

        Assert.True(request.HasError);
        Assert.Equal("Answer all questions before submitting.", request.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_WhenHandlerFails_SurfacesStableEnglishError()
    {
        var question = CreateAnsweredQuestion();
        var request = new AskUserRequestViewModel("id", "session", "prompt", [question])
        {
            OnSubmit = _ => Task.FromResult(false)
        };

        await request.SubmitCommand.ExecuteAsync(null);

        Assert.True(request.HasError);
        Assert.Equal("Failed to submit answers. Please try again.", request.ErrorMessage);
    }

    [Fact]
    public void Question_SelectionHint_UsesStableEnglishCopy()
    {
        var single = new AskUserQuestionViewModel("H", "Q", isMultiSelect: false, []);
        var multi = new AskUserQuestionViewModel("H", "Q", isMultiSelect: true, []);

        Assert.Equal("Single choice", single.SelectionHint);
        Assert.Equal("Multiple choice", multi.SelectionHint);
    }

    private static AskUserQuestionViewModel CreateAnsweredQuestion()
    {
        var option = new AskUserOptionViewModel("Option A", "Desc");
        var question = new AskUserQuestionViewModel("Header", "Question?", isMultiSelect: false, [option]);
        option.ToggleSelectedCommand.Execute(null);
        return question;
    }
}

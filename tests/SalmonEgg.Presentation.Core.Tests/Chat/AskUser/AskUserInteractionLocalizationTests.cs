using System.Collections.Generic;
using System.Threading.Tasks;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.AskUser;

public sealed class AskUserInteractionLocalizationTests
{
    [Fact]
    public void Question_SelectionHint_UsesLocalizedChoiceLabels()
    {
        var localizer = new TestCoreStringLocalizer();

        var multi = new AskUserQuestionViewModel(
            "Header",
            "Question?",
            isMultiSelect: true,
            options: [new AskUserOptionViewModel("A", "desc")],
            localizer);
        var single = new AskUserQuestionViewModel(
            "Header",
            "Question?",
            isMultiSelect: false,
            options: [new AskUserOptionViewModel("A", "desc")],
            localizer);

        Assert.Equal("Multiple choice", multi.SelectionHint);
        Assert.Equal("Single choice", single.SelectionHint);
    }

    [Fact]
    public async Task SubmitAsync_WhenOnSubmitMissing_UsesLocalizedUnavailableMessage()
    {
        var localizer = new TestCoreStringLocalizer();
        var question = new AskUserQuestionViewModel(
            "Header",
            "Question?",
            isMultiSelect: false,
            options: [new AskUserOptionViewModel("A", "desc")],
            localizer);
        question.Options[0].IsSelected = true;
        var request = new AskUserRequestViewModel(
            "message-1",
            "session-1",
            "prompt",
            [question],
            localizer);

        await request.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Answers cannot be submitted right now.", request.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_WhenSubmitFails_UsesLocalizedFailureMessage()
    {
        var localizer = new TestCoreStringLocalizer();
        var question = new AskUserQuestionViewModel(
            "Header",
            "Question?",
            isMultiSelect: false,
            options: [new AskUserOptionViewModel("A", "desc")],
            localizer);
        question.Options[0].IsSelected = true;
        var request = new AskUserRequestViewModel(
            "message-1",
            "session-1",
            "prompt",
            [question],
            localizer)
        {
            OnSubmit = _ => Task.FromResult(false)
        };

        await request.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Failed to submit answers. Please try again.", request.ErrorMessage);
    }

    [Fact]
    public void Factory_Create_PropagatesLocalizerToSelectionHint()
    {
        var localizer = new TestCoreStringLocalizer();
        var request = new AskUserRequest
        {
            SessionId = "remote-1",
            Questions =
            {
                new AskUserQuestion
                {
                    Header = "Execution",
                    Question = "Choose",
                    MultiSelect = true,
                    Options = { new AskUserOption { Label = "Plan", Description = "Planning mode" } }
                }
            }
        };

        var viewModel = AskUserInteractionViewModelFactory.Create(
            request,
            "message-1",
            _ => Task.FromResult(true),
            localizer);

        Assert.Equal("Multiple choice", viewModel.Questions[0].SelectionHint);
    }
}

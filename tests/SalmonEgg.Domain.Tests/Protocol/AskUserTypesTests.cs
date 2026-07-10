using System;
using System.Collections.Generic;
using Xunit;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

public sealed class AskUserTypesTests
{
    [Fact]
    public void ValidateRequest_DuplicateQuestions_ThrowsInvalidOperationException()
    {
        var request = CreateRequest();
        request.Questions.Add(new AskUserQuestion
        {
            Header = "Execution",
            Question = "Choose a mode",
            MultiSelect = false,
            Options =
            {
                new AskUserOption { Label = "Agent", Description = "Interactive mode" },
                new AskUserOption { Label = "Plan", Description = "Planning mode" }
            }
        });

        var ex = Assert.Throws<InvalidOperationException>((Action)(() => AskUserContract.ValidateRequest(request)));

        Assert.NotNull(ex);
        Assert.Contains("Duplicate question", ex!.Message);
    }

    [Fact]
    public void ValidateAnswers_MultiSelectAnswer_IsAccepted()
    {
        var request = CreateRequest(multiSelect: true);
        var answers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Choose a mode"] = "Agent, Plan"
        };

        AskUserContract.ValidateAnswers(request, answers);
    }

    [Fact]
    public void ValidateAnswers_UnknownAnswer_ThrowsInvalidOperationException()
    {
        var request = CreateRequest();
        var answers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Choose a mode"] = "YOLO"
        };

        var ex = Assert.Throws<InvalidOperationException>((Action)(() => AskUserContract.ValidateAnswers(request, answers)));

        Assert.NotNull(ex);
        Assert.Contains("Invalid answer", ex!.Message);
    }

    private static AskUserRequest CreateRequest(bool multiSelect = false)
    {
        return new AskUserRequest
        {
            SessionId = "session-1",
            Questions =
            {
                new AskUserQuestion
                {
                    Header = "Execution",
                    Question = "Choose a mode",
                    MultiSelect = multiSelect,
                    Options =
                    {
                        new AskUserOption { Label = "Agent", Description = "Interactive mode" },
                        new AskUserOption { Label = "Plan", Description = "Planning mode" }
                    }
                }
            }
        };
    }
}

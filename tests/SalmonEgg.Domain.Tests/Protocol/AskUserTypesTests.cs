using System;
using System.Collections.Generic;
using NUnit.Framework;
using SalmonEgg.Domain.Models.Protocol;

namespace SalmonEgg.Domain.Tests.Protocol;

[TestFixture]
public sealed class AskUserTypesTests
{
    [Test]
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

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("Duplicate question"));
    }

    [Test]
    public void ValidateAnswers_MultiSelectAnswer_IsAccepted()
    {
        var request = CreateRequest(multiSelect: true);
        var answers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Choose a mode"] = "Agent, Plan"
        };

        Assert.DoesNotThrow((Action)(() => AskUserContract.ValidateAnswers(request, answers)));
    }

    [Test]
    public void ValidateAnswers_UnknownAnswer_ThrowsInvalidOperationException()
    {
        var request = CreateRequest();
        var answers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Choose a mode"] = "YOLO"
        };

        var ex = Assert.Throws<InvalidOperationException>((Action)(() => AskUserContract.ValidateAnswers(request, answers)));

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.Message, Does.Contain("Invalid answer"));
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

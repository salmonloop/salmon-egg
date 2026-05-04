using System.Collections.Generic;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.ViewModels.Chat.SessionOptions;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.SessionOptions;

[Collection("NonParallel")]
public sealed class ChatSessionOptionsPresenterTests
{
    private readonly ChatSessionOptionsPresenter _sut = new();

    [Fact]
    public void Present_WithConfigBackedModeAndNoLegacyModes_ProjectsModesFromConfig()
    {
        var projection = _sut.Present(
            availableModes: [],
            selectedModeId: null,
            configOptions:
            [
                new ConversationConfigOptionSnapshot
                {
                    Id = "mode",
                    Category = "mode",
                    SelectedValue = "plan",
                    Options =
                    [
                        new ConversationConfigOptionChoiceSnapshot { Value = "agent", Name = "Agent" },
                        new ConversationConfigOptionChoiceSnapshot { Value = "plan", Name = "Plan" }
                    ]
                }
            ],
            showConfigOptionsPanel: true);

        Assert.Equal("mode", projection.ModeConfigId);
        Assert.Equal("agent", projection.AvailableModes[0].ModeId);
        Assert.Equal("plan", projection.AvailableModes[1].ModeId);
        Assert.Equal("agent", projection.SelectedModeId);
        Assert.True(projection.ShowConfigOptionsPanel);
    }

    [Fact]
    public void Present_WithSelectedModeId_PrefersProjectedSelectedMode()
    {
        var projection = _sut.Present(
            availableModes:
            [
                new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" },
                new ConversationModeOptionSnapshot { ModeId = "plan", ModeName = "Plan" }
            ],
            selectedModeId: "plan",
            configOptions: [],
            showConfigOptionsPanel: false);

        var selected = _sut.ResolveSelectedMode(projection.AvailableModes, projection.SelectedModeId);

        Assert.Equal("plan", projection.SelectedModeId);
        Assert.NotNull(selected);
        Assert.Equal("plan", selected!.ModeId);
    }

    [Fact]
    public void Present_WithUnknownSelectedMode_FallsBackToFirstAvailableMode()
    {
        var projection = _sut.Present(
            availableModes:
            [
                new ConversationModeOptionSnapshot { ModeId = "agent", ModeName = "Agent" },
                new ConversationModeOptionSnapshot { ModeId = "plan", ModeName = "Plan" }
            ],
            selectedModeId: "unknown",
            configOptions: [],
            showConfigOptionsPanel: false);

        Assert.Equal("agent", projection.SelectedModeId);
    }
}

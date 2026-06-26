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

    [Fact]
    public void Present_WithModelConfigOption_ProjectsModelSelectorState()
    {
        var projection = _sut.Present(
            availableModes: [],
            selectedModeId: null,
            configOptions:
            [
                new ConversationConfigOptionSnapshot
                {
                    Id = "model",
                    Name = "Model",
                    Category = "model",
                    SelectedValue = "claude-sonnet",
                    Options =
                    [
                        new ConversationConfigOptionChoiceSnapshot { Value = "claude-haiku", Name = "Haiku" },
                        new ConversationConfigOptionChoiceSnapshot { Value = "claude-sonnet", Name = "Sonnet" }
                    ]
                }
            ],
            showConfigOptionsPanel: true);

        Assert.Equal("model", projection.ModelConfigId);
        Assert.Equal("claude-sonnet", projection.SelectedModelValue);
        Assert.Equal(["claude-haiku", "claude-sonnet"], projection.ModelOptions.Select(option => option.Value).ToArray());

        var selected = _sut.ResolveSelectedModelOption(projection.ModelOptions, projection.SelectedModelValue);
        Assert.NotNull(selected);
        Assert.Equal("claude-sonnet", selected!.Value);
    }

    [Fact]
    public void Present_WithoutModelCategory_DoesNotProjectModelSelectorState()
    {
        var projection = _sut.Present(
            availableModes: [],
            selectedModeId: null,
            configOptions:
            [
                new ConversationConfigOptionSnapshot
                {
                    Id = "temperature",
                    Name = "Temperature",
                    Category = "sampling",
                    SelectedValue = "0.7",
                    Options =
                    [
                        new ConversationConfigOptionChoiceSnapshot { Value = "0.7", Name = "0.7" }
                    ]
                }
            ],
            showConfigOptionsPanel: true);

        Assert.Null(projection.ModelConfigId);
        Assert.Null(projection.SelectedModelValue);
        Assert.Empty(projection.ModelOptions);
    }
}

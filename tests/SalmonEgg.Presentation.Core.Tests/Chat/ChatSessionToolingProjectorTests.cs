using System.Collections.Immutable;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatSessionToolingProjectorTests
{
    [Fact]
    public void Project_WhenHydratedConversationHasSessionSlice_UsesConversationScopedTooling()
    {
        var projector = new ChatSessionToolingProjector();
        var expectedCommand = new ConversationAvailableCommandSnapshot("plan", "Planning command", "target");
        var expectedMode = new ConversationModeOptionSnapshot
        {
            ModeId = "agent",
            ModeName = "Agent",
            Description = "Default"
        };
        var expectedOption = new ConversationConfigOptionSnapshot
        {
            Id = "mode",
            Name = "Mode",
            Description = "Primary mode",
            Category = "mode",
            ValueType = "string",
            SelectedValue = "agent",
            Options =
            [
                new ConversationConfigOptionChoiceSnapshot
                {
                    Value = "agent",
                    Name = "Agent",
                    Description = "Default"
                }
            ]
        };

        var state = new ChatState(
            HydratedConversationId: "conv-1",
            ConversationSessionStates: ImmutableDictionary<string, ConversationSessionStateSlice>.Empty.Add(
                "conv-1",
                new ConversationSessionStateSlice(
                    [expectedMode],
                    "agent",
                    [expectedOption],
                    true,
                    [expectedCommand],
                    SessionInfo: null,
                    Usage: null)));

        var projection = projector.Project(state, "conv-1");

        var command = Assert.Single(projection.AvailableCommands);
        Assert.Equal("plan", command.Name);
        Assert.Equal("agent", Assert.Single(projection.AvailableModes).ModeId);
        Assert.Equal("agent", projection.SelectedModeId);
        Assert.True(projection.ShowConfigOptionsPanel);
        Assert.Equal("mode", Assert.Single(projection.ConfigOptions).Id);
    }

    [Fact]
    public void Project_WhenHydratedConversationHasNoSessionSlice_FallsBackToRootTooling()
    {
        var projector = new ChatSessionToolingProjector();
        var rootCommand = new ConversationAvailableCommandSnapshot("plan", "Planning command", "target");

        var state = new ChatState(
            HydratedConversationId: "conv-2",
            AvailableModes:
            [
                new ConversationModeOptionSnapshot
                {
                    ModeId = "agent",
                    ModeName = "Agent",
                    Description = "Default"
                }
            ],
            SelectedModeId: "agent",
            ConfigOptions:
            [
                new ConversationConfigOptionSnapshot
                {
                    Id = "mode",
                    Name = "Mode",
                    Description = "Primary mode",
                    Category = "mode",
                    ValueType = "string",
                    SelectedValue = "agent"
                }
            ],
            ShowConfigOptionsPanel: true,
            AvailableCommands: [rootCommand]);

        var projection = projector.Project(state, "conv-2");

        Assert.Equal("agent", Assert.Single(projection.AvailableModes).ModeId);
        Assert.Equal("agent", projection.SelectedModeId);
        Assert.True(projection.ShowConfigOptionsPanel);
        Assert.Equal("plan", Assert.Single(projection.AvailableCommands).Name);
    }

    [Fact]
    public void Project_WhenNoHydratedConversation_FallsBackToRootTooling()
    {
        var projector = new ChatSessionToolingProjector();
        var rootCommand = new ConversationAvailableCommandSnapshot("plan", "Planning command", "target");

        var state = new ChatState(
            HydratedConversationId: null,
            AvailableModes:
            [
                new ConversationModeOptionSnapshot
                {
                    ModeId = "agent",
                    ModeName = "Agent",
                    Description = "Default"
                }
            ],
            SelectedModeId: "agent",
            ConfigOptions:
            [
                new ConversationConfigOptionSnapshot
                {
                    Id = "mode",
                    Name = "Mode",
                    Description = "Primary mode",
                    Category = "mode",
                    ValueType = "string",
                    SelectedValue = "agent"
                }
            ],
            ShowConfigOptionsPanel: true,
            AvailableCommands: [rootCommand]);

        var projection = projector.Project(state, null);

        Assert.Equal("agent", Assert.Single(projection.AvailableModes).ModeId);
        Assert.Equal("agent", projection.SelectedModeId);
        Assert.True(projection.ShowConfigOptionsPanel);
        Assert.Equal("plan", Assert.Single(projection.AvailableCommands).Name);
    }
}

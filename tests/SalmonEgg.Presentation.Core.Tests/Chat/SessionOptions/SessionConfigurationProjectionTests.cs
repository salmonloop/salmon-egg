using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.ViewModels.Chat.SessionOptions;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat.SessionOptions;

public sealed class SessionConfigurationProjectionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Project_BooleanConfig_RetainsTypedStateThroughTheProductPresenter(bool value)
    {
        var projector = new AcpSessionUpdateProjector();
        var option = new ConfigOption
        {
            Id = "thinking",
            Name = "Thinking",
            Type = "boolean",
            CurrentBooleanValue = value
        };

        var delta = projector.Project(new SessionUpdateEventArgs("s", new ConfigOptionUpdate { ConfigOptions = [option] }));
        var configuration = Assert.Single(delta.ConfigOptions!);
        var presented = new ChatSessionOptionsPresenter().Present([], null,
            [new ConversationConfigOptionSnapshot
            {
                Id = configuration.Id,
                Name = configuration.Name,
                ValueType = configuration.ValueType,
                SelectedValue = configuration.SelectedValue,
                BooleanValue = configuration.BooleanValue
            }], true);

        var row = Assert.Single(presented.ConfigOptions);
        Assert.True(row.IsBoolType);
        Assert.Equal(value, row.BoolValue);
        Assert.Equal(value, Assert.IsType<bool>(row.Value));
        Assert.Null(configuration.SelectedValue);
        Assert.True(delta.ShowConfigOptionsPanel);
        Assert.True(presented.ShowConfigOptionsPanel);
    }

    [Fact]
    public void Project_UnknownOption_RetainsRawPayloadAndPriorityWithoutInventingAnEditor()
    {
        const string json = """{"id":"budget","name":"Budget","type":"_budget","currentValue":{"tokens":1e2},"future":{"x":true}}""";
        var unknown = JsonSerializer.Deserialize(json, AcpJsonContext.Default.ConfigOption)!;
        var known = new ConfigOption { Id = "enabled", Name = "Enabled", Type = "boolean", CurrentBooleanValue = true };

        var delta = new AcpSessionUpdateProjector().Project(new SessionUpdateEventArgs("s",
            new ConfigOptionUpdate { ConfigOptions = [unknown, known] }));
        var options = delta.ConfigOptions!;
        var view = new ChatSessionOptionsPresenter().Present([], null,
            options.Select(static option => new ConversationConfigOptionSnapshot
            {
                Id = option.Id,
                ValueType = option.ValueType,
                BooleanValue = option.BooleanValue,
                RawProtocolJson = option.RawProtocolJson
            }).ToArray(), true);

        Assert.Equal(["budget", "enabled"], options.Select(static option => option.Id));
        Assert.Equal(json, options[0].RawProtocolJson);
        Assert.Equal("enabled", Assert.Single(view.ConfigOptions).Id);
    }

    [Fact]
    public void Project_OnlyUnknownOption_DoesNotDisplayAnEmptyConfigurationPanel()
    {
        var option = new ConfigOption { Id = "future", Name = "Future", Type = "future" };

        var delta = new AcpSessionUpdateProjector().ProjectSessionNew(new SessionNewResponse("s", configOptions: [option]));
        var view = new ChatSessionOptionsPresenter().Present([], null,
            [new ConversationConfigOptionSnapshot { Id = "future", ValueType = "future" }], true);

        Assert.Single(delta.ConfigOptions!);
        Assert.False(delta.ShowConfigOptionsPanel);
        Assert.Empty(view.ConfigOptions);
        Assert.False(view.ShowConfigOptionsPanel);
    }

    [Fact]
    public void Present_ChangedBoolean_MustRefreshExistingRows()
    {
        var presenter = new ChatSessionOptionsPresenter();
        var before = presenter.Present([], null,
            [new ConversationConfigOptionSnapshot { Id = "enabled", ValueType = "boolean", BooleanValue = false }], true);
        var after = presenter.Present([], null,
            [new ConversationConfigOptionSnapshot { Id = "enabled", ValueType = "boolean", BooleanValue = true }], true);

        Assert.False(presenter.ConfigOptionCollectionMatches(before.ConfigOptions, after.ConfigOptions));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateFromAcp_BooleanOption_KeepsItsValue(bool value)
    {
        var row = ConfigOptionViewModel.CreateFromAcp(new ConfigOption
        {
            Id = "toggle",
            Name = "Toggle",
            Type = "boolean",
            CurrentBooleanValue = value
        });

        Assert.True(row.IsBoolType);
        Assert.Equal(value, row.BoolValue);
        Assert.Equal(value, row.Value);
    }
}

using System.Collections.Immutable;
using Moq;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionConfiguration_Apply_UsesTypedValueAndCommitsAgentResponse(bool boolean)
    {
        // Arrange
        await using var fixture = CreateInteractionViewModel();
        var service = CreateConnectedChatService();
        var response = new TaskCompletionSource<SessionSetConfigOptionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        SessionSetConfigOptionParams? sent = null;
        service.Setup(value => value.SetSessionConfigOptionAsync(It.IsAny<SessionSetConfigOptionParams>()))
            .Returns<SessionSetConfigOptionParams>(request => { sent = request; return response.Task; });
        await PrepareConfigurationAsync(fixture, service.Object, boolean);
        var row = Assert.Single(fixture.ViewModel.ConfigOptions);
        if (boolean) row.BoolValue = true;
        else row.SelectedOption = row.Options.Single(option => option.Value == "careful");

        // Act
        var apply = row.ApplyCommand.ExecuteAsync(null);
        Assert.NotNull(sent);
        Assert.True(row.IsApplying);
        Assert.False(row.EditorEnabled);
        Assert.Equal("remote", sent.SessionId);
        Assert.Equal("profile-setting", sent.ConfigId);
        Assert.Equal(boolean ? null : "careful", sent.Value);
        Assert.Equal(boolean ? true : (bool?)null, sent.BooleanValue);
        Assert.Equal(boolean ? false : "fast", row.Value);
        response.SetResult(new SessionSetConfigOptionResponse { ConfigOptions = [MakeConfigOption(boolean, changed: true)] });
        await apply;

        // Assert
        row = Assert.Single(fixture.ViewModel.ConfigOptions);
        Assert.False(row.IsApplying);
        Assert.False(row.HasChanges);
        Assert.False(row.HasError);
        var option = Assert.Single((await fixture.GetStateAsync()).ResolveSessionStateSlice("conversation")!.Value.ConfigOptions);
        Assert.Equal(boolean ? true : (bool?)null, option.BooleanValue);
        Assert.Equal(boolean ? null : "careful", option.SelectedValue);
    }

    [Fact]
    public async Task SessionConfiguration_FailedApply_KeepsDraftForRetryAndLocalizesError()
    {
        // Arrange
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "SessionConfig_Failed", "设置未能应用，编辑已保留，请重试。");
        await using var fixture = CreateInteractionViewModel(localizer: localizer);
        var service = CreateConnectedChatService();
        service.SetupSequence(value => value.SetSessionConfigOptionAsync(It.IsAny<SessionSetConfigOptionParams>()))
            .ThrowsAsync(new InvalidOperationException("private backend details"))
            .ReturnsAsync(new SessionSetConfigOptionResponse { ConfigOptions = [MakeConfigOption(false, true)] });
        await PrepareConfigurationAsync(fixture, service.Object);
        var row = Assert.Single(fixture.ViewModel.ConfigOptions);
        row.SelectedOption = row.Options[1];

        // Act / Assert
        await row.ApplyCommand.ExecuteAsync(null);
        Assert.Equal("fast", row.Value);
        Assert.Equal("careful", row.SelectedOption?.Value);
        Assert.True(row.HasChanges);
        Assert.True(row.ApplyCommand.CanExecute(null));
        Assert.Equal("设置未能应用，编辑已保留，请重试。", row.ErrorMessage);
        await row.ApplyCommand.ExecuteAsync(null);
        Assert.False(row.HasError);
        Assert.Equal("careful", row.Value);
    }

    [Fact]
    public async Task SessionConfiguration_AuthoritativeRefresh_PreservesDraftAndAllowsExplicitReset()
    {
        // Arrange
        await using var fixture = CreateInteractionViewModel();
        var service = CreateConnectedChatService();
        await PrepareConfigurationAsync(fixture, service.Object);
        var row = Assert.Single(fixture.ViewModel.ConfigOptions);
        row.SelectedOption = row.Options[1];

        // Act
        await fixture.DispatchAsync(new SetConversationSessionStateAction("conversation", [], null,
            ConfigOptions: [MakeConfigSnapshot(false, selected: "agent-choice")], ShowConfigOptionsPanel: true));

        // Assert
        Assert.Same(row, Assert.Single(fixture.ViewModel.ConfigOptions));
        Assert.Equal("agent-choice", row.Value);
        Assert.Equal("careful", row.SelectedOption?.Value);
        Assert.True(row.ChangedWhileEditing);
        row.ResetCommand.Execute(null);
        Assert.Equal("agent-choice", row.SelectedOption?.Value);
        Assert.False(row.HasChanges);
        Assert.False(row.ChangedWhileEditing);
        service.Verify(value => value.SetSessionConfigOptionAsync(It.IsAny<SessionSetConfigOptionParams>()), Times.Never);
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("selection")]
    [InlineData("connection")]
    [InlineData("type")]
    [InlineData("hydration")]
    public async Task SessionConfiguration_StaleEditor_NeverSendsToReplacement(string change)
    {
        // Arrange
        await using var fixture = CreateInteractionViewModel();
        var service = CreateConnectedChatService();
        await PrepareConfigurationAsync(fixture, service.Object);
        var row = Assert.Single(fixture.ViewModel.ConfigOptions);
        row.SelectedOption = row.Options[1];

        // Act
        await ReplaceConfigurationOwnerAsync(fixture, change);
        await row.ApplyCommand.ExecuteAsync(null);

        // Assert
        Assert.False(row.ApplyCommand.CanExecute(null));
        service.Verify(value => value.SetSessionConfigOptionAsync(It.IsAny<SessionSetConfigOptionParams>()), Times.Never);
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("selection")]
    [InlineData("connection")]
    public async Task SessionConfiguration_LateResponse_DoesNotOverwriteNewOwner(string change)
    {
        // Arrange
        await using var fixture = CreateInteractionViewModel();
        var service = CreateConnectedChatService();
        var response = new TaskCompletionSource<SessionSetConfigOptionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Setup(value => value.SetSessionConfigOptionAsync(It.IsAny<SessionSetConfigOptionParams>())).Returns(response.Task);
        await PrepareConfigurationAsync(fixture, service.Object);
        var row = Assert.Single(fixture.ViewModel.ConfigOptions);
        row.SelectedOption = row.Options[1];
        var apply = row.ApplyCommand.ExecuteAsync(null);

        // Act
        await ReplaceConfigurationOwnerAsync(fixture, change);
        response.SetResult(new SessionSetConfigOptionResponse { ConfigOptions = [MakeConfigOption(false, true)] });
        await apply;

        // Assert
        Assert.Equal("fast", Assert.Single((await fixture.GetStateAsync())
            .ResolveSessionStateSlice("conversation")!.Value.ConfigOptions).SelectedValue);
    }

    [Fact]
    public async Task SessionConfiguration_InjectedChoice_IsRejectedByAuthoritativeOptions()
    {
        // Arrange
        await using var fixture = CreateInteractionViewModel();
        var service = CreateConnectedChatService();
        await PrepareConfigurationAsync(fixture, service.Object);
        var row = Assert.Single(fixture.ViewModel.ConfigOptions);
        var injected = new OptionValueViewModel { Value = "unsupported", Name = "Unsupported" };
        row.Options.Add(injected);
        row.SelectedOption = injected;

        // Act
        await row.ApplyCommand.ExecuteAsync(null);

        // Assert
        service.Verify(value => value.SetSessionConfigOptionAsync(It.IsAny<SessionSetConfigOptionParams>()), Times.Never);
        Assert.True(row.HasError);
        Assert.Equal("fast", row.Value);
    }

    [Fact]
    public async Task SessionConfiguration_ServerRemovedSelection_KeepsDraftUntilExplicitReset()
    {
        // Arrange
        await using var fixture = CreateInteractionViewModel();
        var service = CreateConnectedChatService();
        await PrepareConfigurationAsync(fixture, service.Object);
        var row = Assert.Single(fixture.ViewModel.ConfigOptions);
        row.SelectedOption = row.Options[1];
        var updated = MakeConfigSnapshot(false);
        updated.Options.RemoveAll(option => option.Value == "careful");

        // Act
        await fixture.DispatchAsync(new SetConversationSessionStateAction("conversation", [], null,
            ConfigOptions: [updated], ShowConfigOptionsPanel: true));

        // Assert
        Assert.False(row.ApplyCommand.CanExecute(null));
        Assert.True(row.ChangedWhileEditing);
        Assert.Equal("careful", row.SelectedOption?.Value);
        row.ResetCommand.Execute(null);
        Assert.Equal("fast", row.SelectedOption?.Value);
    }

    [Fact]
    public async Task SessionConfiguration_BackgroundResponse_MarshalsEditorCompletionToUiDispatcher()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        await using var fixture = CreateInteractionViewModel(dispatcher);
        var service = CreateConnectedChatService();
        var response = new TaskCompletionSource<SessionSetConfigOptionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Setup(value => value.SetSessionConfigOptionAsync(It.IsAny<SessionSetConfigOptionParams>())).Returns(response.Task);
        await PrepareConfigurationAsync(fixture, service.Object, dispatcher: dispatcher);
        var row = Assert.Single(fixture.ViewModel.ConfigOptions);
        row.SelectedOption = row.Options[1];
        var completionOnUi = false;
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(row.IsApplying) && !row.IsApplying)
                completionOnUi = dispatcher.HasThreadAccess;
        };

        // Act
        var apply = row.ApplyCommand.ExecuteAsync(null);
        await dispatcher.RunUntilIdleAsync();
        await Task.Run(() => response.SetResult(new SessionSetConfigOptionResponse
        { ConfigOptions = [MakeConfigOption(false, true)] }), TestContext.Current.CancellationToken);
        await dispatcher.RunUntilCompletedAsync(apply);

        // Assert
        Assert.True(completionOnUi);
        Assert.False(row.HasError);
        Assert.False(row.HasChanges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionConfiguration_PriorityChanges_DefersMovesWhileEditingAndConvergesAfterReset(bool editing)
    {
        // Arrange
        await using var fixture = CreateInteractionViewModel();
        await PrepareConfigurationAsync(fixture, CreateConnectedChatService().Object);
        var row = Assert.Single(fixture.ViewModel.ConfigOptions);
        var other = new ConversationConfigOptionSnapshot
        { Id = "another-setting", Name = "Another", ValueType = "boolean", BooleanValue = false };
        await fixture.DispatchAsync(new SetConversationSessionStateAction("conversation", [], null,
            ConfigOptions: [MakeConfigSnapshot(false), other], ShowConfigOptionsPanel: true));
        if (editing) row.SelectedOption = row.Options[1];

        // Act
        await fixture.DispatchAsync(new SetConversationSessionStateAction("conversation", [], null,
            ConfigOptions: [other, MakeConfigSnapshot(false)], ShowConfigOptionsPanel: true));

        // Assert
        if (editing)
        {
            Assert.Same(row, fixture.ViewModel.ConfigOptions[0]);
            row.ResetCommand.Execute(null);
        }
        Assert.Equal(["another-setting", "profile-setting"], fixture.ViewModel.ConfigOptions.Select(option => option.Id));
        Assert.Same(row, fixture.ViewModel.ConfigOptions[1]);
    }

    private static async Task PrepareConfigurationAsync(ViewModelFixture fixture,
        SalmonEgg.Application.Services.Chat.IChatService service, bool boolean = false,
        QueueingSynchronizationContext? dispatcher = null)
    {
        var adapter = RegisterInteractionMock(fixture, service, "profile");
        var replace = fixture.ViewModel.ReplaceChatServiceAsync(adapter, TestContext.Current.CancellationToken);
        if (dispatcher is not null)
            await dispatcher.RunUntilCompletedAsync(replace);
        else await replace;
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conversation",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conversation", new("conversation", "remote", "profile")),
            ConversationSessionStates = ImmutableDictionary<string, ConversationSessionStateSlice>.Empty
                .Add("conversation", new([], null, [MakeConfigSnapshot(boolean)], true, [], null, null))
        });
    }

    private static Task ReplaceConfigurationOwnerAsync(ViewModelFixture fixture, string change)
        => change switch
        {
            "binding" => fixture.UpdateStateAsync(state => state with
            { Bindings = state.Bindings!.SetItem("conversation", new("conversation", "replacement", "profile")) }).AsTask(),
            "selection" => fixture.UpdateStateAsync(state => state with { HydratedConversationId = "other" }).AsTask(),
            "connection" => fixture.ViewModel.ReplaceChatServiceAsync(CreateConnectedChatService().Object, TestContext.Current.CancellationToken),
            "hydration" => fixture.UpdateStateAsync(state => state with { IsHydrating = true }).AsTask(),
            _ => fixture.DispatchAsync(new SetConversationSessionStateAction("conversation", [], null,
                ConfigOptions: [new ConversationConfigOptionSnapshot { Id = "profile-setting", Name = "Future", ValueType = "future" }],
                ShowConfigOptionsPanel: false)).AsTask()
        };

    private static ConversationConfigOptionSnapshot MakeConfigSnapshot(bool boolean, string selected = "fast") => new()
    {
        Id = "profile-setting",
        Name = "Performance",
        Description = "How carefully the agent should work.",
        ValueType = boolean ? "boolean" : "select",
        BooleanValue = boolean ? false : null,
        SelectedValue = boolean ? null : selected,
        Options = boolean ? [] : [new() { Value = "fast", Name = "Fast" }, new() { Value = "careful", Name = "Careful" },
            new() { Value = "agent-choice", Name = "Agent choice" }]
    };

    private static ConfigOption MakeConfigOption(bool boolean, bool changed) => new()
    {
        Id = "profile-setting",
        Name = "Performance",
        Description = "How carefully the agent should work.",
        Type = boolean ? "boolean" : "select",
        CurrentBooleanValue = boolean ? changed : null,
        CurrentValue = boolean ? null : changed ? "careful" : "fast",
        Options = boolean ? [] : [new() { Value = "fast", Name = "Fast" }, new() { Value = "careful", Name = "Careful" }]
    };
}

using System.Collections.Immutable;
using System.Linq;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services.Chat.Slash;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task CurrentPrompt_WhenSlashPrefixEntered_ShowsMatchingCommandsAndSelectsFirstMatch()
    {
        await using var harness = await CreateSlashHarnessAsync();
        await harness.PushRemoteSlashCommandsAsync(
            new ConversationAvailableCommandSnapshot("plan", "Planning command", "goal"),
            new ConversationAvailableCommandSnapshot("prompt", "Prompt command", "text"),
            new ConversationAvailableCommandSnapshot("status", "Status command", "scope"));

        harness.ViewModel.CurrentPrompt = "/p";

        Assert.True(harness.ViewModel.ShowSlashCommands);
        Assert.Equal(new[] { "plan", "prompt" }, harness.ViewModel.FilteredSlashCommands.Select(static command => command.Name).ToArray());
        Assert.Equal("plan", harness.ViewModel.SelectedSlashCommand?.Name);
        Assert.Equal("lan ", harness.ViewModel.SlashGhostSuffix);
    }

    [Fact]
    public async Task TryMoveSlashSelection_WhenVisible_UpdatesSelectionWithinBounds()
    {
        await using var harness = await CreateSlashHarnessAsync();
        await harness.PushRemoteSlashCommandsAsync(
            new ConversationAvailableCommandSnapshot("plan", "Planning command", "goal"),
            new ConversationAvailableCommandSnapshot("prompt", "Prompt command", "text"));

        harness.ViewModel.CurrentPrompt = "/p";

        Assert.True(harness.ViewModel.TryMoveSlashSelection(1));
        Assert.Equal("prompt", harness.ViewModel.SelectedSlashCommand?.Name);
        Assert.Equal("rompt ", harness.ViewModel.SlashGhostSuffix);

        Assert.True(harness.ViewModel.TryMoveSlashSelection(1));
        Assert.Equal("prompt", harness.ViewModel.SelectedSlashCommand?.Name);

        Assert.True(harness.ViewModel.TryMoveSlashSelection(-5));
        Assert.Equal("plan", harness.ViewModel.SelectedSlashCommand?.Name);
    }

    [Fact]
    public async Task TryAcceptSelectedSlashCommand_WhenSelectionExists_CompletesPromptAndClosesTopLevelSuggestions()
    {
        await using var harness = await CreateSlashHarnessAsync();
        await harness.PushRemoteSlashCommandsAsync(
            new ConversationAvailableCommandSnapshot("plan", "Planning command", "goal"),
            new ConversationAvailableCommandSnapshot("prompt", "Prompt command", "text"));

        harness.ViewModel.CurrentPrompt = "   /pr";

        var accepted = harness.ViewModel.TryAcceptSelectedSlashCommand();

        Assert.True(accepted);
        Assert.Equal("   /prompt ", harness.ViewModel.CurrentPrompt);
        Assert.False(harness.ViewModel.ShowSlashCommands);
        Assert.Equal(string.Empty, harness.ViewModel.SlashGhostSuffix);
    }

    [Fact]
    public async Task CurrentPrompt_WhenNotSlashPrefix_HidesCommandsAndClearsSelection()
    {
        await using var harness = await CreateSlashHarnessAsync();
        await harness.PushRemoteSlashCommandsAsync(
            new ConversationAvailableCommandSnapshot("plan", "Planning command", "goal"));

        harness.ViewModel.CurrentPrompt = "/p";
        Assert.True(harness.ViewModel.ShowSlashCommands);
        Assert.NotNull(harness.ViewModel.SelectedSlashCommand);

        harness.ViewModel.CurrentPrompt = "plain text";

        Assert.False(harness.ViewModel.ShowSlashCommands);
        Assert.Null(harness.ViewModel.SelectedSlashCommand);
        Assert.Empty(harness.ViewModel.FilteredSlashCommands);
        Assert.Equal(string.Empty, harness.ViewModel.SlashGhostSuffix);
    }

    [Fact]
    public async Task CurrentPrompt_WhenLocalAndRemoteCommandsConflict_LocalCommandRemainsAuthoritative()
    {
        var localSource = new StaticSlashCommandSource(
        [
            new SlashCommandSpec
            {
                Name = "help",
                Description = "Local help",
                Source = SlashCommandSourceKind.Local
            }
        ]);

        await using var harness = await CreateSlashHarnessAsync(localSource);
        await harness.PushRemoteSlashCommandsAsync(
            new ConversationAvailableCommandSnapshot("help", "Remote help", "topic"));

        harness.ViewModel.CurrentPrompt = "/he";

        Assert.True(harness.ViewModel.ShowSlashCommands);
        var selected = Assert.Single(harness.ViewModel.FilteredSlashCommands);
        Assert.Equal("help", selected.Name);
        Assert.Equal("Local help", selected.Description);
        Assert.True(harness.ViewModel.TryAcceptSelectedSlashCommand());
        Assert.Equal("/help ", harness.ViewModel.CurrentPrompt);
    }

    private static async Task<SlashHarness> CreateSlashHarnessAsync(ISlashCommandSource? localSlashCommandSource = null)
    {
        var syncContext = new QueueingSynchronizationContext();
        var fixture = CreateViewModel(syncContext, localSlashCommandSource: localSlashCommandSource);
        var chatService = CreateConnectedChatService();

        await AwaitWithSynchronizationContextAsync(syncContext, fixture.ViewModel.ReplaceChatServiceAsync(chatService.Object));
        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Bindings = ImmutableDictionary<string, ConversationBindingSlice>.Empty
                .Add("conv-1", new ConversationBindingSlice("conv-1", "remote-1", "profile-1"))
        });
        syncContext.RunAll();

        return new SlashHarness(fixture, syncContext, chatService);
    }

    private sealed class SlashHarness : IAsyncDisposable
    {
        private readonly ViewModelFixture _fixture;
        private readonly QueueingSynchronizationContext _syncContext;
        private readonly Mock<IChatService> _chatService;

        public SlashHarness(
            ViewModelFixture fixture,
            QueueingSynchronizationContext syncContext,
            Mock<IChatService> chatService)
        {
            _fixture = fixture;
            _syncContext = syncContext;
            _chatService = chatService;
        }

        public ChatViewModel ViewModel => _fixture.ViewModel;

        public async Task PushRemoteSlashCommandsAsync(params ConversationAvailableCommandSnapshot[] commands)
        {
            _chatService.Raise(
                service => service.SessionUpdateReceived += null,
                new SessionUpdateEventArgs(
                    "remote-1",
                    new AvailableCommandsUpdate
                    {
                        AvailableCommands =
                        [
                            .. commands.Select(static command => new AvailableCommand
                            {
                                Name = command.Name,
                                Description = command.Description,
                                Input = string.IsNullOrWhiteSpace(command.InputHint)
                                    ? null
                                    : new AvailableCommandInput
                                    {
                                        Hint = command.InputHint
                                    }
                            })
                        ]
                    }));

            await WaitForPendingSessionUpdatesAsync(ViewModel);
            await _fixture.ApplyCurrentStoreProjectionAsync();
            await WaitForConditionAsync(() =>
            {
                _syncContext.RunAll();
                var actualNames = ViewModel.AvailableSlashCommands.Select(static command => command.Name).ToArray();
                var expectedNames = commands.Select(static command => command.Name).ToArray();
                return Task.FromResult(
                    actualNames.Length == expectedNames.Length
                    && actualNames.SequenceEqual(expectedNames));
            });
        }

        public async ValueTask DisposeAsync()
        {
            await _fixture.DisposeAsync();
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Interfaces.Storage;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.ConversationPreview;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.ViewModels.Chat.Transcript;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact]
    public async Task ApplyProjection_WhenSessionChanges_ReusesMessageHistory()
    {
        await using var fixture = CreateViewModel();
        await fixture.ViewModel.RestoreAsync();

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "hello"
                }
            ]
        });

        var originalHistory = fixture.ViewModel.MessageHistory;

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-2",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-2",
                    Timestamp = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "world"
                }
            ]
        });

        Assert.Same(originalHistory, fixture.ViewModel.MessageHistory);
        Assert.Collection(
            fixture.ViewModel.MessageHistory,
            message =>
            {
                Assert.Equal("message-2", message.Id);
                Assert.Equal("world", message.TextContent);
            });
    }

    [Fact]
    public async Task ApplyProjection_WhenSessionStaysSame_DiffsMessageHistory()
    {
        await using var fixture = CreateViewModel();
        await fixture.ViewModel.RestoreAsync();

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "hello"
                }
            ]
        });

        ObservableCollection<ChatMessageViewModel> originalHistory = fixture.ViewModel.MessageHistory;

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "hello updated"
                },
                new ConversationMessageSnapshot
                {
                    Id = "message-2",
                    Timestamp = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = true,
                    ContentType = "text",
                    TextContent = "follow up"
                }
            ]
        });

        Assert.Same(originalHistory, fixture.ViewModel.MessageHistory);
        Assert.Collection(
            fixture.ViewModel.MessageHistory,
            message =>
            {
                Assert.Equal("message-1", message.Id);
                Assert.Equal("hello updated", message.TextContent);
            },
            message =>
            {
                Assert.Equal("message-2", message.Id);
                Assert.Equal("follow up", message.TextContent);
            });
    }

    [Fact]
    public async Task ApplyProjection_WhenHydratingEmptyTranscript_UpdatesVisibleTranscriptOwner()
    {
        await using var fixture = CreateViewModel();
        await fixture.ViewModel.RestoreAsync();

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-1",
            Transcript =
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "stale transcript"
                }
            ]
        });
        await WaitForConditionAsync(() => Task.FromResult(fixture.ViewModel.CurrentSessionId == "conv-1"));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-2",
            Transcript = [],
            IsHydrating = true
        });
        await WaitForConditionAsync(() => Task.FromResult(fixture.ViewModel.CurrentSessionId == "conv-2"));

        Assert.Equal("conv-2", fixture.ViewModel.CurrentSessionId);
        Assert.Empty(fixture.ViewModel.MessageHistory);
        Assert.False(fixture.ViewModel.HasVisibleTranscriptContent);
        Assert.True(fixture.ViewModel.ShouldShowBlockingLoadingMask);
        Assert.DoesNotContain(
            fixture.ViewModel.MessageHistory,
            message => string.Equals(message.TextContent, "stale transcript", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportCurrentSessionJson_UsesAuthoritativeTranscript_NotRenderedViewportWindow()
    {
        await using var fixture = CreateViewModel();
        await fixture.ViewModel.RestoreAsync();
        var transcript = ImmutableList.CreateRange(
            Enumerable.Range(0, 160)
                .Select(index => new ConversationMessageSnapshot
                {
                    Id = $"message-{index}",
                    Timestamp = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc).AddSeconds(index),
                    IsOutgoing = index % 2 == 0,
                    ContentType = "text",
                    TextContent = $"payload-{index}"
                }));

        await fixture.UpdateStateAsync(state => state with
        {
            HydratedConversationId = "conv-large",
            Transcript = transcript,
            ConversationContents = ImmutableDictionary<string, ConversationContentSlice>.Empty.SetItem(
                "conv-large",
                new ConversationContentSlice(
                    transcript,
                    ImmutableList<ConversationPlanEntrySnapshot>.Empty,
                    ShowPlanPanel: false,
                    PlanTitle: null))
        });
        await WaitForConditionAsync(() => Task.FromResult(fixture.ViewModel.CurrentSessionId == "conv-large"));

        Assert.True(fixture.ViewModel.MessageHistory.Count < transcript.Count);

        var exportDirectory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"salmon-export-test-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(exportDirectory);

        try
        {
            var paths = new Mock<IAppDataService>();
            paths.SetupGet(path => path.ExportsDirectoryPath).Returns(exportDirectory);
            var maintenance = new Mock<IAppMaintenanceService>();
            var diagnostics = new Mock<IDiagnosticsBundleService>();
            var shell = new Mock<IPlatformShellService>();
            shell.Setup(service => service.OpenFileAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
            var preferences = (AppPreferencesViewModel)RuntimeHelpers.GetUninitializedObject(typeof(AppPreferencesViewModel));
            var settings = new DataStorageSettingsViewModel(
                preferences,
                fixture.ViewModel,
                paths.Object,
                maintenance.Object,
                diagnostics.Object,
                shell.Object,
                Mock.Of<ILogger<DataStorageSettingsViewModel>>());

            await settings.ExportCurrentSessionJsonCommand.ExecuteAsync(null);

            var exportFile = Assert.Single(System.IO.Directory.GetFiles(exportDirectory, "*.json"));
            using var json = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(exportFile));
            Assert.Equal(transcript.Count, json.RootElement.GetProperty("Messages").GetArrayLength());
        }
        finally
        {
            System.IO.Directory.Delete(exportDirectory, recursive: true);
        }
    }
}

public sealed class ChatTranscriptProjectionCoordinatorUnitTests
{
    [Fact]
    public async Task ApplyProjection_WhenSessionChanges_ReusesCollectionAndSavesPreviewOnce()
    {
        var previewStore = new Mock<IConversationPreviewStore>();
        previewStore.Setup(store => store.SaveAsync(It.IsAny<ConversationPreviewSnapshot>(), default))
            .Returns(Task.CompletedTask);
        var coordinator = new ChatTranscriptProjectionCoordinator(previewStore.Object);
        var owner = (string?)null;
        var history = new ObservableCollection<ChatMessageViewModel>
        {
            new() { Id = "stale", TextContent = "stale" }
        };
        var originalHistory = history;
        var context = CreateContext(
            () => history,
            value => history = value,
            (conversationId, hasVisibleTranscript) =>
            {
                owner = hasVisibleTranscript ? conversationId : conversationId;
                return true;
            });

        coordinator.ApplyProjection(
            context,
            "conv-2",
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "hello"
                }
            ],
            preparedTranscript: null,
            sessionChanged: true);
        coordinator.UpdatePreviewSnapshot(
            "conv-2",
            [
                new ConversationMessageSnapshot
                {
                    Id = "message-1",
                    Timestamp = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = "hello"
                }
            ],
            isHydrating: false);

        Assert.Same(originalHistory, history);
        Assert.Collection(
            history,
            message =>
            {
                Assert.Equal("message-1", message.Id);
                Assert.Equal("hello", message.TextContent);
            });
        Assert.Equal("conv-2", owner);
        previewStore.Verify(store => store.SaveAsync(
                It.Is<ConversationPreviewSnapshot>(snapshot => snapshot.ConversationId == "conv-2"),
                default),
            Times.Once);
    }

    [Fact]
    public async Task UpdatePreviewSnapshot_WhenOnlyTranscriptReferenceIsUnchanged_DoesNotResavePreview()
    {
        var previewStore = new Mock<IConversationPreviewStore>();
        previewStore.Setup(store => store.SaveAsync(It.IsAny<ConversationPreviewSnapshot>(), default))
            .Returns(Task.CompletedTask);
        var coordinator = new ChatTranscriptProjectionCoordinator(previewStore.Object);
        var transcript = ImmutableList.Create(
            new ConversationMessageSnapshot
            {
                Id = "message-1",
                Timestamp = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                IsOutgoing = false,
                ContentType = "text",
                TextContent = "hello"
            });

        coordinator.UpdatePreviewSnapshot("conv-1", transcript, isHydrating: false);
        coordinator.UpdatePreviewSnapshot("conv-1", transcript, isHydrating: false);

        previewStore.Verify(store => store.SaveAsync(
                It.Is<ConversationPreviewSnapshot>(snapshot => snapshot.ConversationId == "conv-1"),
                default),
            Times.Once);
    }

    [Fact]
    public async Task BuildPreviewSnapshot_WhenTranscriptContainsMessages_CreatesExpectedEntriesWithoutStoreSideEffects()
    {
        var coordinator = new ChatTranscriptProjectionCoordinator(Mock.Of<IConversationPreviewStore>());
        var transcript = ImmutableList.Create(
            new ConversationMessageSnapshot
            {
                Id = "message-1",
                Timestamp = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                IsOutgoing = true,
                ContentType = "text",
                TextContent = "hello"
            },
            new ConversationMessageSnapshot
            {
                Id = "message-2",
                Timestamp = new DateTime(2026, 5, 3, 0, 0, 1, DateTimeKind.Utc),
                IsOutgoing = false,
                ContentType = "text",
                TextContent = "world"
            });

        var snapshot = coordinator.BuildPreviewSnapshot("conv-1", transcript, isHydrating: false);

        Assert.NotNull(snapshot);
        Assert.Equal("conv-1", snapshot!.ConversationId);
        Assert.Collection(
            snapshot.Entries,
            entry =>
            {
                Assert.Equal("user", entry.Sender);
                Assert.Equal("hello", entry.Text);
            },
            entry =>
            {
                Assert.Equal("assistant", entry.Sender);
                Assert.Equal("world", entry.Text);
            });
    }

    [Fact]
    public void BuildPreviewSnapshot_WhenHydratingOrInvalidInput_ReturnsNull()
    {
        var coordinator = new ChatTranscriptProjectionCoordinator(Mock.Of<IConversationPreviewStore>());
        var transcript = ImmutableList.Create(
            new ConversationMessageSnapshot
            {
                Id = "message-1",
                Timestamp = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                IsOutgoing = false,
                ContentType = "text",
                TextContent = "hello"
            });

        Assert.Null(coordinator.BuildPreviewSnapshot(null, transcript, isHydrating: false));
        Assert.Null(coordinator.BuildPreviewSnapshot("conv-1", transcript, isHydrating: true));
        Assert.Null(coordinator.BuildPreviewSnapshot("conv-1", ImmutableList<ConversationMessageSnapshot>.Empty, isHydrating: false));
    }

    [Fact]
    public void ApplyProjection_WhenSameSessionReceivesLargeTranscriptGrowth_ReplacesCollection()
    {
        var previewStore = new Mock<IConversationPreviewStore>();
        var coordinator = new ChatTranscriptProjectionCoordinator(previewStore.Object);
        var originalHistory = new ObservableCollection<ChatMessageViewModel>
        {
            new()
            {
                Id = "message-0",
                Timestamp = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
                ContentType = "text",
                TextContent = "seed"
            }
        };
        var history = originalHistory;
        var context = CreateContext(
            () => history,
            value => history = value,
            static (_, _) => false);

        var transcript = ImmutableList.CreateRange(
            Enumerable.Range(0, 96)
                .Select(index => new ConversationMessageSnapshot
                {
                    Id = $"message-{index}",
                    Timestamp = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc).AddSeconds(index),
                    IsOutgoing = false,
                    ContentType = "text",
                    TextContent = index == 0 ? "seed" : $"payload-{index}"
                }));

        coordinator.ApplyProjection(
            context,
            "conv-1",
            transcript,
            preparedTranscript: null,
            sessionChanged: false);

        Assert.NotSame(originalHistory, history);
        Assert.Equal(96, history.Count);
        Assert.Equal("message-0", history[0].Id);
        Assert.Equal("message-95", history[^1].Id);
    }

    private static ChatTranscriptProjectionContext CreateContext(
        Func<ObservableCollection<ChatMessageViewModel>> getHistory,
        Action<ObservableCollection<ChatMessageViewModel>> setHistory,
        Func<string?, bool, bool> updateVisibleTranscriptConversationId)
        => new()
        {
            GetMessageHistory = getHistory,
            SetMessageHistory = setHistory,
            FromSnapshot = (snapshot, index) => new ChatMessageViewModel
            {
                Id = snapshot.Id,
                ProjectionItemKey = SalmonEgg.Presentation.Core.Services.Chat.TranscriptProjectionRestoreTokenProjector.CreateProjectionItemKey(snapshot, index),
                Timestamp = snapshot.Timestamp,
                IsOutgoing = snapshot.IsOutgoing,
                ContentType = snapshot.ContentType,
                TextContent = snapshot.TextContent
            },
            MatchesSnapshot = static (message, snapshot) =>
                string.Equals(message.Id, snapshot.Id, StringComparison.Ordinal)
                && string.Equals(message.TextContent, snapshot.TextContent, StringComparison.Ordinal),
            GetProjectionItemKey = SalmonEgg.Presentation.Core.Services.Chat.TranscriptProjectionRestoreTokenProjector.CreateProjectionItemKey,
            IsTailAnchored = static () => true,
            UpdateVisibleTranscriptConversationId = updateVisibleTranscriptConversationId,
            RaiseTranscriptStateChanged = static () => { }
        };
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Tests.Threading;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ConversationOpenRouterTests
{
    [Fact]
    public async Task OpenConversation_ForKnownConversation_ActivatesItWithItsResolvedProject()
    {
        // Arrange
        var catalog = new FakeConversationCatalog(CreateItem("conv-1"));
        var activation = new RecordingActivationEntryPoint(activated: true);
        var router = CreateRouter(catalog, activation, resolvedProjectId: "project-1");

        // Act
        var result = await router.OpenConversationAsync("conv-1");

        // Assert
        Assert.Equal(ConversationOpenResult.Opened, result);
        Assert.Equal(new[] { ("conv-1", "project-1") }, activation.Requests);
    }

    [Fact]
    public async Task OpenConversation_PassesTheConversationsOwnAffinityFactsToTheResolver()
    {
        // Arrange
        var catalog = new FakeConversationCatalog(CreateItem(
            "conv-1",
            cwd: "/repo/remote",
            remoteSessionId: "remote-1",
            boundProfileId: "profile-1",
            overrideProjectId: "override-1"));
        var affinity = new RecordingAffinityResolver("project-1");
        var router = new ConversationOpenRouter(
            catalog,
            affinity,
            new RecordingActivationEntryPoint(activated: true),
            new ImmediateUiDispatcher(),
            NullLogger<ConversationOpenRouter>.Instance);

        // Act
        await router.OpenConversationAsync("conv-1");

        // Assert
        var request = Assert.Single(affinity.Requests);
        Assert.Equal("/repo/remote", request.Cwd);
        Assert.Equal("remote-1", request.RemoteSessionId);
        Assert.Equal("profile-1", request.BoundProfileId);
        Assert.Equal("override-1", request.OverrideProjectId);
    }

    [Fact]
    public async Task OpenConversation_ForUnknownConversation_DoesNotActivateAnything()
    {
        // Arrange
        var catalog = new FakeConversationCatalog(CreateItem("conv-1"));
        var activation = new RecordingActivationEntryPoint(activated: true);
        var router = CreateRouter(catalog, activation);

        // Act
        var result = await router.OpenConversationAsync("conv-missing");

        // Assert
        Assert.Equal(ConversationOpenResult.NotFound, result);
        Assert.Empty(activation.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task OpenConversation_WithBlankId_IsRejectedWithoutTouchingTheCatalog(string conversationId)
    {
        // Arrange
        var catalog = new FakeConversationCatalog(CreateItem("conv-1"));
        var activation = new RecordingActivationEntryPoint(activated: true);
        var router = CreateRouter(catalog, activation);

        // Act
        var result = await router.OpenConversationAsync(conversationId);

        // Assert
        Assert.Equal(ConversationOpenResult.Invalid, result);
        Assert.Equal(0, catalog.SnapshotReads);
        Assert.Empty(activation.Requests);
    }

    [Fact]
    public async Task OpenConversation_TrimsTheRequestedId()
    {
        // Arrange
        var catalog = new FakeConversationCatalog(CreateItem("conv-1"));
        var activation = new RecordingActivationEntryPoint(activated: true);
        var router = CreateRouter(catalog, activation, resolvedProjectId: "project-1");

        // Act
        var result = await router.OpenConversationAsync("  conv-1  ");

        // Assert
        Assert.Equal(ConversationOpenResult.Opened, result);
        Assert.Equal(new[] { ("conv-1", "project-1") }, activation.Requests);
    }

    [Fact]
    public async Task OpenConversation_WhenActivationIsRejected_ReportsFailure()
    {
        // Arrange
        var catalog = new FakeConversationCatalog(CreateItem("conv-1"));
        var activation = new RecordingActivationEntryPoint(activated: false);
        var router = CreateRouter(catalog, activation);

        // Act
        var result = await router.OpenConversationAsync("conv-1");

        // Assert — a rejected activation returns false without throwing; it must not read as Opened.
        Assert.Equal(ConversationOpenResult.Failed, result);
        Assert.Single(activation.Requests);
    }

    [Fact]
    public async Task OpenConversation_WhenActivationThrows_ReportsFailureWithoutPropagating()
    {
        // Arrange
        var catalog = new FakeConversationCatalog(CreateItem("conv-1"));
        var activation = new ThrowingActivationEntryPoint();
        var router = CreateRouter(catalog, activation);

        // Act
        var result = await router.OpenConversationAsync("conv-1");

        // Assert — a platform activation callback has nowhere to surface an exception.
        Assert.Equal(ConversationOpenResult.Failed, result);
    }

    [Fact]
    public async Task OpenConversation_ReadsTheCatalogAndActivatesOnTheUiThread()
    {
        // Arrange
        var catalog = new FakeConversationCatalog(CreateItem("conv-1"));
        var activation = new RecordingActivationEntryPoint(activated: true);
        var dispatcher = new OffThreadUiDispatcher();
        var router = new ConversationOpenRouter(
            catalog,
            new RecordingAffinityResolver("project-1"),
            activation,
            dispatcher,
            NullLogger<ConversationOpenRouter>.Instance);

        // Act
        var result = await router.OpenConversationAsync("conv-1");

        // Assert — both the catalog read and the activation are bound UI state, so both must happen
        // inside the dispatched work rather than on the notification callback's thread.
        Assert.Equal(ConversationOpenResult.Opened, result);
        Assert.Equal(1, dispatcher.EnqueueCount);
        Assert.True(catalog.SnapshotReadOnUiThread);
        Assert.True(activation.ActivatedOnUiThread);
    }

    private static ConversationOpenRouter CreateRouter(
        FakeConversationCatalog catalog,
        IConversationActivationEntryPoint activation,
        string? resolvedProjectId = null)
        => new(
            catalog,
            new RecordingAffinityResolver(resolvedProjectId),
            activation,
            new ImmediateUiDispatcher(),
            NullLogger<ConversationOpenRouter>.Instance);

    private static ConversationCatalogDisplayItem CreateItem(
        string conversationId,
        string? cwd = null,
        string? remoteSessionId = null,
        string? boundProfileId = null,
        string? overrideProjectId = null)
        => new(
            conversationId,
            $"Display {conversationId}",
            cwd,
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow,
            HasUnreadAttention: false,
            remoteSessionId,
            boundProfileId,
            overrideProjectId);

    private sealed class FakeConversationCatalog : IConversationCatalogDisplayReadModel
    {
        private readonly ConversationCatalogDisplayItem[] _items;

        public FakeConversationCatalog(params ConversationCatalogDisplayItem[] items)
        {
            _items = items;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsConversationListLoading => false;

        public int ConversationListVersion => 1;

        public int SnapshotReads { get; private set; }

        public bool SnapshotReadOnUiThread { get; private set; }

        public IReadOnlyList<ConversationCatalogDisplayItem> Snapshot
        {
            get
            {
                SnapshotReads++;
                SnapshotReadOnUiThread = OffThreadUiDispatcher.IsOnDispatchedWork;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Snapshot)));
                return _items;
            }
        }
    }

    private sealed class RecordingAffinityResolver : IConversationProjectAffinityResolver
    {
        private readonly string? _projectId;

        public RecordingAffinityResolver(string? projectId)
        {
            _projectId = projectId;
        }

        public List<ConversationProjectAffinityRequest> Requests { get; } = new();

        public string? ResolveActivationProjectId(ConversationProjectAffinityRequest request)
        {
            Requests.Add(request);
            return _projectId;
        }
    }

    private sealed class RecordingActivationEntryPoint : IConversationActivationEntryPoint
    {
        private readonly bool _activated;

        public RecordingActivationEntryPoint(bool activated)
        {
            _activated = activated;
        }

        public List<(string SessionId, string? ProjectId)> Requests { get; } = new();

        public bool ActivatedOnUiThread { get; private set; }

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId)
        {
            Requests.Add((sessionId, projectId));
            ActivatedOnUiThread = OffThreadUiDispatcher.IsOnDispatchedWork;
            return Task.FromResult(_activated);
        }
    }

    private sealed class ThrowingActivationEntryPoint : IConversationActivationEntryPoint
    {
        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId)
            => throw new InvalidOperationException("activation exploded");
    }

    /// <summary>
    /// Reports "no UI thread access" so the router must marshal, and marks work it runs so a test can
    /// tell whether a read happened inside the dispatched callback or on the calling thread.
    /// </summary>
    private sealed class OffThreadUiDispatcher : IUiDispatcher
    {
        private static readonly AsyncLocal<bool> DispatchedWork = new();

        public static bool IsOnDispatchedWork => DispatchedWork.Value;

        public int EnqueueCount { get; private set; }

        public bool HasThreadAccess => false;

        public void Enqueue(Action action) => EnqueueAsync(action).GetAwaiter().GetResult();

        public Task EnqueueAsync(Action action)
            => EnqueueAsync(() =>
            {
                action();
                return Task.CompletedTask;
            });

        public async Task EnqueueAsync(Func<Task> function)
        {
            EnqueueCount++;
            DispatchedWork.Value = true;
            try
            {
                await function().ConfigureAwait(false);
            }
            finally
            {
                DispatchedWork.Value = false;
            }
        }
    }
}

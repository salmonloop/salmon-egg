using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.ProjectAffinity;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

[Collection("NonParallel")]
public sealed class MainNavigationViewModelSelectionTests
{
    [Fact]
    public async Task LogicalSelection_RemainsActiveSession_WhenPaneClosesAndReopens()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);

            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();

            var chatCatalog = CreateChatSessionCatalog("session-1");
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);

            navVm.RebuildTree();
            SetSessionSelection(selectionStore, "session-1");

            Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);

            navState.SetPaneOpen(false);
            navState.SetPaneOpen(true);

            var selectedSession = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", selectedSession.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void PaneStateChange_DoesNotRaiseSelectionNotification_WhenSemanticSelectionStaysStable()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);

            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");

            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);
            navVm.RebuildTree();
            SetSessionSelection(selectionStore, "session-1");

            var selectedItemChanges = 0;
            navVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainNavigationViewModel.ProjectedControlSelectedItem))
                {
                    selectedItemChanges++;
                }
            };

            navState.SetPaneOpen(false);

            Assert.Equal(0, selectedItemChanges);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void RefreshSelectionProjection_DoesNotRaiseSelectionNotification_WhenProjectionIsStable()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);

            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");

            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);
            navVm.RebuildTree();
            SetSessionSelection(selectionStore, "session-1");

            var selectedItemChanges = 0;
            navVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainNavigationViewModel.ProjectedControlSelectedItem))
                {
                    selectedItemChanges++;
                }
            };

            navVm.RefreshSelectionProjection();

            Assert.Equal(0, selectedItemChanges);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void PaneOpenToClosed_KeepsSessionAsProjectedSelection()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);

            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");

            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);
            navVm.RebuildTree();
            SetSessionSelection(selectionStore, "session-1");

            Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);

            navState.SetPaneOpen(false);

            var projectedSession = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", projectedSession.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ProjectVisualState_FollowsActiveSession_WhenPaneIsClosed()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(false);

            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();

            var chatCatalog = CreateChatSessionCatalog("session-1");
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);

            navVm.RebuildTree();
            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            SetSessionSelection(selectionStore, "session-1");

            Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.True(project.IsActiveDescendant);
            var projectedSession = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", projectedSession.SessionId);
            var selectedSession = Assert.Single(project.Children.OfType<SessionNavItemViewModel>(), s => s.SessionId == "session-1");
            Assert.True(selectedSession.IsLogicallySelected);
            Assert.Equal("session-1", selectedSession.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActiveDescendantState_PersistsAcrossPaneStateChanges()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(false);

            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();

            var chatCatalog = CreateChatSessionCatalog("session-1");
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);

            navVm.RebuildTree();
            SetSessionSelection(selectionStore, "session-1");

            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            Assert.True(project.IsActiveDescendant);

            navState.SetPaneOpen(true);

            Assert.True(project.IsActiveDescendant);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task PaneClosed_KeepsSessionAsProjectedSelection()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(false);

            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();

            var chatCatalog = CreateChatSessionCatalog("session-1");
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);

            navVm.RebuildTree();
            SetSessionSelection(selectionStore, "session-1");

            var projectedSession = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", projectedSession.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task PaneClosed_TogglingProjectExpansion_DoesNotChangeSessionProjection()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(false);

            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();

            var chatCatalog = CreateChatSessionCatalog("session-1");
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);

            navVm.RebuildTree();
            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            project.IsExpanded = false;

            SetSessionSelection(selectionStore, "session-1");
            var projectedBefore = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", projectedBefore.SessionId);

            project.IsExpanded = true;
            var projectedAfter = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", projectedAfter.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ReopenPane_DoesNotForceExpandProject_WhenUserCollapsedWhileClosed()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);

            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();

            var chatCatalog = CreateChatSessionCatalog("session-1");
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);

            navVm.RebuildTree();
            SetSessionSelection(selectionStore, "session-1");

            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            Assert.True(project.IsExpanded);

            navState.SetPaneOpen(false);
            project.IsExpanded = false;
            navState.SetPaneOpen(true);

            Assert.False(project.IsExpanded);
            Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task SelectSettings_UsesSemanticSelectionInsteadOfNavItemObject()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager();
            var preferences = CreatePreferencesWithProject();

            var chatCatalog = CreateChatSessionCatalog();
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);

            selectionStore.SetSelection(NavigationSelectionState.SettingsSelection);

            Assert.IsType<NavigationSelectionState.Settings>(navVm.CurrentSelection);
            Assert.True(navVm.IsSettingsSelected);
            Assert.Null(navVm.ProjectedControlSelectedItem);
            Assert.Null(navVm.ProjectedControlSelectedItem);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task TryGetProjectIdForSession_UsesSemanticSessionIndex()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();

            var chatCatalog = CreateChatSessionCatalog("session-1");
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);

            navVm.RebuildTree();

            Assert.Equal("project-1", navVm.TryGetProjectIdForSession("session-1"));
            Assert.Null(navVm.TryGetProjectIdForSession("missing-session"));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void RebuildTree_UsesCatalogSnapshotAsSingleReadSource()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = new Mock<ISessionManager>(MockBehavior.Strict);
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");
            var ui = new Mock<IUiInteractionService>();
            var shellNavigation = new Mock<IShellNavigationService>();
            var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
            var metricsSink = new Mock<IShellLayoutMetricsSink>();
            var presenter = new ConversationCatalogPresenter();
            presenter.SetLoading(false);
            presenter.Refresh(
            [
                new ConversationCatalogItem(
                    "session-1",
                    "Catalog Session",
                    @"C:\repo\demo",
                    new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc))
            ]);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                new StubNavigationCoordinator(),
                navLogger.Object,
                navState,
                metricsSink.Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                presenter,
                new ProjectAffinityResolver());

            navVm.RebuildTree();

            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            var session = Assert.Single(project.Children.OfType<SessionNavItemViewModel>());
            Assert.Equal("Catalog Session", session.Title);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void RebuildTree_MultipleRapidCalls_CoalesceToSinglePostedWorkItem()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new QueuedSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();

            var chatCatalog = CreateChatSessionCatalog("session-1");
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);

            var baselinePending = syncContext.PendingPostCount;
            navVm.RebuildTree();
            navVm.RebuildTree();
            navVm.RebuildTree();

            Assert.Equal(baselinePending + 1, syncContext.PendingPostCount);

            syncContext.DrainAll();

            Assert.Equal(0, syncContext.PendingPostCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void RebuildTree_KeepsLastUpdatedOrderingWhenOnlyAccessTimesChange()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(
                new Session("session-new", @"C:\repo\demo"),
                new Session("session-old", @"C:\repo\demo"));
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-new", "session-old");

            var presenter = new ConversationCatalogPresenter();
            presenter.SetLoading(false);
            var oldUpdated = new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc);
            var newUpdated = new DateTime(2026, 3, 1, 0, 3, 0, DateTimeKind.Utc);

            var snapshot = new[]
            {
                new ConversationCatalogItem(
                    "session-old",
                    "Old Session",
                    @"C:\repo\demo",
                    new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    oldUpdated,
                    oldUpdated),
                new ConversationCatalogItem(
                    "session-new",
                    "New Session",
                    @"C:\repo\demo",
                    new DateTime(2026, 3, 1, 0, 2, 0, DateTimeKind.Utc),
                    newUpdated,
                    newUpdated)
            };
            presenter.Refresh(snapshot);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                new Mock<IUiInteractionService>().Object,
                new StubNavigationCoordinator(),
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                presenter,
                new ProjectAffinityResolver());

            navVm.RebuildTree();

            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            var orderedBeforeAccess = project.Children
                .OfType<SessionNavItemViewModel>()
                .Select(child => child.SessionId)
                .ToArray();
            Assert.Equal(new[] { "session-new", "session-old" }, orderedBeforeAccess);

            var accessedSnapshot = new[]
            {
                new ConversationCatalogItem(
                    "session-old",
                    "Old Session",
                    @"C:\repo\demo",
                    new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    oldUpdated,
                    new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)),
                snapshot[1]
            };
            presenter.Refresh(accessedSnapshot);
            navVm.RebuildTree();

            var orderedAfterAccess = project.Children
                .OfType<SessionNavItemViewModel>()
                .Select(child => child.SessionId)
                .ToArray();
            Assert.Equal(new[] { "session-new", "session-old" }, orderedAfterAccess);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void RebuildTree_GroupsRemoteConversationByResolverOutput()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(new Session("session-remote", "/remote/worktrees/demo/feature")
            {
                DisplayName = "Remote Session"
            });
            var preferences = CreatePreferencesWithProject();
            preferences.ProjectPathMappings.Add(new ProjectPathMapping
            {
                ProfileId = "profile-1",
                RemoteRootPath = "/remote/worktrees",
                LocalRootPath = @"C:\repo"
            });

            var chatCatalog = CreateChatSessionCatalog("session-remote");
            var presenter = new ConversationCatalogPresenter();
            presenter.SetLoading(false);
            presenter.Refresh(
            [
                new ConversationCatalogItem(
                    "session-remote",
                    "Remote Session",
                    "/remote/worktrees/demo/feature",
                    new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                    RemoteSessionId: "remote-1",
                    BoundProfileId: "profile-1",
                    ProjectAffinityOverrideProjectId: null)
            ]);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                new Mock<IUiInteractionService>().Object,
                new StubNavigationCoordinator(),
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                presenter,
                new ProjectAffinityResolver());

            navVm.RebuildTree();

            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            var session = Assert.Single(project.Children.OfType<SessionNavItemViewModel>());
            Assert.Equal("session-remote", session.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ShowAllSessionsForProjectAsync_UsesLastUpdatedOrderingForDialogItems()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-new", "session-old");
            var ui = new Mock<IUiInteractionService>();
            IReadOnlyList<SessionNavItemViewModel>? capturedSessions = null;
            ui.Setup(service => service.ShowSessionsListDialogAsync(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<SessionNavItemViewModel>>(),
                    It.IsAny<Action<string>>()))
                .Callback<string, IReadOnlyList<SessionNavItemViewModel>, Action<string>>((_, sessions, _) => capturedSessions = sessions)
                .Returns(Task.CompletedTask);

            var presenter = new ConversationCatalogPresenter();
            presenter.SetLoading(false);
            presenter.Refresh(
            [
                new ConversationCatalogItem(
                    "session-old",
                    "Old Session",
                    @"C:\repo\demo",
                    new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc)),
                new ConversationCatalogItem(
                    "session-new",
                    "New Session",
                    @"C:\repo\demo",
                    new DateTime(2026, 3, 1, 0, 2, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 1, 0, 3, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 1, 0, 3, 0, DateTimeKind.Utc))
            ]);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                new StubNavigationCoordinator(),
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                presenter,
                new ProjectAffinityResolver());

            await navVm.ShowAllSessionsForProjectAsync("project-1");

            Assert.NotNull(capturedSessions);
            Assert.Equal(
                new[] { "session-new", "session-old" },
                capturedSessions!.Select(session => session.SessionId).ToArray());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void ApplySelectionProjection_DoesNotOverride_InjectedProjectorOutput()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(false);

            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");
            var presenter = new ConversationCatalogPresenter();
            presenter.SetLoading(false);
            presenter.Refresh(CreateSnapshot(chatCatalog.GetKnownConversationIds()));

            var selectionStore = new ShellSelectionStateStore();
            var runtimeState = new ShellNavigationRuntimeStateStore();
            var sentinelItem = new DiscoverSessionsNavItemViewModel(navState);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                new Mock<IUiInteractionService>().Object,
                new StubNavigationCoordinator(),
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new StubNavigationSelectionProjector(new NavigationViewProjection(
                    ControlSelectedItem: sentinelItem,
                    IsSettingsSelected: false,
                    ActiveProjectIds: new HashSet<string>(StringComparer.Ordinal),
                    SelectedSessionIds: new HashSet<string>(StringComparer.Ordinal))),
                selectionStore,
                runtimeState,
                presenter,
                new ProjectAffinityResolver());

            navVm.RebuildTree();
            SetSessionSelection(selectionStore, "session-1");

            Assert.Same(sentinelItem, navVm.ProjectedControlSelectedItem);
            Assert.Same(sentinelItem, navVm.ProjectedControlSelectedItem);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void SessionActivationPreview_ProjectsDesiredSession_WithoutCommittingSemanticSelection()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");
            var presenter = new ConversationCatalogPresenter();
            presenter.SetLoading(false);
            presenter.Refresh(CreateSnapshot(chatCatalog.GetKnownConversationIds()));
            var selectionStore = new ShellSelectionStateStore();
            var runtimeState = new ShellNavigationRuntimeStateStore();

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                new Mock<IUiInteractionService>().Object,
                new StubNavigationCoordinator(),
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                selectionStore,
                runtimeState,
                presenter,
                new ProjectAffinityResolver());

            navVm.RebuildTree();

            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);

            runtimeState.DesiredSessionId = "session-1";
            runtimeState.IsSessionActivationInProgress = true;

            var previewSession = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", previewSession.SessionId);
            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);

            runtimeState.IsSessionActivationInProgress = false;
            runtimeState.DesiredSessionId = null;

            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void SessionActivationPreview_FallsBackToSemanticSelection_WhenDesiredSessionIsNotInNavigationTree()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");
            var presenter = new ConversationCatalogPresenter();
            presenter.SetLoading(false);
            presenter.Refresh(CreateSnapshot(chatCatalog.GetKnownConversationIds()));
            var selectionStore = new ShellSelectionStateStore();
            var runtimeState = new ShellNavigationRuntimeStateStore();

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                new Mock<IUiInteractionService>().Object,
                new StubNavigationCoordinator(),
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                selectionStore,
                runtimeState,
                presenter,
                new ProjectAffinityResolver());

            navVm.RebuildTree();

            runtimeState.DesiredSessionId = "missing-session";
            runtimeState.IsSessionActivationInProgress = false;

            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void PaneStateChange_DoesNotAlterProjectedSelection()
    {
        // When the pane closes (e.g. during a display-mode transition from Expanded
        // to Compact), the projected SelectedItem must remain the leaf session.
        // NavigationView's native ancestor visual (IsChildSelected) depends on
        // SelectedItem staying on the leaf — any re-push or change would disrupt it.
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);
            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");

            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);
            navVm.RebuildTree();
            SetSessionSelection(selectionStore, "session-1");

            var projectedBefore = navVm.ProjectedControlSelectedItem;
            Assert.NotNull(projectedBefore);
            Assert.IsType<SessionNavItemViewModel>(projectedBefore);

            var projectedNotifies = 0;
            navVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainNavigationViewModel.ProjectedControlSelectedItem))
                {
                    projectedNotifies++;
                }
            };

            // Simulate pane closing (as happens during Expanded → Compact transition).
            navState.SetPaneOpen(false);

            // ProjectedControlSelectedItem must NOT have changed or re-fired.
            // Pane state changes must not touch selection projection at all —
            // NavigationView handles ancestor visuals natively.
            Assert.Equal(0, projectedNotifies);
            Assert.Same(projectedBefore, navVm.ProjectedControlSelectedItem);

            // Semantic selection must remain unchanged.
            var selectedSession = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-1", selectedSession.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    /// <summary>
    /// Smoke test: simulates the full Expanded → Compact → Expanded lifecycle.
    /// Verifies that at every step, ProjectedControlSelectedItem stays on the leaf
    /// session and no spurious PropertyChanged notifications fire. This is the
    /// necessary condition for NavigationView's native ancestor visual to work.
    /// </summary>
    [Fact]
    public void Smoke_ExpandedCompactExpandedCycle_ProjectedSelectionNeverDrifts()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);
            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");

            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);
            navVm.RebuildTree();
            SetSessionSelection(selectionStore, "session-1");

            var projectedAtStart = navVm.ProjectedControlSelectedItem;
            Assert.IsType<SessionNavItemViewModel>(projectedAtStart);
            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");

            var projectedNotifies = 0;
            navVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainNavigationViewModel.ProjectedControlSelectedItem))
                {
                    projectedNotifies++;
                }
            };

            // Phase 1: Expanded → Compact (pane closes)
            navState.SetPaneOpen(false);

            Assert.Equal(0, projectedNotifies);
            Assert.Same(projectedAtStart, navVm.ProjectedControlSelectedItem);
            Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.True(project.IsActiveDescendant);

            // Phase 2: stay in Compact, toggle pane open/close (overlay)
            navState.SetPaneOpen(true);
            Assert.Equal(0, projectedNotifies);
            Assert.Same(projectedAtStart, navVm.ProjectedControlSelectedItem);

            navState.SetPaneOpen(false);
            Assert.Equal(0, projectedNotifies);
            Assert.Same(projectedAtStart, navVm.ProjectedControlSelectedItem);

            // Phase 3: Compact → Expanded (pane opens)
            navState.SetPaneOpen(true);

            Assert.Equal(0, projectedNotifies);
            Assert.Same(projectedAtStart, navVm.ProjectedControlSelectedItem);
            Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.True(project.IsActiveDescendant);

            // Final: the exact same object reference throughout
            var sessionVm = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", sessionVm.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void RebuildTree_DoesNotPushNullProjection_WhenSessionIsTemporarilyAbsentFromIndex()
    {
        // During RebuildTreeCore, _sessionIndex is cleared and rebuilt. If any
        // callback triggers ApplySelectionProjection in that window, the projector
        // can't find the session and would return ControlSelectedItem=null.
        // This null must NOT be pushed to the binding — it causes NavigationView
        // to lose IsChildSelected on the ancestor item during display-mode transitions.
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);
            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");

            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out var selectionStore);
            navVm.RebuildTree();
            SetSessionSelection(selectionStore, "session-1");

            var projectedBefore = navVm.ProjectedControlSelectedItem;
            Assert.IsType<SessionNavItemViewModel>(projectedBefore);

            var sawNull = false;
            navVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainNavigationViewModel.ProjectedControlSelectedItem)
                    && navVm.ProjectedControlSelectedItem is null)
                {
                    sawNull = true;
                }
            };

            // Trigger a rebuild — this clears and rebuilds the session index.
            // During this process, ProjectedControlSelectedItem must never become null.
            navVm.RebuildTree();

            Assert.False(sawNull, "ProjectedControlSelectedItem was pushed as null during RebuildTree. " +
                "This causes NavigationView to lose IsChildSelected on the ancestor item.");
            Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void DesiredSessionPreview_DoesNotKeepStartAsLogicalSelection()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);

            var sessionManager = CreateSessionManager(new Session("session-1", @"C:\repo\demo")
            {
                DisplayName = "Session 1"
            });
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");

            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                sessionManager.Object,
                preferences,
                navState,
                out var selectionStore,
                out var runtimeState);

            navVm.RebuildTree();
            selectionStore.SetSelection(NavigationSelectionState.StartSelection);
            runtimeState.DesiredSessionId = "session-1";
            runtimeState.IsSessionActivationInProgress = true;
            navVm.RefreshSelectionProjection();

            Assert.False(navVm.StartItem.IsLogicallySelected);
            var projectedSession = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", projectedSession.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task PrepareStartForProjectAsync_UsesCoordinatorAndStoresPendingProjectRoot_OnSuccess()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateStartAsync("project-1"))
                .ReturnsAsync(true);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                new Mock<IUiInteractionService>().Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver());

            await navVm.PrepareStartForProjectAsync("project-1");

            Assert.Equal(@"C:\repo\demo", navVm.ConsumePendingProjectRootPath());
            navigationCoordinator.Verify(coordinator => coordinator.ActivateStartAsync("project-1"), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task PrepareStartForProjectAsync_DoesNotStorePendingProjectRoot_WhenCoordinatorFails()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateStartAsync("project-1"))
                .ReturnsAsync(false);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                new Mock<IUiInteractionService>().Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver());

            await navVm.PrepareStartForProjectAsync("project-1");

            Assert.Null(navVm.ConsumePendingProjectRootPath());
            navigationCoordinator.Verify(coordinator => coordinator.ActivateStartAsync("project-1"), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static MainNavigationViewModel CreateNavigationViewModel(
        IConversationCatalog chatCatalog,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        FakeNavigationPaneState navState,
        out ShellSelectionStateStore selectionStore)
    {
        return CreateNavigationViewModel(
            chatCatalog,
            sessionManager,
            preferences,
            navState,
            out selectionStore,
            out _);
    }

    private static MainNavigationViewModel CreateNavigationViewModel(
        IConversationCatalog chatCatalog,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        FakeNavigationPaneState navState,
        out ShellSelectionStateStore selectionStore,
        out ShellNavigationRuntimeStateStore runtimeState)
    {
        var ui = new Mock<IUiInteractionService>();
        var navigationCoordinator = new StubNavigationCoordinator();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();
        var presenter = CreatePresenter(chatCatalog);
        selectionStore = new ShellSelectionStateStore();
        runtimeState = new ShellNavigationRuntimeStateStore();

        return new MainNavigationViewModel(
            chatCatalog,
            CreateProjectPreferences(preferences),
            ui.Object,
            navigationCoordinator,
            navLogger.Object,
            navState,
            metricsSink.Object,
            new NavigationSelectionProjector(),
            selectionStore,
            runtimeState,
            presenter,
            new ProjectAffinityResolver());
    }

    private static ConversationCatalogPresenter CreatePresenter(IConversationCatalog chatCatalog)
    {
        var presenter = new ConversationCatalogPresenter();
        presenter.SetLoading(false);
        presenter.Refresh(CreateSnapshot(chatCatalog.GetKnownConversationIds()));
        return presenter;
    }

    private static void SetSessionSelection(ShellSelectionStateStore selectionStore, string sessionId)
        => selectionStore.SetSelection(new NavigationSelectionState.Session(sessionId));

    private static Mock<ISessionManager> CreateSessionManager(params Session[] sessions)
    {
        var sessionManager = new Mock<ISessionManager>();
        foreach (var session in sessions)
        {
            sessionManager.Setup(s => s.GetSession(session.SessionId)).Returns(session);
        }

        return sessionManager;
    }

    private sealed class FakeNavigationPaneState : INavigationPaneState
    {
        public bool IsPaneOpen { get; private set; }
        public event EventHandler? PaneStateChanged;

        public void SetPaneOpen(bool isOpen)
        {
            if (IsPaneOpen == isOpen)
            {
                return;
            }

            IsPaneOpen = isOpen;
            PaneStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int PendingPostCount
        {
            get
            {
                lock (_queue)
                {
                    return _queue.Count;
                }
            }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue)
            {
                _queue.Enqueue((d, state));
            }
        }

        public void DrainAll(int maxIterations = 64)
        {
            for (var i = 0; i < maxIterations; i++)
            {
                (SendOrPostCallback Callback, object? State) workItem;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                    {
                        return;
                    }

                    workItem = _queue.Dequeue();
                }

                workItem.Callback(workItem.State);
            }

            throw new InvalidOperationException("SynchronizationContext queue did not drain within the expected iteration budget.");
        }
    }

    private static FakeChatSessionCatalog CreateChatSessionCatalog(params string[] conversationIds)
        => new(conversationIds);

    private static IReadOnlyList<ConversationCatalogItem> CreateSnapshot(IEnumerable<string> conversationIds)
    {
        var now = DateTime.UtcNow;
        return conversationIds.Select(id => new ConversationCatalogItem(
            id,
            id,
            @"C:\repo\demo",
            now,
            now,
            now)).ToArray();
    }

    private static INavigationProjectPreferences CreateProjectPreferences(AppPreferencesViewModel preferences)
        => new NavigationProjectPreferencesAdapter(preferences);

    private static AppPreferencesViewModel CreatePreferencesWithProject()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        var preferences = new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object);

        preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = "project-1",
            Name = "Demo",
            RootPath = @"C:\repo\demo"
        });

        return preferences;
    }

    private sealed class FakeChatSessionCatalog : IConversationCatalog
    {
        private readonly List<string> _conversationIds;

        public FakeChatSessionCatalog(params string[] conversationIds)
        {
            _conversationIds = new List<string>(conversationIds);
        }

        public bool IsConversationListLoading { get; set; }

        public int ConversationListVersion { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string[] GetKnownConversationIds() => _conversationIds.ToArray();

        public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void RenameConversation(string conversationId, string newName)
        {
        }

        public Task<ConversationMutationResult> ArchiveConversationAsync(string conversationId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ConversationMutationResult(true, false, null));

        public Task<ConversationMutationResult> DeleteConversationAsync(string conversationId, CancellationToken cancellationToken = default)
            => Task.FromResult(new ConversationMutationResult(true, false, null));

        public IReadOnlyList<ConversationProjectTargetOption> GetConversationProjectTargets()
            => [new(NavigationProjectIds.Unclassified, "未归类")];

        public void MoveConversationToProject(string conversationId, string projectId)
        {
        }

        public void RaiseConversationListChanged()
        {
            ConversationListVersion++;
            RaisePropertyChanged(nameof(ConversationListVersion));
        }

        public void RaisePropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class StubNavigationCoordinator : INavigationCoordinator
    {
        public Task<bool> ActivateStartAsync(string? projectIdForNewSession = null) => Task.FromResult(true);

        public Task ActivateDiscoverSessionsAsync() => Task.CompletedTask;

        public Task ActivateSettingsAsync(string settingsKey) => Task.CompletedTask;

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId) => Task.FromResult(false);

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
        }

    }

    private sealed class StubNavigationSelectionProjector : INavigationSelectionProjector
    {
        private readonly NavigationViewProjection _projection;

        public StubNavigationSelectionProjector(NavigationViewProjection projection)
        {
            _projection = projection;
        }

        public NavigationViewProjection Project(
            NavigationSelectionState selection,
            StartNavItemViewModel startItem,
            DiscoverSessionsNavItemViewModel discoverSessionsItem,
            IReadOnlyDictionary<string, SessionNavItemViewModel> sessionIndex,
            IReadOnlyDictionary<string, ProjectNavItemViewModel> projectIndex)
            => _projection;
    }
}



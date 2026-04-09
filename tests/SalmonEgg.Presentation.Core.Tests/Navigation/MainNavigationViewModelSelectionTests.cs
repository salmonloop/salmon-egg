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
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState);

            navVm.RebuildTree();
            navVm.SelectSession("session-1");

            Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.IsType<SessionNavItemViewModel>(navVm.SelectedItem);

            navState.SetPaneOpen(false);
            navState.SetPaneOpen(true);

            var selectedSession = Assert.IsType<SessionNavItemViewModel>(navVm.SelectedItem);
            Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", selectedSession.SessionId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void PaneStateChange_ReassertsSelectedItemNotification_ForSessionSelection()
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

            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState);
            navVm.RebuildTree();
            navVm.SelectSession("session-1");

            var selectedItemChanges = 0;
            navVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainNavigationViewModel.SelectedItem))
                {
                    selectedItemChanges++;
                }
            };

            navState.SetPaneOpen(false);

            Assert.True(selectedItemChanges > 0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void RefreshSelectionProjection_ReassertsSelectedItemNotification_ForSessionSelection()
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

            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState);
            navVm.RebuildTree();
            navVm.SelectSession("session-1");

            var selectedItemChanges = 0;
            navVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainNavigationViewModel.SelectedItem))
                {
                    selectedItemChanges++;
                }
            };

            navVm.RefreshSelectionProjection();

            Assert.True(selectedItemChanges > 0);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void PaneOpenToClosed_ReprojectsControlSelection_FromSessionToProject()
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

            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState);
            navVm.RebuildTree();
            navVm.SelectSession("session-1");

            Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);

            navState.SetPaneOpen(false);

            var projectedProject = Assert.IsType<ProjectNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("project-1", projectedProject.ProjectId);
            var selectedProject = Assert.IsType<ProjectNavItemViewModel>(navVm.SelectedItem);
            Assert.Equal("project-1", selectedProject.ProjectId);
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
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState);

            navVm.RebuildTree();
            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            navVm.SelectSession("session-1");

            Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.True(project.IsActiveDescendant);
            Assert.True(project.HasActiveDescendantIndicator);
            var projectedProject = Assert.IsType<ProjectNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("project-1", projectedProject.ProjectId);

            var selectedProjectItem = Assert.IsType<ProjectNavItemViewModel>(navVm.SelectedItem);
            Assert.Equal("project-1", selectedProjectItem.ProjectId);
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
    public async Task ActiveDescendantIndicator_OnlyShowsWhenPaneIsClosed()
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
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState);

            navVm.RebuildTree();
            navVm.SelectSession("session-1");

            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            Assert.True(project.IsActiveDescendant);
            Assert.True(project.HasActiveDescendantIndicator);

            navState.SetPaneOpen(true);

            Assert.True(project.IsActiveDescendant);
            Assert.False(project.HasActiveDescendantIndicator);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task PaneClosed_ProjectsSelectionToProject()
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
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState);

            navVm.RebuildTree();
            navVm.SelectSession("session-1");

            var projectedProject = Assert.IsType<ProjectNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("project-1", projectedProject.ProjectId);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task PaneClosed_TogglingProjectExpansion_DoesNotChangeProjectProjection()
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
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState);

            navVm.RebuildTree();
            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            project.IsExpanded = false;

            navVm.SelectSession("session-1");
            var projectedBefore = Assert.IsType<ProjectNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("project-1", projectedBefore.ProjectId);

            project.IsExpanded = true;
            var projectedAfter = Assert.IsType<ProjectNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("project-1", projectedAfter.ProjectId);
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
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState);

            navVm.RebuildTree();
            navVm.SelectSession("session-1");

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
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState);

            navVm.SelectSettings();

            Assert.IsType<NavigationSelectionState.Settings>(navVm.CurrentSelection);
            Assert.True(navVm.IsSettingsSelected);
            Assert.Null(navVm.SelectedItem);
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
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState);

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
                shellNavigation.Object,
                new StubNavigationCoordinator(),
                navLogger.Object,
                navState,
                metricsSink.Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
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
            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState);

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
                new Mock<IShellNavigationService>().Object,
                new StubNavigationCoordinator(),
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
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
                new Mock<IShellNavigationService>().Object,
                new StubNavigationCoordinator(),
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
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
                new Mock<IShellNavigationService>().Object,
                new StubNavigationCoordinator(),
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
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

    private static MainNavigationViewModel CreateNavigationViewModel(
        IConversationCatalog chatCatalog,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        FakeNavigationPaneState navState)
    {
        var ui = new Mock<IUiInteractionService>();
        var shellNavigation = new Mock<IShellNavigationService>();
        var navigationCoordinator = new StubNavigationCoordinator();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();
        var presenter = new ConversationCatalogPresenter();
        presenter.SetLoading(false);
        presenter.Refresh(CreateSnapshot(chatCatalog.GetKnownConversationIds()));

        return new MainNavigationViewModel(
            chatCatalog,
            CreateProjectPreferences(preferences),
            ui.Object,
            shellNavigation.Object,
            navigationCoordinator,
            navLogger.Object,
            navState,
            metricsSink.Object,
            new NavigationSelectionProjector(),
            new ShellSelectionStateStore(),
            presenter,
            new ProjectAffinityResolver());
    }

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
        public Task ActivateStartAsync() => Task.CompletedTask;

        public Task ActivateDiscoverSessionsAsync() => Task.CompletedTask;

        public Task ActivateSettingsAsync(string settingsKey) => Task.CompletedTask;

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId) => Task.FromResult(false);

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
        }
    }
}

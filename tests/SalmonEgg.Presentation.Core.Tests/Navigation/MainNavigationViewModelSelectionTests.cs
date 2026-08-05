using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;
using SalmonEgg.Presentation.Core.Tests.Localization;

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
    public void RebuildTree_DoesNotRepublishSelectedItemBinding_WhenNativeMenuSourceStaysStable()
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

            var projectedBefore = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            var selectedItemChanges = 0;
            navVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainNavigationViewModel.ProjectedControlSelectedItem))
                {
                    selectedItemChanges++;
                }
            };

            navVm.RebuildTree();

            Assert.Equal(0, selectedItemChanges);
            Assert.Same(projectedBefore, navVm.ProjectedControlSelectedItem);
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
            var projectedSession = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", projectedSession.SessionId);
            var selectedSession = Assert.Single(project.Children.OfType<SessionNavItemViewModel>(), s => s.SessionId == "session-1");
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

            navState.SetPaneOpen(true);

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
    public void RebuildTree_KeepsAddProjectPinnedBeforeProjects()
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

            using var navVm = CreateNavigationViewModel(chatCatalog, sessionManager.Object, preferences, navState, out _);

            navVm.RebuildTree();
            navVm.RebuildTree();

            Assert.True(navVm.Items.Count >= 4);
            Assert.Same(navVm.StartItem, navVm.Items[0]);
            Assert.Same(navVm.SessionsLabelItem, navVm.Items[1]);
            Assert.Same(navVm.AddProjectItem, navVm.Items[2]);
            Assert.IsType<ProjectNavItemViewModel>(navVm.Items[3]);
            Assert.DoesNotContain(navVm.FooterItems, item => ReferenceEquals(item, navVm.AddProjectItem));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task RebuildTree_UpdatesStableNativeMenuSourceCollectionsInPlace()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager(
                new Session("session-1", @"C:\repo\demo")
                {
                    DisplayName = "Session 1"
                },
                new Session("session-2", @"C:\repo\demo")
                {
                    DisplayName = "Session 2"
                });
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1", "session-2");
            var presenter = CreatePresenter(chatCatalog);

            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                sessionManager.Object,
                preferences,
                navState,
                out _,
                out _,
                presenter);
            navVm.RebuildTree();

            var nativeMenuSource = navVm.Items;
            var nativeFooterSource = navVm.FooterItems;
            var project = Assert.Single(nativeMenuSource.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            var nativeChildrenSource = project.Children;
            Assert.Same(project, navVm.Items.OfType<ProjectNavItemViewModel>().Single(p => p.ProjectId == "project-1"));
            Assert.Equal(["session-1", "session-2"], nativeChildrenSource.OfType<SessionNavItemViewModel>().Select(item => item.SessionId).ToArray());

            presenter.Refresh(CreateSnapshot(["session-1"]));

            Assert.Same(nativeMenuSource, navVm.Items);
            Assert.Same(nativeFooterSource, navVm.FooterItems);
            Assert.Same(nativeChildrenSource, project.Children);
            Assert.Contains(project, nativeMenuSource);
            Assert.Same(project, navVm.Items.OfType<ProjectNavItemViewModel>().Single(p => p.ProjectId == "project-1"));
            Assert.Equal(["session-1"], project.Children.OfType<SessionNavItemViewModel>().Select(item => item.SessionId).ToArray());

            navVm.Dispose();

            Assert.Empty(navVm.Items);
            Assert.Empty(navVm.FooterItems);
            Assert.Empty(project.Children);
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
            Assert.IsType<SettingsNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.IsType<SettingsNavItemViewModel>(navVm.ProjectedControlSelectedItem);
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
    public void SessionSelection_MaterializesOverflowSessionBeforeProjectingNativeSelection()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);
            var preferences = CreatePreferencesWithProject();
            var sessionIds = Enumerable.Range(1, 25)
                .Select(index => $"session-{index:00}")
                .ToArray();
            var chatCatalog = CreateChatSessionCatalog(sessionIds);
            var presenter = new MutableConversationCatalogDisplayReadModel();
            presenter.SetLoading(false);
            var baseline = new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc);
            presenter.Refresh(sessionIds.Select((id, index) => new ConversationCatalogItem(
                id,
                $"Session {index + 1:00}",
                @"C:\repo\demo",
                baseline.AddMinutes(-index),
                baseline.AddMinutes(-index),
                baseline.AddMinutes(-index))));

            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                Mock.Of<ISessionManager>(),
                preferences,
                navState,
                out var selectionStore,
                out _,
                presenter);

            navVm.RebuildTree();
            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), item => item.ProjectId == "project-1");
            Assert.DoesNotContain(project.Children.OfType<SessionNavItemViewModel>(), item => item.SessionId == "session-25");

            SetSessionSelection(selectionStore, "session-25");

            var projected = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-25", projected.SessionId);
            AssertProjectedSelectionIsMaterializedInMenuSource(navVm);
            Assert.Contains(project.Children.OfType<SessionNavItemViewModel>(), item => item.SessionId == "session-25");
            var selection = Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Equal("session-25", selection.SessionId);
            var more = Assert.Single(project.Children.OfType<MoreSessionsNavItemViewModel>());
            Assert.Equal(4, more.Count);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void ActiveSessionActivationPreview_MaterializesOverflowSessionBeforeSemanticCommit()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);
            var preferences = CreatePreferencesWithProject();
            var sessionIds = Enumerable.Range(1, 25)
                .Select(index => $"session-{index:00}")
                .ToArray();
            var chatCatalog = CreateChatSessionCatalog(sessionIds);
            var presenter = new MutableConversationCatalogDisplayReadModel();
            presenter.SetLoading(false);
            var baseline = new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc);
            presenter.Refresh(sessionIds.Select((id, index) => new ConversationCatalogItem(
                id,
                $"Session {index + 1:00}",
                @"C:\repo\demo",
                baseline.AddMinutes(-index),
                baseline.AddMinutes(-index),
                baseline.AddMinutes(-index))));

            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                Mock.Of<ISessionManager>(),
                preferences,
                navState,
                out _,
                out var runtimeState,
                presenter);

            navVm.RebuildTree();
            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), item => item.ProjectId == "project-1");
            Assert.DoesNotContain(project.Children.OfType<SessionNavItemViewModel>(), item => item.SessionId == "session-25");

            runtimeState.LatestActivationToken = 1;
            runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                "session-25",
                "project-1",
                Version: 1,
                SessionActivationPhase.SelectingConversation);

            var projected = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-25", projected.SessionId);
            AssertProjectedSelectionIsMaterializedInMenuSource(navVm);
            Assert.Contains(project.Children.OfType<SessionNavItemViewModel>(), item => item.SessionId == "session-25");
            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            var more = Assert.Single(project.Children.OfType<MoreSessionsNavItemViewModel>());
            Assert.Equal(4, more.Count);
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
            var presenter = new MutableConversationCatalogDisplayReadModel();
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
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                Mock.Of<IStringLocalizer<CoreStrings>>());

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
    public void AddLocalProjectCommand_WhenFolderPickerUnsupported_IsDisabled()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var ui = new Mock<IUiInteractionService>();
            ui.SetupGet(service => service.CanPickFolder).Returns(false);

            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                Mock.Of<ISessionManager>(),
                preferences,
                navState,
                out _,
                out _,
                uiOverride: ui.Object);

            Assert.False(navVm.CanAddProject);
            Assert.False(navVm.AddLocalProjectCommand.CanExecute(null));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task AddLocalProjectCommand_WhenFolderPickerSupported_AddsPickedProject()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var ui = new Mock<IUiInteractionService>();
            ui.SetupGet(service => service.CanPickFolder).Returns(true);
            var pickedPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"salmonegg-new-project-{Guid.NewGuid():N}");
            ui.Setup(service => service.PickFolderAsync()).ReturnsAsync(pickedPath);

            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                Mock.Of<ISessionManager>(),
                preferences,
                navState,
                out _,
                out _,
                uiOverride: ui.Object);

            await navVm.AddLocalProjectCommand.ExecuteAsync(null);

            Assert.Contains(preferences.Projects, project =>
                string.Equals(
                    project.RootPath,
                    NavTimeFormatter.NormalizePathForPrefixMatch(pickedPath).TrimEnd(System.IO.Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(project.Name, System.IO.Path.GetFileName(pickedPath), StringComparison.Ordinal));
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

            var presenter = new MutableConversationCatalogDisplayReadModel();
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
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                Mock.Of<IStringLocalizer<CoreStrings>>());

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
    public void RebuildTree_ReordersSessionWithSingleMove_SoNativeSelectionVisualIsNotStranded()
    {
        // The native NavigationView paints its selection (the gray mask + pill) on the realized
        // container for SelectedItem. If a reorder is expressed as Remove+Add, NavigationView
        // recycles a container onto a different data item and can strand the gray mask on the
        // vacated slot — that is the "several sessions masked at once" regression. A single Move
        // translates the existing container and carries its selection visual with it, so the
        // relocated session MUST raise exactly one Move (and zero Remove/Add) on Children.
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-new", "session-old");

            var presenter = new MutableConversationCatalogDisplayReadModel();
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
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                Mock.Of<IStringLocalizer<CoreStrings>>());

            navVm.RebuildTree();

            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            var orderedBefore = project.Children
                .OfType<SessionNavItemViewModel>()
                .Select(child => child.SessionId)
                .ToArray();
            Assert.Equal(new[] { "session-new", "session-old" }, orderedBefore);

            var moveCount = 0;
            var removeCount = 0;
            var addCount = 0;
            void OnChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
            {
                switch (e.Action)
                {
                    case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                        moveCount++;
                        break;
                    case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                        removeCount++;
                        break;
                    case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                        addCount++;
                        break;
                }
            }

            project.Children.CollectionChanged += OnChanged;
            try
            {
                // session-old now becomes the most recently updated -> it must move to the top.
                // Ordering keys off Updated (the 5th arg), not access time, so bump Updated past
                // newUpdated; see RebuildTree_KeepsLastUpdatedOrderingWhenOnlyAccessTimesChange.
                var reorderedUpdated = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);
                var reorderedSnapshot = new[]
                {
                    new ConversationCatalogItem(
                        "session-old",
                        "Old Session",
                        @"C:\repo\demo",
                        new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                        reorderedUpdated,
                        reorderedUpdated),
                    snapshot[1]
                };
                presenter.Refresh(reorderedSnapshot);
                navVm.RebuildTree();
            }
            finally
            {
                project.Children.CollectionChanged -= OnChanged;
            }

            var orderedAfter = project.Children
                .OfType<SessionNavItemViewModel>()
                .Select(child => child.SessionId)
                .ToArray();
            Assert.Equal(new[] { "session-old", "session-new" }, orderedAfter);

            Assert.Equal(1, moveCount);
            Assert.Equal(0, removeCount);
            Assert.Equal(0, addCount);
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
            var sessionManager = CreateSessionManager(new Session("session-remote", "/remote/worktrees")
            {
                DisplayName = "Remote Session"
            });
            var preferences = CreatePreferencesWithProject();
            preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = "dir-1",
                DisplayName = "Worktrees",
                RemotePath = "/remote/worktrees"
            });

            var chatCatalog = CreateChatSessionCatalog("session-remote");
            var presenter = new MutableConversationCatalogDisplayReadModel();
            presenter.SetLoading(false);
            presenter.Refresh(
            [
                new ConversationCatalogItem(
                    "session-remote",
                    "Remote Session",
                    "/remote/worktrees",
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
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                Mock.Of<IStringLocalizer<CoreStrings>>());

            navVm.RebuildTree();

            var unclassifiedProject = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == NavigationProjectIds.Unclassified);
            var session = Assert.Single(unclassifiedProject.Children.OfType<SessionNavItemViewModel>());
            Assert.Equal("session-remote", session.SessionId);
            Assert.Equal("remote-1", session.RemoteSessionId);
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

            var presenter = new MutableConversationCatalogDisplayReadModel();
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
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                Mock.Of<IStringLocalizer<CoreStrings>>());

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
    public async Task ShowAllSessionsForProjectAsync_WhenDialogThrows_SurfacesLocalizedInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");
            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.Setup(service => service.ShowSessionsListDialogAsync(
                    It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<SessionNavItemViewModel>>(),
                    It.IsAny<Action<string>>()))
                .ThrowsAsync(new InvalidOperationException("dialog host unavailable"));
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);

            var presenter = new MutableConversationCatalogDisplayReadModel();
            presenter.SetLoading(false);
            presenter.Refresh(
            [
                new ConversationCatalogItem(
                    "session-1",
                    "Session 1",
                    @"C:\repo\demo",
                    new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 1, 0, 1, 0, DateTimeKind.Utc),
                    new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc))
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
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());

            await navVm.ShowAllSessionsForProjectAsync("project-1");

            Assert.Equal(
                ["Failed to open the sessions list. Please try again later."],
                shownMessages);
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
            var presenter = new MutableConversationCatalogDisplayReadModel();
            presenter.SetLoading(false);
            presenter.Refresh(CreateSnapshot(chatCatalog.GetKnownConversationIds()));

            var selectionStore = new ShellSelectionStateStore();
            var runtimeState = new ShellNavigationRuntimeStateStore();
            var sentinelItem = new DiscoverSessionsNavItemViewModel(navState, new ImmediateUiDispatcher());

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
                    IsSettingsSelected: false)),
                selectionStore,
                runtimeState,
                presenter,
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                Mock.Of<IStringLocalizer<CoreStrings>>());

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
    public void SessionActivationPreview_ProjectsActiveSessionActivation_WithoutCommittingSemanticSelection()
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
            var presenter = new MutableConversationCatalogDisplayReadModel();
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
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                Mock.Of<IStringLocalizer<CoreStrings>>());

            navVm.RebuildTree();

            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);

            runtimeState.LatestActivationToken = 1;
            runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                "session-1",
                "project-1",
                Version: 1,
                SessionActivationPhase.NavigatingToChatShell);

            var projected = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", projected.SessionId);
            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);

            runtimeState.ActiveSessionActivation = null;

            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void SessionActivationPreview_FallsBackToSemanticSelection_WhenActiveSessionIsNotInNavigationTree()
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
            var presenter = new MutableConversationCatalogDisplayReadModel();
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
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                Mock.Of<IStringLocalizer<CoreStrings>>());

            navVm.RebuildTree();

            runtimeState.LatestActivationToken = 1;
            runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                "missing-session",
                "project-1",
                Version: 1,
                SessionActivationPhase.NavigatingToChatShell);

            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void SessionActivationPreview_IgnoresStaleActivationVersion()
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
            runtimeState.LatestActivationToken = 2;
            runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                "session-1",
                "project-1",
                Version: 1,
                SessionActivationPhase.NavigatingToChatShell);

            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void SessionActivationPreview_IgnoresSessionActivation_WhenPendingShellContentIsNotChat()
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
            runtimeState.LatestActivationToken = 1;
            runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                "session-1",
                "project-1",
                Version: 1,
                SessionActivationPhase.NavigatingToChatShell);

            Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);

            runtimeState.PendingShellContent = ShellNavigationContent.Start;

            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void SessionSelection_ForSessionNotInCatalog_ProjectsNothingRatherThanStart()
    {
        // Regression: a persisted/committed session selection whose session is not (yet)
        // in the catalog — e.g. deferred conversation restore on cold start — must project
        // to "nothing selected", NOT Start. Painting Start for the deferred session caused
        // the native NavigationView to briefly highlight Start and then the session, leaving
        // two items highlighted. Projecting null keeps exactly one (or zero) highlight.
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog("session-1");
            var presenter = new MutableConversationCatalogDisplayReadModel();
            presenter.SetLoading(false);
            presenter.Refresh(CreateSnapshot(chatCatalog.GetKnownConversationIds()));

            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                Mock.Of<ISessionManager>(),
                preferences,
                navState,
                out var selectionStore,
                out _,
                presenter);

            navVm.RebuildTree();
            SetSessionSelection(selectionStore, "missing-session");

            Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            Assert.Null(navVm.ProjectedControlSelectedItem);
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
    public void ActiveSessionActivationPreview_DoesNotKeepStartAsLogicalSelection()
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
            runtimeState.LatestActivationToken = 1;
            runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                "session-1",
                "project-1",
                Version: 1,
                SessionActivationPhase.SelectingConversation);
            navVm.RefreshSelectionProjection();

            var projected = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", projected.SessionId);
            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void FaultedSessionActivation_DoesNotProjectPreviewSelection()
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
            var projectedBefore = Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);

            var projectedSelectionChanged = 0;
            navVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainNavigationViewModel.ProjectedControlSelectedItem))
                {
                    projectedSelectionChanged++;
                }
            };

            runtimeState.LatestActivationToken = 1;
            runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                "session-1",
                "project-1",
                Version: 1,
                SessionActivationPhase.Faulted,
                "NavigationFailed");

            Assert.Same(projectedBefore, navVm.ProjectedControlSelectedItem);
            Assert.Equal(0, projectedSelectionChanged);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void ActiveSessionActivation_ProjectsImmediatelyWithoutSelectionInteractionGate()
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
            Assert.IsType<StartNavItemViewModel>(navVm.ProjectedControlSelectedItem);

            var projectedSelectionChanged = 0;
            navVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainNavigationViewModel.ProjectedControlSelectedItem))
                {
                    projectedSelectionChanged++;
                }
            };

            runtimeState.LatestActivationToken = 1;
            runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                "session-1",
                "project-1",
                Version: 1,
                SessionActivationPhase.NavigatingToChatShell);

            var projected = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", projected.SessionId);
            Assert.Equal(1, projectedSelectionChanged);
            Assert.Equal(NavigationSelectionState.StartSelection, navVm.CurrentSelection);
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
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                Mock.Of<IStringLocalizer<CoreStrings>>());

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
    public async Task PrepareStartForProjectAsync_UsesRemoteDirectoryCwdForPendingProject()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var sessionManager = CreateSessionManager();
            var preferences = CreatePreferencesWithProject();
            preferences.AgentRemoteDirectories.Add(new AgentRemoteDirectory
            {
                DirectoryId = "dir-alpha",
                DisplayName = "Remote Alpha",
                RemotePath = "/remote/alpha"
            });

            var remoteProjectId = ProjectSelectionCwdResolver.BuildRemoteDirectoryProjectId("dir-alpha");
            var chatCatalog = CreateChatSessionCatalog();
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateStartAsync(remoteProjectId))
                .ReturnsAsync(true);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                Mock.Of<IUiInteractionService>(),
                navigationCoordinator.Object,
                Mock.Of<ILogger<MainNavigationViewModel>>(),
                navState,
                Mock.Of<IShellLayoutMetricsSink>(),
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                Mock.Of<IStringLocalizer<CoreStrings>>());

            await navVm.PrepareStartForProjectAsync(remoteProjectId);

            Assert.Equal("/remote/alpha", navVm.ConsumePendingProjectRootPath());
            Assert.Null(navVm.PeekPendingProjectIdForNewSession());
            navigationCoordinator.Verify(coordinator => coordinator.ActivateStartAsync(remoteProjectId), Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void RefreshLocalizedText_ReevaluatesSingletonNavigationLabels()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);

            var sessions = Enumerable.Range(1, 21)
                .Select(i => new Session($"session-{i}", @"C:\repo\demo")
                {
                    DisplayName = $"Session {i}"
                })
                .ToArray();
            var sessionManager = CreateSessionManager(sessions);
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog(sessions.Select(session => session.SessionId).ToArray());
            var localizer = new MutableNavigationLocalizer
            {
                Start = "开始",
                Discover = "发现更多会话",
                Settings = "设置",
                Sessions = "会话",
                Unclassified = "未归类",
                MoreSessions = "展开显示（+{0}）"
            };
            var languageService = new Mock<IAppLanguageService>();

            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                sessionManager.Object,
                preferences,
                navState,
                out _,
                out _,
                localizerOverride: localizer,
                languageServiceOverride: languageService.Object);

            navVm.RebuildTree();
            var more = navVm.Items
                .OfType<ProjectNavItemViewModel>()
                .SelectMany(project => project.Children.OfType<MoreSessionsNavItemViewModel>())
                .Single();
            var unclassified = navVm.Items
                .OfType<ProjectNavItemViewModel>()
                .Single(project => project.ProjectId == MainNavigationViewModel.UnclassifiedProjectId);

            Assert.Equal("开始", navVm.StartItem.Title);
            Assert.Equal("发现更多会话", navVm.DiscoverSessionsItem.Title);
            Assert.Equal("设置", navVm.SettingsItem.Title);
            Assert.Equal("会话", navVm.SessionsLabelItem.Title);
            Assert.Equal("未归类", unclassified.Title);
            Assert.Equal("展开显示（+1）", more.Title);

            localizer.Start = "Start";
            localizer.Discover = "Discover sessions";
            localizer.Settings = "Settings";
            localizer.Sessions = "Sessions";
            localizer.Unclassified = "Unclassified";
            localizer.MoreSessions = "Show more (+{0})";

            languageService.Raise(service => service.LanguageChanged += null, EventArgs.Empty);

            Assert.Equal("Start", navVm.StartItem.Title);
            Assert.Equal("Discover sessions", navVm.DiscoverSessionsItem.Title);
            Assert.Equal("Settings", navVm.SettingsItem.Title);
            Assert.Equal("Sessions", navVm.SessionsLabelItem.Title);
            Assert.Equal("Unclassified", unclassified.Title);
            Assert.Equal("Show more (+1)", more.Title);
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
            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateStartAsync("project-1"))
                .ReturnsAsync(false);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());

            await navVm.PrepareStartForProjectAsync("project-1");

            Assert.Null(navVm.ConsumePendingProjectRootPath());
            navigationCoordinator.Verify(coordinator => coordinator.ActivateStartAsync("project-1"), Times.Once);
            Assert.Equal(
                ["Failed to open the start page. Please try again later."],
                shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task PrepareStartForProjectAsync_WhenCoordinatorThrows_SurfacesLocalizedInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateStartAsync("project-1"))
                .ThrowsAsync(new InvalidOperationException("start shell unavailable"));

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());

            await navVm.PrepareStartForProjectAsync("project-1");

            Assert.Null(navVm.ConsumePendingProjectRootPath());
            Assert.Equal(
                ["Failed to open the start page. Please try again later."],
                shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }


    [Fact]
    public async Task ActivateDiscoverSessionsAsync_WhenCoordinatorFails_SurfacesLocalizedInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateDiscoverSessionsAsync())
                .ReturnsAsync(false);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());

            var opened = await navVm.ActivateDiscoverSessionsAsync();

            Assert.False(opened);
            Assert.Equal(
                ["Failed to open Discover sessions. Please try again later."],
                shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateDiscoverSessionsAsync_WhenCoordinatorThrows_SurfacesLocalizedInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateDiscoverSessionsAsync())
                .ThrowsAsync(new InvalidOperationException("discover shell unavailable"));

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());

            var opened = await navVm.ActivateDiscoverSessionsAsync();

            Assert.False(opened);
            Assert.Equal(
                ["Failed to open Discover sessions. Please try again later."],
                shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateStartAsync_WhenCoordinatorFails_SurfacesLocalizedInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateStartAsync(null))
                .ReturnsAsync(false);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());

            var opened = await navVm.ActivateStartAsync();

            Assert.False(opened);
            Assert.Equal(
                ["Failed to open the start page. Please try again later."],
                shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateStartAsync_WhenCoordinatorThrows_SurfacesLocalizedInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateStartAsync(null))
                .ThrowsAsync(new InvalidOperationException("start shell unavailable"));

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());

            var opened = await navVm.ActivateStartAsync();

            Assert.False(opened);
            Assert.Equal(
                ["Failed to open the start page. Please try again later."],
                shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSettingsAsync_WhenCoordinatorFails_SurfacesLocalizedInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateSettingsAsync(SettingsSectionCatalog.GeneralKey))
                .ReturnsAsync(false);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());

            var opened = await navVm.ActivateSettingsAsync(SettingsSectionCatalog.GeneralKey);

            Assert.False(opened);
            Assert.Equal(
                ["Failed to open settings. Please try again later."],
                shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSettingsAsync_WhenCoordinatorThrows_SurfacesLocalizedInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateSettingsAsync(SettingsSectionCatalog.GeneralKey))
                .ThrowsAsync(new InvalidOperationException("settings shell unavailable"));

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());

            var opened = await navVm.ActivateSettingsAsync(SettingsSectionCatalog.GeneralKey);

            Assert.False(opened);
            Assert.Equal(
                ["Failed to open settings. Please try again later."],
                shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }



    [Fact]
    public async Task ActivateSessionAsync_WhenCoordinatorFailsBeforeSelectionCommit_SurfacesLocalizedInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateSessionAsync("session-1", "project-1"))
                .ReturnsAsync(false);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());

            var opened = await navVm.ActivateSessionAsync("session-1", "project-1");

            Assert.False(opened);
            Assert.Equal(
                ["Failed to open this session. Please try again later."],
                shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task ActivateSessionAsync_WhenSelectionAlreadyCommitted_DoesNotDuplicateCalloutWithInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            var chatCatalog = CreateChatSessionCatalog();
            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);
            var selectionStore = new ShellSelectionStateStore();
            selectionStore.SetSelection(new NavigationSelectionState.Session("session-1"));
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateSessionAsync("session-1", "project-1"))
                .ReturnsAsync(false);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                selectionStore,
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());

            var opened = await navVm.ActivateSessionAsync("session-1", "project-1");

            Assert.False(opened);
            Assert.Empty(shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task PrepareStartForProjectAsync_WhenOlderRequestFailsAfterNewerSuccess_PreservesLatestPendingProject()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            var preferences = CreatePreferencesWithProject();
            preferences.Projects.Add(new ProjectDefinition
            {
                ProjectId = "project-2",
                Name = "Second",
                RootPath = @"C:\repo\second"
            });
            var chatCatalog = CreateChatSessionCatalog();
            var firstActivation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var navigationCoordinator = new Mock<INavigationCoordinator>();
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateStartAsync("project-1"))
                .Returns(firstActivation.Task);
            navigationCoordinator
                .Setup(coordinator => coordinator.ActivateStartAsync("project-2"))
                .ReturnsAsync(true);

            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                CreateProjectPreferences(preferences),
                ui.Object,
                navigationCoordinator.Object,
                new Mock<ILogger<MainNavigationViewModel>>().Object,
                navState,
                new Mock<IShellLayoutMetricsSink>().Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                new ShellNavigationRuntimeStateStore(),
                CreatePresenter(chatCatalog),
                new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(),
                new TestCoreStringLocalizer());

            var staleTask = navVm.PrepareStartForProjectAsync("project-1");
            var latestTask = navVm.PrepareStartForProjectAsync("project-2");

            await latestTask;
            firstActivation.SetResult(false);
            await staleTask;

            Assert.Equal(@"C:\repo\second", navVm.ConsumePendingProjectRootPath());
            Assert.Empty(shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void Rebuild_WhenDisplaySnapshotContainsUnread_ProjectsUnreadToSessionRow()
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
            var presenter = new MutableConversationCatalogDisplayReadModel();
            presenter.SetLoading(false);
            presenter.Refresh(
            [
                new ConversationCatalogDisplayItem(
                    "session-1",
                    "Session 1",
                    @"C:\repo\demo",
                    new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc),
                    HasUnreadAttention: true)
            ]);

            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                sessionManager.Object,
                preferences,
                navState,
                out _,
                out _,
                presenter);

            navVm.RebuildTree();

            var session = navVm.Items
                .OfType<ProjectNavItemViewModel>()
                .SelectMany(project => project.Children.OfType<SessionNavItemViewModel>())
                .Single(item => item.SessionId == "session-1");

            Assert.True(session.HasUnreadAttention);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void Rebuild_WhenDisplaySnapshotClearsUnread_ClearsSessionRowUnread()
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
            var presenter = new MutableConversationCatalogDisplayReadModel();
            presenter.SetLoading(false);
            presenter.Refresh(
            [
                new ConversationCatalogDisplayItem(
                    "session-1",
                    "Session 1",
                    @"C:\repo\demo",
                    new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc),
                    HasUnreadAttention: true)
            ]);

            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                sessionManager.Object,
                preferences,
                navState,
                out _,
                out _,
                presenter);

            navVm.RebuildTree();
            presenter.Refresh(
            [
                new ConversationCatalogDisplayItem(
                    "session-1",
                    "Session 1",
                    @"C:\repo\demo",
                    new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc),
                    HasUnreadAttention: false)
            ]);

            var session = navVm.Items
                .OfType<ProjectNavItemViewModel>()
                .SelectMany(project => project.Children.OfType<SessionNavItemViewModel>())
                .Single(item => item.SessionId == "session-1");

            Assert.False(session.HasUnreadAttention);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task SelectRemoteProjectCommand_WhenManageRequestedAndSettingsActivationFails_SurfacesLocalizedInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);

            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.SetupGet(service => service.CanPickFolder).Returns(true);
            ui.Setup(service => service.ShowRemoteProjectSelectionAsync(It.IsAny<RemoteProjectSelectionViewModel>()))
                .ReturnsAsync(RemoteProjectSelectionResult.Manage);
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);

            var navigationCoordinator = new ControllableNavigationCoordinator
            {
                SettingsActivationResult = false,
            };

            var preferences = CreatePreferencesWithProject();
            var chatCatalog = new FakeChatSessionCatalog();
            var sessionManager = CreateSessionManager().Object;
            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                sessionManager,
                preferences,
                navState,
                out _,
                out _,
                uiOverride: ui.Object,
                localizerOverride: new TestCoreStringLocalizer(),
                navigationCoordinatorOverride: navigationCoordinator);

            await navVm.SelectRemoteProjectCommand.ExecuteAsync(null);

            Assert.Equal(SettingsSectionCatalog.AgentAcpKey, navigationCoordinator.LastSettingsKey);
            Assert.Equal(
                ["Failed to open settings. Please try again later."],
                shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public async Task SelectRemoteProjectCommand_WhenManageRequestedAndSettingsActivationThrows_SurfacesLocalizedInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);

            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.SetupGet(service => service.CanPickFolder).Returns(true);
            ui.Setup(service => service.ShowRemoteProjectSelectionAsync(It.IsAny<RemoteProjectSelectionViewModel>()))
                .ReturnsAsync(RemoteProjectSelectionResult.Manage);
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);

            var navigationCoordinator = new ControllableNavigationCoordinator
            {
                SettingsActivationException = new InvalidOperationException("settings shell unavailable"),
            };

            var preferences = CreatePreferencesWithProject();
            var chatCatalog = new FakeChatSessionCatalog();
            var sessionManager = CreateSessionManager().Object;
            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                sessionManager,
                preferences,
                navState,
                out _,
                out _,
                uiOverride: ui.Object,
                localizerOverride: new TestCoreStringLocalizer(),
                navigationCoordinatorOverride: navigationCoordinator);

            await navVm.SelectRemoteProjectCommand.ExecuteAsync(null);

            Assert.Equal(SettingsSectionCatalog.AgentAcpKey, navigationCoordinator.LastSettingsKey);
            Assert.Equal(
                ["Failed to open settings. Please try again later."],
                shownMessages);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }


    [Fact]
    public async Task AddLocalProjectCommand_WhenSelectionIsInvalid_SurfacesLocalizedInfo()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new ImmediateSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var navState = new FakeNavigationPaneState();
            navState.SetPaneOpen(true);

            var shownMessages = new List<string>();
            var ui = new Mock<IUiInteractionService>();
            ui.SetupGet(service => service.CanPickFolder).Returns(true);
            ui.Setup(service => service.PickFolderAsync())
                .ReturnsAsync(@"C:\repo\invalid-empty");
            ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
                .Callback<string>(shownMessages.Add)
                .Returns(Task.CompletedTask);

            var addProjectCoordinator = new Mock<IAddProjectCoordinator>();
            addProjectCoordinator
                .Setup(coordinator => coordinator.AddProject(It.IsAny<ProjectSourceSelection>()))
                .Returns(AddProjectOutcome.Invalid);

            var preferences = CreatePreferencesWithProject();
            var chatCatalog = new FakeChatSessionCatalog();
            var sessionManager = CreateSessionManager().Object;
            using var navVm = CreateNavigationViewModel(
                chatCatalog,
                sessionManager,
                preferences,
                navState,
                out _,
                out _,
                uiOverride: ui.Object,
                localizerOverride: new TestCoreStringLocalizer(),
                addProjectCoordinatorOverride: addProjectCoordinator.Object);

            await navVm.AddLocalProjectCommand.ExecuteAsync(null);

            Assert.Equal(
                ["That project selection is not valid. Please choose another folder or remote directory."],
                shownMessages);
            addProjectCoordinator.Verify(
                coordinator => coordinator.AddProject(It.IsAny<ProjectSourceSelection.LocalFolder>()),
                Times.Once);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    // --- Session order cadence ---
    // Reordering a rendered row makes Uno's ItemsRepeater recycle its realized container (Move is
    // decomposed into Remove+Add). The recycled container keeps NavigationView's selected flag and
    // the deselect of the previous item no-ops once that container is gone, so the selection mask
    // strands across rows. The pane therefore holds rendered rows while unsettled and converges to
    // recency order once quiet.

    [Fact]
    public void SessionOrder_WhenSettled_AppliesRecencyOrder()
    {
        RunWithImmediateContext(() =>
        {
            using var harness = CreateSessionOrderHarness("session-1", "session-2", "session-3");

            harness.PublishRecency("session-1", "session-2", "session-3");
            harness.Rebuild();
            Assert.Equal(["session-1", "session-2", "session-3"], harness.RenderedSessionIds());

            harness.PublishRecency("session-3", "session-1", "session-2");
            harness.Rebuild();
            Assert.Equal(["session-3", "session-1", "session-2"], harness.RenderedSessionIds());
        });
    }

    [Fact]
    public void SessionOrder_WhileActivationInFlight_HoldsRenderedRowsInPlace()
    {
        RunWithImmediateContext(() =>
        {
            using var harness = CreateSessionOrderHarness("session-1", "session-2", "session-3");

            harness.PublishRecency("session-1", "session-2", "session-3");
            harness.Rebuild();

            harness.BeginActivation("session-1");
            harness.PublishRecency("session-3", "session-2", "session-1");
            harness.Rebuild();

            Assert.Equal(["session-1", "session-2", "session-3"], harness.RenderedSessionIds());
        });
    }

    [Fact]
    public void SessionOrder_WhileConversationListLoading_HoldsRenderedRowsInPlace()
    {
        RunWithImmediateContext(() =>
        {
            using var harness = CreateSessionOrderHarness("session-1", "session-2", "session-3");

            harness.PublishRecency("session-1", "session-2", "session-3");
            harness.Rebuild();

            harness.SetLoading(true);
            harness.PublishRecency("session-3", "session-2", "session-1");
            harness.Rebuild();

            Assert.Equal(["session-1", "session-2", "session-3"], harness.RenderedSessionIds());
        });
    }

    [Fact]
    public void SessionOrder_AfterActivationSettles_ConvergesToRecencyOrder()
    {
        RunWithImmediateContext(() =>
        {
            using var harness = CreateSessionOrderHarness("session-1", "session-2", "session-3");

            harness.PublishRecency("session-1", "session-2", "session-3");
            harness.Rebuild();

            harness.BeginActivation("session-1");
            harness.PublishRecency("session-3", "session-2", "session-1");
            harness.Rebuild();
            Assert.Equal(["session-1", "session-2", "session-3"], harness.RenderedSessionIds());

            // The hold must not starve: a terminal activation lets the pending order apply.
            harness.CompleteActivation();
            harness.Rebuild();
            Assert.Equal(["session-3", "session-2", "session-1"], harness.RenderedSessionIds());
        });
    }

    [Fact]
    public void SessionOrder_WhileUnsettled_StillAdmitsNewSessions()
    {
        RunWithImmediateContext(() =>
        {
            using var harness = CreateSessionOrderHarness("session-1", "session-2", "session-3");

            harness.PublishRecency("session-1", "session-2");
            harness.Rebuild();
            Assert.Equal(["session-1", "session-2"], harness.RenderedSessionIds());

            harness.BeginActivation("session-1");
            // A newly catalogued session tops recency; it must still appear, appended after the
            // held rows rather than displacing them (an insert does not recycle its siblings).
            harness.PublishRecency("session-3", "session-1", "session-2");
            harness.Rebuild();

            Assert.Equal(["session-1", "session-2", "session-3"], harness.RenderedSessionIds());
        });
    }

    private static void RunWithImmediateContext(Action body)
    {
        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        try
        {
            body();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static SessionOrderHarness CreateSessionOrderHarness(params string[] sessionIds)
    {
        var navState = new FakeNavigationPaneState();
        navState.SetPaneOpen(true);
        var presenter = new MutableConversationCatalogDisplayReadModel();
        presenter.SetLoading(false);

        var navVm = CreateNavigationViewModel(
            CreateChatSessionCatalog(sessionIds),
            Mock.Of<ISessionManager>(),
            CreatePreferencesWithProject(),
            navState,
            out _,
            out var runtimeState,
            presenter);

        return new SessionOrderHarness(navVm, presenter, runtimeState);
    }

    private sealed class SessionOrderHarness(
        MainNavigationViewModel navVm,
        MutableConversationCatalogDisplayReadModel presenter,
        ShellNavigationRuntimeStateStore runtimeState) : IDisposable
    {
        private static readonly DateTime OrderBaseline = new(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc);
        private long _activationVersion;

        public void PublishRecency(params string[] sessionIdsMostRecentFirst)
            => presenter.Refresh(sessionIdsMostRecentFirst.Select((id, index) => new ConversationCatalogItem(
                id,
                id,
                @"C:\repo\demo",
                OrderBaseline.AddMinutes(-index),
                OrderBaseline.AddMinutes(-index),
                OrderBaseline.AddMinutes(-index))));

        public void SetLoading(bool value) => presenter.SetLoading(value);

        public void BeginActivation(string sessionId)
        {
            _activationVersion++;
            runtimeState.LatestActivationToken = _activationVersion;
            runtimeState.ActiveSessionActivationVersion = _activationVersion;
            runtimeState.IsSessionActivationInProgress = true;
            runtimeState.ActiveSessionActivation = new SessionActivationSnapshot(
                sessionId,
                "project-1",
                _activationVersion,
                SessionActivationPhase.SelectingConversation);
        }

        public void CompleteActivation()
        {
            var activation = runtimeState.ActiveSessionActivation;
            runtimeState.IsSessionActivationInProgress = false;
            runtimeState.ActiveSessionActivationVersion = 0;
            if (activation is not null)
            {
                runtimeState.ActiveSessionActivation = activation with
                {
                    Phase = SessionActivationPhase.Hydrated
                };
            }
        }

        public void Rebuild() => navVm.RebuildTree();

        public string[] RenderedSessionIds()
            => navVm.Items
                .OfType<ProjectNavItemViewModel>()
                .SelectMany(project => project.Children.OfType<SessionNavItemViewModel>())
                .Where(session => !session.IsPlaceholder)
                .Select(session => session.SessionId)
                .ToArray();

        public void Dispose() => navVm.Dispose();
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
        out ShellNavigationRuntimeStateStore runtimeState,
        IConversationCatalogDisplayReadModel? presenterOverride = null,
        IUiInteractionService? uiOverride = null,
        IStringLocalizer<CoreStrings>? localizerOverride = null,
        IAppLanguageService? languageServiceOverride = null,
        INavigationCoordinator? navigationCoordinatorOverride = null,
        IAddProjectCoordinator? addProjectCoordinatorOverride = null)
    {
        var ui = new Mock<IUiInteractionService>();
        ui.SetupGet(service => service.CanPickFolder).Returns(true);
        var navigationCoordinator = navigationCoordinatorOverride ?? new StubNavigationCoordinator();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();
        var presenter = presenterOverride ?? CreatePresenter(chatCatalog);
        selectionStore = new ShellSelectionStateStore();
        runtimeState = new ShellNavigationRuntimeStateStore();
        var uiDispatcher = SynchronizationContext.Current as IUiDispatcher ?? new ImmediateUiDispatcher();

        return new MainNavigationViewModel(
            chatCatalog,
            CreateProjectPreferences(preferences),
            uiOverride ?? ui.Object,
            navigationCoordinator,
            navLogger.Object,
            navState,
            metricsSink.Object,
            new NavigationSelectionProjector(),
            selectionStore,
            runtimeState,
            presenter,
            new ProjectAffinityResolver(),
            uiDispatcher,
            localizerOverride ?? new TestCoreStringLocalizer(),
            languageService: languageServiceOverride,
            addProjectCoordinator: addProjectCoordinatorOverride);
    }

    private static MutableConversationCatalogDisplayReadModel CreatePresenter(IConversationCatalog chatCatalog)
    {
        var presenter = new MutableConversationCatalogDisplayReadModel();
        presenter.SetLoading(false);
        presenter.Refresh(CreateSnapshot(chatCatalog.GetKnownConversationIds()));
        return presenter;
    }

    private static void SetSessionSelection(ShellSelectionStateStore selectionStore, string sessionId)
        => selectionStore.SetSelection(new NavigationSelectionState.Session(sessionId));

    private static void AssertProjectedSelectionIsMaterializedInMenuSource(MainNavigationViewModel navVm)
    {
        var selectedItem = navVm.ProjectedControlSelectedItem;
        Assert.NotNull(selectedItem);
        Assert.Contains(EnumerateProjectedMenuSourceItems(navVm), item => ReferenceEquals(item, selectedItem));
    }

    private static IEnumerable<MainNavItemViewModel> EnumerateProjectedMenuSourceItems(MainNavigationViewModel navVm)
    {
        foreach (var item in navVm.Items.Concat(navVm.FooterItems))
        {
            yield return item;

            if (item is ProjectNavItemViewModel project)
            {
                foreach (var child in project.Children)
                {
                    yield return child;
                }
            }
        }
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

    private sealed class MutableConversationCatalogDisplayReadModel : IConversationCatalogDisplayReadModel
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsConversationListLoading { get; private set; }

        public int ConversationListVersion { get; private set; }

        public IReadOnlyList<ConversationCatalogDisplayItem> Snapshot { get; private set; } = Array.Empty<ConversationCatalogDisplayItem>();

        public void SetLoading(bool isConversationListLoading)
        {
            if (IsConversationListLoading == isConversationListLoading)
            {
                return;
            }

            IsConversationListLoading = isConversationListLoading;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConversationListLoading)));
        }

        public void Refresh(IEnumerable<ConversationCatalogItem> snapshot)
        {
            Refresh(snapshot.Select(item => new ConversationCatalogDisplayItem(
                item.ConversationId,
                item.DisplayName,
                item.Cwd,
                item.CreatedAt,
                item.CatalogUpdatedAt,
                item.LastAccessedAt,
                HasUnreadAttention: false,
                item.RemoteSessionId,
                item.BoundProfileId,
                item.ProjectAffinityOverrideProjectId)));
        }

        public void Refresh(IEnumerable<ConversationCatalogDisplayItem> snapshot)
        {
            Snapshot = snapshot.ToArray();
            ConversationListVersion++;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Snapshot)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConversationListVersion)));
        }
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

    private sealed class MutableNavigationLocalizer : IStringLocalizer<CoreStrings>
    {
        public string Start { get; set; } = string.Empty;
        public string Discover { get; set; } = string.Empty;
        public string Settings { get; set; } = string.Empty;
        public string Sessions { get; set; } = string.Empty;
        public string Unclassified { get; set; } = string.Empty;
        public string MoreSessions { get; set; } = string.Empty;

        public LocalizedString this[string name] => new(name, Resolve(name));

        public LocalizedString this[string name, params object[] arguments]
            => new(name, string.Format(System.Globalization.CultureInfo.InvariantCulture, Resolve(name), arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;

        private string Resolve(string name)
            => name switch
            {
                "Nav_Start" => Start,
                "Nav_DiscoverSessions" => Discover,
                "Nav_Settings" => Settings,
                "Nav_Sessions" => Sessions,
                "Nav_Unclassified" => Unclassified,
                "Nav_MoreSessionsFormat" => MoreSessions,
                _ => name
            };
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext, IUiDispatcher
    {
        public bool HasThreadAccess => true;

        public override void Post(SendOrPostCallback d, object? state) => d(state);

        public void Enqueue(Action action) => action();

        public Task EnqueueAsync(Action action)
        {
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        public async Task EnqueueAsync(Func<Task> function)
        {
            await function().ConfigureAwait(false);
        }
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext, IUiDispatcher
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public bool HasThreadAccess => ReferenceEquals(Current, this);

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

        public void Enqueue(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            Post(_ => action(), null);
        }

        public Task EnqueueAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);

            return tcs.Task;
        }

        public Task EnqueueAsync(Func<Task> function)
        {
            ArgumentNullException.ThrowIfNull(function);

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ =>
            {
                try
                {
                    await function().ConfigureAwait(false);
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);

            return tcs.Task;
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
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            prefsLogger.Object,
            new ImmediateUiDispatcher());

        preferences.Projects.Add(new ProjectDefinition
        {
            ProjectId = "project-1",
            Name = "Demo",
            RootPath = @"C:\repo\demo"
        });

        return preferences;
    }

    private sealed class ControllableNavigationCoordinator : INavigationCoordinator
    {
        public bool SettingsActivationResult { get; set; } = true;

        public Exception? SettingsActivationException { get; set; }

        public string? LastSettingsKey { get; private set; }

        public Task<bool> ActivateStartAsync(string? projectIdForNewSession = null) => Task.FromResult(true);

        public Task<bool> ActivateDiscoverSessionsAsync() => Task.FromResult(true);

        public Task<bool> ActivateSettingsAsync(string settingsKey)
        {
            LastSettingsKey = settingsKey;
            if (SettingsActivationException is not null)
            {
                throw SettingsActivationException;
            }

            return Task.FromResult(SettingsActivationResult);
        }

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId) => Task.FromResult(false);

        public Task<DiscoverRemoteSessionOpenResult> ActivateDiscoveredRemoteSessionAsync(
            DiscoverRemoteSessionOpenRequest request)
            => Task.FromResult(new DiscoverRemoteSessionOpenResult(false, null, null));

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
        }
    }

    private sealed class StubNavigationCoordinator : INavigationCoordinator
    {
        public Task<bool> ActivateStartAsync(string? projectIdForNewSession = null) => Task.FromResult(true);

        public Task<bool> ActivateDiscoverSessionsAsync() => Task.FromResult(true);

        public Task<bool> ActivateSettingsAsync(string settingsKey) => Task.FromResult(true);

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId) => Task.FromResult(false);

        public Task<DiscoverRemoteSessionOpenResult> ActivateDiscoveredRemoteSessionAsync(
            DiscoverRemoteSessionOpenRequest request)
            => Task.FromResult(new DiscoverRemoteSessionOpenResult(false, null, null));

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
            SettingsNavItemViewModel settingsItem,
            IReadOnlyDictionary<string, SessionNavItemViewModel> sessionIndex)
            => _projection;
    }
}

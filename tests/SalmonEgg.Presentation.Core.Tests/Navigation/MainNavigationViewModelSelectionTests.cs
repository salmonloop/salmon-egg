using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

[Collection("NonParallel")]
public sealed class MainNavigationViewModelSelectionTests
{
    [Fact]
    public void Constructor_UsesShellSelectionReadModelProjectionInsteadOfPrivateSelectionOwnership()
    {
        var constructor = typeof(MainNavigationViewModel)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .OrderByDescending(ctor => ctor.GetParameters().Length)
            .First();

        Assert.Contains(
            constructor.GetParameters(),
            parameter => string.Equals(parameter.ParameterType.Name, "IShellSelectionReadModel", StringComparison.Ordinal));
    }

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
    public async Task ProjectVisualState_FollowsActiveSessionWithoutChangingLogicalSelection()
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

            Assert.IsType<NavigationSelectionState.Session>(navVm.CurrentSelection);
            var project = Assert.Single(navVm.Items.OfType<ProjectNavItemViewModel>(), p => p.ProjectId == "project-1");
            Assert.True(project.IsActiveDescendant);
            Assert.True(project.HasActiveDescendantIndicator);
            var projectedSession = Assert.IsType<SessionNavItemViewModel>(navVm.ProjectedControlSelectedItem);
            Assert.Equal("session-1", projectedSession.SessionId);

            var selectedSession = Assert.IsType<SessionNavItemViewModel>(navVm.SelectedItem);
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
    public void ExternalCurrentSessionChange_DoesNotOverrideVisibleStartSelection()
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

            navVm.SelectStart();
            chatCatalog.RaisePropertyChanged("CurrentSessionId");

            Assert.IsType<NavigationSelectionState.Start>(navVm.CurrentSelection);
            Assert.Same(navVm.StartItem, navVm.SelectedItem);
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
                    new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc))
            ]);

            using var navVm = new MainNavigationViewModel(
                chatCatalog,
                new FakeConversationSessionSwitcher(),
                CreateProjectPreferences(preferences),
                ui.Object,
                shellNavigation.Object,
                navLogger.Object,
                navState,
                metricsSink.Object,
                new NavigationSelectionProjector(),
                new ShellSelectionStateStore(),
                presenter);

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

    private static MainNavigationViewModel CreateNavigationViewModel(
        IConversationCatalog chatCatalog,
        ISessionManager sessionManager,
        AppPreferencesViewModel preferences,
        FakeNavigationPaneState navState,
        IConversationSessionSwitcher? sessionSwitcher = null)
    {
        var ui = new Mock<IUiInteractionService>();
        var shellNavigation = new Mock<IShellNavigationService>();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();
        var presenter = new ConversationCatalogPresenter();
        presenter.SetLoading(false);
        presenter.Refresh(CreateSnapshot(chatCatalog.GetKnownConversationIds()));

        return new MainNavigationViewModel(
            chatCatalog,
            sessionSwitcher ?? new FakeConversationSessionSwitcher(),
            CreateProjectPreferences(preferences),
            ui.Object,
            shellNavigation.Object,
            navLogger.Object,
            navState,
            metricsSink.Object,
            new NavigationSelectionProjector(),
            new ShellSelectionStateStore(),
            presenter);
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

    private static FakeChatSessionCatalog CreateChatSessionCatalog(params string[] conversationIds)
        => new(conversationIds);

    private static IReadOnlyList<ConversationCatalogItem> CreateSnapshot(IEnumerable<string> conversationIds)
        => conversationIds.Select(id => new ConversationCatalogItem(
            id,
            id,
            @"C:\repo\demo",
            DateTime.UtcNow,
            DateTime.UtcNow)).ToArray();

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

        public void ArchiveConversation(string conversationId)
        {
        }

        public void DeleteConversation(string conversationId)
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

    private sealed class FakeConversationSessionSwitcher : IConversationSessionSwitcher
    {
        public string? CurrentConversationId { get; set; }

        public Task<bool> TrySwitchToSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            CurrentConversationId = sessionId;
            return Task.FromResult(true);
        }
    }
}

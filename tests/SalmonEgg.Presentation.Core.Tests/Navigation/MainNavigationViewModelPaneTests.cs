using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

[Collection("NonParallel")]
public sealed class MainNavigationViewModelPaneTests
{
    [Fact]
    public void NavigationState_IsSharedAcrossViewModels()
    {
        var navState = new FakeNavigationPaneState();
        var sessionManager = new Mock<ISessionManager>();
        var preferences = CreatePreferences();
        var chatCatalog = CreateChatSessionCatalog();

        using var navVm = CreateNavigationViewModel(
            chatCatalog,
            sessionManager,
            preferences,
            navState);

        var startItem = new StartNavItemViewModel(navState, new ImmediateUiDispatcher());

        navState.SetPaneOpen(true);

        Assert.True(navVm.IsPaneOpen);
        Assert.True(startItem.IsPaneOpen);

        navState.SetPaneOpen(false);

        Assert.False(navVm.IsPaneOpen);
        Assert.False(startItem.IsPaneOpen);
    }

    [Fact]
    public void NavigationState_TriggersPropertyChangeNotifications()
    {
        var navState = new FakeNavigationPaneState();
        var item = new StartNavItemViewModel(navState, new ImmediateUiDispatcher());

        bool isPaneOpenChangedCalled = false;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(item.IsPaneOpen))
                isPaneOpenChangedCalled = true;
        };

        navState.SetPaneOpen(!navState.IsPaneOpen);

        Assert.True(isPaneOpenChangedCalled);
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

    private static MainNavigationViewModel CreateNavigationViewModel(
        IConversationCatalog chatCatalog,
        Mock<ISessionManager> sessionManager,
        AppPreferencesViewModel preferences,
        FakeNavigationPaneState navState)
    {
        var ui = new Mock<IUiInteractionService>();
        var navigationCoordinator = new StubNavigationCoordinator();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();
        var presenter = CreatePresenter(chatCatalog);
        var uiDispatcher = SynchronizationContext.Current as IUiDispatcher ?? new ImmediateUiDispatcher();

        return new MainNavigationViewModel(
            chatCatalog,
            CreateProjectPreferences(preferences),
            ui.Object,
            navigationCoordinator,
            navLogger.Object,
            navState,
            metricsSink.Object,
            new NavigationSelectionProjector(),
            new ShellSelectionStateStore(),
            new ShellNavigationRuntimeStateStore(),
            presenter,
            new ProjectAffinityResolver(),
            uiDispatcher,
            Mock.Of<IStringLocalizer<CoreStrings>>());
    }

    private static FakeChatSessionCatalog CreateChatSessionCatalog(params string[] conversationIds)
        => new(conversationIds);

    private static AppPreferencesViewModel CreatePreferences()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(s => s.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(s => s.IsSupported).Returns(false);
        var languageService = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefsLogger = new Mock<ILogger<AppPreferencesViewModel>>();

        return new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            languageService.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefsLogger.Object,
            new ImmediateUiDispatcher());
    }

    private static INavigationProjectPreferences CreateProjectPreferences(AppPreferencesViewModel preferences)
        => new NavigationProjectPreferencesAdapter(preferences);

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

        public IReadOnlyList<ConversationCatalogItem> CreateSnapshot()
        {
            var now = DateTime.UtcNow;
            return _conversationIds.ConvertAll(id => new ConversationCatalogItem(
                id,
                id,
                @"C:\repo\demo",
                now,
                now,
                now));
        }

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConversationListVersion)));
        }
    }

    private static MutableConversationCatalogDisplayReadModel CreatePresenter(IConversationCatalog chatCatalog)
    {
        var presenter = new MutableConversationCatalogDisplayReadModel();
        presenter.SetLoading(false);
        presenter.Refresh(chatCatalog.GetKnownConversationIds().Select(id => new ConversationCatalogItem(
            id,
            id,
            @"C:\repo\demo",
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow)));
        return presenter;
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
            Snapshot = snapshot.Select(item => new ConversationCatalogDisplayItem(
                item.ConversationId,
                item.DisplayName,
                item.Cwd,
                item.CreatedAt,
                item.LastUpdatedAt,
                item.LastAccessedAt,
                HasUnreadAttention: false,
                item.RemoteSessionId,
                item.BoundProfileId,
                item.ProjectAffinityOverrideProjectId)).ToArray();
            ConversationListVersion++;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Snapshot)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConversationListVersion)));
        }
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
}

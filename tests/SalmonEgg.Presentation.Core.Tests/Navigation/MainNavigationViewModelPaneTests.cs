using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Models.Navigation;
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
        var originalContext = SynchronizationContext.Current;
        var syncContext = new SynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
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

            var startItem = new StartNavItemViewModel(navState);

            navState.SetPaneOpen(true);

            Assert.True(navVm.IsPaneOpen);
            Assert.True(startItem.IsPaneOpen);

            navState.SetPaneOpen(false);

            Assert.False(navVm.IsPaneOpen);
            Assert.False(startItem.IsPaneOpen);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    [Fact]
    public void NavigationState_TriggersPropertyChangeNotifications()
    {
        var navState = new FakeNavigationPaneState();
        var item = new StartNavItemViewModel(navState);

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

    private static ConversationCatalogPresenter CreatePresenter(FakeChatSessionCatalog catalog)
    {
        var presenter = new ConversationCatalogPresenter();
        presenter.SetLoading(catalog.IsConversationListLoading);
        presenter.Refresh(catalog.CreateSnapshot());
        return presenter;
    }

    private static MainNavigationViewModel CreateNavigationViewModel(
        IConversationCatalog chatCatalog,
        Mock<ISessionManager> sessionManager,
        AppPreferencesViewModel preferences,
        FakeNavigationPaneState navState)
    {
        var ui = new Mock<IUiInteractionService>();
        var shellNavigation = new Mock<IShellNavigationService>();
        var navigationCoordinator = new StubNavigationCoordinator();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var metricsSink = new Mock<IShellLayoutMetricsSink>();

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
            CreatePresenter((FakeChatSessionCatalog)chatCatalog));
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
            prefsLogger.Object);
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
            => _conversationIds.ConvertAll(id => new ConversationCatalogItem(
                id,
                id,
                @"C:\repo\demo",
                DateTime.UtcNow,
                DateTime.UtcNow));

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConversationListVersion)));
        }
    }

    private sealed class StubNavigationCoordinator : INavigationCoordinator
    {
        public Task ActivateStartAsync() => Task.CompletedTask;

        public Task ActivateSettingsAsync(string settingsKey) => Task.CompletedTask;

        public Task<bool> ActivateSessionAsync(string sessionId, string? projectId) => Task.FromResult(false);

        public void SyncSelectionFromShellContent(ShellNavigationContent content)
        {
        }
    }
}

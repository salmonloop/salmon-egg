using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Conversation;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using SerilogLogger = Serilog.ILogger;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

[Collection("NonParallel")]
public sealed class MainNavigationViewModelPaneTests
{
    [Fact]
    public void PaneDisplayMode_PolicyClosesPane_WhenBecomingCompact()
    {
        var nav = CreateNav();

        nav.IsPaneOpen = true;
        nav.PaneDisplayMode = NavigationPaneDisplayMode.Compact;

        Assert.False(nav.IsPaneOpen);
    }

    [Fact]
    public void PaneDisplayMode_PolicyOpensPane_WhenBecomingExpanded()
    {
        var nav = CreateNav();

        nav.PaneDisplayMode = NavigationPaneDisplayMode.Compact;
        nav.IsPaneOpen = false;
        nav.PaneDisplayMode = NavigationPaneDisplayMode.Expanded;

        Assert.True(nav.IsPaneOpen);
    }

    [Fact]
    public void NavigationState_IsSharedAcrossViewModels()
    {
        var navState = new NavigationStateService();
        
        // Mock dependencies for ViewModels
        var sessionManager = new Mock<ISessionManager>();
        var preferences = CreatePreferences();
        var chatViewModel = CreateChatViewModel(new SynchronizationContext(), preferences, sessionManager.Object);
        var ui = new Mock<IUiInteractionService>();
        var shellNavigation = new Mock<IShellNavigationService>();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();

        var navVm = new MainNavigationViewModel(
            chatViewModel,
            sessionManager.Object,
            preferences,
            ui.Object,
            shellNavigation.Object,
            navLogger.Object,
            navState,
            new Mock<IRightPanelService>().Object);

        // Child ViewModel
        var startItem = new StartNavItemViewModel(navState);

        // Act: Change state in parent
        navVm.IsPaneOpen = true;

        // Assert: Child reflects change
        Assert.True(startItem.IsPaneOpen);

        // Act: Change state in child (simulated by service change)
        navState.IsPaneOpen = false;

        // Assert: Parent reflects change
        Assert.False(navVm.IsPaneOpen);
        Assert.False(navState.IsPaneOpen);
    }

    [Fact]
    public void NavigationState_TriggersPropertyChangeNotifications()
    {
        var navState = new NavigationStateService();
        var shellNavigation = new Mock<IShellNavigationService>();
        var item = new StartNavItemViewModel(navState);
        
        bool isPaneOpenChangedCalled = false;
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(item.IsPaneOpen))
                isPaneOpenChangedCalled = true;
        };

        // Act
        navState.IsPaneOpen = !navState.IsPaneOpen;

        // Assert
        Assert.True(isPaneOpenChangedCalled);
    }

    [Fact]
    public void OpenPaneLength_UsesCompactLength_WhenPaneClosedInCompact()
    {
        var nav = CreateNav();

        nav.NavCompactPaneLength = 80;
        nav.NavOpenPaneLength = 320;
        nav.PaneDisplayMode = NavigationPaneDisplayMode.Compact;
        nav.IsPaneOpen = false;

        Assert.Equal(80, nav.OpenPaneLength);
    }

    [Fact]
    public void OpenPaneLength_UsesCompactLength_WhenPaneClosedInMinimal()
    {
        var nav = CreateNav();

        nav.NavCompactPaneLength = 96;
        nav.NavOpenPaneLength = 360;
        nav.PaneDisplayMode = NavigationPaneDisplayMode.Minimal;
        nav.IsPaneOpen = false;

        Assert.Equal(96, nav.OpenPaneLength);
    }

    [Fact]
    public void OpenPaneLength_UsesOpenLength_WhenPaneOpen()
    {
        var nav = CreateNav();

        nav.NavCompactPaneLength = 72;
        nav.NavOpenPaneLength = 400;
        nav.PaneDisplayMode = NavigationPaneDisplayMode.Compact;
        nav.IsPaneOpen = true;

        Assert.Equal(400, nav.OpenPaneLength);
    }

    [Fact]
    public void OpenPaneLength_UsesOpenLength_WhenExpandedAndPaneClosed()
    {
        var nav = CreateNav();

        nav.NavCompactPaneLength = 72;
        nav.NavOpenPaneLength = 380;
        nav.PaneDisplayMode = NavigationPaneDisplayMode.Expanded;
        nav.IsPaneOpen = false;

        Assert.Equal(380, nav.OpenPaneLength);
    }

    [Fact]
    public void LeftNavResizerVisible_WhenPaneOpenAndExpanded()
    {
        var nav = CreateNav();

        nav.PaneDisplayMode = NavigationPaneDisplayMode.Expanded;
        nav.IsPaneOpen = true;
        nav.IsNavPaneAnimating = false;

        Assert.True(nav.IsLeftNavResizerVisible);
    }

    [Fact]
    public void LeftNavResizerHidden_WhenPaneClosed()
    {
        var nav = CreateNav();

        nav.PaneDisplayMode = NavigationPaneDisplayMode.Expanded;
        nav.IsPaneOpen = false;
        nav.IsNavPaneAnimating = false;

        Assert.False(nav.IsLeftNavResizerVisible);
    }

    [Fact]
    public void LeftNavResizerHidden_WhenCompact()
    {
        var nav = CreateNav();

        nav.PaneDisplayMode = NavigationPaneDisplayMode.Compact;
        nav.IsPaneOpen = true;
        nav.IsNavPaneAnimating = false;

        Assert.False(nav.IsLeftNavResizerVisible);
    }

    [Fact]
    public void LeftNavResizerHidden_WhenAnimating()
    {
        var nav = CreateNav();

        nav.PaneDisplayMode = NavigationPaneDisplayMode.Expanded;
        nav.IsPaneOpen = true;
        nav.IsNavPaneAnimating = true;

        Assert.False(nav.IsLeftNavResizerVisible);
    }

    private static MainNavigationViewModel CreateNav()
    {
        var originalContext = SynchronizationContext.Current;
        var syncContext = new SynchronizationContext();

        var sessionManager = new Mock<ISessionManager>();
        sessionManager.Setup(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync((string id, string? cwd) => new Session { SessionId = id, Cwd = cwd });

        var preferences = CreatePreferences();
        var chatViewModel = CreateChatViewModel(syncContext, preferences, sessionManager.Object);

        SynchronizationContext.SetSynchronizationContext(originalContext);

        var ui = new Mock<IUiInteractionService>();
        var shellNavigation = new Mock<IShellNavigationService>();
        var navLogger = new Mock<ILogger<MainNavigationViewModel>>();
        var navState = new NavigationStateService();

        return new MainNavigationViewModel(
            chatViewModel,
            sessionManager.Object,
            preferences,
            ui.Object,
            shellNavigation.Object,
            navLogger.Object,
            navState,
            new Mock<IRightPanelService>().Object);
    }

    private static ChatViewModel CreateChatViewModel(
        SynchronizationContext syncContext,
        AppPreferencesViewModel preferences,
        ISessionManager sessionManager)
    {
        var transportFactory = new Mock<ITransportFactory>();
        var messageParser = new Mock<IMessageParser>();
        var messageValidator = new Mock<IMessageValidator>();
        var errorLogger = new Mock<IErrorLogger>();
        var capabilityManager = new Mock<ICapabilityManager>();
        var serilog = new Mock<SerilogLogger>();

        var chatServiceFactory = new ChatServiceFactory(
            transportFactory.Object,
            messageParser.Object,
            messageValidator.Object,
            errorLogger.Object,
            capabilityManager.Object,
            sessionManager,
            serilog.Object);

        var configService = new Mock<IConfigurationService>();
        var profilesLogger = new Mock<ILogger<AcpProfilesViewModel>>();
        var profiles = new AcpProfilesViewModel(configService.Object, preferences, profilesLogger.Object);

        var conversationStore = new Mock<IConversationStore>();
        var neverComplete = new TaskCompletionSource<ConversationDocument>();
        conversationStore.Setup(s => s.LoadAsync(It.IsAny<CancellationToken>())).Returns(neverComplete.Task);

        var vmLogger = new Mock<ILogger<ChatViewModel>>();

        var originalContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            return new ChatViewModel(
                chatServiceFactory,
                configService.Object,
                preferences,
                profiles,
                sessionManager,
                conversationStore.Object,
                vmLogger.Object);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

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
}

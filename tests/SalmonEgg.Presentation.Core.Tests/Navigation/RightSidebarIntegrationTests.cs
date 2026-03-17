using Moq;
using SalmonEgg.Domain.Interfaces;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Chat;
using Microsoft.Extensions.Logging;
using Xunit;
using System.Threading;

using System.Collections.ObjectModel;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Domain.Services;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Presentation.ViewModels;
using SalmonEgg.Presentation.ViewModels.Settings;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class RightSidebarIntegrationTests
{
    [Fact]
    public void RightPanelMode_ReflectsServiceState()
    {
        var navState = new Mock<INavigationStateService>();
        var rightPanelService = new RightPanelService();
        var nav = CreateNav(navState.Object, rightPanelService);

        // Act
        rightPanelService.CurrentMode = RightPanelMode.Todo;

        // Assert
        Assert.Equal(RightPanelMode.Todo, nav.RightPanelMode);
    }

    [Fact]
    public void RightPanelMode_NotifiesOnServiceChange()
    {
        var navState = new Mock<INavigationStateService>();
        var rightPanelService = new RightPanelService();
        var nav = CreateNav(navState.Object, rightPanelService);
        
        bool notified = false;
        nav.PropertyChanged += (s, e) => 
        {
            if (e.PropertyName == nameof(nav.RightPanelMode))
                notified = true;
        };

        // Act
        rightPanelService.CurrentMode = RightPanelMode.Diff;

        // Assert
        Assert.True(notified);
    }

    private static MainNavigationViewModel CreateNav(INavigationStateService navState, IRightPanelService rightPanelService)
    {
        var chatServiceFactoryMock = new Mock<ChatServiceFactory>(
            new Mock<SalmonEgg.Domain.Interfaces.ITransportFactory>().Object,
            new Mock<SalmonEgg.Domain.Interfaces.IMessageParser>().Object,
            new Mock<SalmonEgg.Domain.Interfaces.IMessageValidator>().Object,
            new Mock<SalmonEgg.Domain.Services.IErrorLogger>().Object,
            new Mock<SalmonEgg.Domain.Services.ICapabilityManager>().Object,
            new Mock<ISessionManager>().Object,
            new Mock<Serilog.ILogger>().Object);
        var configService = new Mock<IConfigurationService>();
        var sessionManager = new Mock<ISessionManager>();
        
        var appSettings = new Mock<IAppSettingsService>();
        var appStartup = new Mock<IAppStartupService>();
        var appLanguage = new Mock<IAppLanguageService>();
        var capabilities = new Mock<IPlatformCapabilityService>();
        var uiRuntime = new Mock<IUiRuntimeService>();
        var prefLogger = new Mock<ILogger<AppPreferencesViewModel>>();
        
        var preferences = new Mock<AppPreferencesViewModel>(
            appSettings.Object,
            appStartup.Object,
            appLanguage.Object,
            capabilities.Object,
            uiRuntime.Object,
            prefLogger.Object);

        var acpProfiles = new Mock<AcpProfilesViewModel>(
            configService.Object,
            preferences.Object,
            new Mock<ILogger<AcpProfilesViewModel>>().Object);
            
        var conversationStore = new Mock<IConversationStore>();
        var chatLogger = new Mock<ILogger<ChatViewModel>>();
        
        var chatVm = new Mock<ChatViewModel>(
            chatServiceFactoryMock.Object,
            configService.Object,
            preferences.Object,
            acpProfiles.Object,
            sessionManager.Object,
            conversationStore.Object,
            chatLogger.Object,
            null); // 8th arg is SynchronizationContext?

        var ui = new Mock<IUiInteractionService>();
        var shellNavigation = new Mock<IShellNavigationService>();
        var logger = new Mock<ILogger<MainNavigationViewModel>>();

        return new MainNavigationViewModel(
            chatVm.Object,
            sessionManager.Object,
            preferences.Object,
            ui.Object,
            shellNavigation.Object,
            logger.Object,
            navState,
            rightPanelService);
    }
}

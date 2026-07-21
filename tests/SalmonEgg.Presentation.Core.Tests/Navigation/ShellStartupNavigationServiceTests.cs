using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Models.Settings;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class ShellStartupNavigationServiceTests
{
    [Fact]
    public async Task ActivateInitialContentAsync_ActivatesStartThroughNavigationViewModelOwner()
    {
        var coordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
        coordinator
            .Setup(x => x.ActivateStartAsync(null))
            .ReturnsAsync(true);
        using var navigationViewModel = CreateNavigationViewModel(coordinator.Object);
        var service = new ShellStartupNavigationService(navigationViewModel);

        await service.ActivateInitialContentAsync();

        coordinator.Verify(x => x.ActivateStartAsync(null), Times.Once);
        coordinator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivateInitialContentAsync_DoesNotRepeatAfterSuccessfulActivation()
    {
        var coordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
        coordinator
            .Setup(x => x.ActivateStartAsync(null))
            .ReturnsAsync(true);
        using var navigationViewModel = CreateNavigationViewModel(coordinator.Object);
        var service = new ShellStartupNavigationService(navigationViewModel);

        await service.ActivateInitialContentAsync();
        await service.ActivateInitialContentAsync();

        coordinator.Verify(x => x.ActivateStartAsync(null), Times.Once);
        coordinator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivateInitialContentAsync_RetriesAfterRejectedActivation()
    {
        var coordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
        coordinator
            .SetupSequence(x => x.ActivateStartAsync(null))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        using var navigationViewModel = CreateNavigationViewModel(coordinator.Object);
        var service = new ShellStartupNavigationService(navigationViewModel);

        await service.ActivateInitialContentAsync();
        await service.ActivateInitialContentAsync();

        coordinator.Verify(x => x.ActivateStartAsync(null), Times.Exactly(2));
        coordinator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivateInitialContentAsync_WhenStartActivationFails_SurfacesLocalizedInfoViaNavOwner()
    {
        var shownMessages = new List<string>();
        var ui = new Mock<IUiInteractionService>();
        ui.Setup(service => service.ShowInfoAsync(It.IsAny<string>()))
            .Callback<string>(shownMessages.Add)
            .Returns(Task.CompletedTask);

        var coordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
        coordinator
            .Setup(x => x.ActivateStartAsync(null))
            .ReturnsAsync(false);
        using var navigationViewModel = CreateNavigationViewModel(coordinator.Object, ui.Object);
        var service = new ShellStartupNavigationService(navigationViewModel);

        await service.ActivateInitialContentAsync();

        coordinator.Verify(x => x.ActivateStartAsync(null), Times.Once);
        Assert.Equal(
            ["Failed to open the start page. Please try again later."],
            shownMessages);
    }

    private static MainNavigationViewModel CreateNavigationViewModel(
        INavigationCoordinator navigationCoordinator,
        IUiInteractionService? ui = null)
    {
        var preferences = CreatePreferences();
        return new MainNavigationViewModel(
            Mock.Of<IConversationCatalog>(),
            new NavigationProjectPreferencesAdapter(preferences),
            ui ?? Mock.Of<IUiInteractionService>(),
            navigationCoordinator,
            Mock.Of<ILogger<MainNavigationViewModel>>(),
            new FakeNavigationPaneState(),
            Mock.Of<IShellLayoutMetricsSink>(),
            new NavigationSelectionProjector(),
            new ShellSelectionStateStore(),
            new ShellNavigationRuntimeStateStore(),
            new ConversationCatalogPresenter(),
            new ProjectAffinityResolver(),
            new ImmediateUiDispatcher(),
            new TestCoreStringLocalizer());
    }

    private static AppPreferencesViewModel CreatePreferences()
    {
        var appSettingsService = new Mock<IAppSettingsService>();
        appSettingsService.Setup(service => service.LoadAsync()).ReturnsAsync(new AppSettings());
        var startupService = new Mock<IAppStartupService>();
        startupService.SetupGet(service => service.IsSupported).Returns(false);

        return new AppPreferencesViewModel(
            appSettingsService.Object,
            startupService.Object,
            Mock.Of<IAppLanguageService>(),
            Mock.Of<IPlatformCapabilityService>(),
            Mock.Of<IUiRuntimeService>(),
            Mock.Of<IUiInteractionService>(),
            new TestCoreStringLocalizer(),
            Mock.Of<ILogger<AppPreferencesViewModel>>(),
            new ImmediateUiDispatcher());
    }

    private sealed class FakeNavigationPaneState : INavigationPaneState
    {
        public bool IsPaneOpen => true;

        public event System.EventHandler? PaneStateChanged
        {
            add { }
            remove { }
        }
    }
}

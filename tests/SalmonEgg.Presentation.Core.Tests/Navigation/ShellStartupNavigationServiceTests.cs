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
        var service = CreateService(navigationViewModel);

        await service.ActivateInitialContentAsync();

        coordinator.Verify(x => x.ActivateStartAsync(null), Times.Once);
        coordinator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivateInitialContentAsync_AfterSuccessfulActivation_ReprojectsStartWithoutSecondActivation()
    {
        var coordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
        coordinator
            .Setup(x => x.ActivateStartAsync(null))
            .ReturnsAsync(true);
        using var navigationViewModel = CreateNavigationViewModel(coordinator.Object);
        var shellNavigation = CreateShellNavigationService();
        var service = CreateService(navigationViewModel, shellNavigation: shellNavigation.Object);

        await service.ActivateInitialContentAsync();
        await service.ActivateInitialContentAsync();

        coordinator.Verify(x => x.ActivateStartAsync(null), Times.Once);
        coordinator.VerifyNoOtherCalls();
        shellNavigation.As<IActivationTokenShellNavigationService>()
            .Verify(x => x.NavigateToStart(It.IsAny<long>()), Times.Once);
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
        var service = CreateService(navigationViewModel);

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
        var service = CreateService(navigationViewModel);

        await service.ActivateInitialContentAsync();

        coordinator.Verify(x => x.ActivateStartAsync(null), Times.Once);
        Assert.Equal(
            ["Failed to open the start page. Please try again later."],
            shownMessages);
    }

    [Fact]
    public async Task ActivateInitialContentAsync_AfterShellReload_ReprojectsCurrentChatWithoutChangingSelection()
    {
        var coordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
        coordinator.Setup(x => x.ActivateStartAsync(null)).ReturnsAsync(true);
        using var navigationViewModel = CreateNavigationViewModel(coordinator.Object);
        var runtimeState = new ShellNavigationRuntimeStateStore();
        var shellNavigation = CreateShellNavigationService();
        var service = CreateService(navigationViewModel, runtimeState, shellNavigation.Object);
        await service.ActivateInitialContentAsync();
        runtimeState.CurrentShellContent = SalmonEgg.Presentation.Models.Navigation.ShellNavigationContent.Chat;
        runtimeState.LatestActivationToken = 7;

        await service.ActivateInitialContentAsync();

        coordinator.Verify(x => x.ActivateStartAsync(null), Times.Once);
        shellNavigation.As<IActivationTokenShellNavigationService>()
            .Verify(x => x.NavigateToChat(7), Times.Once);
    }

    [Fact]
    public async Task ActivateInitialContentAsync_AfterSettingsReload_RestoresSelectedSection()
    {
        var coordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
        coordinator.Setup(x => x.ActivateStartAsync(null)).ReturnsAsync(true);
        using var navigationViewModel = CreateNavigationViewModel(coordinator.Object);
        var runtimeState = new ShellNavigationRuntimeStateStore();
        var settingsSelection = new SettingsSectionSelectionStore();
        _ = settingsSelection.Select(SettingsSectionCatalog.DiagnosticsKey);
        var shellNavigation = CreateShellNavigationService();
        var service = CreateService(
            navigationViewModel,
            runtimeState,
            shellNavigation.Object,
            settingsSelection);
        await service.ActivateInitialContentAsync();
        runtimeState.CurrentShellContent = SalmonEgg.Presentation.Models.Navigation.ShellNavigationContent.Settings;

        await service.ActivateInitialContentAsync();

        shellNavigation.As<IActivationTokenShellNavigationService>().Verify(
            x => x.NavigateToSettings(SettingsSectionCatalog.DiagnosticsKey, It.IsAny<long>()),
            Times.Once);
    }

    [Fact]
    public async Task ActivateInitialContentAsync_WhenLatestIntentIsPending_RestoresPendingContent()
    {
        var coordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
        coordinator.Setup(x => x.ActivateStartAsync(null)).ReturnsAsync(true);
        using var navigationViewModel = CreateNavigationViewModel(coordinator.Object);
        var runtimeState = new ShellNavigationRuntimeStateStore();
        var shellNavigation = CreateShellNavigationService();
        var service = CreateService(navigationViewModel, runtimeState, shellNavigation.Object);
        await service.ActivateInitialContentAsync();
        runtimeState.CurrentShellContent = SalmonEgg.Presentation.Models.Navigation.ShellNavigationContent.Chat;
        runtimeState.PendingShellContent = SalmonEgg.Presentation.Models.Navigation.ShellNavigationContent.DiscoverSessions;

        await service.ActivateInitialContentAsync();

        shellNavigation.As<IActivationTokenShellNavigationService>()
            .Verify(x => x.NavigateToDiscoverSessions(It.IsAny<long>()), Times.Once);
        shellNavigation.As<IActivationTokenShellNavigationService>()
            .Verify(x => x.NavigateToChat(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task ActivateInitialContentAsync_WhenSettingsIntentIsPending_RestoresLatestSection()
    {
        var coordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
        using var navigationViewModel = CreateNavigationViewModel(coordinator.Object);
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = SalmonEgg.Presentation.Models.Navigation.ShellNavigationContent.Chat,
            PendingShellContent = SalmonEgg.Presentation.Models.Navigation.ShellNavigationContent.Settings
        };
        var settingsSelection = new SettingsSectionSelectionStore();
        _ = settingsSelection.Select(SettingsSectionCatalog.DiagnosticsKey);
        var shellNavigation = CreateShellNavigationService();
        var service = CreateService(
            navigationViewModel,
            runtimeState,
            shellNavigation.Object,
            settingsSelection);

        await service.ActivateInitialContentAsync();

        shellNavigation.As<IActivationTokenShellNavigationService>().Verify(
            x => x.NavigateToSettings(SettingsSectionCatalog.DiagnosticsKey, It.IsAny<long>()),
            Times.Once);
        coordinator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ActivateInitialContentAsync_WhenRestoreThrows_LogsActualContent()
    {
        var coordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
        using var navigationViewModel = CreateNavigationViewModel(coordinator.Object);
        var runtimeState = new ShellNavigationRuntimeStateStore
        {
            CurrentShellContent = SalmonEgg.Presentation.Models.Navigation.ShellNavigationContent.Chat
        };
        var exception = new InvalidOperationException("Navigation failed");
        var shellNavigation = CreateShellNavigationService();
        shellNavigation.As<IActivationTokenShellNavigationService>()
            .Setup(x => x.NavigateToChat(It.IsAny<long>()))
            .Throws(exception);
        var logger = new Mock<ILogger<ShellStartupNavigationService>>();
        var service = CreateService(
            navigationViewModel,
            runtimeState,
            shellNavigation.Object,
            logger: logger.Object);

        await service.ActivateInitialContentAsync();

        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) =>
                    value != null
                    && value.ToString()!.Contains("content=Chat", StringComparison.Ordinal)),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ActivateInitialContentAsync_WhenReloadArrivesDuringInitialActivation_RestoresReloadedShellAfterActivation()
    {
        var activationCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinator = new Mock<INavigationCoordinator>(MockBehavior.Strict);
        coordinator
            .Setup(x => x.ActivateStartAsync(null))
            .Returns(activationCompletion.Task);
        using var navigationViewModel = CreateNavigationViewModel(coordinator.Object);
        var runtimeState = new ShellNavigationRuntimeStateStore();
        var shellNavigation = CreateShellNavigationService();
        var service = CreateService(navigationViewModel, runtimeState, shellNavigation.Object);
        var initialActivation = service.ActivateInitialContentAsync();
        var reloadActivation = service.ActivateInitialContentAsync();
        runtimeState.CurrentShellContent = SalmonEgg.Presentation.Models.Navigation.ShellNavigationContent.Chat;

        activationCompletion.SetResult(true);
        await Task.WhenAll(initialActivation, reloadActivation);

        coordinator.Verify(x => x.ActivateStartAsync(null), Times.Once);
        shellNavigation.As<IActivationTokenShellNavigationService>()
            .Verify(x => x.NavigateToChat(It.IsAny<long>()), Times.Once);
    }

    private static ShellStartupNavigationService CreateService(
        MainNavigationViewModel navigationViewModel,
        IShellNavigationRuntimeState? runtimeState = null,
        IShellNavigationService? shellNavigation = null,
        ISettingsSectionSelectionStore? settingsSelection = null,
        ILogger<ShellStartupNavigationService>? logger = null)
    {
        shellNavigation ??= CreateShellNavigationService().Object;
        return new ShellStartupNavigationService(
            navigationViewModel,
            runtimeState ?? new ShellNavigationRuntimeStateStore(),
            Assert.IsAssignableFrom<IActivationTokenShellNavigationService>(shellNavigation),
            settingsSelection ?? new SettingsSectionSelectionStore(),
            logger);
    }

    private static Mock<IShellNavigationService> CreateShellNavigationService()
    {
        var service = new Mock<IShellNavigationService>(MockBehavior.Strict);
        service.Setup(x => x.NavigateToStart())
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        service.Setup(x => x.NavigateToChat())
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        service.Setup(x => x.NavigateToSettings(It.IsAny<string>()))
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        service.Setup(x => x.NavigateToDiscoverSessions())
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        var tokenAware = service.As<IActivationTokenShellNavigationService>();
        tokenAware.Setup(x => x.NavigateToStart(It.IsAny<long>()))
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        tokenAware.Setup(x => x.NavigateToChat(It.IsAny<long>()))
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        tokenAware.Setup(x => x.NavigateToSettings(It.IsAny<string>(), It.IsAny<long>()))
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        tokenAware.Setup(x => x.NavigateToDiscoverSessions(It.IsAny<long>()))
            .Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        return service;
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
            new ImmediateUiDispatcher(),
            TestSystemNotificationService.Instance);
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

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Mvux.Chat;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Uno.Extensions.Reactive;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public partial class ChatViewModelTests
{
    [Fact(Timeout = 15000)]
    public async Task NavigationToCurrentLocalConversation_WarmReuseCompletesActivationAndAllowsStatusMigration()
    {
        // Arrange
        var dispatcher = new QueueingSynchronizationContext();
        var runtime = new ShellNavigationRuntimeStateStore { CurrentShellContent = ShellNavigationContent.Chat };
        var sessionManager = CreateSessionManagerWithStore();
        await sessionManager.Object.CreateSessionAsync("warm-local", "/repo/local");
        var activation = new Mock<IConversationActivationCoordinator>(MockBehavior.Strict);
        await using var fixture = CreateViewModel(dispatcher, sessionManager: sessionManager,
            shellNavigationRuntimeState: runtime, conversationActivationCoordinator: activation.Object);
        await dispatcher.RunUntilCompletedAsync(fixture.ViewModel.RestoreAsync(TestContext.Current.CancellationToken));
        await fixture.UpdateStateAsync(state => state with { HydratedConversationId = "warm-local" });
        await WaitForConditionAsync(() =>
        {
            dispatcher.RunAll();
            return Task.FromResult(fixture.ViewModel.CurrentSessionId == "warm-local");
        }, timeoutMilliseconds: 2000);
        fixture.Preferences.SidebarConversationGrouping = AppSettingValueCatalog.StatusConversationGrouping;
        fixture.Preferences.Projects.Add(new ProjectDefinition { ProjectId = "local-project", Name = "Local", RootPath = "/repo/local" });
        dispatcher.RunAll();

        var selection = new ShellSelectionStateStore();
        var shell = new Mock<IShellNavigationService>();
        shell.Setup(service => service.NavigateToChat()).Returns(ValueTask.FromResult(ShellNavigationResult.Success()));
        var coordinator = new NavigationCoordinator(selection, runtime, fixture.ViewModel,
            Mock.Of<IDiscoverSessionsConnectionFacade>(), new NavigationProjectSelectionStoreAdapter(fixture.Preferences),
            shell.Object, new SettingsSectionSelectionStore());
        var catalog = new ConversationCatalogPresenter();
        var timestamp = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
        catalog.SetLoading(false);
        catalog.Refresh([new ConversationCatalogItem("warm-local", "Local session", "/repo/local", timestamp, timestamp, timestamp)]);
        await using var attentionState = State.Value(new object(), () => ConversationAttentionState.Empty);
        using var display = new ConversationCatalogDisplayPresenter(catalog,
            new ConversationAttentionStore(attentionState), dispatcher, fixture.ChatStore);
        var conversationCatalog = new ConversationCatalogFacade(fixture.Workspace, activation.Object, selection,
            new Lazy<INavigationCoordinator>(() => coordinator), catalog, NullLogger<ConversationCatalogFacade>.Instance);
        using var navigation = new MainNavigationViewModel(
            conversationCatalog, new NavigationProjectPreferencesAdapter(fixture.Preferences), Mock.Of<IUiInteractionService>(),
            coordinator, NullLogger<MainNavigationViewModel>.Instance, Mock.Of<INavigationPaneState>(),
            Mock.Of<IShellLayoutMetricsSink>(), new NavigationSelectionProjector(), selection, runtime, display,
            new ProjectAffinityResolver(), dispatcher, new TestCoreStringLocalizer());
        navigation.RebuildTree();
        dispatcher.RunAll();

        // Act
        var activated = coordinator.ActivateSessionAsync("warm-local", "local-project");
        await dispatcher.RunUntilCompletedAsync(activated);

        // Assert
        Assert.True(await activated);
        Assert.Equal(SessionActivationPhase.Hydrated, runtime.ActiveSessionActivation?.Phase);
        Assert.False(runtime.IsSessionActivationInProgress);
        Assert.Equal(new NavigationSelectionState.Session("warm-local"), selection.CurrentSelection);
        Assert.Empty(activation.Invocations);
        var originalRow = Assert.IsType<SessionNavItemViewModel>(navigation.ProjectedControlSelectedItem);

        // Act
        await fixture.ChatStore.Dispatch(new BeginTurnAction("warm-local", "turn-1", ChatTurnPhase.Thinking));
        await WaitForConditionAsync(() =>
        {
            dispatcher.RunAll();
            return Task.FromResult(navigation.Items.OfType<StatusGroupNavItemViewModel>()
                .Single(group => group.Group == ConversationStatusGroup.Working).Children.Contains(originalRow));
        }, timeoutMilliseconds: 2000);

        // Assert
        Assert.Same(originalRow, navigation.ProjectedControlSelectedItem);
        Assert.Equal(1, navigation.Items.OfType<StatusGroupNavItemViewModel>()
            .Single(group => group.Group == ConversationStatusGroup.Working).Count);
    }
}

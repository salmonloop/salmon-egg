using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using SalmonEgg.Presentation.Core.Services.ProjectAffinity;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Tests.Threading;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.ViewModels.Settings;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class MainNavigationStatusGroupingTests
{
    private static readonly DateTime Baseline = new(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RebuildTree_StatusMode_ProjectsThreeGroupsAndOriginalProjectIdentity()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();

        // Act
        harness.Publish(
            Item("attention", ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Permission),
            Item("working", ConversationStatusGroup.Working, ConversationStatusIcon.Working),
            Item("other", ConversationStatusGroup.Other));

        // Assert
        Assert.Equal(
            new[] { ConversationStatusGroup.NeedsAttention, ConversationStatusGroup.Working, ConversationStatusGroup.Other },
            harness.Groups.Select(group => group.Group));
        Assert.All(harness.Groups, group => Assert.Equal(1, group.Count));
        Assert.True(harness.Group(ConversationStatusGroup.NeedsAttention).IsExpanded);
        Assert.True(harness.Group(ConversationStatusGroup.Working).IsExpanded);
        Assert.False(harness.Group(ConversationStatusGroup.Other).IsExpanded);
        Assert.All(harness.Rows, row =>
        {
            Assert.Equal("project-1", row.ProjectId);
            Assert.Equal("Demo", row.ProjectDisplayName);
            Assert.True(row.ShowProjectDisplayName);
        });
        Assert.Equal(ConversationStatusIcon.Permission, harness.Row("attention").StatusIcon);
        Assert.Empty(harness.Navigation.Invocations);
    }

    [Fact]
    public async Task RebuildTree_EmptyCatalog_KeepsZeroCountGroups()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();

        // Act
        harness.Publish();

        // Assert
        Assert.Equal(3, harness.Groups.Count());
        Assert.All(harness.Groups, group =>
        {
            Assert.Equal(0, group.Count);
            Assert.Empty(group.Children);
        });
    }

    [Fact]
    public async Task GroupingPreference_Changes_ReusesSelectedSessionWithoutActivation()
    {
        // Arrange
        using var harness = await Harness.CreateAsync(AppSettingValueCatalog.ProjectConversationGrouping);
        harness.Publish(Item("session-1", ConversationStatusGroup.Working, ConversationStatusIcon.Working));
        harness.Selection.SetSelection(new NavigationSelectionState.Session("session-1"));
        var original = harness.Row("session-1");

        // Act
        harness.Preferences.SidebarConversationGrouping = AppSettingValueCatalog.StatusConversationGrouping;
        var grouped = harness.Row("session-1");
        harness.Preferences.SidebarConversationGrouping = AppSettingValueCatalog.ProjectConversationGrouping;

        // Assert
        Assert.Same(original, grouped);
        Assert.Same(original, harness.Row("session-1"));
        Assert.Same(original, harness.ViewModel.ProjectedControlSelectedItem);
        Assert.False(original.ShowProjectDisplayName);
        Assert.Equal("project-1", harness.ViewModel.TryGetProjectIdForSession("session-1"));
        Assert.Empty(harness.Navigation.Invocations);
        var paneChanged = false;
        original.PropertyChanged += (_, args) => paneChanged |= args.PropertyName == nameof(original.IsPaneOpen);
        harness.Pane.SetOpen(false);
        Assert.True(paneChanged);
    }

    [Fact]
    public async Task StatusChange_SelectedConversation_MovesSameRowIntoCollapsedGroup()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        harness.Publish(Item("session-1", ConversationStatusGroup.Working, ConversationStatusIcon.Working));
        harness.Selection.SetSelection(new NavigationSelectionState.Session("session-1"));
        var row = harness.Row("session-1");

        // Act
        harness.Publish(Item("session-1", ConversationStatusGroup.Other));

        // Assert
        Assert.Empty(harness.Group(ConversationStatusGroup.Working).Children);
        Assert.Same(row, Assert.Single(harness.Group(ConversationStatusGroup.Other).Children));
        Assert.Same(row, harness.ViewModel.ProjectedControlSelectedItem);
        Assert.False(harness.Group(ConversationStatusGroup.Other).IsExpanded);
        Assert.Empty(harness.Navigation.Invocations);
    }

    [Fact]
    public async Task FocusedConversation_StatusChanges_HoldsOnlyItsMembershipUntilFocusLeaves()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        harness.Publish(
            Item("focused", ConversationStatusGroup.Working, ConversationStatusIcon.Working),
            Item("background", ConversationStatusGroup.Working, ConversationStatusIcon.Working),
            Item("other", ConversationStatusGroup.Other));
        harness.Selection.SetSelection(new NavigationSelectionState.Session("focused"));
        var focused = harness.Row("focused");
        harness.ViewModel.SetFocusedConversationId("focused");

        // Act
        harness.Publish(
            Item("focused", ConversationStatusGroup.Other),
            Item("background", ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Unread),
            Item("other", ConversationStatusGroup.Other));

        // Assert
        Assert.Contains(focused, harness.Group(ConversationStatusGroup.Working).Children);
        Assert.Equal(ConversationStatusIcon.Conversation, focused.StatusIcon);
        Assert.Contains(harness.Row("background"), harness.Group(ConversationStatusGroup.NeedsAttention).Children);
        Assert.Equal(1, harness.Group(ConversationStatusGroup.Working).Count);
        Assert.Same(focused, harness.ViewModel.ProjectedControlSelectedItem);

        // Act
        harness.ViewModel.SetFocusedConversationId(null);

        // Assert
        Assert.Contains(focused, harness.Group(ConversationStatusGroup.Other).Children);
        Assert.Equal(0, harness.Group(ConversationStatusGroup.Working).Count);
        Assert.Same(focused, harness.ViewModel.ProjectedControlSelectedItem);
        Assert.Empty(harness.Navigation.Invocations);
    }

    [Fact]
    public async Task FocusedConversation_RecencyChanges_PreservesSourceGroupOrderWithoutFreezingOtherGroups()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        var first = Item("first", ConversationStatusGroup.Working, ConversationStatusIcon.Working, 3);
        var focused = Item("focused", ConversationStatusGroup.Working, ConversationStatusIcon.Working, 2);
        var third = Item("third", ConversationStatusGroup.Working, ConversationStatusIcon.Working, 1);
        harness.Publish(first, focused, third, Item("other", ConversationStatusGroup.Other));
        harness.ViewModel.SetFocusedConversationId("focused");

        // Act
        harness.Publish(first with { ActivityAt = Baseline }, focused with { StatusGroup = ConversationStatusGroup.Other, StatusIcon = ConversationStatusIcon.Conversation },
            third with { ActivityAt = Baseline.AddMinutes(20) }, Item("new", ConversationStatusGroup.Working, ConversationStatusIcon.Working, 30),
            Item("other", ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Unread));

        // Assert
        Assert.Equal(new[] { "first", "focused", "third", "new" }, harness.Group(ConversationStatusGroup.Working).Children
            .OfType<SessionNavItemViewModel>().Select(row => row.SessionId));
        Assert.Contains(harness.Row("other"), harness.Group(ConversationStatusGroup.NeedsAttention).Children);

        // Act
        harness.ViewModel.SetFocusedConversationId(null);

        // Assert
        Assert.Equal(new[] { "new", "third", "first" }, harness.Group(ConversationStatusGroup.Working).Children
            .OfType<SessionNavItemViewModel>().Select(row => row.SessionId));
        Assert.Contains(harness.Row("focused"), harness.Group(ConversationStatusGroup.Other).Children);
    }

    [Fact]
    public async Task FocusedConversation_ReceivesNewerDestination_AppliesOnlyLatestWhenFocusChanges()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        harness.Publish(Item("focused", ConversationStatusGroup.Working, ConversationStatusIcon.Working), Item("next", ConversationStatusGroup.Other));
        var row = harness.Row("focused");
        harness.ViewModel.SetFocusedConversationId("focused");
        harness.Publish(Item("focused", ConversationStatusGroup.Other), Item("next", ConversationStatusGroup.Other));

        // Act
        harness.Publish(Item("focused", ConversationStatusGroup.NeedsAttention, ConversationStatusIcon.Permission), Item("next", ConversationStatusGroup.Other));
        harness.ViewModel.SetFocusedConversationId("next");

        // Assert
        Assert.Contains(row, harness.Group(ConversationStatusGroup.NeedsAttention).Children);
        Assert.DoesNotContain(row, harness.Group(ConversationStatusGroup.Other).Children);
        Assert.Equal(ConversationStatusIcon.Permission, row.StatusIcon);
    }

    [Fact]
    public async Task FocusedConversation_PrecedingRowLeaves_PreservesSurvivingRowWithoutRelocation()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        harness.Publish(Item("preceding", ConversationStatusGroup.Working, ConversationStatusIcon.Working, 2),
            Item("focused", ConversationStatusGroup.Working, ConversationStatusIcon.Working, 1));
        harness.ViewModel.SetFocusedConversationId("focused");
        var row = harness.Row("focused");
        var source = harness.Group(ConversationStatusGroup.Working).Children;
        var relocatedFocusedContainer = false;
        source.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Move
                && args.OldItems?.Contains(row) == true) relocatedFocusedContainer = true;
        };

        // Act
        harness.Publish(Item("preceding", ConversationStatusGroup.Other), Item("focused", ConversationStatusGroup.Other));

        // Assert
        Assert.Same(row, Assert.Single(source));
        Assert.False(relocatedFocusedContainer);
    }

    [Fact]
    public async Task SetTransitionsFrozen_StatusChanges_HoldsFullMembershipAndConvergesToLatest()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        var sessions = Enumerable.Range(0, 25)
            .Select(index => Item($"session-{index:00}", ConversationStatusGroup.Working, ConversationStatusIcon.Working, -index))
            .ToArray();
        harness.Publish(sessions);
        var rendered = harness.Rows.Select(row => row.SessionId).ToArray();
        harness.ViewModel.SetTransitionsFrozen(true);

        // Act
        harness.Publish(sessions.Select(session => session with
        {
            StatusGroup = ConversationStatusGroup.NeedsAttention,
            StatusIcon = ConversationStatusIcon.Unread
        }).ToArray());

        // Assert
        Assert.Equal(25, harness.Group(ConversationStatusGroup.Working).Count);
        Assert.Equal(0, harness.Group(ConversationStatusGroup.NeedsAttention).Count);
        Assert.Equal(rendered, harness.Rows.Select(row => row.SessionId));
        Assert.All(harness.Rows, row => Assert.Equal(ConversationStatusIcon.Unread, row.StatusIcon));

        // Act: supersede the held destination before releasing the native interaction.
        harness.Publish(sessions.Select(session => session with
        {
            StatusGroup = ConversationStatusGroup.Other,
            StatusIcon = ConversationStatusIcon.Conversation
        }).ToArray());
        harness.ViewModel.SetTransitionsFrozen(false);

        // Assert
        Assert.Equal(25, harness.Group(ConversationStatusGroup.Other).Count);
        Assert.Equal(0, harness.Group(ConversationStatusGroup.Working).Count);
        Assert.Equal(0, harness.Group(ConversationStatusGroup.NeedsAttention).Count);
        Assert.Equal(20, harness.Rows.Count());
    }

    [Fact]
    public async Task SetTransitionsFrozen_RecencyCrossesPageBoundary_PreservesRenderedRows()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        var sessions = Enumerable.Range(0, 25)
            .Select(index => Item($"session-{index:00}", ConversationStatusGroup.Other, activityMinutes: -index))
            .ToArray();
        harness.Publish(sessions);
        var rendered = harness.Rows.ToArray();
        harness.ViewModel.SetTransitionsFrozen(true);

        // Act
        harness.Publish(sessions.Select((session, index) => session with { ActivityAt = Baseline.AddMinutes(index) }).ToArray());

        // Assert
        Assert.Equal(rendered, harness.Rows.Take(rendered.Length));
        Assert.Equal(25, harness.Group(ConversationStatusGroup.Other).Count);
        Assert.Equal(25, harness.Rows.Count());

        // Act
        harness.ViewModel.SetTransitionsFrozen(false);

        // Assert
        Assert.Equal(20, harness.Rows.Count());
        Assert.Equal("session-24", harness.Rows.First().SessionId);
    }

    [Fact]
    public async Task LoadingEnds_WithPendingStatusMove_ConvergesWithoutAnotherCatalogUpdate()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        harness.Publish(Item("session-1", ConversationStatusGroup.Working, ConversationStatusIcon.Working));
        harness.Display.SetLoading(true);
        harness.Publish(Item("session-1", ConversationStatusGroup.Other));
        Assert.Equal(1, harness.Group(ConversationStatusGroup.Working).Count);

        // Act
        harness.Display.SetLoading(false);

        // Assert
        Assert.Equal(0, harness.Group(ConversationStatusGroup.Working).Count);
        Assert.Equal(1, harness.Group(ConversationStatusGroup.Other).Count);
    }

    [Fact]
    public async Task Activation_InFlight_KeepsPageOutsideTargetVisibleAndReleasesPendingMoves()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        var sessions = Enumerable.Range(0, 25)
            .Select(index => Item($"session-{index:00}", ConversationStatusGroup.Working, ConversationStatusIcon.Working, -index))
            .ToArray();
        harness.Publish(sessions);
        harness.Runtime.LatestActivationToken = 1;
        harness.Runtime.ActiveSessionActivationVersion = 1;
        harness.Runtime.IsSessionActivationInProgress = true;

        // Act
        harness.Runtime.ActiveSessionActivation = new SessionActivationSnapshot(
            "session-24", "project-1", 1, SessionActivationPhase.SelectingConversation);
        harness.Publish(sessions.Select(item => item with
        {
            StatusGroup = ConversationStatusGroup.Other,
            StatusIcon = ConversationStatusIcon.Conversation
        }).ToArray());

        // Assert
        Assert.Equal("session-24", Assert.IsType<SessionNavItemViewModel>(harness.ViewModel.ProjectedControlSelectedItem).SessionId);
        Assert.Contains(harness.Row("session-24"), harness.Group(ConversationStatusGroup.Working).Children);
        Assert.Equal(25, harness.Group(ConversationStatusGroup.Working).Count);

        // Act
        harness.Selection.SetSelection(new NavigationSelectionState.Session("session-24"));
        harness.Runtime.IsSessionActivationInProgress = false;
        harness.Runtime.ActiveSessionActivation = harness.Runtime.ActiveSessionActivation with { Phase = SessionActivationPhase.Hydrated };

        // Assert
        Assert.Equal(25, harness.Group(ConversationStatusGroup.Other).Count);
        Assert.Equal(0, harness.Group(ConversationStatusGroup.Working).Count);
        Assert.Contains(harness.Row("session-24"), harness.Group(ConversationStatusGroup.Other).Children);
    }

    [Fact]
    public async Task SetTransitionsFrozen_ModeChanges_AppliesLatestModeOnceReleased()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        harness.Publish(Item("session-1", ConversationStatusGroup.Other));
        var row = harness.Row("session-1");
        harness.ViewModel.SetTransitionsFrozen(true);

        // Act
        harness.Preferences.SidebarConversationGrouping = AppSettingValueCatalog.ProjectConversationGrouping;

        // Assert
        Assert.True(harness.ViewModel.IsStatusGrouping);

        // Act
        harness.ViewModel.SetTransitionsFrozen(false);
        var rebuilds = 0;
        harness.ViewModel.TreeRebuilt += (_, _) => rebuilds++;
        harness.ViewModel.SetTransitionsFrozen(false);

        // Assert
        Assert.False(harness.ViewModel.IsStatusGrouping);
        Assert.Same(row, harness.Row("session-1"));
        Assert.Equal(0, rebuilds);
    }

    [Fact]
    public async Task MoreCommand_StatusGroup_RevealsNextBatchAndKeepsSelectedRow()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        harness.Publish(Enumerable.Range(0, 45)
            .Select(index => Item($"session-{index:00}", ConversationStatusGroup.Other, activityMinutes: -index)).ToArray());
        harness.Selection.SetSelection(new NavigationSelectionState.Session("session-44"));
        var selected = harness.Row("session-44");
        var group = harness.Group(ConversationStatusGroup.Other);
        var more = Assert.Single(group.Children.OfType<MoreSessionsNavItemViewModel>());

        // Act
        await more.ShowMoreCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(45, group.Count);
        Assert.Equal(41, group.Children.OfType<SessionNavItemViewModel>().Count());
        Assert.Equal(4, Assert.Single(group.Children.OfType<MoreSessionsNavItemViewModel>()).Count);
        Assert.Same(selected, harness.ViewModel.ProjectedControlSelectedItem);
        Assert.Equal(ConversationStatusGroup.Other, more.StatusGroup);
        Assert.Empty(harness.Ui.Invocations);

        // Act
        await more.ShowMoreCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(45, group.Children.Count);
        Assert.Empty(group.Children.OfType<MoreSessionsNavItemViewModel>());
        Assert.Equal(45, harness.Rows.Select(row => row.SessionId).Distinct().Count());
    }

    [Fact]
    public async Task GroupExpansion_NativeChanges_DoNotOverwriteUserPreference()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        harness.Publish(Item("session-1", ConversationStatusGroup.Working, ConversationStatusIcon.Working));
        var group = harness.Group(ConversationStatusGroup.Working);

        // Act
        group.IsExpanded = false;

        // Assert
        Assert.True(harness.Preferences.SidebarWorkingGroupExpanded);

        // Act
        group.ApplyUserExpandedPreference(false);
        group.IsExpanded = true;

        // Assert
        Assert.False(harness.Preferences.SidebarWorkingGroupExpanded);
        Assert.True(group.IsExpanded);
    }

    [Fact]
    public async Task GroupingPreference_ReturnsToStatus_RestoresExplicitExpansionChoice()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        harness.Publish(Item("session-1", ConversationStatusGroup.Working, ConversationStatusIcon.Working));
        var oldGroup = harness.Group(ConversationStatusGroup.Working);
        oldGroup.IsExpanded = false;
        var row = harness.Row("session-1");

        // Act
        harness.Preferences.SidebarConversationGrouping = AppSettingValueCatalog.ProjectConversationGrouping;
        harness.Preferences.SidebarConversationGrouping = AppSettingValueCatalog.StatusConversationGrouping;

        // Assert
        Assert.NotSame(oldGroup, harness.Group(ConversationStatusGroup.Working));
        Assert.True(harness.Group(ConversationStatusGroup.Working).IsExpanded);
        Assert.Same(row, harness.Row("session-1"));
    }

    [Fact]
    public async Task ProjectAffinity_Changes_UpdatesSameSessionRowAndActivationIdentity()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        harness.Preferences.Projects.Add(new ProjectDefinition { ProjectId = "project-2", Name = "Other project", RootPath = "/repo/other" });
        harness.Publish(Item("session-1", ConversationStatusGroup.Other));
        var row = harness.Row("session-1");

        // Act
        harness.Publish(Item("session-1", ConversationStatusGroup.Other) with { Cwd = "/repo/other" });

        // Assert
        Assert.Same(row, harness.Row("session-1"));
        Assert.Equal("project-2", row.ProjectId);
        Assert.Equal("Other project", row.ProjectDisplayName);
        Assert.Equal("project-2", harness.ViewModel.TryGetProjectIdForSession("session-1"));
    }

    [Fact]
    public async Task SessionRemoved_DuringInteractionHold_RemovesRowAndReleasesSubscription()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        harness.Publish(Item("session-1", ConversationStatusGroup.Working, ConversationStatusIcon.Working));
        var subscribers = harness.Pane.SubscriberCount;
        harness.ViewModel.SetTransitionsFrozen(true);

        // Act
        harness.Publish();

        // Assert
        Assert.Empty(harness.Rows);
        Assert.All(harness.Groups, group => Assert.Equal(0, group.Count));
        Assert.Equal(subscribers - 1, harness.Pane.SubscriberCount);
    }

    [Fact]
    public async Task Dispose_AfterModeSwitchAndPaging_ReleasesAllMaterializedRows()
    {
        // Arrange
        using var harness = await Harness.CreateAsync();
        harness.Publish(Enumerable.Range(0, 25)
            .Select(index => Item($"session-{index:00}", ConversationStatusGroup.Other, activityMinutes: -index)).ToArray());
        await harness.ViewModel.ShowMoreSessionsForStatusGroupAsync(ConversationStatusGroup.Other);
        harness.Preferences.SidebarConversationGrouping = AppSettingValueCatalog.ProjectConversationGrouping;
        Assert.True(harness.Pane.SubscriberCount > 0);

        // Act
        harness.ViewModel.Dispose();
        harness.Publish();

        // Assert
        Assert.Equal(0, harness.Pane.SubscriberCount);
        Assert.Empty(harness.ViewModel.Items);
    }

    [Theory]
    [InlineData(ConversationStatusGroup.NeedsAttention)]
    [InlineData(ConversationStatusGroup.Working)]
    [InlineData(ConversationStatusGroup.Other)]
    public void StatusTags_RoundTrip_DoNotParseAsProjects(ConversationStatusGroup group)
    {
        // Arrange
        var tag = NavItemTag.StatusGroup(group);
        var moreTag = NavItemTag.MoreStatusGroup(group);

        // Act / Assert
        Assert.True(NavItemTag.TryParseStatusGroup(tag, out var parsed));
        Assert.Equal(group, parsed);
        Assert.True(NavItemTag.TryParseMoreStatusGroup(moreTag, out parsed));
        Assert.Equal(group, parsed);
        Assert.False(NavItemTag.TryParseProject(tag, out _));
        Assert.False(NavItemTag.TryParseMore(moreTag, out _));
        Assert.False(NavItemTag.TryParseStatusGroup("StatusGroup:0", out _));
        Assert.False(NavItemTag.TryParseStatusGroup("StatusGroup:Unknown", out _));
    }

    private static ConversationCatalogDisplayItem Item(
        string id,
        ConversationStatusGroup group,
        ConversationStatusIcon icon = ConversationStatusIcon.Conversation,
        int activityMinutes = 0)
        => new(id, id, "/repo/demo", Baseline, Baseline, Baseline,
            HasUnreadAttention: icon == ConversationStatusIcon.Unread,
            StatusGroup: group, StatusIcon: icon, ActivityAt: Baseline.AddMinutes(activityMinutes));

    private sealed class Harness : IDisposable
    {
        private Harness(AppPreferencesViewModel preferences)
        {
            Preferences = preferences;
            ViewModel = new MainNavigationViewModel(
                new FakeChatSessionCatalog(), new NavigationProjectPreferencesAdapter(preferences),
                Ui.Object, Navigation.Object, NullLogger<MainNavigationViewModel>.Instance,
                Pane, Mock.Of<IShellLayoutMetricsSink>(), new NavigationSelectionProjector(),
                Selection, Runtime, Display, new ProjectAffinityResolver(),
                new ImmediateUiDispatcher(), new TestCoreStringLocalizer());
        }

        public MainNavigationViewModel ViewModel { get; }
        public AppPreferencesViewModel Preferences { get; }
        public DisplayReadModel Display { get; } = new();
        public PaneState Pane { get; } = new();
        public ShellSelectionStateStore Selection { get; } = new();
        public ShellNavigationRuntimeStateStore Runtime { get; } = new();
        public Mock<INavigationCoordinator> Navigation { get; } = new();
        public Mock<IUiInteractionService> Ui { get; } = new();
        public IEnumerable<StatusGroupNavItemViewModel> Groups => ViewModel.Items.OfType<StatusGroupNavItemViewModel>();
        public IEnumerable<SessionNavItemViewModel> Rows => ViewModel.Items.SelectMany(group => group.Children)
            .OfType<SessionNavItemViewModel>().Where(row => !row.IsPlaceholder);

        public static async Task<Harness> CreateAsync(string grouping = AppSettingValueCatalog.StatusConversationGrouping)
        {
            var settings = new Mock<IAppSettingsService>();
            settings.Setup(service => service.LoadAsync()).ReturnsAsync(new AppSettings
            {
                SidebarConversationGrouping = grouping,
                Projects = [new ProjectDefinition { ProjectId = "project-1", Name = "Demo", RootPath = "/repo/demo" }]
            });
            settings.Setup(service => service.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
            var preferences = new AppPreferencesViewModel(
                settings.Object, Mock.Of<IAppStartupService>(), Mock.Of<IAppLanguageService>(),
                Mock.Of<IPlatformCapabilityService>(), Mock.Of<IUiRuntimeService>(), Mock.Of<IUiInteractionService>(),
                new TestCoreStringLocalizer(), NullLogger<AppPreferencesViewModel>.Instance,
                new ImmediateUiDispatcher(), TestSystemNotificationService.Instance);
            await preferences.InitializeAsync(TestContext.Current.CancellationToken);
            return new Harness(preferences);
        }

        public void Publish(params ConversationCatalogDisplayItem[] items) => Display.Publish(items);
        public StatusGroupNavItemViewModel Group(ConversationStatusGroup group) => Groups.Single(item => item.Group == group);
        public SessionNavItemViewModel Row(string id) => Rows.Single(row => row.SessionId == id);
        public void Dispose() => ViewModel.Dispose();
    }

    private sealed class DisplayReadModel : IConversationCatalogDisplayReadModel
    {
        public bool IsConversationListLoading { get; private set; }
        public int ConversationListVersion { get; private set; }
        public IReadOnlyList<ConversationCatalogDisplayItem> Snapshot { get; private set; } = [];
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Publish(IReadOnlyList<ConversationCatalogDisplayItem> items)
        {
            Snapshot = items;
            ConversationListVersion++;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Snapshot)));
        }

        public void SetLoading(bool loading)
        {
            IsConversationListLoading = loading;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConversationListLoading)));
        }
    }

    private sealed class PaneState : INavigationPaneState
    {
        private EventHandler? _changed;
        public bool IsPaneOpen { get; private set; } = true;
        public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

        public event EventHandler? PaneStateChanged
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public void SetOpen(bool open)
        {
            IsPaneOpen = open;
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

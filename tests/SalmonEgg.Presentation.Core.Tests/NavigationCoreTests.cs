using System;
using System.IO;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Tests;

public sealed class NavigationCoreTests
{
    [Fact]
    public void NavTimeFormatter_ToRelativeText_UsesExpectedBuckets()
    {
        var now = DateTime.UtcNow;

        Assert.Equal("刚刚", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromSeconds(30)));
        Assert.Equal("2 分", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromMinutes(2)));
        Assert.Equal("3 小时", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromHours(3)));
        Assert.Equal("2 天", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromDays(2)));
    }

    [Fact]
    public void NavTimeFormatter_NormalizePathForPrefixMatch_AppendsSeparator()
    {
        var path = Path.Combine("C:", "Temp", "Demo");
        var normalized = NavTimeFormatter.NormalizePathForPrefixMatch(path);

        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), normalized, StringComparison.Ordinal);
        Assert.Contains("Demo", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NavItemTag_MoreTag_RoundTrips()
    {
        var tag = NavItemTag.More("proj-9");

        Assert.True(NavItemTag.TryParseMore(tag, out var projectId));
        Assert.Equal("proj-9", projectId);
    }

    [Fact]
    public void NavItemTag_SessionTag_RoundTrips()
    {
        var tag = NavItemTag.Session("session-42");

        Assert.True(NavItemTag.TryParseSession(tag, out var sessionId));
        Assert.Equal("session-42", sessionId);
    }

    [Fact]
    public void NavItemTag_ProjectTag_RoundTrips()
    {
        var tag = NavItemTag.Project("project-7");

        Assert.True(NavItemTag.TryParseProject(tag, out var projectId));
        Assert.Equal("project-7", projectId);
    }

    [Fact]
    public void NavItemTag_ParseRejectsInvalid()
    {
        Assert.False(NavItemTag.TryParseSession("Session:", out _));
        Assert.False(NavItemTag.TryParseSession("Other:123", out _));
        Assert.False(NavItemTag.TryParseProject("Project:", out _));
        Assert.False(NavItemTag.TryParseProject("Other:123", out _));
        Assert.False(NavItemTag.TryParseMore("More:", out _));
        Assert.False(NavItemTag.TryParseMore("Other:123", out _));
    }

    [Fact]
    public void MainPage_DoesNotMapFramePagesToShellNavigationContentDirectly()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.DoesNotContain("ShellNavigationContent.", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SyncNavSelectionFromCurrentPage(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_DoesNotImperativelyProjectNavigationPaneState()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.DoesNotContain("MainNavView.PaneDisplayMode =", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MainNavView.IsPaneOpen =", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveNavigationViewPaneDisplayMode(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_DoesNotOwnNavigationViewPaneSuppressionStateMachine()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.DoesNotContain("_suppressNextPaneIntentFromDisplayModeTransition", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_suppressProjectExpansionSyncFromDisplayModeTransition", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ReapplyNavPaneProjectionDeferred(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShouldSyncProjectExpansion(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_DoesNotKeepNavigationCoordinatorAsCodeBehindState()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.DoesNotContain("private readonly INavigationCoordinator _navigationCoordinator;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_navigationCoordinator = App.ServiceProvider.GetRequiredService<INavigationCoordinator>();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_DoesNotImperativelyMutateNavigationSelectionOrInvokeCoordinator()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.DoesNotContain("MainNavView.SelectedItem =", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_navigationCoordinator.Activate", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_navigationCoordinator.SyncSelectionFromShellContent", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".SetSelection(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_DoesNotBackWriteSelectionFromFrameNavigation()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.DoesNotContain("MainNavigationContentSyncAdapter", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_mainNavigationContentSyncAdapter", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".OnFrameNavigated(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SyncSelectionFromShellContent", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationViewModel_DoesNotExposeLegacySelectedItemAlias()
    {
        var code = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs");

        Assert.DoesNotContain("public object? SelectedItem =>", code, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(SelectedItem)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationViewModel_DoesNotContainDisplayModeTransitionHacks()
    {
        var code = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs");

        // These methods were hack workarounds for NavigationView ancestor visual issues.
        // The correct fix is to let NavigationView handle ancestor visuals natively
        // by keeping SelectedItem on the leaf and not interfering during transitions.
        Assert.DoesNotContain("ReassertSelectionProjection", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearAndDeferRestore", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearAndRestoreSelectionProjection", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ReassertExpandedProjects", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationViewModel_DoesNotDependOnSelectionProjectionApplyGate()
    {
        var code = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs");

        Assert.DoesNotContain("SelectionProjectionApplyGate", code, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginSelectionInteraction", code, StringComparison.Ordinal);
        Assert.DoesNotContain("EndSelectionInteractionDeferred", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_DoesNotContainDisplayModeTransitionHacks()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.DoesNotContain("ClearAndDeferRestore", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearAndRestoreSelectionProjection", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ReassertSelectionProjection", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_displayModeTransitionVersion", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LayoutUpdated += OnLayoutSettled", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_DoesNotRouteLeftNavPaneLifecycleThroughCustomPolicies()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.DoesNotContain("HandlePanePresentationChanged(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShellPanePolicy.ShouldCancelClosing(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("OnMainNavPaneClosing(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewCodeBehind_DoesNotUseLegacyViewportDriftDetachHeuristic()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");

        Assert.DoesNotContain("_lastObservedViewportAtBottom is true && !_transcriptScrollSettler.HasPendingWork", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptAutoFollowController", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewCodeBehind_DoesNotForceSynchronousListLayoutDuringTranscriptSettle()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");

        Assert.DoesNotContain(".UpdateLayout()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollIntoView(ViewModel.MessageHistory.Last());\r\n            MessagesList.UpdateLayout();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewCodeBehind_DoesNotTreatPointerPressedAsViewportDetachIntent()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var section = ExtractSection(
            code,
            "private void OnMessagesListPointerPressed",
            "private void OnMessagesListPointerWheelChanged");

        Assert.Contains("FocusTranscriptScroller();", section, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterUserViewportIntent();", section, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationSelectionProjector_DoesNotSwapLeafSelectionForAncestorOnClosedPane()
    {
        var code = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\NavigationSelectionProjector.cs");

        Assert.DoesNotContain("project the selected ancestor", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("controlSelectedItem", code, StringComparison.Ordinal);
        Assert.DoesNotContain("? sessionItem", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationXaml_ExposesStableAutomationIds_ForGuiTesting()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"MainNavView\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"TitleBar.ToggleSidebar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.StartItem()", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.SessionsLabel()", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.AddProject()", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.ProjectItem(ProjectId)", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.SessionItem(SessionId)", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.MoreItem(ProjectId)", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationXaml_DoesNotOverrideNativeChildSelectionProjection()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("IsChildSelected=\"{x:Bind IsActiveDescendant, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationXaml_UsesNativeAutoPaneDisplayMode()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("PaneDisplayMode=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CompactModeThresholdWidth=\"640\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ExpandedModeThresholdWidth=\"1000\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("NavPaneDisplayModeConverter", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationXaml_BindsNativeSelectedItemToProjectedControlSelection()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains(
            "SelectedItem=\"{x:Bind NavVM.ProjectedControlSelectedItem, Mode=OneWay}\"",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_TreeRebuildReliesOnProjectedSelectionBindingInsteadOfImperativeSelectedItemWriteback()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("NavVM.TreeRebuilt += OnNavigationTreeRebuilt;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MainNavView.SelectedItem = NavVM.ProjectedControlSelectedItem;", code, StringComparison.Ordinal);
        Assert.Contains("UpdateMainNavAutomationSelectionState();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationViewAdapter_ItemInvoked_OwnsDestinationActivationPath()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Navigation\MainNavigationViewAdapter.cs");
        var section = ExtractSection(code, "private Task<bool> HandleItemInvokedCoreAsync");

        Assert.Contains("ActivateSettingsAsync", section, StringComparison.Ordinal);
        Assert.Contains("ActivateStartAsync", section, StringComparison.Ordinal);
        Assert.Contains("ActivateDiscoverSessionsAsync", section, StringComparison.Ordinal);
        Assert.Contains("ActivateSessionAsync", section, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationViewAdapter_SelectionChanged_DoesNotActivateNavigationDestinations()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Navigation\MainNavigationViewAdapter.cs");
        var section = ExtractSection(
            code,
            "public Task HandleSelectionChangedAsync",
            "private Task<bool> HandleItemInvokedCoreAsync");

        Assert.DoesNotContain("ActivateStartAsync", section, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivateDiscoverSessionsAsync", section, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivateSessionAsync", section, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationViewAdapter_SelectionChanged_DoesNotDependOnProjectedSelectionEcho()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Navigation\MainNavigationViewAdapter.cs");
        var section = ExtractSection(
            code,
            "public Task HandleSelectionChangedAsync",
            "private Task<bool> HandleItemInvokedCoreAsync");

        Assert.DoesNotContain("IsProjectedSelectionEcho", section, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectedControlSelectedItem", section, StringComparison.Ordinal);
        Assert.Contains("return Task.CompletedTask;", section, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionsDialogXaml_ExposesStableAutomationIds_ForGuiTesting()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\SessionsListDialog.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"SessionsDialog\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SessionsDialog.SearchBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SessionsDialog.List\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.SessionsDialogItem(SessionId)", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartViewXaml_ExposesStableAutomationIds_ForGuiTesting()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"StartView.Title\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartViewXaml_ExposesSharedAgentSelector_ForNewSessionLaunch()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"StartView.AgentSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.Chat.AcpProfileList, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{x:Bind ViewModel.Chat.SelectedAcpProfile, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartViewXaml_ExposesProjectSelector_ForNewSessionLaunch()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"StartView.ProjectSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.StartProjectOptions, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{x:Bind ViewModel.SelectedStartProjectId, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValuePath=\"ProjectId\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DisplayMemberPath=\"DisplayName\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_ExposesStableAutomationIds_ForGuiTesting()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.ActiveRoot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.CurrentSessionNameButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.CurrentSessionNameEditor\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.CurrentAgentDisplay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.MessagesList\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPageXaml_UsesAutomationCapableChatLoadingOverlayRoot()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.LoadingOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ContentControl x:Name=\"ShellLoadingOverlayPresenter\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"{x:Bind ShellOverlayVM.ShowsBlockingMask, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind ShellOverlayVM.ShowsPresenter, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind ShellOverlayVM.StatusText, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatVM.ShouldShowBlockingLoadingMask", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatVM.ShouldShowLoadingOverlayPresenter", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatVM.ShouldShowLoadingOverlayStatusPill", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatVM.OverlayStatusText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Grid AutomationProperties.AutomationId=\"ChatView.LoadingOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.LoadingOverlayStatus\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.LiveSetting=\"Assertive\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_DoesNotOwnMainWindowLoadingOverlay()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.DoesNotContain("AutomationProperties.AutomationId=\"ChatView.LoadingOverlay\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatView.LoadingOverlayStatus", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ChatView.LoadingOverlayMask", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"ChatViewLoadingOverlay\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_DoesNotContainLegacyInactiveAgentSetupPlaceholder()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.DoesNotContain("AutomationProperties.AutomationId=\"ChatView.InactiveRoot\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.AutomationId=\"ChatView.GoToSettingsButton\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("准备好开始了吗？", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("去配置 Agent", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_DoesNotExposeAgentSwitchSelectorInHeader()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.DoesNotContain("SelectedItem=\"{x:Bind ViewModel.SelectedAcpProfile, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"ChatAcpProfileSelector\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewCodeBehind_DoesNotCallMessagesListUpdateLayoutDuringTranscriptSettle()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");

        Assert.DoesNotContain("MessagesList.UpdateLayout()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewCodeBehind_UsesRestoreAnchorCommandAndDoesNotFallbackToBottomOnRestoreFailure()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var restoreAnchorSection = ExtractSection(
            code,
            "case TranscriptViewportCommandKind.RestoreAnchor:",
            "case TranscriptViewportCommandKind.MarkAutoFollowDetached:");

        Assert.Contains("case TranscriptViewportCommandKind.RestoreAnchor:", code, StringComparison.Ordinal);
        Assert.Contains("TryRestoreViewportAnchor(anchor);", restoreAnchorSection, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleScrollToBottom();", restoreAnchorSection, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestScrollToBottom();", restoreAnchorSection, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewCodeBehind_UsesUserDetachedEventWhenAnchorCaptureSucceeds()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var detachSection = ExtractSection(
            code,
            "private void RegisterUserViewportDetachment()",
            "private TranscriptViewportAnchor? TryCaptureViewportAnchor()");

        Assert.Contains("new TranscriptViewportEvent.UserDetached(", detachSection, StringComparison.Ordinal);
        Assert.Contains("new TranscriptViewportEvent.UserIntentScroll(", detachSection, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewCodeBehind_PointerPendingDetachUsesSharedDetachedRoute()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var pointerPendingSection = ExtractSection(
            code,
            "if (_pointerScrollIntentPending && !fact.IsProgrammaticScrollInFlight)",
            "ApplyViewportCommand(_viewportCoordinator.Handle(new TranscriptViewportEvent.ViewportFactChanged(");

        Assert.Contains("RegisterUserViewportDetachment();", pointerPendingSection, StringComparison.Ordinal);
        Assert.DoesNotContain("new TranscriptViewportEvent.UserIntentScroll(", pointerPendingSection, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewCodeBehind_WarmAndOverlayResumeActivateCoordinatorInsteadOfRedetach()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var overlayResumeSection = ExtractSection(
            code,
            "private void ResumeViewportCoordinatorAfterOverlayIfNeeded()",
            "private void RestoreViewportForWarmResume()");

        Assert.Contains("ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind.WarmReturn);", code, StringComparison.Ordinal);
        Assert.Contains("ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind.OverlayResume);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new TranscriptViewportEvent.UserIntentScroll(", overlayResumeSection, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewModel_DoesNotEnforceArtificialSessionSwitchDelay()
    {
        var code = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatViewModel.cs");

        Assert.DoesNotContain("TimeSpan.FromMilliseconds(600)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("premium", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChatViewModel_MainPartial_StaysBelowFourThousandLines()
    {
        var code = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatViewModel.cs");
        var lineCount = code.Split(["\r\n", "\n"], StringSplitOptions.None).Length;

        Assert.True(lineCount < 4000, $"ChatViewModel.cs should stay below 4000 lines, actual: {lineCount}.");
    }

    [Fact]
    public void MainNavigationXaml_UsesNativeNavigationViewItemHeaderForSessionsLabel()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        // SessionsLabel should use NavigationViewItemHeader, not NavigationViewItem
        Assert.Contains("<NavigationViewItemHeader Content=\"{x:Bind Title, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MainNavigationAutomationIds.SessionsLabel()", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationXaml_AddProjectUsesStaticAddIcon()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("MainNavigationAutomationIds.AddProject()", xaml, StringComparison.Ordinal);
        Assert.Contains("<SymbolIcon Symbol=\"Add\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("NavItemTag.AddProject", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationXaml_ProjectItemsAreGroupingOnly()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("Tag=\"{x:Bind navModels:NavItemTag.Project(ProjectId)}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectsOnInvoked=\"False\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationXaml_DoesNotHookPaneClosingOverride()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("PaneClosing=\"OnMainNavPaneClosing\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationShell_CompensationPolicyFiles_AreRemoved()
    {
        var root = FindRepoRoot();

        Assert.False(
            File.Exists(Path.Combine(root, @"src\SalmonEgg.Application\Common\Shell\NavigationViewPanePresentationPolicy.cs")),
            "NavigationViewPanePresentationPolicy.cs must remain removed. Do not reintroduce pane compensation policies.");
        Assert.False(
            File.Exists(Path.Combine(root, @"src\SalmonEgg.Application\Common\Shell\ShellPanePolicy.cs")),
            "ShellPanePolicy.cs must remain removed. Do not reintroduce pane closing suppression policies.");
        Assert.False(
            File.Exists(Path.Combine(root, @"SalmonEgg\SalmonEgg\Presentation\Converters\NavigationPaneDisplayModeConverter.cs")),
            "NavigationPaneDisplayModeConverter.cs must remain removed. PaneDisplayMode should stay native Auto.");
        Assert.False(
            File.Exists(Path.Combine(root, @"SalmonEgg\SalmonEgg\Presentation\Navigation\MainNavigationContentSyncAdapter.cs")),
            "MainNavigationContentSyncAdapter.cs must remain removed. Frame navigation must not back-write shell selection.");
        Assert.False(
            File.Exists(Path.Combine(root, @"src\SalmonEgg.Presentation.Core\Services\Navigation\SelectionProjectionApplyGate.cs")),
            "SelectionProjectionApplyGate.cs must remain removed. Selection projection must stay state-driven.");
    }

    [Fact]
    public void MainNavigation_SessionFlyout_DefersDialogCommandsUntilFlyoutCloses()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("x:Uid=\"SessionNavMoveItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnSessionMoveMenuItemClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"SessionNavMoveItem\"\r\n                                        Command=\"{x:Bind MoveCommand}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("x:Uid=\"SessionNavRenameItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnSessionRenameMenuItemClick\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"SessionNavRenameItem\"\r\n                                        Command=\"{x:Bind RenameCommand}\"", xaml, StringComparison.Ordinal);

        Assert.Contains("private void OnSessionMoveMenuItemClick(", code, StringComparison.Ordinal);
        Assert.Contains("private void OnSessionRenameMenuItemClick(", code, StringComparison.Ordinal);
        Assert.Contains("_moveOnFlyoutClosed.TryConsume(sessionId)", code, StringComparison.Ordinal);
        Assert.Contains("_renameOnFlyoutClosed.TryConsume(sessionId)", code, StringComparison.Ordinal);
    }

    private static string LoadFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string ExtractSection(string content, string startMarker, string? endMarker = null)
    {
        var start = content.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Unable to locate marker '{startMarker}'.");

        var end = endMarker is null
            ? content.Length
            : content.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = content.Length;
        }

        return content.Substring(start, end - start);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SalmonEgg.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root (SalmonEgg.sln) not found.");
    }
}

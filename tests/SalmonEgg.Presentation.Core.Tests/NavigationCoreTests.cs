using System;
using System.Collections.Generic;
using System.IO;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Core.Services.Navigation;
using SalmonEgg.Presentation.Services;
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
    public void CoreStrings_ProvidesEnglishSettingsNavigationLabel()
    {
        var root = FindRepoRoot();
        var en = File.ReadAllText(Path.Combine(root, NormalizeRelativePath(@"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.en.resx")));
        var enUs = File.ReadAllText(Path.Combine(root, NormalizeRelativePath(@"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.en-US.resx")));

        Assert.Contains("<data name=\"Nav_Settings\"", en, StringComparison.Ordinal);
        Assert.Contains("<value>Settings</value>", en, StringComparison.Ordinal);
        Assert.Contains("<data name=\"Nav_Settings\"", enUs, StringComparison.Ordinal);
        Assert.Contains("<value>Settings</value>", enUs, StringComparison.Ordinal);
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
    public void MainPage_ActivatesInitialContentFromLoadedLifecycle()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var constructorSection = ExtractSection(code, "public MainPage()", "private async void OnAutomationArchiveSelectedClick");
        var loadedSection = ExtractSection(code, "private async void OnMainPageLoaded", "private void AttachGamepadInput");

        Assert.DoesNotContain("EnsureStartContent", constructorSection, StringComparison.Ordinal);
        Assert.Contains("await _startupNavigation.ActivateInitialContentAsync().ConfigureAwait(true);", loadedSection, StringComparison.Ordinal);
        Assert.DoesNotContain("_navigationCoordinator.ActivateStartAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_StartupFocusSeed_UsesConcreteNavigationTargetInsteadOfNavigationRoot()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var loadedSection = ExtractSection(code, "private async void OnMainPageLoaded", "private void AttachGamepadInput");

        Assert.Contains("TryMoveFocusFromCurrentContentIntoMainNavigation();", loadedSection, StringComparison.Ordinal);
        Assert.DoesNotContain("MainNavView.Focus(FocusState.Programmatic);", loadedSection, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_StartupFocusSeed_IsScheduledBeforeChatRestore()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var loadedSection = ExtractSection(code, "private async void OnMainPageLoaded", "private void AttachGamepadInput");
        var focusSeedIndex = loadedSection.IndexOf("TryMoveFocusFromCurrentContentIntoMainNavigation();", StringComparison.Ordinal);
        var restoreIndex = loadedSection.IndexOf("await _chatViewModel.RestoreAsync();", StringComparison.Ordinal);

        Assert.True(focusSeedIndex >= 0, "MainPage should schedule a concrete startup navigation focus seed.");
        Assert.True(restoreIndex >= 0, "MainPage should still restore chat state during load.");
        Assert.True(focusSeedIndex < restoreIndex, "MainPage should seed startup navigation focus before long-running chat restore.");
    }

    [Fact]
    public void DependencyInjection_ShellStartupNavigationService_IsScopedToShellInstance()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");
        var section = ExtractSection(
            code,
            "services.AddTransient<IShellStartupNavigationService>",
            "// Global search");

        Assert.Contains("new ShellStartupNavigationService(", section, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSingleton<IShellStartupNavigationService>", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjection_NavigationCoordinator_UsesDiscoverConnectionFacade()
    {
        var dependencyInjection = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");
        var navigationCoordinator = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\NavigationCoordinator.cs");
        var section = ExtractSection(
            dependencyInjection,
            "services.AddSingleton<INavigationCoordinator>",
            "services.AddTransient<IShellStartupNavigationService>");

        Assert.Contains("sp.GetRequiredService<IDiscoverSessionsConnectionFacade>()", section, StringComparison.Ordinal);
        Assert.DoesNotContain("NoOpDiscoverSessionsConnectionFacade", navigationCoordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void AcpMcpRuntime_DoesNotExposeFallbackCatalogSources()
    {
        var dependencyInjection = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");
        var root = FindRepoRoot();
        var providerCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Chat\IAcpMcpServerProvider.cs");
        var availabilityPolicy = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Chat\IAcpAvailabilityPolicy.cs");
        var availabilityAdapter = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Services\AppPreferencesAcpAvailabilityPolicy.cs");
        var evictionBridge = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Services\AcpConnectionEvictionOptionsBridge.cs");
        var appPreferences = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\Settings\AppPreferencesViewModel.cs");
        var chatCoordinator = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Chat\AcpChatCoordinator.cs");
        var chatLaunchWorkflow = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Chat\ChatLaunchWorkflow.cs");
        var commandOrchestrator = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Chat\AcpSessionCommandOrchestrator.cs");
        var connectionCoordinator = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Chat\AcpConnectionCoordinator.cs");
        var chatViewModel = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatViewModel.cs");
        var connectionState = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Chat\IAcpConnectionState.cs");

        Assert.DoesNotContain("EmptyAcpMcpServerProvider", providerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("SinkSnapshotAcpMcpServerResolver", providerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("SinkSnapshotAcpMcpServerResolver", chatCoordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("SinkSnapshotAcpMcpServerResolver", commandOrchestrator, StringComparison.Ordinal);
        Assert.DoesNotContain("SinkSnapshotAcpMcpServerResolver", connectionCoordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("SinkSnapshotAcpMcpServerResolver", chatViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveCurrentMcpServersAsync(CancellationToken", connectionState, StringComparison.Ordinal);
        Assert.DoesNotContain("ServerConfiguration? profile", providerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMcpServersAsync(profile", providerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Presentation.ViewModels.Settings", availabilityPolicy, StringComparison.Ordinal);
        Assert.DoesNotContain("AppPreferencesAcpAvailabilityPolicy", availabilityPolicy, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            root,
            NormalizeRelativePath(@"src\SalmonEgg.Presentation.Core\Services\Chat\AcpConnectionEvictionOptionsBridge.cs"))));
        Assert.DoesNotContain("new AcpSessionCommandOrchestrator(", chatCoordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("AppPreferencesViewModel", chatLaunchWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ObservableObject, IAcpAvailabilityPolicy", appPreferences, StringComparison.Ordinal);
        Assert.Contains("class AppPreferencesAcpAvailabilityPolicy : IAcpAvailabilityPolicy", availabilityAdapter, StringComparison.Ordinal);
        Assert.Contains("class AcpConnectionEvictionOptionsBridge : IDisposable", evictionBridge, StringComparison.Ordinal);
        Assert.Contains("sp.GetRequiredService<IAcpMcpServerProvider>()", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("sp.GetRequiredService<IAcpSessionCommandOrchestrator>()", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("new AppPreferencesAcpAvailabilityPolicy(sp.GetRequiredService<AppPreferencesViewModel>())", dependencyInjection, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjection_AcpEvictionOptions_DoesNotLoadAppSettingsInSingletonFactory()
    {
        var dependencyInjection = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");
        var section = ExtractSection(
            dependencyInjection,
            "services.AddSingleton(sp =>\n            AcpConnectionEvictionOptionsLoader",
            "services.AddSingleton<AcpConnectionEvictionOptionsBridge>();");

        Assert.Contains("AcpConnectionEvictionOptionsLoader.LoadEnvironmentDefaults", section, StringComparison.Ordinal);
        Assert.DoesNotContain("IAppSettingsService", section, StringComparison.Ordinal);
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
    public void RemoteDirectoryProjectIdPrefix_IsOwnedByProjectSelectionCwdResolver()
    {
        var root = FindRepoRoot();
        var ownerPath = Path.Combine(
            root,
            NormalizeRelativePath(@"src\SalmonEgg.Presentation.Core\Services\ProjectSelectionCwdResolver.cs"));
        var ownerCode = File.ReadAllText(ownerPath);

        Assert.Contains("RemoteDirectoryProjectIdPrefix = \"remote-directory:\"", ownerCode, StringComparison.Ordinal);

        foreach (var path in EnumerateProductionCSharpFiles(root))
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(ownerPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var code = File.ReadAllText(path);
            Assert.DoesNotContain("remote-directory:", code, StringComparison.Ordinal);
        }
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
    public void ChatViewCodeBehind_RegistersPointerViewportInputThroughHandledEventsPath()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var loadSection = ExtractSection(
            code,
            "private void OnMessagesListLoaded",
            "private void OnMessagesListUnloaded");
        var unloadSection = ExtractSection(
            code,
            "private void OnMessagesListUnloaded",
            "private void OnMessagesListLayoutUpdated");

        Assert.Contains("AddHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler, true);", loadSection, StringComparison.Ordinal);
        Assert.Contains("AddHandler(UIElement.PointerPressedEvent, _messagesListHandledPointerPressedHandler, true);", loadSection, StringComparison.Ordinal);
        Assert.Contains("AddHandler(UIElement.PointerWheelChangedEvent, _messagesListHandledPointerWheelChangedHandler, true);", loadSection, StringComparison.Ordinal);
        Assert.Contains("MessagesList?.RemoveHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler);", unloadSection, StringComparison.Ordinal);
        Assert.Contains("MessagesList?.RemoveHandler(UIElement.PointerPressedEvent, _messagesListHandledPointerPressedHandler);", unloadSection, StringComparison.Ordinal);
        Assert.Contains("MessagesList?.RemoveHandler(UIElement.PointerWheelChangedEvent, _messagesListHandledPointerWheelChangedHandler);", unloadSection, StringComparison.Ordinal);
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
    public void MainNavigationXaml_HidesBuiltInSettingsItem_WhenSettingsLivesInFooterSelectionModel()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("IsSettingsVisible=\"False\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_DoesNotBridgeSettingsThroughControlSpecificSelectedItemWriteback()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.DoesNotContain("MainNavControlSelectedItem", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MainNavView.SettingsItem", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_TreeRebuildReliesOnProjectedSelectionBindingInsteadOfImperativeSelectedItemWriteback()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("NavVM.TreeRebuilt += OnNavigationTreeRebuilt;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MainNavView.SelectedItem =", code, StringComparison.Ordinal);
        Assert.Contains("UpdateMainNavAutomationSelectionState();", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_NavigationCompletionReliesOnFrameEventsAndProjectedContent()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var adapter = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Navigation\ContentFrameNavigationAdapter.cs");
        var tracker = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Navigation\ContentNavigationRequestTracker.cs");

        Assert.Contains("new ContentFrameNavigationAdapter(ContentFrame)", code, StringComparison.Ordinal);
        Assert.Contains("_contentNavigation.NavigateAsync(pageType, parameter, activationToken)", code, StringComparison.Ordinal);
        Assert.Contains("_contentNavigation.NavigationCompleted += OnContentFrameNavigationCompleted;", code, StringComparison.Ordinal);
        Assert.Contains("_frame.Navigating += OnFrameNavigating;", adapter, StringComparison.Ordinal);
        Assert.Contains("_frame.Navigated += OnFrameNavigated;", adapter, StringComparison.Ordinal);
        Assert.Contains("_frame.NavigationFailed += OnFrameNavigationFailed;", adapter, StringComparison.Ordinal);
        Assert.Contains("_requests.TryResolveNavigating(e.SourcePageType, e.Parameter, out var cancel)", adapter, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = cancel;", adapter, StringComparison.Ordinal);
        Assert.Contains("request.Matches(pageType, parameter)", tracker, StringComparison.Ordinal);
        Assert.Contains("RememberPendingFrameRequest", tracker, StringComparison.Ordinal);
        Assert.Contains("ConsumePendingFrameRequest(pageType)", tracker, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Parameter", ExtractSection(adapter, "private void OnFrameNavigationFailed", "private ShellNavigationResult CompleteCurrentRequest"), StringComparison.Ordinal);
        Assert.Contains("ShellNavigationResult.Failed(\"StaleNavigation\")", tracker, StringComparison.Ordinal);
        Assert.Contains("ShellNavigationResult.Failed(\"ContentNotProjected\")", tracker, StringComparison.Ordinal);
        Assert.Contains("pageType.IsInstanceOfType(_frame.Content)", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentFrame.Navigated +=", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private void EnsureStartContent(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private void EnsureChatContent(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private void EnsureDiscoverSessionsContent(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private void EnsureSettingsContent(", code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentNavigationRequestTracker_FailedNavigationWithoutParameterFailsMatchingActiveRequest()
    {
        var tracker = new ContentNavigationRequestTracker();
        var request = tracker.BeginRequest(typeof(TestPageA), "settings", activationToken: 1);
        request.Completion = new TaskCompletionSource<ShellNavigationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var navigationMatched = tracker.TryResolveNavigating(typeof(TestPageA), "settings", out var cancel);
        var failure = tracker.ResolveNavigationFailed(typeof(TestPageA));

        Assert.True(navigationMatched);
        Assert.False(cancel);
        Assert.Equal(ContentNavigationFailureKind.Active, failure.Kind);
        Assert.Same(request, failure.Request);

        var result = request.Complete(ShellNavigationResult.Failed("InvalidOperationException"));
        Assert.False(result.Succeeded);
        Assert.Equal("InvalidOperationException", result.FailureReason);
        Assert.True(request.Completion.Task.IsCompletedSuccessfully);
        Assert.Equal(result, await request.Completion.Task);
    }

    [Fact]
    public async Task ContentNavigationRequestTracker_FailedSupersededNavigationDoesNotCorruptLatestActiveRequest()
    {
        var tracker = new ContentNavigationRequestTracker();
        var first = tracker.BeginRequest(typeof(TestPageA), "first", activationToken: 1);
        first.Completion = new TaskCompletionSource<ShellNavigationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var firstNavigationMatched = tracker.TryResolveNavigating(typeof(TestPageA), "first", out var firstCancel);
        var latest = tracker.BeginRequest(typeof(TestPageB), "latest", activationToken: 2);
        latest.Completion = new TaskCompletionSource<ShellNavigationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var failure = tracker.ResolveNavigationFailed(typeof(TestPageA));
        var latestCompletion = tracker.ResolveNavigated(typeof(TestPageB), "latest");

        Assert.True(firstNavigationMatched);
        Assert.False(firstCancel);
        Assert.Equal(ContentNavigationFailureKind.Stale, failure.Kind);
        Assert.Same(first, failure.Request);
        Assert.True(first.Completion.Task.IsCompletedSuccessfully);
        var firstResult = await first.Completion.Task;
        Assert.False(firstResult.Succeeded);
        Assert.Equal("StaleNavigation", firstResult.FailureReason);

        Assert.Equal(ContentNavigationCompletionKind.Active, latestCompletion.Kind);
        Assert.Same(latest, latestCompletion.Request);

        var latestResult = tracker.CompleteActive(latest, isDisplaying: true);
        Assert.True(latestResult.Succeeded);
        Assert.True(latest.Completion.Task.IsCompletedSuccessfully);
        Assert.Equal(latestResult, await latest.Completion.Task);
    }

    [Fact]
    public void FolderPickerCapability_DoesNotFallbackToManualPathInputWhenUnsupported()
    {
        var uiService = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Services\UiInteractionService.cs");
        var navigationViewModel = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\Navigation\MainNavigationViewModel.cs");
        var dependencyInjection = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");

        var unsupportedCheckIndex = uiService.IndexOf("if (!_folderPicker.IsSupported)", StringComparison.Ordinal);
        var promptFallbackIndex = uiService.IndexOf("return await PromptTextAsync(", StringComparison.Ordinal);

        Assert.True(unsupportedCheckIndex >= 0, "Folder picker support must be checked before UI fallback.");
        Assert.True(promptFallbackIndex > unsupportedCheckIndex, "Manual path input must not run before capability gating.");
        Assert.Contains("return null;", uiService.Substring(unsupportedCheckIndex, promptFallbackIndex - unsupportedCheckIndex), StringComparison.Ordinal);
        Assert.Contains("public bool CanAddProject => _ui.CanPickFolder;", navigationViewModel, StringComparison.Ordinal);
        Assert.Contains("new AsyncRelayCommand(AddProjectAsync, () => CanAddProject)", navigationViewModel, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IFolderPickerService, UnavailableFolderPickerService>();", dependencyInjection, StringComparison.Ordinal);
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
        Assert.DoesNotContain("_ = _navigationCoordinator.ActivateSessionAsync", section, StringComparison.Ordinal);
        Assert.Contains("AwaitActivationHandledAsync(_navigationCoordinator.ActivateSessionAsync", section, StringComparison.Ordinal);
        Assert.Contains("return await activationTask.ConfigureAwait(true);", section, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationViewAdapter_DoesNotHandleSelectionChangedAsNavigationInput()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Navigation\MainNavigationViewAdapter.cs");

        Assert.DoesNotContain("HandleSelectionChangedAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigationViewSelectionChangedEventArgs", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainNavigationViewAdapter_DoesNotDependOnProjectedSelectionEcho()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Navigation\MainNavigationViewAdapter.cs");

        Assert.DoesNotContain("IsProjectedSelectionEcho", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectedControlSelectedItem", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.CompletedTask", code, StringComparison.Ordinal);
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
        Assert.Contains("InputBoxAutomationId=\"StartView.PromptBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LinearGradientBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("SystemAccentColorLight2", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartViewXaml_ExposesSharedAgentSelector_ForNewSessionLaunch()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("SelectorSlots=\"{x:Bind ViewModel.ComposerSelectorSlots, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AgentSelectorAutomationId=\"StartView.AgentSelector\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartViewXaml_ExposesProjectSelector_ForNewSessionLaunch()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("SelectorSlots=\"{x:Bind ViewModel.ComposerSelectorSlots, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ProjectSelectorAutomationId=\"StartView.ProjectSelector\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartViewXaml_ExposesModeSelectorAndVoiceButtons_ForNewSessionLaunch()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");
        Assert.Contains("SelectorSlots=\"{x:Bind ViewModel.ComposerSelectorSlots, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsStartModeSelectorVisible", xaml, StringComparison.Ordinal);
        Assert.Contains("ModeSelectorAutomationId=\"StartView.ModeSelector\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel=\"{x:Bind ViewModel.Chat, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartView_ComposerMoveUpEscapeHandler_ReturnsInputFocusToHeroSuggestions()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml.cs");

        Assert.Contains("ComposerShell.MoveUpEscapeHandler = HandlePromptMoveUpEscape;", code, StringComparison.Ordinal);
        Assert.Contains("promptBox.XYFocusUp = firstSuggestion;", code, StringComparison.Ordinal);
        Assert.Contains("button.XYFocusDown = promptFocusTarget;", code, StringComparison.Ordinal);
        Assert.Contains("button.ClearValue(Control.XYFocusDownProperty);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void StartView_DirectionalFocusEntry_UsesKeyboardFocusState()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml.cs");

        Assert.Contains("firstSuggestion.Focus(FocusState.Keyboard)", code, StringComparison.Ordinal);
        Assert.Contains("promptBox.Focus(FocusState.Keyboard)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("firstSuggestion.Focus(FocusState.Programmatic)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("promptBox.Focus(FocusState.Programmatic)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewXaml_ExposesStableAutomationIds_ForGuiTesting()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.ActiveRoot\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"ChatView.CurrentSessionTitle\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationProperties.AutomationId=\"ChatView.CurrentSessionNameEditor\"", xaml, StringComparison.Ordinal);
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
    public void ChatViewCodeBehind_UsesProjectionOwnedRestoreCommandAndDoesNotFallbackToBottomOnRestoreFailure()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var restoreSection = ExtractSection(
            code,
            "case TranscriptViewportControllerActionKind.RequestRestore:",
            "case TranscriptViewportControllerActionKind.StopProgrammaticScroll:");

        Assert.Contains("case TranscriptViewportControllerActionKind.RequestRestore:", code, StringComparison.Ordinal);
        Assert.Contains("QueueProjectionOwnedRestore(restoreToken, action.Generation);", restoreSection, StringComparison.Ordinal);
        Assert.DoesNotContain("ScheduleScrollToBottom();", restoreSection, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestScrollToBottom();", restoreSection, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewCodeBehind_DelegatesUserDetachIntentToViewportController()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");

        Assert.Contains("OnUserViewportDetachIntent(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateUserDetachedEvent(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateUserIntentScrollEvent(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewCodeBehind_DelegatesViewportObservationPolicyToViewportController()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var refreshSection = ExtractSection(
            code,
            "private void TryRefreshViewportCoordinatorFromView",
            "private TranscriptViewportViewState CreateViewportViewState");

        Assert.Contains("_viewportController.OnViewportChanged(", refreshSection, StringComparison.Ordinal);
        Assert.DoesNotContain("ObserveViewportFact(", refreshSection, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterUserViewportDetachment();", refreshSection, StringComparison.Ordinal);
        Assert.DoesNotContain("new TranscriptViewportEvent.UserIntentScroll(", refreshSection, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewsCodeBehind_NativeViewportMovementPolicyIsOwnedByViewportController()
    {
        foreach (var path in new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs"
        })
        {
            var code = LoadFile(path);
            var refreshSection = ExtractSection(
                code,
                "private void TryRefreshViewportCoordinatorFromView",
                "private TranscriptViewportViewState CreateViewportViewState");

            Assert.Contains("_viewportController.OnViewportChanged(", refreshSection, StringComparison.Ordinal);
            Assert.DoesNotContain("ObserveViewportFact(", refreshSection, StringComparison.Ordinal);
            Assert.DoesNotContain("ShouldDetachForNativeViewportMovement", code, StringComparison.Ordinal);
            Assert.DoesNotContain("RefreshDetachedViewportRestoreToken", code, StringComparison.Ordinal);
        }

        var controllerCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptViewportController.cs");
        Assert.Contains("ObserveViewport(", controllerCode, StringComparison.Ordinal);
        Assert.Contains("OnUserViewportDetachIntent(", controllerCode, StringComparison.Ordinal);
        Assert.Contains("CreateUserDetachedEvent(_conversationId", controllerCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewportController_TranscriptSettleRequiresNativeViewportAndLastItemAtBottom()
    {
        var code = LoadFile(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptViewportController.cs");
        var observationSection = ExtractSection(
            code,
            "private static TranscriptScrollSettleObservation ResolveSettleObservation",
            "private static string ResolveConversationId");

        Assert.Contains("viewState.IsAtBottom && viewState.IsLastItemVisibleAtBottom", observationSection, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewsCodeBehind_TranscriptSettleAdvancesFromNativeViewportChanges()
    {
        foreach (var path in new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs"
        })
        {
            var code = LoadFile(path);
            var viewportChangedSection = ExtractSection(
                code,
                "private void OnMessagesListViewportChanged",
                "private void OnMessagesListPointerPressed");

            Assert.Contains("_viewportController.OnViewportChanged(", viewportChangedSection, StringComparison.Ordinal);
            Assert.DoesNotContain("TryAdvanceTranscriptSettleFromLayout", viewportChangedSection, StringComparison.Ordinal);
            Assert.DoesNotContain("_viewportOrchestrator", viewportChangedSection, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ChatViewsCodeBehind_UseListViewTranscriptViewportHostWithoutItemsRepeaterViewportApis()
    {
        foreach (var path in new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs"
        })
        {
            var code = LoadFile(path);

            Assert.Contains("ITranscriptViewportHost", code, StringComparison.Ordinal);
            Assert.Contains("ListViewTranscriptViewportHost", code, StringComparison.Ordinal);
            Assert.DoesNotContain("ItemsRepeaterTranscriptViewportHost", code, StringComparison.Ordinal);
            Assert.DoesNotContain("ConfigureWindowsTranscriptListView", code, StringComparison.Ordinal);
            if (code.Contains("ShowsScrollingPlaceholders", StringComparison.Ordinal))
            {
                Assert.Contains("#if WINDOWS", code, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("ContainerFromIndex(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("ScrollIntoView(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("ScrollViewerViewportMonitor.", code, StringComparison.Ordinal);
            Assert.DoesNotContain("RegisterPropertyChangedCallback(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("MessagesScrollViewer", code, StringComparison.Ordinal);
            Assert.DoesNotContain("MessagesRepeater", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TranscriptProjectionRestoreState_IsOwnedBySingleUiController()
    {
        var controllerCode = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Transcript\TranscriptProjectionRestoreController.cs");
        Assert.Contains("private TranscriptProjectionRestoreToken? _pendingToken;", controllerCode, StringComparison.Ordinal);
        Assert.Contains("public TranscriptProjectionRestoreResult TryApply(", controllerCode, StringComparison.Ordinal);
        Assert.Contains("public bool TryScheduleRetry(", controllerCode, StringComparison.Ordinal);

        foreach (var path in new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs"
        })
        {
            var code = LoadFile(path);

            Assert.Contains("TranscriptProjectionRestoreController", code, StringComparison.Ordinal);
            Assert.DoesNotContain("_pendingRestoreToken", code, StringComparison.Ordinal);
            Assert.DoesNotContain("_pendingRestoreConversationId", code, StringComparison.Ordinal);
            Assert.DoesNotContain("_pendingRestoreGeneration", code, StringComparison.Ordinal);
            Assert.DoesNotContain("_pendingRestoreAttemptCount", code, StringComparison.Ordinal);
            Assert.DoesNotContain("_pendingRestoreResolvedIndex", code, StringComparison.Ordinal);
            Assert.DoesNotContain("_pendingRestoreRequestedMaterializationIndex", code, StringComparison.Ordinal);
            Assert.DoesNotContain("_pendingRestoreRetryScheduled", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TranscriptAutoFollowSettleState_IsOwnedBySingleCoreController()
    {
        var controllerCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptViewportController.cs");

        Assert.Contains("private readonly TranscriptViewportOrchestrator _orchestrator", controllerCode, StringComparison.Ordinal);
        Assert.Contains("OnViewportChanged(", controllerCode, StringComparison.Ordinal);
        Assert.Contains("OnUserViewportDetachIntent(", controllerCode, StringComparison.Ordinal);

        foreach (var path in new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs"
        })
        {
            var code = LoadFile(path);

            Assert.Contains("TranscriptViewportController", code, StringComparison.Ordinal);
            Assert.DoesNotContain("TranscriptViewportOrchestrator", code, StringComparison.Ordinal);
            Assert.DoesNotContain("private readonly TranscriptScrollSettler _transcriptScrollSettler", code, StringComparison.Ordinal);
            Assert.DoesNotContain("private readonly TranscriptViewportCoordinator _viewportCoordinator", code, StringComparison.Ordinal);
            Assert.DoesNotContain("private bool _attachToBottomIntentPending", code, StringComparison.Ordinal);
            Assert.DoesNotContain("private bool _pointerScrollIntentPending", code, StringComparison.Ordinal);
            Assert.DoesNotContain("private bool _pointerScrollReleasePending", code, StringComparison.Ordinal);
            Assert.DoesNotContain("private bool _suspendAutoScrollTracking", code, StringComparison.Ordinal);
            Assert.DoesNotContain("private int _scrollScheduleGeneration", code, StringComparison.Ordinal);
            Assert.DoesNotContain("private int _scheduledScrollRequestVersion", code, StringComparison.Ordinal);
            Assert.DoesNotContain("private int _activeTranscriptScrollGeneration", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ChatViewsCodeBehind_UseOpaqueTokensInsteadOfReadingOrchestratorInternalCounters()
    {
        foreach (var path in new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs"
        })
        {
            var code = LoadFile(path);

            Assert.Contains("TranscriptScrollRequestToken requestToken", code, StringComparison.Ordinal);
            Assert.Contains("MatchesActiveScrollRequest(", code, StringComparison.Ordinal);
            Assert.Contains("OnActiveScrollObservation(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("TryCaptureActiveScrollRequestToken(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("TryBeginScrollToBottomSchedule(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("CanExecuteScrollToBottomSchedule(", code, StringComparison.Ordinal);
            Assert.DoesNotContain(".ActiveScrollGeneration", code, StringComparison.Ordinal);
            Assert.DoesNotContain(".ScheduledScrollRequestVersion", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TranscriptProjectionRestoreContract_IsAnchorOnly()
    {
        var contractsCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptViewportContracts.cs");
        var hostContractCode = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Transcript\ITranscriptViewportHost.cs");
        var listViewHostCode = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Transcript\ListViewTranscriptViewportHost.cs");

        Assert.Contains("string ProjectionItemKey);", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("OffsetHint", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetRelativeOffsetWithinItem", hostContractCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetRelativeOffsetWithinItem", listViewHostCode, StringComparison.Ordinal);

        foreach (var path in new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs"
        })
        {
            var code = LoadFile(path);

            Assert.Contains("CreateViewportProjectionRestoreToken(ViewModel.MessageHistory[firstVisibleIndex])", code, StringComparison.Ordinal);
            Assert.DoesNotContain("OffsetHint", code, StringComparison.Ordinal);
            Assert.DoesNotContain("TryGetRelativeOffsetWithinItem", code, StringComparison.Ordinal);
            Assert.DoesNotContain("TrySetVerticalOffset", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ChatViewCodeBehind_WarmAndOverlayResumeActivateCoordinatorInsteadOfRedetach()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var overlayResumeSection = ExtractSection(
            code,
            "private void ResumeViewportCoordinatorAfterOverlayIfNeeded()",
            "private void RestoreViewportForWarmResume()");

        Assert.Contains("ActivateViewportForCurrentSession(TranscriptViewportActivationKind.WarmReturn);", code, StringComparison.Ordinal);
        Assert.Contains("ActivateViewportForCurrentSession(TranscriptViewportActivationKind.OverlayResume);", code, StringComparison.Ordinal);
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
    public void ChatViewModel_DoesNotExposeConnectionStoreAsProjectionApi()
    {
        var code = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatViewModel.cs");

        Assert.DoesNotContain("IChatConnectionStore ConnectionStore =>", code, StringComparison.Ordinal);
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
    public void MainNavigation_SessionFlyout_DoesNotExposeMoveConversation()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.DoesNotContain("x:Uid=\"SessionNavMoveItem\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MainNav.Session.Context.Move", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Click=\"OnSessionMoveMenuItemClick\"", xaml, StringComparison.Ordinal);

        Assert.DoesNotContain("x:Uid=\"SessionNavRenameItem\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"SessionNavRenameItem\"\r\n                                        Command=\"{x:Bind RenameCommand}\"", xaml, StringComparison.Ordinal);

        Assert.DoesNotContain("private void OnSessionMoveMenuItemClick(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_moveOnFlyoutClosed", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_pendingMoveSessionId", code, StringComparison.Ordinal);
        Assert.DoesNotContain("private void OnSessionRenameMenuItemClick(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_renameOnFlyoutClosed", code, StringComparison.Ordinal);
    }

    private static string LoadFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, NormalizeRelativePath(relativePath)));
    }

    private static string ExtractSection(string content, string startMarker, string? endMarker = null)
    {
        var normalizedContent = NormalizeLineEndings(content);
        var normalizedStartMarker = NormalizeLineEndings(startMarker);
        var normalizedEndMarker = endMarker is null ? null : NormalizeLineEndings(endMarker);
        var start = normalizedContent.IndexOf(normalizedStartMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Unable to locate marker '{startMarker}'.");

        var end = normalizedEndMarker is null
            ? normalizedContent.Length
            : normalizedContent.IndexOf(normalizedEndMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = normalizedContent.Length;
        }

        return normalizedContent.Substring(start, end - start);
    }

    private static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal);

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

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', Path.DirectorySeparatorChar);

    private static IEnumerable<string> EnumerateProductionCSharpFiles(string root)
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            yield return path;
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "SalmonEgg"), "*.cs", SearchOption.AllDirectories))
        {
            yield return path;
        }
    }

    private sealed class TestPageA;

    private sealed class TestPageB;
}

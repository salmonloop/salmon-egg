using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;
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

        Assert.Equal("Just now", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromSeconds(30)));
        Assert.Equal("2 min", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromMinutes(2)));
        Assert.Equal("3 hr", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromHours(3)));
        Assert.Equal("2 d", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromDays(2)));
    }

    [Fact]
    public void NavTimeFormatter_ToRelativeText_UsesLocalizerWhenProvided()
    {
        var now = DateTime.UtcNow;
        var localizer = new PrefixLocalizer("zh");

        Assert.Equal("zh:Nav_RelativeJustNow", NavTimeFormatter.ToRelativeText(now - TimeSpan.FromSeconds(30), localizer));
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "zh:Nav_RelativeMinutesFormat", 2),
            NavTimeFormatter.ToRelativeText(now - TimeSpan.FromMinutes(2), localizer));
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "zh:Nav_RelativeHoursFormat", 3),
            NavTimeFormatter.ToRelativeText(now - TimeSpan.FromHours(3), localizer));
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "zh:Nav_RelativeDaysFormat", 2),
            NavTimeFormatter.ToRelativeText(now - TimeSpan.FromDays(2), localizer));
    }

    private sealed class PrefixLocalizer : IStringLocalizer<CoreStrings>
    {
        private readonly string _prefix;

        public PrefixLocalizer(string prefix)
        {
            _prefix = prefix;
        }

        public LocalizedString this[string name]
            => new(name, $"{_prefix}:{name}");

        public LocalizedString this[string name, params object[] arguments]
            => new(name, string.Format(CultureInfo.InvariantCulture, $"{_prefix}:{name}", arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => Array.Empty<LocalizedString>();
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
    public void PresentationCoreAndLowerLayers_DoNotReachIntoNativeUiControlState()
    {
        var root = FindRepoRoot();
        var sourceRoots = new[]
        {
            @"src\SalmonEgg.Domain",
            @"src\SalmonEgg.Application",
            @"src\SalmonEgg.Infrastructure",
            @"src\SalmonEgg.Presentation.Core"
        };
        var forbiddenTokens = new[]
        {
            "Microsoft.UI.Xaml",
            "Windows.UI.Xaml",
            "FocusManager",
            ".Focus(",
            "DispatcherQueue.TryEnqueue",
            "NavigationViewSelectionChangedEventArgs",
            "Microsoft.UI.Xaml.Controls.NavigationView"
        };
        var violations = new List<string>();

        foreach (var sourceRoot in sourceRoots)
        {
            var absoluteRoot = Path.Combine(root, NormalizeRelativePath(sourceRoot));
            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories))
            {
                var code = File.ReadAllText(file);
                foreach (var token in forbiddenTokens)
                {
                    if (code.Contains(token, StringComparison.Ordinal))
                    {
                        violations.Add($"{Path.GetRelativePath(root, file)} contains '{token}'");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Non-UI layers must report facts through VM/store/coordinator contracts instead of mutating native control state."
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void MainPage_ActivatesInitialContentFromLoadedLifecycle()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var constructorSection = ExtractSection(code, "public MainPage()", "private async void OnAutomationArchiveSelectedClick");
        var loadedSection = ExtractSection(code, "private async void OnMainPageLoaded", "private void AttachGamepadInput");

        Assert.DoesNotContain("EnsureStartContent", constructorSection, StringComparison.Ordinal);
        Assert.Contains("await _startupWorkflow.ActivateShellAsync().ConfigureAwait(true);", loadedSection, StringComparison.Ordinal);
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
    public void MainPage_StartupFocusSeed_IsScheduledBeforeRuntimeInitialization()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var loadedSection = ExtractSection(code, "private async void OnMainPageLoaded", "private void AttachGamepadInput");
        var focusSeedIndex = loadedSection.IndexOf("TryMoveFocusFromCurrentContentIntoMainNavigation();", StringComparison.Ordinal);
        var initializationIndex = loadedSection.IndexOf("await _startupWorkflow.InitializeRuntimeAsync().ConfigureAwait(true);", StringComparison.Ordinal);

        Assert.True(focusSeedIndex >= 0, "MainPage should schedule a concrete startup navigation focus seed.");
        Assert.True(initializationIndex >= 0, "MainPage should initialize application runtime state during load.");
        Assert.True(focusSeedIndex < initializationIndex, "MainPage should seed startup navigation focus before runtime initialization.");
    }

    [Fact]
    public void DependencyInjection_ShellStartupNavigationService_IsApplicationScopedForReloadProjection()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");
        var section = ExtractSection(
            code,
            "services.AddSingleton<IShellStartupNavigationService>",
            "// Global search");

        Assert.Contains("new ShellStartupNavigationService(", section, StringComparison.Ordinal);
        Assert.Contains("sp.GetRequiredService<IActivationTokenShellNavigationService>()", section, StringComparison.Ordinal);
        Assert.DoesNotContain("AddTransient<IShellStartupNavigationService>", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IApplicationStartupWorkflow>", code, StringComparison.Ordinal);
        Assert.Contains("new ApplicationStartupWorkflow(", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IChatRuntimeInitialization>", code, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<ShellNavigationService>();", code, StringComparison.Ordinal);
        Assert.Contains(
            "services.AddSingleton<IShellNavigationService>(sp => sp.GetRequiredService<ShellNavigationService>());",
            code,
            StringComparison.Ordinal);
        Assert.Contains(
            "services.AddSingleton<IActivationTokenShellNavigationService>(sp => sp.GetRequiredService<ShellNavigationService>());",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjection_NavigationCoordinator_UsesDiscoverConnectionFacade()
    {
        var dependencyInjection = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");
        var navigationCoordinator = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\NavigationCoordinator.cs");
        var section = ExtractSection(
            dependencyInjection,
            "services.AddSingleton<INavigationCoordinator>",
            "services.AddSingleton<ISettingsSectionSelectionStore");

        Assert.Contains("sp.GetRequiredService<IDiscoverSessionsConnectionFacade>()", section, StringComparison.Ordinal);
        Assert.DoesNotContain("NoOpDiscoverSessionsConnectionFacade", navigationCoordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void AcpMcpRuntime_DoesNotExposeFallbackCatalogSources()
    {
        var dependencyInjection = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");
        var root = FindRepoRoot();
        var providerCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Chat\IAcpMcpServerProvider.cs");
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
        Assert.False(File.Exists(Path.Combine(
            root,
            NormalizeRelativePath(@"src\SalmonEgg.Presentation.Core\Services\Chat\AcpConnectionEvictionOptionsBridge.cs"))));
        Assert.DoesNotContain("new AcpSessionCommandOrchestrator(", chatCoordinator, StringComparison.Ordinal);
        Assert.DoesNotContain("AppPreferencesViewModel", chatLaunchWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("AcpEnabled", appPreferences, StringComparison.Ordinal);
        Assert.Contains("class AcpConnectionEvictionOptionsBridge : IDisposable", evictionBridge, StringComparison.Ordinal);
        Assert.Contains("sp.GetRequiredService<IAcpMcpServerProvider>()", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("sp.GetRequiredService<IAcpSessionCommandOrchestrator>()", dependencyInjection, StringComparison.Ordinal);
    }

    [Fact]
    public void AppLaunch_InitializesPreferencesBeforeAttachingBackdropService()
    {
        var app = NormalizeLineEndings(LoadFile(@"SalmonEgg\SalmonEgg\App.xaml.cs"));
        var initializeIndex = app.IndexOf("await preferences.InitializeAsync();", StringComparison.Ordinal);
        var attachIndex = app.IndexOf("_windowBackdropService?.Attach(MainWindow);", StringComparison.Ordinal);

        Assert.True(initializeIndex >= 0, "App launch must explicitly initialize preferences.");
        Assert.True(attachIndex >= 0, "App launch must attach the backdrop service.");
        Assert.True(
            initializeIndex < attachIndex,
            "Window backdrop attachment must read initialized preferences instead of constructor defaults.");
    }

    [Fact]
    public void StartLaunch_DoesNotRetainLegacyProfileAgnosticCwdResolver()
    {
        var root = FindRepoRoot();
        var legacyResolver = Path.Combine(
            root,
            NormalizeRelativePath(@"src\SalmonEgg.Presentation.Core\Services\StartSessionCwdResolver.cs"));
        var dependencyInjection = LoadFile(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");
        var chatLaunchWorkflow = LoadFile(@"src\SalmonEgg.Presentation.Core\Services\Chat\ChatLaunchWorkflow.cs");

        Assert.False(File.Exists(legacyResolver));
        Assert.DoesNotContain("CreateStartCwdResolver", dependencyInjection, StringComparison.Ordinal);
        Assert.DoesNotContain("StartSessionCwdResolver", dependencyInjection, StringComparison.Ordinal);
        Assert.DoesNotContain("StartSessionCwdResolver", chatLaunchWorkflow, StringComparison.Ordinal);
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

        Assert.Contains("FocusTranscriptScroller(FocusState.Pointer);", section, StringComparison.Ordinal);
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
        Assert.Contains("new AsyncRelayCommand(AddLocalProjectAsync, () => CanAddProject)", navigationViewModel, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IFolderPickerService, UnavailableFolderPickerService>();", dependencyInjection, StringComparison.Ordinal);
    }

    [Fact]
    public void FolderPickerCancellation_DoesNotFallbackToManualPathInput()
    {
        var uiService = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Services\UiInteractionService.cs");
        var pickerCallIndex = uiService.IndexOf("var pickedFolder = await _folderPicker.PickFolderAsync()", StringComparison.Ordinal);
        var cancelledCheckIndex = uiService.IndexOf("if (pickedFolder is null)", StringComparison.Ordinal);
        var catchIndex = uiService.IndexOf("catch", cancelledCheckIndex, StringComparison.Ordinal);
        var promptFallbackIndex = uiService.IndexOf("return await PromptTextAsync(", StringComparison.Ordinal);

        Assert.True(pickerCallIndex >= 0, "PickFolderAsync must use the native picker first.");
        Assert.True(cancelledCheckIndex > pickerCallIndex, "Native picker cancellation must be handled immediately after the picker returns.");
        Assert.True(catchIndex > cancelledCheckIndex, "The picker failure fallback must stay separate from user cancellation.");
        Assert.DoesNotContain("PromptTextAsync", uiService.Substring(cancelledCheckIndex, catchIndex - cancelledCheckIndex), StringComparison.Ordinal);
        Assert.True(promptFallbackIndex > catchIndex, "Manual path fallback should only run from the picker failure path.");
    }

    [Fact]
    public void ShowSessionsListDialog_DoesNotSwallowPickSessionExceptions()
    {
        var uiService = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Services\UiInteractionService.cs");
        var method = ExtractSection(uiService, "public async Task ShowSessionsListDialogAsync");

        Assert.Contains("onPickSession(dialog.PickedSessionId!)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("try { onPickSession", method, StringComparison.Ordinal);
        Assert.DoesNotContain("catch { }", method, StringComparison.Ordinal);
    }



    [Fact]
    public void StartViewLoaded_DoesNotOwnApplicationRuntimeInitialization()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml.cs");
        var method = ExtractSection(code, "private void OnLoaded", "private void OnUnloaded");

        Assert.DoesNotContain("EnsureAcpProfilesLoadedAsync()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreConversationsAsync()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("IApplicationStartupWorkflow", code, StringComparison.Ordinal);
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
        Assert.Contains("AwaitActivationHandledAsync(_viewModel.ActivateStartAsync())", section, StringComparison.Ordinal);
        Assert.Contains("AwaitActivationHandledAsync(_viewModel.ActivateDiscoverSessionsAsync())", section, StringComparison.Ordinal);
        Assert.Contains("AwaitActivationHandledAsync(_viewModel.ActivateSettingsAsync(SettingsSectionCatalog.GeneralKey))", section, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = _navigationCoordinator.ActivateSessionAsync", section, StringComparison.Ordinal);
        Assert.DoesNotContain("AwaitActivationHandledAsync(_navigationCoordinator.ActivateSessionAsync", section, StringComparison.Ordinal);
        Assert.Contains("AwaitActivationHandledAsync(_viewModel.ActivateSessionAsync", section, StringComparison.Ordinal);
        Assert.Contains("return await activationTask.ConfigureAwait(true);", section, StringComparison.Ordinal);
        Assert.DoesNotContain("INavigationCoordinator", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_navigationCoordinator", code, StringComparison.Ordinal);
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
    public void ChatViewCodeBehind_UsesFollowControllerWithoutProjectionEpoch()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var controllerCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptViewportController.cs");
        Assert.Contains("TranscriptViewportController", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectionEpoch", code, StringComparison.Ordinal);
        Assert.Contains("TranscriptFollowController", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectionEpoch", controllerCode, StringComparison.Ordinal);
    }


        [Fact]
    public void ChatViewCodeBehind_DelegatesUserDetachIntentToViewportController()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        Assert.Contains("OnUserViewportDetachIntent(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectionEpoch", code, StringComparison.Ordinal);
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
        var controllerCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptViewportController.cs");
        Assert.Contains("TranscriptFollowController", controllerCode, StringComparison.Ordinal);
        Assert.Contains("OnUserViewportDetachIntent(", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateUserDetachedEvent(_conversationId", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectionEpoch", controllerCode, StringComparison.Ordinal);

        foreach (var path in new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs"
        })
        {
            var code = LoadFile(path);
            Assert.Contains("_viewportController.OnViewportChanged(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("ObserveViewportFact(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("ProjectionEpoch", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ViewportController_UsesFollowControllerWithoutProjectionEpoch()
    {
        var controllerCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptViewportController.cs");
        Assert.Contains("TranscriptFollowController", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectionEpoch", controllerCode, StringComparison.Ordinal);
    }


    [Fact]
    public void ChatViewsCodeBehind_ObserveViewportThroughController()
    {
        foreach (var path in new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs"
        })
        {
            var code = LoadFile(path);
            Assert.Contains("OnViewportChanged(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("ProjectionEpoch", code, StringComparison.Ordinal);
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
    public void TranscriptFollowState_IsOwnedBySingleCoreFollowController()
    {
        var controllerCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptViewportController.cs");
        var followCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptFollowController.cs");
        Assert.Contains("TranscriptFollowController", controllerCode, StringComparison.Ordinal);
        Assert.Contains("TranscriptFollowMode", followCode, StringComparison.Ordinal);
        Assert.DoesNotContain("new TranscriptViewportOrchestrator", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectionEpoch", controllerCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TranscriptViewportContracts_DoesNotReintroduceLegacyCompatibilityShell()
    {
        var contractsCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptViewportContracts.cs");
        var controllerCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptViewportController.cs");

        Assert.DoesNotContain("TranscriptViewportFact", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportTransition", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportAnchorKind", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportAnchor", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportOrchestratorSnapshot", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptScrollScheduleToken", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("HasPendingSettle", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("AttachToBottomIntentPending", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("LastTransition", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProjectionRestoreQueued", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkDetachedViewportInteractionStarted", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("StopProgrammaticScroll", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportControllerActionKind.AutoFollowDetached", controllerCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportControllerActionKind.AutoFollowAttached", controllerCode, StringComparison.Ordinal);
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
            Assert.Contains("TranscriptNativeScrollScheduler", code, StringComparison.Ordinal);
            Assert.Contains("MatchesActiveScrollRequest(", code, StringComparison.Ordinal);
            Assert.Contains("OnActiveScrollObservation(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("_queuedNativeTranscriptScrollRequestToken", code, StringComparison.Ordinal);
            Assert.DoesNotContain("TryCaptureActiveScrollRequestToken(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("TryBeginScrollToEndSchedule(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("CanExecuteScrollToEndSchedule(", code, StringComparison.Ordinal);
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

            Assert.DoesNotContain("OffsetHint", code, StringComparison.Ordinal);
            Assert.DoesNotContain("TryGetRelativeOffsetWithinItem", code, StringComparison.Ordinal);
            Assert.DoesNotContain("TrySetVerticalOffset", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TranscriptViewportContracts_ExposeOnlyCurrentFollowAndRestoreShape()
    {
        var contractsCode = LoadFile(@"src\SalmonEgg.Presentation.Core\Utilities\TranscriptViewportContracts.cs");

        Assert.Contains("public enum TranscriptViewportState", contractsCode, StringComparison.Ordinal);
        Assert.Contains("public readonly record struct TranscriptProjectionRestoreToken", contractsCode, StringComparison.Ordinal);
        Assert.Contains("public enum TranscriptViewportActivationKind", contractsCode, StringComparison.Ordinal);
        Assert.Contains("public readonly record struct TranscriptViewportConversationState", contractsCode, StringComparison.Ordinal);
        Assert.Contains("public readonly record struct TranscriptScrollRequestToken", contractsCode, StringComparison.Ordinal);
        Assert.Contains("public readonly record struct TranscriptViewportViewState", contractsCode, StringComparison.Ordinal);
        Assert.Contains("public enum TranscriptViewportControllerActionKind", contractsCode, StringComparison.Ordinal);
        Assert.Contains("ScrollTranscriptToEnd = 1", contractsCode, StringComparison.Ordinal);
        Assert.Contains("RequestRestore = 5", contractsCode, StringComparison.Ordinal);
        Assert.Contains("ScrollIntoView = 6", contractsCode, StringComparison.Ordinal);

        Assert.DoesNotContain("TranscriptViewportFact", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportTransition", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportAnchorKind", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportAnchor", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportOrchestratorSnapshot", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptScrollScheduleToken", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("StopProgrammaticScroll", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportControllerActionKind.AutoFollowDetached", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("TranscriptViewportControllerActionKind.AutoFollowAttached", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("IsViewReady", contractsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("IsViewportReady", contractsCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatViewCodeBehind_OverlayResumeIsConsumedByCoreController()
    {
        var code = LoadFile(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");
        var overlayResumeSection = ExtractSection(
            code,
            "private void TryResumeViewportAfterOverlay()",
            "private void TryActivateViewportAfterLoad()");

        Assert.Contains("_viewportController.OnConversationChanged(", code, StringComparison.Ordinal);
        Assert.Contains("_viewportController.TryResumeAfterOverlay(", overlayResumeSection, StringComparison.Ordinal);
        Assert.Contains("_viewportController.TryActivateAfterLoad(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new TranscriptViewportEvent.UserIntentScroll(", overlayResumeSection, StringComparison.Ordinal);
        Assert.DoesNotContain("_wasOverlayVisible", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_resumeViewportCoordinatorAfterOverlayPending", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreViewportForWarmResume", code, StringComparison.Ordinal);
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
    public void MainNavigationXaml_AddProjectUsesStaticAddIconWithSourceMenu()
    {
        var xaml = LoadFile(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        // The unified add-project entry is a top NavigationViewItem with a static Add icon.
        // Invoking it opens the item's attached MenuFlyout offering the local-folder and
        // remote-project sources, each bound to its own command on the item view model.
        Assert.Contains("MainNavigationAutomationIds.AddProject()", xaml, StringComparison.Ordinal);
        Assert.Contains("<SymbolIcon Symbol=\"Add\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("NavItemTag.AddProject", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind AddLocalProjectCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind SelectRemoteProjectCommand}\"", xaml, StringComparison.Ordinal);
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

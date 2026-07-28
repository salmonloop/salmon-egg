using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

using static SalmonEgg.Presentation.Core.Tests.Ui.XamlComplianceTestHelpers;

public sealed class XamlComplianceGamepadInputTests
{

    [Fact]
    public void ChatInputArea_DoesNotHijackGeneralFocusFlowForGamepadEntry()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");

        Assert.Contains("XYFocusKeyboardNavigation=\"Enabled\"", xaml);
        Assert.Contains("x:Name=\"SlashCommandsList\"", xaml);
        Assert.DoesNotContain("FocusEngaged=\"OnInputAreaFocusEngaged\"", xaml);
        Assert.DoesNotContain("FocusDisengaged=\"OnInputAreaFocusDisengaged\"", xaml);
        Assert.DoesNotContain("private void OnInputAreaFocusEngaged(", code);
        Assert.DoesNotContain("private void OnInputAreaFocusDisengaged(", code);
    }

    [Fact]
    public void DependencyInjection_RegistersGamepadInputBehindAnAbstraction()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");

        Assert.Contains("IGamepadInputService", code);
        Assert.Contains("IGamepadDiagnosticsService", code);
        Assert.Contains("SupportsGamepadInput", code);
        Assert.Contains("IsGuiAutomationEnabled()", code);
        Assert.DoesNotContain("new WindowsGamepadInputService(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new NoOpGamepadInputService(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GuiGamepadInputService", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsGuiGamepadInputEnabled", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SALMONEGG_GUI_CONTROL_FILE", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new WindowsGamepadDiagnosticsService(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsSettingsPage_ExposesGamepadDiagnosticsThroughViewModel()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml");
        var viewModel = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Settings\GamepadDiagnosticsViewModel.cs");
        var windowsService = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadDiagnosticsService.cs");
        var gamepadSection = ExtractSection(xaml, "Diagnostics_GamepadTitle", "Diagnostics_LogsTitle");

        Assert.Contains("AutomationProperties.AutomationId=\"Diagnostics.GamepadMonitorHeader\"", gamepadSection, StringComparison.Ordinal);
        Assert.DoesNotContain("<Expander", gamepadSection, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.StatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.ConnectedGamepadsText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.ConnectedRawControllersText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.InputSourceText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.ActiveInputsText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.ThumbstickText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.StandardGamepadsText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.RawControllersText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.StartMonitoringCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.StopMonitoringCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GamepadDiagnostics.RefreshSnapshotCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Gaming.Input", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Gaming.Input", viewModel, StringComparison.Ordinal);
        Assert.Contains("Windows.Gaming.Input", windowsService, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsGamepadInputService_DelegatesRepeatAndDeadzonePolicyToCoreProcessor()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadInputService.cs");

        Assert.Contains("GamepadReadingPipeline", code, StringComparison.Ordinal);
        Assert.Contains("ProcessFrame", code, StringComparison.Ordinal);
        Assert.Contains("WindowsStandardGamepadReadingMapper.GetInputReading", code, StringComparison.Ordinal);
        Assert.Contains("WindowsGameControllerButtonLabelMapper.GetIdentity", code, StringComparison.Ordinal);
        Assert.Contains("_standardGamepadIdentities", code, StringComparison.Ordinal);
        Assert.Contains("CacheStandardGamepadIdentity", code, StringComparison.Ordinal);
        // Live poll must not call FromGameController; identity is resolved once on connect and reused.
        Assert.DoesNotContain("RawGameController.FromGameController", code, StringComparison.Ordinal);
        // Platform host must not re-own edge processors; Core pipeline is the single owner.
        Assert.DoesNotContain("new GamepadIntentProcessor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new GamepadShortcutProcessor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new GamepadContextIntentProcessor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new GamepadInputPathTracker", code, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialRepeatDelay", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RepeatInterval", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ThumbstickDeadzone", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PressState", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WasmGamepadInputService_DelegatesPollFrameToCoreReadingPipeline()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmGamepadInputService.cs");

        Assert.Contains("GamepadReadingPipeline", code, StringComparison.Ordinal);
        Assert.Contains("ProcessFrame", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new GamepadIntentProcessor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new GamepadShortcutProcessor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new GamepadContextIntentProcessor", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new GamepadInputPathTracker", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ThumbstickDeadzone", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsAndWasmGamepadDiagnostics_DelegateActivePathSelectionToCoreProjector()
    {
        var windows = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadDiagnosticsService.cs");
        var wasm = LoadText(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmGamepadSnapshotReader.cs");

        Assert.Contains("GamepadDiagnosticsActiveReadingProjector.Project", windows, StringComparison.Ordinal);
        Assert.Contains("GamepadDiagnosticsActiveReadingProjector.Project", wasm, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool HasActiveInput", windows, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsGamepadDiagnosticsService_DelegatesStandardReadingSemanticsToCorePolicy()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadDiagnosticsService.cs");

        Assert.Contains("WindowsStandardGamepadReadingMapper.GetInputReading", code, StringComparison.Ordinal);
        Assert.Contains("WindowsGameControllerButtonLabelMapper.GetFaceButtonLabels", code, StringComparison.Ordinal);
        Assert.Contains("WindowsGameControllerButtonLabelMapper.GetIdentity", code, StringComparison.Ordinal);
        Assert.Contains("GamepadDiagnosticsActiveReadingProjector.Project", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new GamepadInputReading(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("StandardGamepadInputReadingMapper.GetInputReading", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsStandardGamepadReadingMapper_DelegatesButtonFlagsToCoreStandardMapper()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsStandardGamepadReadingMapper.cs");

        Assert.Contains("StandardGamepadInputReadingMapper.GetInputReading", code, StringComparison.Ordinal);
        Assert.Contains("faceAPressed: reading.Buttons.HasFlag(GamepadButtons.A)", code, StringComparison.Ordinal);
        Assert.Contains("displayName: identity.DisplayName", code, StringComparison.Ordinal);
        Assert.Contains("hardwareVendorId: identity.HardwareVendorId", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new GamepadInputReading(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_GamepadNavigation_UsesServiceAndDoesNotMaintainSyntheticSelectionState()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("IGamepadInputService", code);
        Assert.DoesNotContain("currentGamepadIndex", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("selectedByGamepad", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainShellGamepadNavigationDispatcher_DoesNotSynthesizeNativeControlFocusOrActivation()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadNavigationDispatcher.cs");

        Assert.Contains("IShellBackNavigationService", code);
        Assert.Contains("TryConsumeNavigationIntent", code);
        Assert.Contains("GamepadNavigationIntent.Back", code);
        Assert.DoesNotContain("IGamepadNativeInputBridge", code);
        Assert.DoesNotContain("_nativeInputBridge", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryDispatchWithoutNativeFallback", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SendInput", code, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlFocusManager.TryMoveFocus", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FindNextElementOptions", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchRoot = searchRoot", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FrameworkElementAutomationPeer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IInvokeProvider", code);
        Assert.DoesNotContain("IToggleProvider", code);
        Assert.DoesNotContain("IExpandCollapseProvider", code);
        Assert.DoesNotContain("ISelectionItemProvider", code);
        Assert.DoesNotContain(".Select()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem =", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsOpen = false", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentFrame", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleBarBackButton", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOpenPopupsForXamlRoot", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".GoBack(", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".Hide()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_GamepadNavigation_DoesNotInterceptNavigationViewActivation()
    {
        var mainPage = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var adapter = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Navigation\MainNavigationViewAdapter.cs");

        Assert.Contains("HandleItemInvokedAsync", adapter, StringComparison.Ordinal);
        Assert.Contains("HandleActivatableTagAsync(navItem, tag)", adapter, StringComparison.Ordinal);
        Assert.Contains("MainPage : Page, INavigationIntentConsumer", mainPage, StringComparison.Ordinal);
        Assert.Contains("public bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)", mainPage, StringComparison.Ordinal);
        Assert.Contains("intent != GamepadNavigationIntent.MoveRight", mainPage, StringComparison.Ordinal);
        Assert.Contains("IsFocusWithinMainNavigation()", mainPage, StringComparison.Ordinal);
        Assert.Contains("TryMoveFocusFromMainNavigationIntoCurrentContent()", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("TryHandleFocusedMainNavigationActivationAsync", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveFocusedMainNavigationItem", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateFocusedItemActivationTask", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("HandleFocusedItemActivationAsync", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("_mainNavigationViewAdapter.CreateFocusedItemActivationTask", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("MainNav.Start", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("MainNav.DiscoverSessions", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem =", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem =", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadNavigationIntent.Activate", mainPage, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_GamepadMainNavFocus_AllowsProjectChildrenToReceiveNativeFocus()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        var navigationViewSection = ExtractSection(
            xaml,
            "<NavigationView x:Name=\"MainNavView\"",
            "<NavigationView.Content>");
        var projectTemplateSection = ExtractSection(
            xaml,
            "<DataTemplate x:Key=\"ProjectNavTemplate\"",
            "<DataTemplate x:Key=\"SessionNavTemplate\"");
        var sessionTemplateSection = ExtractSection(
            xaml,
            "<DataTemplate x:Key=\"SessionNavTemplate\"",
            "<DataTemplate x:Key=\"MoreNavTemplate\"");

        Assert.DoesNotContain("IsFocusEngagementEnabled=\"True\"", navigationViewSection, StringComparison.Ordinal);
        Assert.Contains("XYFocusKeyboardNavigation=\"Enabled\"", navigationViewSection, StringComparison.Ordinal);
        Assert.Contains("XYFocusRight=\"{x:Bind ContentFrame, Mode=OneWay}\"", navigationViewSection, StringComparison.Ordinal);
        Assert.Contains("XYFocusUp=\"{x:Bind TitleBarToggleLeftNavButton, Mode=OneWay}\"", navigationViewSection, StringComparison.Ordinal);
        Assert.DoesNotContain("IsFocusEngagementEnabled=\"True\"", projectTemplateSection, StringComparison.Ordinal);
        Assert.DoesNotContain("XYFocusKeyboardNavigation=\"Enabled\"", projectTemplateSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded=\"OnMainNavItemLoaded\"", projectTemplateSection, StringComparison.Ordinal);
        Assert.DoesNotContain("XYFocusKeyboardNavigation=\"Enabled\"", sessionTemplateSection, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded=\"OnMainNavItemLoaded\"", sessionTemplateSection, StringComparison.Ordinal);
        var mainPageCode = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        Assert.DoesNotContain("UpdateMainNavHierarchicalFocusRoutes", mainPageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("OnMainNavItemLoaded", mainPageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshNavGamepadFocusRoutes", mainPageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("EnumerateNavigationViewItems", mainPageCode, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem =", LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_TitleBarCommands_DoNotTrapGamepadDirectionalNavigation()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var leftCommandsSection = ExtractSection(
            xaml,
            "<StackPanel x:Name=\"TitleBarLeftButtons\"",
            "</StackPanel>");
        var rightCommandsSection = ExtractSection(
            xaml,
            "<StackPanel x:Name=\"TitleBarRightButtons\"",
            "</StackPanel>");

        Assert.DoesNotContain("XYFocusKeyboardNavigation", leftCommandsSection, StringComparison.Ordinal);
        Assert.DoesNotContain("XYFocusKeyboardNavigation", rightCommandsSection, StringComparison.Ordinal);
        AssertTitleBarCommandTargetsMainNavigationOnGamepadDown(xaml, "TitleBarBackButton");
        AssertTitleBarCommandTargetsMainNavigationOnGamepadDown(xaml, "TitleBarToggleLeftNavButton");
        AssertTitleBarCommandTargetsMainNavigationOnGamepadDown(xaml, "TitleBarMiniWindowButton");
        AssertTitleBarCommandTargetsMainNavigationOnGamepadDown(xaml, "BottomPanelButton");
        AssertTitleBarCommandTargetsMainNavigationOnGamepadDown(xaml, "TaskOverviewPanelButton");
    }

    [Fact]
    public void WindowsGuiAppSession_ActivatesThroughInvokeOrPointerWithoutManualSelection()
    {
        var code = LoadText(@"tests\SalmonEgg.GuiTests.Windows\WindowsGuiAppSession.cs");
        var activateElement = ExtractSection(
            code,
            "public void ActivateElement",
            "public void ClickElement");

        var invokeIndex = activateElement.IndexOf("Patterns.Invoke.IsSupported", StringComparison.Ordinal);
        var pointerIndex = activateElement.IndexOf("GetClickablePoint()", StringComparison.Ordinal);

        Assert.True(invokeIndex >= 0, "Activation helper must prefer the native Invoke pattern.");
        Assert.True(pointerIndex >= 0, "Activation helper must fall back to a real pointer click.");
        Assert.DoesNotContain("Patterns.SelectionItem.IsSupported", activateElement, StringComparison.Ordinal);
        Assert.DoesNotContain(".Select()", activateElement, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellBackNavigationService_UsesCurrentShellBackOwner()
    {
        var service = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\ShellBackNavigationService.cs");
        var mainPage = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("IShellBackNavigationService", service, StringComparison.Ordinal);
        Assert.Contains("public sealed class ShellBackNavigationService : IShellBackNavigationService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("IShellBackNavigationService", mainPage, StringComparison.Ordinal);
        Assert.Contains("rootFrame.Content as MainPage", service, StringComparison.Ordinal);
        Assert.Contains("TryHandleGamepadBack()", service, StringComparison.Ordinal);
        Assert.Contains("public bool TryHandleGamepadBack()", mainPage, StringComparison.Ordinal);
        Assert.Contains("public bool TryGoBack()", mainPage, StringComparison.Ordinal);
        Assert.Contains("_titleBarAdapter.TryGoBack()", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("TitleBarBackButton", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentFrame", service, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsRawGameControllerMapper_UsesTypedGameControllerButtonLabels()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsRawGameControllerMapper.cs");

        Assert.Contains("GameControllerButtonLabel", code);
        Assert.DoesNotContain("ToString()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsRawGameControllerMapper_DelegatesRawReadingSemanticsToCorePolicy()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsRawGameControllerMapper.cs");

        Assert.Contains("RawGameControllerInputReadingMapper.GetInputReadingFromPresses", code, StringComparison.Ordinal);
        Assert.Contains("RawGameControllerFaceButtonLayoutResolver.Resolve", code, StringComparison.Ordinal);
        Assert.Contains("RawGameControllerUnlabeledFaceIndexPolicy.SupportsFullGamepadUnlabeledIndexFallback", code, StringComparison.Ordinal);
        Assert.Contains("new RawGameControllerButtonPress", code, StringComparison.Ordinal);
        Assert.Contains("controller.DisplayName", code, StringComparison.Ordinal);
        Assert.Contains("controller.HardwareVendorId", code, StringComparison.Ordinal);
        Assert.Contains("WindowsGameControllerButtonLabelMapper.Map(controller.GetButtonLabel(i))", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RawGameControllerAxisNormalizer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadDirectionalSwitchMapper.Apply", code, StringComparison.Ordinal);
        Assert.DoesNotContain("reading with", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GameControllerSwitchPosition.Up) == GameControllerSwitchPosition.Up", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GameControllerSwitchPosition.Down) == GameControllerSwitchPosition.Down", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GameControllerSwitchPosition.Left) == GameControllerSwitchPosition.Left", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GameControllerSwitchPosition.Right) == GameControllerSwitchPosition.Right", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsGamepadDiagnosticsService_DoesNotHideRawFallbackBehindInactiveStandardGamepad()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadDiagnosticsService.cs");

        // Dual-path rows are always collected; Core projector owns active path (including raw when standard is idle).
        Assert.Contains("Gamepad.Gamepads.Select(CreateStandardGamepadDiagnostics)", code, StringComparison.Ordinal);
        Assert.Contains("RawGameController.RawGameControllers.Select(CreateRawControllerDiagnostics)", code, StringComparison.Ordinal);
        Assert.Contains("GamepadDiagnosticsActiveReadingProjector.Project", code, StringComparison.Ordinal);
        Assert.Contains("WindowsGameControllerButtonLabelMapper.GetIdentity", code, StringComparison.Ordinal);
        Assert.Contains("gamepad.GetButtonLabel(button)", code, StringComparison.Ordinal);
        Assert.Contains("controller.GetCurrentReading(buttons, switches, axes)", code, StringComparison.Ordinal);
        Assert.Contains("_rawMapper.GetInputReading(controller, buttons, switches, axes)", code, StringComparison.Ordinal);
        Assert.Contains("StandardGamepads: standardGamepads", code, StringComparison.Ordinal);
        Assert.Contains("RawControllers: rawControllers", code, StringComparison.Ordinal);
        Assert.DoesNotContain("HasMatchingGamepad", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RawGameController.FromGameController", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadDiagnosticsInputSource.Gamepad", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadDiagnosticsInputSource.RawGameController", code, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeDeviceGamepadBridge_UsesConfigurableHidMaestroProfile()
    {
        var bridge = LoadText(@"tests\SalmonEgg.GamepadBridge.Windows\Program.cs");
        var nativeInput = LoadText(@"tests\SalmonEgg.GuiTests.Windows\NativeDeviceGamepadTestInput.cs");
        var catalog = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\GamepadHidMaestroProfileCatalog.cs");

        Assert.Contains("SALMONEGG_HIDMAESTRO_PROFILE_ID", bridge, StringComparison.Ordinal);
        Assert.Contains("GamepadHidMaestroProfileCatalog", bridge, StringComparison.Ordinal);
        Assert.Contains("ResolveHidMaestroProfileId()", bridge, StringComparison.Ordinal);
        Assert.Contains("new HidMaestroBridge(hidMaestroCorePath, hidMaestroProfileId)", bridge, StringComparison.Ordinal);
        Assert.Contains("_getProfileMethod.Invoke(_context, [_profileId])", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"x\":", bridge, StringComparison.Ordinal);
        Assert.Contains("SubmitState(buttonName: \"X\")", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"y\":", bridge, StringComparison.Ordinal);
        Assert.Contains("SubmitState(buttonName: \"Y\")", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"activate\":", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"back\":", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"west\":", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"voice\":", bridge, StringComparison.Ordinal);
        Assert.Contains("ResolveSemanticFaceButton", bridge, StringComparison.Ordinal);
        Assert.Contains("GetPhysicalButtonNameCandidates", bridge, StringComparison.Ordinal);
        Assert.Contains("GamepadFaceSemantic", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("private enum FaceSemantic", bridge, StringComparison.Ordinal);
        Assert.Contains("GetPhysicalButtonNameCandidates", catalog, StringComparison.Ordinal);

        Assert.Contains("GamepadHidMaestroProfileCatalog.GetPhysicalButtonNameCandidates", bridge, StringComparison.Ordinal);
        Assert.Contains("GamepadHidMaestroProfileCatalog.FormatFamilyToken", bridge, StringComparison.Ordinal);
        Assert.Contains("GamepadHidMaestroProfileCatalog.NormalizeProfileId", bridge, StringComparison.Ordinal);
        Assert.Contains("using SalmonEgg.Presentation.Core.Services.Input;", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveProfileFaceFamily", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfileFaceFamily", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveFamilyToken", bridge, StringComparison.Ordinal);

        Assert.Contains("switch-pro", catalog, StringComparison.Ordinal);
        Assert.Contains("dualsense", catalog, StringComparison.Ordinal);
        Assert.Contains("dualsense-bt", catalog, StringComparison.Ordinal);
        Assert.Contains("dualshock-4-v2", catalog, StringComparison.Ordinal);
        Assert.Contains("xbox-360-wired", catalog, StringComparison.Ordinal);
        Assert.Contains("xbox-series-xs", catalog, StringComparison.Ordinal);
        Assert.Contains("DefaultProfileId", catalog, StringComparison.Ordinal);
        Assert.Contains("IsConfirmedProfileId", catalog, StringComparison.Ordinal);
        Assert.Contains("GamepadControllerFamily.Unknown", catalog, StringComparison.Ordinal);
        Assert.Contains("case \"cross\":", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"circle\":", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"square\":", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"triangle\":", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"release\":", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"lt\":", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"left-trigger\":", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"rt\":", bridge, StringComparison.Ordinal);
        Assert.Contains("case \"right-trigger\":", bridge, StringComparison.Ordinal);
        Assert.Contains("leftTrigger: 1f", bridge, StringComparison.Ordinal);
        Assert.Contains("rightTrigger: 1f", bridge, StringComparison.Ordinal);
        Assert.Contains("ProfileHasAnalogTriggers()", bridge, StringComparison.Ordinal);
        Assert.Contains("BuildTriggerAxes", bridge, StringComparison.Ordinal);
        Assert.Contains("WriteAxis(\"Z\", left)", bridge, StringComparison.Ordinal);
        Assert.Contains("WriteAxis(\"Rz\", right)", bridge, StringComparison.Ordinal);
        Assert.Contains("WriteAxis(\"Rx\", left)", bridge, StringComparison.Ordinal);
        Assert.Contains("WriteAxis(\"Ry\", right)", bridge, StringComparison.Ordinal);
        Assert.Contains("1u << 6", bridge, StringComparison.Ordinal);
        Assert.Contains("1u << 7", bridge, StringComparison.Ordinal);
        Assert.Contains("Sticky press", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("_getProfileMethod.Invoke(_context, [DefaultProfileId])", bridge, StringComparison.Ordinal);
        Assert.Contains("HoldThenAutoRelease(\"activate\")", nativeInput, StringComparison.Ordinal);
        Assert.Contains("HoldThenAutoRelease(\"back\")", nativeInput, StringComparison.Ordinal);
        Assert.Contains("HoldThenAutoRelease(\"west\")", nativeInput, StringComparison.Ordinal);
        Assert.Contains("HoldThenAutoRelease(\"voice\")", nativeInput, StringComparison.Ordinal);
        Assert.DoesNotContain("PressActivate() => HoldThenAutoRelease(\"a\")", nativeInput, StringComparison.Ordinal);
        Assert.DoesNotContain("PressBack() => HoldThenAutoRelease(\"b\")", nativeInput, StringComparison.Ordinal);
        Assert.Contains("Equals(command, \"info\"", bridge, StringComparison.Ordinal);
        Assert.Contains("FormatFamilyToken", bridge, StringComparison.Ordinal);
        Assert.Contains("ok profile=", bridge, StringComparison.Ordinal);
        Assert.Contains("family=", bridge, StringComparison.Ordinal);
        Assert.Contains("SendCommand(\"info\")", nativeInput, StringComparison.Ordinal);
        Assert.Contains("ActiveFamily", nativeInput, StringComparison.Ordinal);
        Assert.Contains("ParseBridgeInfo", nativeInput, StringComparison.Ordinal);

        var diagnosticsSmoke = LoadText(@"tests\SalmonEgg.GuiTests.Windows\DiagnosticsSettingsSmokeTests.cs");
        Assert.Contains("nativeGamepad.ActiveFamily", diagnosticsSmoke, StringComparison.Ordinal);
        Assert.Contains("familyToken", diagnosticsSmoke, StringComparison.Ordinal);
        Assert.Contains("var familyToken = \"family \" + expectedFamily", diagnosticsSmoke, StringComparison.Ordinal);
    }

    [Fact]
    public void HidMaestroMultiProfileRunner_StaysAlignedWithCoreCatalog()
    {
        // Prevent PS1 multi-profile loop from drifting brand tables off Core catalog.
        var runner = LoadText(@"scripts\gates\run-hidmaestro-multiprofile-native-smoke.ps1");
        var catalog = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\GamepadHidMaestroProfileCatalog.cs");
        var manifest = LoadText(@"scripts\gates\hidmaestro-multiprofile-manifest.txt");

        var expected = GamepadHidMaestroProfileCatalog.FormatMultiProfileGateManifest().Replace("\r\n", "\n");
        var actual = manifest.Replace("\r\n", "\n");
        if (!actual.EndsWith("\n", StringComparison.Ordinal))
        {
            actual += "\n";
        }

        Assert.Equal(expected, actual);

        foreach (var profileId in GamepadHidMaestroProfileCatalog.ConfirmedProfileIds)
        {
            // Profile ids are owned by Core catalog + checked-in manifest; the runner
            // loads them from the manifest rather than re-encoding brand tables.
            Assert.Contains(profileId, catalog, StringComparison.Ordinal);
            Assert.Contains(profileId + "|", manifest, StringComparison.Ordinal);
        }

        // Runner must consume the checked-in Core manifest instead of inventing brand tables.
        Assert.Contains("hidmaestro-multiprofile-manifest.txt", runner, StringComparison.Ordinal);
        Assert.Contains("Get-MultiProfileManifestRows", runner, StringComparison.Ordinal);
        Assert.Contains("Get-ExpectedFamilyToken", runner, StringComparison.Ordinal);
        Assert.Contains("Get-ExpectedPreferredFaceKey", runner, StringComparison.Ordinal);
        Assert.Contains("GetPhysicalButtonNameCandidates", catalog, StringComparison.Ordinal);
        Assert.Contains("FormatMultiProfileGateManifest", catalog, StringComparison.Ordinal);
        Assert.Contains("GetMultiProfileGateRows", catalog, StringComparison.Ordinal);
        Assert.Contains("GamepadControllerIdentity.FormatFamilyToken", catalog, StringComparison.Ordinal);

        // Manifest preferred keys cover multi-brand physical faces.
        Assert.Contains("|Cross|", manifest, StringComparison.Ordinal);
        Assert.Contains("|Circle|", manifest, StringComparison.Ordinal);
        Assert.Contains("|Square|", manifest, StringComparison.Ordinal);
        Assert.Contains("|Triangle", manifest, StringComparison.Ordinal);
        Assert.Contains("switch-pro|Nintendo|B|A|Y|X", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void GamepadDiagnosticsSnapshot_UsesTypedInputSourceContract()
    {
        var snapshot = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\GamepadDiagnosticsSnapshot.cs");
        var viewModel = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Settings\GamepadDiagnosticsViewModel.cs");
        var windowsService = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGamepadDiagnosticsService.cs");
        var projector = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\GamepadDiagnosticsActiveReadingProjector.cs");

        Assert.Contains("GamepadDiagnosticsInputSource InputSource", snapshot, StringComparison.Ordinal);
        Assert.Contains("InputSource: active.InputSource", windowsService, StringComparison.Ordinal);
        Assert.Contains("GamepadDiagnosticsActiveReadingProjector.Project", windowsService, StringComparison.Ordinal);
        Assert.Contains("GamepadDiagnosticsInputSource.Gamepad", projector, StringComparison.Ordinal);
        Assert.Contains("GamepadDiagnosticsInputSource.RawGameController", projector, StringComparison.Ordinal);
        Assert.Contains("RawGameControllerFaceButtonLayoutResolver.Resolve", viewModel, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyCollection<GamepadShortcutIntent> ActiveShortcuts", snapshot, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<StandardGamepadDiagnostics> StandardGamepads", snapshot, StringComparison.Ordinal);
        Assert.Contains("RawGameControllerFaceButtonLayout FaceButtonLayout", LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\StandardGamepadDiagnostics.cs"), StringComparison.Ordinal);
        Assert.Contains("ushort? HardwareVendorId", LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\StandardGamepadDiagnostics.cs"), StringComparison.Ordinal);
        Assert.Contains("GetIdentity", LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGameControllerButtonLabelMapper.cs"), StringComparison.Ordinal);
        Assert.Contains("RawGameController.FromGameController", LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGameControllerButtonLabelMapper.cs"), StringComparison.Ordinal);
        Assert.Contains("WindowsStandardGamepadIdentity.Empty", LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGameControllerButtonLabelMapper.cs"), StringComparison.Ordinal);
        Assert.Contains("catch (Exception)", LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\WindowsGameControllerButtonLabelMapper.cs"), StringComparison.Ordinal);
        Assert.Contains("gamepad.GetButtonLabel(button)", windowsService, StringComparison.Ordinal);
        Assert.Contains("WindowsGameControllerButtonLabelMapper.GetFaceButtonLabels", windowsService, StringComparison.Ordinal);
        Assert.Contains("FormatStandardGamepads", viewModel, StringComparison.Ordinal);
        Assert.Contains("FormatInputSource(GamepadDiagnosticsInputSource inputSource)", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("string InputSource", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("FormatInputSource(string", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("InputSource: \"", windowsService, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadDiagnosticsInputSource.Gamepad", windowsService, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadDiagnosticsInputSource.RawGameController", windowsService, StringComparison.Ordinal);
    }

    [Fact]
    public void MainShellGamepadNavigationDispatcher_DoesNotBridgePolledDirectionsThroughNativeGamepadKeys()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadNavigationDispatcher.cs");
        var mainPage = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("TryConsumeNavigationIntent", code);
        Assert.DoesNotContain("_nativeInputBridge", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShouldSuppressPolledGamepadIntent", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_gamepadNavigationDispatcher.TryDispatch(intent)", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_gamepadInputService.IntentRaised", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("OnGamepadIntentRaised", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadNavigationIntent.MoveDown => TryMoveFocus", code);
        Assert.DoesNotContain("XamlFocusManager.TryMoveFocus", code);
        Assert.DoesNotContain("GetNavigationSearchRoot()", code);
    }

    [Fact]
    public void MainPage_GamepadShortcutAndContextDispatch_UsesDirectDispatcherChain()
    {
        var mainPage = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");

        Assert.Contains("_gamepadShortcutDispatcher.TryDispatch(intent)", mainPage, StringComparison.Ordinal);
        Assert.Contains("_gamepadContextIntentDispatcher.TryDispatch(intent)", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ShouldSuppressPolledGamepadShortcut", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ShouldSuppressPolledGamepadContextIntent", mainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("suppressed due duplicate native keydown", mainPage, StringComparison.Ordinal);
    }

    [Fact]
    public void GamepadNativeInputBridge_IsNotRegisteredOrPackaged()
    {
        var dependencyInjection = LoadText(@"SalmonEgg\SalmonEgg\DependencyInjection.cs");
        var projectFile = LoadText(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj");

        Assert.False(File.Exists(Path.Combine(
            FindRepoRoot(),
            NormalizeRelativePath(@"src\SalmonEgg.Presentation.Core\Services\Input\IGamepadNativeInputBridge.cs"))));
        Assert.False(File.Exists(Path.Combine(
            FindRepoRoot(),
            NormalizeRelativePath(@"src\SalmonEgg.Presentation.Core\Services\Input\NoOpGamepadNativeInputBridge.cs"))));
        Assert.False(File.Exists(Path.Combine(
            FindRepoRoot(),
            NormalizeRelativePath(@"SalmonEgg\SalmonEgg\Platforms\Windows\WindowsGamepadNativeInputBridge.cs"))));
        Assert.False(File.Exists(Path.Combine(
            FindRepoRoot(),
            NormalizeRelativePath(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmGamepadNativeInputBridge.cs"))));
        Assert.False(File.Exists(Path.Combine(
            FindRepoRoot(),
            NormalizeRelativePath(@"SalmonEgg\SalmonEgg\Platforms\WebAssembly\WasmScripts\salmon-egg-wasm-gamepad.js"))));
        Assert.DoesNotContain("IGamepadNativeInputBridge", dependencyInjection, StringComparison.Ordinal);
        Assert.DoesNotContain("NoOpGamepadNativeInputBridge", dependencyInjection, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowsGamepadNativeInputBridge", dependencyInjection, StringComparison.Ordinal);
        Assert.DoesNotContain("WasmGamepadNativeInputBridge", dependencyInjection, StringComparison.Ordinal);
        Assert.DoesNotContain("salmon-egg-wasm-gamepad.js", projectFile, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Remove=""Platforms/Windows/**/*.cs"" />", projectFile, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationIntentConsumer_Contract_Exists_AndDispatcherRemainsControlAgnostic()
    {
        var contract = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\INavigationIntentConsumer.cs");
        var dispatcher = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadNavigationDispatcher.cs");

        Assert.Contains("interface INavigationIntentConsumer", contract);
        Assert.Contains("TryConsumeNavigationIntent", contract);
        Assert.Contains("INavigationIntentConsumer", dispatcher);
        Assert.DoesNotContain("ChatInputArea", dispatcher, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_NavigationIntentSupport_PreservesKeyboardAndSlashHandlers()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");
        var policy = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\ChatInputNavigationPolicy.cs");

        Assert.Contains("INavigationIntentConsumer", code);
        Assert.Contains("MoveUpEscapeHandler", code);
        Assert.Contains("UIElement.KeyDownEvent", code, StringComparison.Ordinal);
        Assert.Contains("_inputBoxHandledKeyDownHandler", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GamepadNavigationIntent.MoveUp when focusContext == ChatInputFocusContext.ModeSelector", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("InputBox.IsEnabled && ViewModel.IsInputEnabled", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_CodeBehind_UsesGamepadShortcutConsumer_WithoutExpandingNavigationEnum()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");
        var navigationEnum = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\GamepadNavigationIntent.cs");

        Assert.Contains("IGamepadShortcutConsumer", code, StringComparison.Ordinal);
        Assert.Contains("TryConsumeShortcutIntent", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleVoiceInput", navigationEnum, StringComparison.Ordinal);
    }

    [Fact]
    public void ValueSelectors_UseNativeFocusEngagementForGamepadTraversal()
    {
        var root = Path.Combine(FindRepoRoot(), "SalmonEgg", "SalmonEgg");
        var failures = new List<string>();

        foreach (var xamlFile in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            if (xamlFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || xamlFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var document = XDocument.Parse(File.ReadAllText(xamlFile));
            foreach (var control in document.Descendants().Where(IsValueSelectorRequiringFocusEngagement))
            {
                if (string.Equals(control.Attribute("IsFocusEngagementEnabled")?.Value, "True", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = control.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name")?.Value
                         ?? control.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "AutomationProperties.AutomationId")?.Value
                         ?? control.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Uid")?.Value
                         ?? "<unnamed>";
                failures.Add($"{Path.GetRelativePath(FindRepoRoot(), xamlFile)} {control.Name.LocalName} {id}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void FontIconsWithGlyph_UseSymbolThemeFontFamily_ForCrossPlatformGlyphRendering()
    {
        var root = Path.Combine(FindRepoRoot(), "SalmonEgg", "SalmonEgg");
        var failures = new List<string>();

        foreach (var xamlFile in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            if (xamlFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || xamlFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var document = XDocument.Parse(File.ReadAllText(xamlFile));
            foreach (var icon in document.Descendants().Where(element => element.Name.LocalName == "FontIcon"))
            {
                if (icon.Attribute("Glyph") is null)
                {
                    continue;
                }

                if (string.Equals(
                    icon.Attribute("FontFamily")?.Value,
                    "{ThemeResource SymbolThemeFontFamily}",
                    StringComparison.Ordinal))
                {
                    continue;
                }

                var id = icon.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name")?.Value
                         ?? icon.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "AutomationProperties.AutomationId")?.Value
                         ?? icon.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Uid")?.Value
                         ?? icon.Attribute("Glyph")?.Value
                         ?? "<unnamed>";
                failures.Add($"{Path.GetRelativePath(FindRepoRoot(), xamlFile)} FontIcon {id}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void NumberBoxes_UseSystemFocusVisuals_ForGamepadFocusVisibility()
    {
        var root = Path.Combine(FindRepoRoot(), "SalmonEgg", "SalmonEgg");
        var failures = new List<string>();

        foreach (var xamlFile in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            if (xamlFile.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || xamlFile.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var document = XDocument.Parse(File.ReadAllText(xamlFile));
            foreach (var control in document.Descendants().Where(element => element.Name.LocalName == "NumberBox"))
            {
                if (string.Equals(control.Attribute("UseSystemFocusVisuals")?.Value, "True", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = control.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Name")?.Value
                         ?? control.Attribute(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml") + "Uid")?.Value
                         ?? "<unnamed>";
                failures.Add($"{Path.GetRelativePath(FindRepoRoot(), xamlFile)} NumberBox {id}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void MainPage_GamepadDirectionalBridge_IsNotAttachedAtShellLevel()
    {
        var sharedPage = LoadText(@"SalmonEgg\SalmonEgg\MainPage.xaml.cs");
        var windowsPage = LoadText(@"SalmonEgg\SalmonEgg\Platforms\Windows\MainPage.Windows.cs");

        Assert.DoesNotContain("AttachPlatformGamepadDirectionalBridge", sharedPage, StringComparison.Ordinal);
        Assert.DoesNotContain("DetachPlatformGamepadDirectionalBridge", sharedPage, StringComparison.Ordinal);
        Assert.DoesNotContain("InputKeyboardSource", sharedPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.System.VirtualKey.GamepadDPadRight", sharedPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.System.VirtualKey.GamepadDPadRight", windowsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("TryDispatchWithoutNativeFallback", windowsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_WindowsPlatformBridge_DoesNotOwnGlobalGamepadNavigationFallbacks()
    {
        var windowsPage = LoadText(@"SalmonEgg\SalmonEgg\Platforms\Windows\MainPage.Windows.cs");
        var dispatcher = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\Input\MainShellGamepadNavigationDispatcher.cs");
        var contract = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\IGamepadNavigationDispatcher.cs");

        Assert.DoesNotContain("TryDispatchWithoutNativeFallback", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("TryDispatchWithoutNativeFallback", dispatcher, StringComparison.Ordinal);
        Assert.DoesNotContain("TryDispatchCore(intent, allowNativeFallback", dispatcher, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPlatformGamepadDirectionalBridgeKeyDown", windowsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.System.VirtualKey.Gamepad", windowsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("TryMoveFocusFromMainNavigationIntoCurrentContent()", windowsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("XamlFocusManager.TryMoveFocus", windowsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("AutomationPeer", windowsPage, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowMetricsProvider_DoesNotExposeAppWindowTitleBar()
    {
        var provider = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\WindowMetricsProvider.cs");
        var titleBarAdapter = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Navigation\MainWindowTitleBarAdapter.cs");

        Assert.Contains("ITitleBarInsetProvider", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("AppWindowTitleBar", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("AppWindowTitleBar =>", titleBarAdapter, StringComparison.Ordinal);
        Assert.Contains("ITitleBarInsetProvider", titleBarAdapter, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsShellPage_UsesNativeXyFocusWithoutPageLevelGamepadTraversal()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml.cs");
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml");
        var document = XDocument.Parse(xaml);
        var pageBase = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsPageBase.cs");
        var acpPage = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml.cs");
        var diagnosticsPage = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml.cs");
        var shellGrid = document
            .Descendants()
            .Single(element => element.Name.LocalName == "Grid"
                && string.Equals(GetAttributeByLocalName(element, "Name"), "SettingsShellRoot", StringComparison.Ordinal));

        Assert.Contains("x:Name=\"SettingsShellHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"SettingsShellRoot.Padding\" Value=\"24,12\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"SettingsShellRoot.Padding\" Value=\"40,24\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SettingsShellPage : Page, IPrimaryContentFocusTarget", code, StringComparison.Ordinal);
        Assert.Contains("public bool TryFocusPrimaryContentTarget()", code, StringComparison.Ordinal);
        Assert.Contains("=> TryFocusCurrentSectionNavigationItem();", code, StringComparison.Ordinal);
        Assert.Contains("SettingsNavView.ContainerFromMenuItem(ViewModel.SelectedSection)", code, StringComparison.Ordinal);
        Assert.Equal("Enabled", shellGrid.Attribute("XYFocusKeyboardNavigation")?.Value);
        Assert.Contains("XYFocusKeyboardNavigation=\"Enabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"SettingsNavView\"", xaml, StringComparison.Ordinal);
        Assert.Contains("navItem.XYFocusDown = sectionEntryTarget;", code, StringComparison.Ordinal);
        Assert.Contains("returnTarget.XYFocusUp = navItem;", code, StringComparison.Ordinal);
        Assert.Contains("protected virtual Control? GetSectionEntryFocusTarget()", pageBase, StringComparison.Ordinal);
        Assert.Contains("protected virtual IEnumerable<Control?> GetSectionFocusReturnTargets()", pageBase, StringComparison.Ordinal);
        Assert.Contains("if (!TryRefreshCurrentSectionFocusTargets())", code, StringComparison.Ordinal);
        Assert.Contains("settingsPage.Loaded += OnDeferredFocusTargetRefreshLoaded;", code, StringComparison.Ordinal);
        Assert.Contains("DetachDeferredFocusTargetRefresh(settingsPage);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("LayoutUpdated", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryConsumeNavigationIntent", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryMoveFocusWithinSettingsContent", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsFocusOnFirstSettingsContentControl", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GetInteractiveControlsInTraversalOrder", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FindDescendants<Control>", code, StringComparison.Ordinal);
        Assert.DoesNotContain("control is ComboBox or NumberBox or ToggleSwitch or TextBox or Button or Expander", code, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedItem.Focus(FocusState.Keyboard)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsNavView.Focus(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Focus(FocusState.Programmatic)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("INavigationIntentConsumer", acpPage, StringComparison.Ordinal);
        Assert.DoesNotContain("INavigationIntentConsumer", diagnosticsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("_lastFocusedGamepadActionButton", diagnosticsPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.SelectedSection.Key == SettingsSectionCatalog.AgentAcpKey", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.SelectedSection.Key == SettingsSectionCatalog.McpKey", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsDpapiSecureStorage_DoesNotDecodeLegacyPlainTextSecrets()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Platforms\Windows\WindowsDpapiSecureStorage.cs");

        Assert.DoesNotContain("TryDecodeLegacyPlainText", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IsPlausibleLegacySecret", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(key,", code, StringComparison.Ordinal);
        Assert.Contains("Stored secure data for key '{key}' could not be decrypted", code, StringComparison.Ordinal);
    }
}

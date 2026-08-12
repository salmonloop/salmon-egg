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

public sealed class XamlComplianceSettingsTests
{

    [Fact]
    public void AcpEditors_ExposeStableAutomationIdsForEditableFields()
    {
        var agentProfileEditor = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml");
        var acpSettings = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml");

        Assert.Contains("AutomationProperties.AutomationId=\"Acp.ProfileEditor.Name\"", agentProfileEditor, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Acp.ProfileEditor.ServerUrl\"", agentProfileEditor, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Acp.RemoteDirectories.DisplayName\"", acpSettings, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"Acp.RemoteDirectories.RemotePath\"", acpSettings, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPages_UseSharedResponsiveContentAndFormRows()
    {
        var settingsFiles = Directory.GetFiles(
            Path.Combine(FindRepoRoot(), "SalmonEgg", "SalmonEgg", "Presentation", "Views", "Settings"),
            "*.xaml",
            SearchOption.TopDirectoryOnly);
        var generalSettings = Path.Combine(
            FindRepoRoot(),
            "SalmonEgg",
            "SalmonEgg",
            "Presentation",
            "Views",
            "GeneralSettingsPage.xaml");

        foreach (var file in settingsFiles.Append(generalSettings))
        {
            var xaml = File.ReadAllText(file);
            Assert.Contains("ResponsiveContentHost", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("ResponsiveSettingsHost", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("<ColumnDefinition Width=\"220\"", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("ResponsiveFormRow", LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml"), StringComparison.Ordinal);
        Assert.Contains("ResponsiveFormRow", LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml"), StringComparison.Ordinal);
        Assert.Contains("ResponsiveFormRow", LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\ShortcutsSettingsPage.xaml"), StringComparison.Ordinal);

        var formRow = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ResponsiveFormRow.xaml");
        Assert.Contains("x:Class=\"SalmonEgg.Controls.ResponsiveFormRow\"", formRow, StringComparison.Ordinal);
        Assert.Contains("<AdaptiveTrigger MinWindowWidth=\"560\" />", formRow, StringComparison.Ordinal);
        Assert.DoesNotContain("MinActualWidthTrigger", formRow, StringComparison.Ordinal);
        Assert.Contains("<Setter Target=\"ValuePresenter.(Grid.Row)\" Value=\"1\" />", formRow, StringComparison.Ordinal);
        Assert.Contains("<Setter Target=\"LabelColumn.Width\" Value=\"220\" />", formRow, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentProfileEditor_DoesNotUseValueChangedHandlers()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml");

        Assert.DoesNotContain("ValueChanged=\"OnTimeoutValueChanged\"", xaml);
    }

    [Fact]
    public void ConfigurationEditorDialog_TextsAreLocalized()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\ConfigurationEditorDialog.xaml");

        Assert.DoesNotContain("Header=\"名称\"", xaml);
        Assert.DoesNotContain("PlaceholderText=\"例如：本地 Agent / 远程测试环境\"", xaml);
        Assert.DoesNotContain("Text=\"保存后会自动与“配置”卡片联动。\"", xaml);
        Assert.Contains("x:Uid=\"ConfigurationEditorDialog\"", xaml);
        Assert.Contains("x:Uid=\"ConfigurationEditorDialogName\"", xaml);
        Assert.Contains("x:Uid=\"ConfigurationEditorDialogHint\"", xaml);
    }

    [Fact]
    public void GeneralSettings_LanguageOptionsAreBoundToViewModelCatalog()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml");

        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.Preferences.LanguageOptions, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{x:Bind ViewModel.Preferences.SelectedLanguageOption, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValuePath=\"Tag\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValue=\"{x:Bind ViewModel.Preferences.Language, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"settings:AppLanguageOptionViewModel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind DisplayNameResourceKey, Mode=OneWay, Converter={StaticResource ResourceStringConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Tag=\"zh-CN\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ComboBoxItem x:Uid=\"General_LanguageZhCn\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppearanceSettings_OptionsAreBoundToViewModelCatalog()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml");

        Assert.Contains("ItemsSource=\"{x:Bind Preferences.ThemeOptions, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{x:Bind Preferences.SelectedThemeOption, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{x:Bind Preferences.BackdropOptions, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{x:Bind Preferences.SelectedBackdropOption, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValuePath=\"Tag\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ComboBoxItem x:Uid=\"Appearance_Theme", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ComboBoxItem x:Uid=\"Appearance_Backdrop", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentProfileEditor_InteractiveTextsExposeLocalizationUids()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml");
        var zhHans = LoadText(@"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw");

        Assert.Contains("x:Uid=\"AgentProfileEditorName\"", xaml);
        Assert.Contains("x:Uid=\"AgentProfileEditorAdvancedTitle\"", xaml);
        Assert.Contains("x:Uid=\"AgentProfileEditorCancelButton\"", xaml);
        Assert.Contains("AgentProfileEditorName.Header", zhHans);
        Assert.Contains("AgentProfileEditorPageTitleNew", zhHans);
        Assert.Contains("AgentProfileEditorPageTitleEdit", zhHans);
    }

    [Fact]
    public void DiagnosticsSettingsPage_ExposesVoiceDiagnosticsThroughViewModel()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml");
        var viewModel = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Settings\VoiceInputDiagnosticsViewModel.cs");
        var service = LoadText(@"src\SalmonEgg.Presentation.Core\Services\Input\VoiceInputDiagnosticsService.cs");
        var voiceSection = ExtractSection(xaml, "Diagnostics_VoiceTitle", "Diagnostics_GamepadTitle");

        Assert.Contains("AutomationProperties.AutomationId=\"Diagnostics.VoiceHeader\"", voiceSection, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.SupportStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.PermissionStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.CurrentLanguageTagText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.InputDeviceText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.SessionStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.CallbackObservationText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.TimelineText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.RecommendationText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.RefreshSnapshotCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.OpenAuthorizationHelpCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.StartProbeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.StopProbeCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeTimelineText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeCapturedText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeSignalObservationText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VoiceInputDiagnostics.Probe.ProbeSignalTimelineText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Media.SpeechRecognition", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Media.SpeechRecognition", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.Media.SpeechRecognition", LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Settings\VoiceInputDiagnosticsProbeViewModel.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.UI.Xaml", service, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsSubPages_ExposePageTitlesAndSummaries()
    {
        string[] pages =
        [
            @"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\ShortcutsSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml"
        ];

        foreach (var page in pages)
        {
            var xaml = LoadXaml(page);

            Assert.Contains("Style=\"{StaticResource SettingsPageTitleTextStyle}\"", xaml, StringComparison.Ordinal);
            Assert.Contains("Style=\"{StaticResource SettingsPageSummaryTextStyle}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void GeneralAndAppearanceSettingsPages_UseNativeSettingsRows()
    {
        var general = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml");
        var appearance = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml");

        Assert.Contains("x:Uid=\"General_PageTitle\"", general, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"General_PageSummary\"", general, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SettingsRowGridStyle}\"", general, StringComparison.Ordinal);
        Assert.Contains("<ToggleSwitch", general, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"General_AutoStartSwitch\"", general, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"General_MinimizeToTraySwitch\"", general, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"General_AutoStart\"", general, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"General_MinimizeToTray\"", general, StringComparison.Ordinal);
        Assert.Contains("<ComboBox", general, StringComparison.Ordinal);

        Assert.Contains("x:Uid=\"Appearance_PageTitle\"", appearance, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"Appearance_PageSummary\"", appearance, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource SettingsRowGridStyle}\"", appearance, StringComparison.Ordinal);
        Assert.Contains("<ToggleSwitch", appearance, StringComparison.Ordinal);
        Assert.Contains("<ComboBox", appearance, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortcutsSettingsPage_RestoreAllRowKeepsDescriptionBesideAction()
    {
        var document = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\ShortcutsSettingsPage.xaml"));
        var restoreAllDescription = FindElementByUid(document, "Shortcuts_RestoreAllDescription");
        var restoreAllButton = FindElementByUid(document, "Shortcuts_RestoreAll");
        var restoreAllRow = Assert.Single(
            restoreAllButton.Ancestors(),
            element => element.Name.LocalName == "Grid"
                && string.Equals(GetAttributeByLocalName(element, "Style"), "{StaticResource SettingsRowGridStyle}", StringComparison.Ordinal));

        Assert.Equal("{StaticResource SettingsRowDescriptionTextStyle}", GetAttributeByLocalName(restoreAllDescription, "Style"));
        Assert.Contains(restoreAllDescription, restoreAllRow.Descendants());
        Assert.Contains(restoreAllButton, restoreAllRow.Descendants());
    }

    [Fact]
    public void Task6SettingsPages_HaveLocalizedVisibleTextResources()
    {
        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        ];
        string[] requiredResources =
        [
            "Shortcuts_PageTitle.Text",
            "Shortcuts_PageSummary.Text",
            "Shortcuts_ConflictInfo.Title",
            "Shortcuts_InvalidInfo.Title",
            "Shortcuts_InvalidInfo.Message",
            "Shortcuts_CustomTitle.Text",
            "Shortcuts_EnableTitle.Text",
            "Shortcuts_EnableDescription.Text",
            "Shortcuts_EnableToggle.OnContent",
            "Shortcuts_EnableToggle.OffContent",
            "Shortcuts_AppOnlyHint.Text",
            "Shortcuts_GestureRecorder.PlaceholderText",
            "Shortcuts_GestureRecorder.RecordingText",
            "Shortcuts_RestoreSingle.Content",
            "Shortcuts_RestoreAllDescription.Text",
            "Shortcuts_RestoreAll.Content",
            "Diagnostics_PageTitle.Text",
            "Diagnostics_PageSummary.Text",
            "Diagnostics_EnvironmentTitle.Text",
            "Diagnostics_OsLabel.Text",
            "Diagnostics_FrameworkLabel.Text",
            "Diagnostics_AppVersionLabel.Text",
            "Diagnostics_ProtocolVersionLabel.Text",
            "Diagnostics_LogsTitle.Text",
            "Diagnostics_LogsFolderLabel.Text",
            "Diagnostics_LatestLogLabel.Text",
            "Diagnostics_OpenLogs.Content",
            "Diagnostics_CopyLogSnippet.Content",
            "Diagnostics_RefreshLogs.Content",
            "Diagnostics_LogActionsTitle.Text",
            "Diagnostics_LiveLogHeader.Text",
            "Diagnostics_LiveLogStart.Content",
            "Diagnostics_LiveLogPause.Content",
            "Diagnostics_LiveLogResume.Content",
            "Diagnostics_LiveLogClear.Content",
            "Diagnostics_LiveLogHint.Text",
            "Diagnostics_VoiceTitle.Text",
            "Diagnostics_VoiceHeader.Text",
            "Diagnostics_VoiceProbeHeader.Text",
            "Diagnostics_VoiceSupportLabel.Text",
            "Diagnostics_VoicePermissionLabel.Text",
            "Diagnostics_VoiceLanguageLabel.Text",
            "Diagnostics_VoiceInputDeviceLabel.Text",
            "Diagnostics_VoiceSessionStatusLabel.Text",
            "Diagnostics_VoiceCallbackStatusLabel.Text",
            "Diagnostics_VoiceTimelineLabel.Text",
            "Diagnostics_VoiceRecommendationLabel.Text",
            "Diagnostics_VoiceProbeStatusLabel.Text",
            "Diagnostics_VoiceProbeTimelineLabel.Text",
            "Diagnostics_VoiceProbeCapturedTextLabel.Text",
            "Diagnostics_VoiceProbeSignalLabel.Text",
            "Diagnostics_VoiceProbeSignalTimelineLabel.Text",
            "Diagnostics_VoiceRefresh.Content",
            "Diagnostics_VoiceOpenAuthorization.Content",
            "Diagnostics_VoiceProbeStart.Content",
            "Diagnostics_VoiceProbeStop.Content",
            "Diagnostics_GamepadTitle.Text",
            "Diagnostics_GamepadMonitorHeader.Text",
            "Diagnostics_GamepadStatusLabel.Text",
            "Diagnostics_GamepadStandardCountLabel.Text",
            "Diagnostics_GamepadRawCountLabel.Text",
            "Diagnostics_GamepadInputSourceLabel.Text",
            "Diagnostics_GamepadActiveInputsLabel.Text",
            "Diagnostics_GamepadThumbstickLabel.Text",
            "Diagnostics_GamepadStandardDetailsLabel.Text",
            "Diagnostics_GamepadRawDetailsLabel.Text",
            "Diagnostics_GamepadStart.Content",
            "Diagnostics_GamepadStop.Content",
            "Diagnostics_GamepadRefresh.Content",
            "Diagnostics_ConnectionTitle.Text",
            "Diagnostics_ConnectionStatusLabel.Text",
            "Diagnostics_AgentLabel.Text",
            "Diagnostics_SessionLabel.Text",
            "Diagnostics_BundleTitle.Text",
            "Diagnostics_BundleDescription.Text",
            "Diagnostics_CreateBundle.Content",
            "About_PageTitle.Text",
            "About_PageSummary.Text",
            "About_AppInfoTitle.Text",
            "About_AppNameLabel.Text",
            "About_VersionLabel.Text",
            "About_ProtocolLabel.Text",
            "About_CommunityTitle.Text",
            "About_DiscordTitle.Text",
            "About_DiscordDescription.Text",
            "About_JoinDiscord.Content",
            "About_GitHubTitle.Text",
            "About_GitHubDescription.Text",
            "About_OpenGitHub.Content",
            "About_SupportProjectTitle.Text",
            "About_KofiTitle.Text",
            "About_KofiDescription.Text",
            "About_OpenKofi.Content",
            "About_SupportTitle.Text",
            "About_SupportActionsTitle.Text",
            "About_OpenAppData.Content",
            "About_ReportInappropriateAiContent.Content",
            "About_OpenReleaseNotes.Content",
            "About_OpenPrivacyPolicy.Content",
            "About_CopyVersionInfo.Content",
            "About_DocsFolderLabel.Text",
            "About_DocsHint.Text",
            "About_OpenSourceTitle.Text",
            "About_OpenSourceDescription.Text",
            "About_OpenSourcePackageHeader.Text",
            "About_OpenSourceVersionHeader.Text",
            "About_OpenSourceLicenseHeader.Text",
            "About_OpenSourceSourceHeader.Text"
        ];

        foreach (var resourceFile in resourceFiles)
        {
            var resources = XDocument.Parse(LoadText(resourceFile));

            foreach (var resourceName in requiredResources)
            {
                Assert.True(
                    resources.Descendants("data")
                        .Any(data => string.Equals((string?)data.Attribute("name"), resourceName, StringComparison.Ordinal)),
                    $"{resourceFile} must define {resourceName}.");
            }
        }
    }

    [Fact]
    public void GeneralAndAppearanceSettingsPages_HaveLocalizedVisibleTextResources()
    {
        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        ];
        string[] requiredResources =
        [
            "General_PageTitle.Text",
            "General_PageSummary.Text",
            "General_AutoStartTitle.Text",
            "General_MinimizeToTrayTitle.Text",
            "Appearance_PageTitle.Text",
            "Appearance_PageSummary.Text",
            "Appearance_ThemeLabel.Text",
            "Appearance_BackdropLabel.Text",
            "Appearance_BackdropMica.Content",
            "Appearance_BackdropAcrylic.Content"
        ];

        foreach (var resourceFile in resourceFiles)
        {
            var resources = XDocument.Parse(LoadText(resourceFile));

            foreach (var resourceName in requiredResources)
            {
                Assert.True(
                    resources.Descendants("data")
                        .Any(data => string.Equals((string?)data.Attribute("name"), resourceName, StringComparison.Ordinal)),
                    $"{resourceFile} must define {resourceName}.");
            }
        }
    }

    [Fact]
    public void SettingsShell_KeepsSectionNavigationAtTheTop()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml");

        Assert.Contains("<Setter Property=\"PaneDisplayMode\" Value=\"Top\" />", xaml);
        Assert.DoesNotContain("PaneDisplayMode\" Value=\"Left", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<NavigationViewItemHeader", xaml, StringComparison.Ordinal);
        Assert.Contains("MenuItemsSource=\"{x:Bind ViewModel.Sections, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{x:Bind ViewModel.SelectedSection, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged=\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsShell_SelectionUsesViewModelSectionIdentity()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml.cs");
        var adapterCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Navigation\SettingsSectionNavigationAdapter.cs");

        Assert.Contains("public SettingsShellViewModel ViewModel { get; }", code, StringComparison.Ordinal);
        Assert.Contains("_sectionNavigation = new SettingsSectionNavigationAdapter(SettingsNavView)", code, StringComparison.Ordinal);
        Assert.Contains("private void AttachSectionNavigation()", code, StringComparison.Ordinal);
        Assert.Contains("private void DetachSectionNavigation()", code, StringComparison.Ordinal);
        Assert.Contains("_sectionNavigation = null;", code, StringComparison.Ordinal);
        Assert.Contains("ViewModel.SelectSection(key)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new NavigationViewItem", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsNavView.MenuItems", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FindNavItemByKey", code, StringComparison.Ordinal);

        Assert.Contains("SettingsShellSectionViewModel section", adapterCode, StringComparison.Ordinal);
        Assert.Contains("_navigationView.ItemInvoked += OnItemInvoked", adapterCode, StringComparison.Ordinal);
        Assert.Contains("_navigationView.ItemInvoked -= OnItemInvoked", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var section in sections)", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_navigationView.MenuItems.Add", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_navigationView.SelectedItem =", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectionChanged", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_suppressSelectionChanged", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MenuItemsSource=", adapterCode, StringComparison.Ordinal);
        Assert.DoesNotContain(".MenuItemsSource", adapterCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsBreadcrumbsUseCanonicalSectionCatalog()
    {
        string[] settingsPageFiles =
        [
            @"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\ShortcutsSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml.cs"
        ];

        foreach (var settingsPageFile in settingsPageFiles)
        {
            var code = LoadText(settingsPageFile);

            Assert.DoesNotContain("SettingsNav_", code, StringComparison.Ordinal);
            Assert.DoesNotContain("SettingsBreadcrumbRoot", code, StringComparison.Ordinal);
            Assert.Contains("SettingsSectionCatalog.", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SettingsBreadcrumbBar_ActivatesThroughNavigationViewModelOwner()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\SettingsBreadcrumbBar.xaml.cs");

        Assert.Contains("MainNavigationViewModel", code, StringComparison.Ordinal);
        Assert.Contains("ActivateSettingsAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("INavigationCoordinator", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IShellNavigationService", code, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigateToSettings", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneralSettingsPage_DoesNotDuplicateCacheMaintenance()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml");

        Assert.DoesNotContain("General_MaintenanceTitle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("General_ClearCacheTitle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("General_ClearCacheAction", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppearanceSettingsPage_MotionPreferenceIsActionable()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml");

        Assert.Contains("IsOn=\"{x:Bind Preferences.IsAnimationEnabled, Mode=TwoWay}\"", xaml);
        Assert.DoesNotContain("未实现", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"False\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppearanceSettingsPage_MotionPreferenceCopyUsesUserLanguage()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml");

        Assert.DoesNotContain("全局过渡动画", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("依赖动画", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("dependent", xaml, StringComparison.OrdinalIgnoreCase);

        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        ];

        foreach (var resourceFile in resourceFiles)
        {
            var resources = XDocument.Parse(LoadText(resourceFile));
            var title = GetResourceValue(resources, "Appearance_MotionToggleTitle.Text");
            var description = GetResourceValue(resources, "Appearance_MotionToggleDescription.Text");
            var combinedText = $"{title} {description}";

            Assert.DoesNotContain("依赖动画", combinedText, StringComparison.Ordinal);
            Assert.DoesNotContain("dependent", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("native control", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("全局", combinedText, StringComparison.Ordinal);
            Assert.DoesNotContain("global", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                resourceFile.Contains("zh-Hans", StringComparison.Ordinal)
                    ? "页面"
                    : "page",
                combinedText,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                resourceFile.Contains("zh-Hans", StringComparison.Ordinal)
                    ? "状态"
                    : "status",
                combinedText,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                resourceFile.Contains("zh-Hans", StringComparison.Ordinal)
                    ? "系统关闭动画"
                    : "system animations are off",
                combinedText,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                resourceFile.Contains("zh-Hans", StringComparison.Ordinal)
                    ? "自动关闭"
                    : "turn off automatically",
                combinedText,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DataStorageSettingsPage_SeparatesRoutineStorageAndDangerActions()
    {
        var document = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml"));
        var xaml = document.ToString(SaveOptions.DisableFormatting);
        var resetDefaultsTitle = FindElementByUid(document, "DataStorage_ResetDefaultsTitle");
        var clearAllDataTitle = FindElementByUid(document, "DataStorage_ClearAllDataTitle");
        var resetDefaults = FindElementByUid(document, "DataStorage_ResetDefaults");
        var clearAllData = FindElementByUid(document, "DataStorage_ClearAllData");
        var dangerTitle = FindElementByUid(document, "DataStorage_DangerTitle");
        var dangerExpander = Assert.Single(dangerTitle.Ancestors(), element => element.Name.LocalName == "Expander");
        var resetDefaultsRow = Assert.Single(
            resetDefaults.Ancestors(),
            element => element.Name.LocalName == "Grid"
                && string.Equals(GetAttributeByLocalName(element, "Style"), "{StaticResource SettingsRowGridStyle}", StringComparison.Ordinal));
        var clearAllDataRow = Assert.Single(
            clearAllData.Ancestors(),
            element => element.Name.LocalName == "Grid"
                && string.Equals(GetAttributeByLocalName(element, "Style"), "{StaticResource SettingsRowGridStyle}", StringComparison.Ordinal));

        Assert.Contains("x:Uid=\"DataStorage_PageTitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"DataStorage_PageSummary\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Expander", xaml, StringComparison.Ordinal);
        Assert.Contains("DataStorage_DangerTitle", xaml, StringComparison.Ordinal);
        Assert.Contains("DataStorage_DangerWarning", xaml, StringComparison.Ordinal);
        Assert.Equal("Stretch", GetAttributeByLocalName(dangerExpander, "HorizontalAlignment"));
        Assert.Equal("Stretch", GetAttributeByLocalName(dangerExpander, "HorizontalContentAlignment"));
        Assert.Equal("0", GetAttributeByLocalName(dangerExpander, "Padding"));
        Assert.Equal("{StaticResource SettingsRowTitleTextStyle}", GetAttributeByLocalName(resetDefaultsTitle, "Style"));
        Assert.Equal("{StaticResource SettingsRowTitleTextStyle}", GetAttributeByLocalName(clearAllDataTitle, "Style"));
        Assert.NotSame(resetDefaults.Parent, clearAllData.Parent);
        Assert.NotSame(resetDefaultsRow, clearAllDataRow);
        Assert.Contains(resetDefaultsRow, dangerExpander.Descendants());
        Assert.Contains(clearAllDataRow, dangerExpander.Descendants());

        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        ];
        foreach (var resourceFile in resourceFiles)
        {
            var resources = LoadText(resourceFile);

            Assert.Contains("DataStorage_ResetDefaultsTitle.Text", resources, StringComparison.Ordinal);
            Assert.Contains("DataStorage_ClearAllDataTitle.Text", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("DataStorage_ResetTitle.Text", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("DataStorage_ResetWarning.Text", resources, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DataStorageSettingsPage_ProjectsCloudSyncStatusAsPersistentTwoLayerSummary()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml");

        Assert.Contains("DataStorage.CloudSync.ConnectionStatus", xaml, StringComparison.Ordinal);
        Assert.Contains("DataStorage.CloudSync.TransferStatus", xaml, StringComparison.Ordinal);
        Assert.Contains("DataStorage.CloudSync.Error", xaml, StringComparison.Ordinal);
        Assert.Contains("DataStorage.CloudSync.Progress", xaml, StringComparison.Ordinal);
        Assert.Contains("DataStorage.CloudSync.RemoteTarget", xaml, StringComparison.Ordinal);
        Assert.Contains("ProgressRing", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.CloudConfig.StatusHeadline", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.CloudConfig.TransferStatusText", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.CloudConfig.ConnectionContextText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.CloudConfig.ActiveRemoteTarget", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DataStorageSettingsPage_HidesCloudSyncDestructiveActionsBehindMoreActions()
    {
        var document = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml"));
        var disable = FindElementByUid(document, "DataStorage_CloudSyncDisable");
        var remove = FindElementByUid(document, "DataStorage_CloudSyncForget");
        var moreActionsTitle = FindElementByUid(document, "DataStorage_CloudSyncMoreActions");
        var moreActionsExpander = Assert.Single(
            moreActionsTitle.Ancestors(),
            element => element.Name.LocalName == "Expander");

        Assert.Equal("False", GetAttributeByLocalName(moreActionsExpander, "IsExpanded"));
        Assert.Contains(disable, moreActionsExpander.Descendants());
        Assert.Contains(remove, moreActionsExpander.Descendants());
    }

    [Fact]
    public void DiagnosticsSettingsPage_DefaultsDeepDiagnosticsToCollapsedSections()
    {
        var document = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml"));

        AssertCollapsedExpanderOwnsUid(document, "Diagnostics_VoiceTitle");
        AssertCollapsedExpanderOwnsUid(document, "Diagnostics_GamepadTitle");
        AssertExpanderOwnsUid(document, "Diagnostics_LiveLogHeader");
        AssertCollapsedExpanderOwnsUid(document, "Diagnostics_ConnectionTitle");
    }

    [Fact]
    public void AboutPage_DefaultsOpenSourceAcknowledgementsToCollapsedDisclosure()
    {
        var document = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml"));
        var openSourceTitle = FindElementByUid(document, "About_OpenSourceTitle");
        var openSourceExpander = Assert.Single(
            openSourceTitle.Ancestors(),
            element => element.Name.LocalName == "Expander");

        Assert.Equal("False", GetAttributeByLocalName(openSourceExpander, "IsExpanded"));
        Assert.Contains(
            document.Descendants().Single(element =>
                string.Equals(GetAttributeByLocalName(element, "AutomationProperties.AutomationId"), "About.OpenSourceAcknowledgements", StringComparison.Ordinal)),
            openSourceExpander.Descendants());
    }

    [Fact]
    public void AboutPage_DisplaysGeneratedOpenSourceAcknowledgements()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml");

        Assert.Contains("x:Uid=\"About_OpenSourceTitle\"", xaml);
        Assert.Contains("ItemsSource=\"{x:Bind ViewModel.OpenSourceAcknowledgements, Mode=OneWay}\"", xaml);
        Assert.Contains("x:DataType=\"settings:OpenSourceAcknowledgementViewModel\"", xaml);
        Assert.DoesNotContain("Binding OpenSourceAcknowledgements", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutPage_SupportActionsIncludeReportInappropriateAiContent()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml");

        Assert.Contains("x:Uid=\"About_ReportInappropriateAiContent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"About.Support.ReportAiContent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.ReportInappropriateAiContentCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.CanReportInappropriateAiContent, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutPage_CommunitySectionOpensDiscordThroughViewModelCommand()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml");

        Assert.Contains("x:Uid=\"About_CommunityTitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"About_DiscordDescription\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"About.Community.JoinDiscord\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.JoinDiscordCommunityCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"About.Community.OpenGitHub\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.OpenGitHubRepositoryCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Uid=\"About_SupportProjectTitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"About.SupportProject.OpenKofi\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{x:Bind ViewModel.OpenKofiSupportCommand}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPages_DoNotUseMenuFlyoutSeparator_AsSectionDividers()
    {
        var xamlFiles = new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\McpSettingsPage.xaml"
        };

        foreach (var relativePath in xamlFiles)
        {
            var xaml = LoadXaml(relativePath);
            Assert.DoesNotContain("MenuFlyoutSeparator", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SettingsShellPage_DoesNotQueueSectionNavigationFocusAfterSectionActivation()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml.cs");

        Assert.Contains("public bool TryFocusPrimaryContentTarget()", code, StringComparison.Ordinal);
        Assert.Contains("=> TryFocusCurrentSectionNavigationItem();", code, StringComparison.Ordinal);
        Assert.DoesNotContain("QueueFocusCurrentSectionNavigationItem", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPages_KeepSectionTraversalOnNativeDirectionalNavigation()
    {
        var diagnosticsXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml");
        var appearanceXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml");
        var pageBase = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\SettingsPageBase.cs");
        var diagnosticsCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml.cs");
        var mcpCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\McpSettingsPage.xaml.cs");
        var aboutCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml.cs");

        Assert.Contains("protected Control? FirstAvailableSectionEntryTarget", pageBase, StringComparison.Ordinal);
        Assert.Contains("GetSectionFocusReturnTargets", pageBase, StringComparison.Ordinal);

        Assert.DoesNotContain("XYFocusUp=", diagnosticsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("XYFocusDown=", diagnosticsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("XYFocusUp=", appearanceXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("XYFocusDown=", appearanceXaml, StringComparison.Ordinal);

        Assert.Contains("FirstAvailableSectionEntryTarget(", diagnosticsCode, StringComparison.Ordinal);
        Assert.Contains("FirstAvailableSectionEntryTarget(", mcpCode, StringComparison.Ordinal);
        Assert.Contains("GetSectionFocusReturnTargets()", mcpCode, StringComparison.Ordinal);
        Assert.Contains("FirstAvailableSectionEntryTarget(", aboutCode, StringComparison.Ordinal);
    }
}

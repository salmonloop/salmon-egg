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

public sealed class XamlComplianceResponsiveDensityTests
{

    [Fact]
    public void ResponsiveContentHost_UsesNativeMaxWidthInsteadOfManualSizeState()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ResponsiveContentHost.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ResponsiveContentHost.xaml.cs");

        Assert.Contains("MaxWidth=\"{x:Bind MaxContentWidth, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SizeChanged=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnDefinition x:Name=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ActualWidth", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GridLength", code, StringComparison.Ordinal);
        Assert.DoesNotContain("MinGutter", code, StringComparison.Ordinal);
    }


    [Fact]
    public void ChatSkeleton_DensifiesPaddingOnShortWindowHeights()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatSkeleton.xaml");

        Assert.Contains("x:Name=\"SkeletonHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SkeletonHeightCompact\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SkeletonHeightComfortable\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RootGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RootGrid.Padding\" Value=\"12\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RootGrid.Padding\" Value=\"20\"", xaml, StringComparison.Ordinal);
        // Compact is the short-height default (matches ChatView MessagesList inset).
        Assert.Contains("Padding=\"12\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Storyboard", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPages_DensifyContentStackSpacingOnShortHeights()
    {
        var appearance = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AppearanceSettingsPage.xaml");
        var general = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\GeneralSettingsPage.xaml");
        var dataStorage = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml");
        var shortcuts = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\ShortcutsSettingsPage.xaml");

        Assert.Contains("x:Name=\"AppearanceHeightStates\"", appearance, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AppearanceContentStack\"", appearance, StringComparison.Ordinal);
        Assert.Contains("Target=\"AppearanceContentStack.Spacing\" Value=\"16\"", appearance, StringComparison.Ordinal);
        Assert.Contains("Target=\"AppearanceContentStack.Spacing\" Value=\"28\"", appearance, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", appearance, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", appearance, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"GeneralHeightStates\"", general, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"GeneralContentStack\"", general, StringComparison.Ordinal);
        Assert.Contains("Target=\"GeneralContentStack.Spacing\" Value=\"16\"", general, StringComparison.Ordinal);
        Assert.Contains("Target=\"GeneralContentStack.Spacing\" Value=\"28\"", general, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", general, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", general, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"DataStorageHeightStates\"", dataStorage, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DataStorageContentStack\"", dataStorage, StringComparison.Ordinal);
        Assert.Contains("Target=\"DataStorageContentStack.Spacing\" Value=\"16\"", dataStorage, StringComparison.Ordinal);
        Assert.Contains("Target=\"DataStorageContentStack.Spacing\" Value=\"28\"", dataStorage, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", dataStorage, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", dataStorage, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"ShortcutsHeightStates\"", shortcuts, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ShortcutsContentStack\"", shortcuts, StringComparison.Ordinal);
        Assert.Contains("Target=\"ShortcutsContentStack.Spacing\" Value=\"14\"", shortcuts, StringComparison.Ordinal);
        Assert.Contains("Target=\"ShortcutsContentStack.Spacing\" Value=\"24\"", shortcuts, StringComparison.Ordinal);
        Assert.Contains("Target=\"ShortcutsContentStack.Padding\" Value=\"0,0,0,16\"", shortcuts, StringComparison.Ordinal);
        Assert.Contains("Target=\"ShortcutsContentStack.Padding\" Value=\"0,0,0,32\"", shortcuts, StringComparison.Ordinal);
        Assert.Contains("Padding=\"{StaticResource SettingsPageVerticalPadding}\"", shortcuts, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", shortcuts, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", shortcuts, StringComparison.Ordinal);

        var diagnostics = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml");
        var acp = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml");
        var mcp = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\McpSettingsPage.xaml");
        var about = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml");

        Assert.Contains("x:Name=\"DiagnosticsHeightStates\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DiagnosticsContentStack\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Spacing=\"16\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Target=\"DiagnosticsContentStack.Spacing\" Value=\"16\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Target=\"DiagnosticsContentStack.Spacing\" Value=\"28\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Target=\"DiagnosticsContentStack.Padding\" Value=\"0,0,0,16\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Target=\"DiagnosticsContentStack.Padding\" Value=\"0,0,0,32\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Padding=\"{StaticResource SettingsPageVerticalPadding}\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", diagnostics, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"AcpHeightStates\"", acp, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AcpContentStack\"", acp, StringComparison.Ordinal);
        Assert.Contains("Spacing=\"16\"", acp, StringComparison.Ordinal);
        Assert.Contains("Target=\"AcpContentStack.Spacing\" Value=\"16\"", acp, StringComparison.Ordinal);
        Assert.Contains("Target=\"AcpContentStack.Spacing\" Value=\"28\"", acp, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", acp, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", acp, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"McpHeightStates\"", mcp, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"McpContentStack\"", mcp, StringComparison.Ordinal);
        Assert.Contains("Spacing=\"16\"", mcp, StringComparison.Ordinal);
        Assert.Contains("Target=\"McpContentStack.Spacing\" Value=\"16\"", mcp, StringComparison.Ordinal);
        Assert.Contains("Target=\"McpContentStack.Spacing\" Value=\"28\"", mcp, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", mcp, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", mcp, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"AboutHeightStates\"", about, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AboutContentStack\"", about, StringComparison.Ordinal);
        Assert.Contains("Spacing=\"14\"", about, StringComparison.Ordinal);
        Assert.Contains("Target=\"AboutContentStack.Spacing\" Value=\"14\"", about, StringComparison.Ordinal);
        Assert.Contains("Target=\"AboutContentStack.Spacing\" Value=\"24\"", about, StringComparison.Ordinal);
        Assert.Contains("Target=\"AboutContentStack.Padding\" Value=\"0,0,0,16\"", about, StringComparison.Ordinal);
        Assert.Contains("Target=\"AboutContentStack.Padding\" Value=\"0,0,0,32\"", about, StringComparison.Ordinal);
        Assert.Contains("Padding=\"{StaticResource SettingsPageVerticalPadding}\"", about, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", about, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", about, StringComparison.Ordinal);

        var appResources = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");
        Assert.Contains("<Thickness x:Key=\"SettingsPageVerticalPadding\">0,0,0,16</Thickness>", appResources, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentProfileEditor_AdaptsDensityByWindowHeightAndAvoidsDoublePadding()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml");

        Assert.Contains("x:Name=\"AgentProfileEditorHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AgentProfileEditorContent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"AgentProfileEditorContent.Spacing\" Value=\"12\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"AgentProfileEditorContent.Spacing\" Value=\"18\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"AgentProfileEditorContent.Padding\" Value=\"0,0,0,16\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"AgentProfileEditorContent.Padding\" Value=\"0,0,0,32\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Padding=\"{StaticResource SettingsPageVerticalPadding}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Padding=\"40,24\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationDialogs_AdaptMaxHeightByWindowHeight()
    {
        var sessions = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\SessionsListDialog.xaml");
        var remote = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\RemoteProjectSelectionDialog.xaml");

        Assert.Contains("x:Name=\"SessionsDialogHeightStates\"", sessions, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SessionsDialogRoot\"", sessions, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"360\"", sessions, StringComparison.Ordinal);
        Assert.Contains("Target=\"SessionsDialogRoot.MaxHeight\" Value=\"360\"", sessions, StringComparison.Ordinal);
        Assert.Contains("Target=\"SessionsDialogRoot.MaxHeight\" Value=\"560\"", sessions, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", sessions, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", sessions, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"RemoteProjectSelectionHeightStates\"", remote, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RemoteProjectSelectionRoot\"", remote, StringComparison.Ordinal);
        Assert.Contains("RemoteProjectSelectionDialogMaxHeight\">360<", remote, StringComparison.Ordinal);
        Assert.Contains("Target=\"RemoteProjectSelectionRoot.MaxHeight\" Value=\"360\"", remote, StringComparison.Ordinal);
        Assert.Contains("Target=\"RemoteProjectSelectionRoot.MaxHeight\" Value=\"560\"", remote, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", remote, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", remote, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationEditorDialog_AdaptsSpacingByWindowHeight()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\ConfigurationEditorDialog.xaml");

        Assert.Contains("x:Name=\"ConfigurationEditorHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConfigurationEditorContent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"ConfigurationEditorContent.Spacing\" Value=\"8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"ConfigurationEditorContent.Spacing\" Value=\"12\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverSessionsPage_EmptyStatesAdaptDensityByWindowHeight()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml");

        Assert.Contains("x:Name=\"EmptyStateHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EmptyHeightCompact\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"EmptyHeightComfortable\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ProfilesHeaderHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailsProfileHeader\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"DetailsProfileHeader.Margin\" Value=\"0,0,0,12\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"DetailsProfileHeader.Margin\" Value=\"0,0,0,24\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"ProfilesHeaderHost.Margin\" Value=\"16,16,16,10\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"ProfilesHeaderHost.Margin\" Value=\"24,32,24,16\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NoSelectionEmptyHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NoSelectionEmptyBadge\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NoSelectionEmptyIcon\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SessionsEmptyHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SessionsEmptyIcon\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"NoSelectionEmptyBadge.Width\" Value=\"72\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"NoSelectionEmptyBadge.Width\" Value=\"120\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"SessionsEmptyIcon.FontSize\" Value=\"32\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"SessionsEmptyIcon.FontSize\" Value=\"48\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutPage_OpenSourceAcknowledgements_AdaptsMaxHeightByWindowHeight()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AboutPage.xaml");

        Assert.Contains("x:Name=\"OpenSourceHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OpenSourceHeightCompact\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OpenSourceHeightComfortable\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OpenSourceAcknowledgementsList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"220\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"OpenSourceAcknowledgementsList.MaxHeight\" Value=\"220\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"OpenSourceAcknowledgementsList.MaxHeight\" Value=\"360\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartView_HeroSuggestions_UseStableButtonIdsInsteadOfListSelectionState()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml.cs");
        var cardXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\HeroSuggestionCard.xaml");
        var cardCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\HeroSuggestionCard.xaml.cs");
        var suggestionVm = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Start\QuickSuggestionViewModel.cs");
        var startVm = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Start\StartViewModel.cs");

        Assert.Contains("ItemsControl x:Name=\"HeroSuggestionsHost\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ListView x:Name=\"HeroSuggestionsList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<start_views:HeroSuggestionCard Suggestion=\"{x:Bind Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Loaded=\"OnHeroSuggestionCardLoaded\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"{x:Bind AutomationId, Mode=OneWay}\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"{x:Bind Title, Mode=OneWay}\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"{x:Bind Icon, Mode=OneWay}\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind Title, Mode=OneWay}\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind Subtitle, Mode=OneWay}\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("CommandParameter=\"{x:Bind Suggestion, Mode=OneWay}\"", cardXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding Title", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding Subtitle", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Glyph=\"{Binding Icon", xaml, StringComparison.Ordinal);
        Assert.Contains("QuickSuggestionViewModel? Suggestion", cardCode, StringComparison.Ordinal);
        Assert.Contains("new PropertyMetadata(null, OnSuggestionChanged)", cardCode, StringComparison.Ordinal);
        Assert.Contains("suggestion.PropertyChanged += OnSuggestionPropertyChanged;", cardCode, StringComparison.Ordinal);
        Assert.Contains("_observedSuggestion.PropertyChanged -= OnSuggestionPropertyChanged;", cardCode, StringComparison.Ordinal);
        Assert.Contains("IPrimaryContentFocusTarget", code, StringComparison.Ordinal);
        Assert.Contains("FindSuggestionButton(ViewModel.Suggestions[0].AutomationId)", code, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Suggestions.CollectionChanged += OnSuggestionsChanged;", code, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Suggestions.CollectionChanged -= OnSuggestionsChanged;", code, StringComparison.Ordinal);
        Assert.Contains("promptBox.XYFocusUp = firstSuggestion;", code, StringComparison.Ordinal);
        Assert.Contains("button.XYFocusDown = promptFocusTarget;", code, StringComparison.Ordinal);
        Assert.Contains("button.ClearValue(Control.XYFocusDownProperty);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryConsumeNavigationIntent", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryMoveFocusedSuggestion", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveFocusedSuggestionIndex", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryActivateSelectedHeroSuggestion", code, StringComparison.Ordinal);
        Assert.DoesNotContain("HeroSuggestionsList.SelectedIndex", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateSlug(", suggestionVm, StringComparison.Ordinal);
        Assert.Contains("ObservableObject", suggestionVm, StringComparison.Ordinal);
        Assert.Contains("StartView.Suggestion.AnalyzeCodebase", startVm, StringComparison.Ordinal);
        Assert.Contains("StartView.Suggestion.RecommendTasks", startVm, StringComparison.Ordinal);
        Assert.Contains("StartView.Suggestion.ResolveErrors", startVm, StringComparison.Ordinal);
    }

    [Fact]
    public void StartView_HeroHeightStates_UseAdaptiveTrigger()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("x:Name=\"HeroHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HeightCompact\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HeightComfortable\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HeroScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HeroLayer\"", xaml, StringComparison.Ordinal);
        // Compact is the short-height default; title densifies further than the original 40px
        // marketing compact so title + three cards remain usable before scrolling.
        Assert.Contains("Target=\"StartTitle.FontSize\" Value=\"28\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"StartTitle.FontSize\" Value=\"64\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"StartRoot.Padding\" Value=\"16,4,16,8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"HeroContentStack.Spacing\" Value=\"10\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"HeroLayer.VerticalAlignment\" Value=\"Top\"", xaml, StringComparison.Ordinal);
        // Never Center inside HeroScrollViewer: overflow would clip the top of the stack.
        Assert.DoesNotContain("Target=\"HeroLayer.VerticalAlignment\" Value=\"Center\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"StartSubtitle.MaxLines\" Value=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"ComposerHost.RowSpacing\" Value=\"4\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"StartDraftErrorInfoBar.Margin\" Value=\"12,0,12,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"StartDraftErrorInfoBar.Margin\" Value=\"24,0,24,0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", xaml, StringComparison.Ordinal);
    }
    [Fact]
    public void HeroSuggestionCard_AdaptsDensityByWindowHeight()
    {
        var cardXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\HeroSuggestionCard.xaml");

        Assert.Contains("x:Name=\"HeroCardHeightStates\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CardHeightCompact\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CardHeightComfortable\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", cardXaml, StringComparison.Ordinal);
        // Compact short-height default: icon beside copy, min-height 64, single-line subtitle,
        // quick-launch chrome collapsed; comfortable restores marketing density.
        Assert.Contains("Target=\"HeroSuggestionButton.MinHeight\" Value=\"64\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"HeroSuggestionButton.MinHeight\" Value=\"112\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions=\"Auto,*\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"HeroCardQuickLaunchLabel.Visibility\" Value=\"Collapsed\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"HeroCardSubtitle.MaxLines\" Value=\"1\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"HeroCardSubtitle.MaxLines\" Value=\"2\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{ThemeResource AccentBrush}\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{ThemeResource TextFillColorPrimaryBrush}\"", cardXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", cardXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void HeroSuggestionCard_AdaptsWidthByWindowWidth()
    {
        var cardXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\HeroSuggestionCard.xaml");
        var startXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("x:Name=\"HeroCardWidthStates\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CardWidthNarrow\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CardWidthWide\"", cardXaml, StringComparison.Ordinal);
        // Same breakpoint as StartView panel swap so width density and orientation stay in lockstep.
        Assert.Contains("MinWindowWidth=\"1060\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowWidth=\"1060\"", startXaml, StringComparison.Ordinal);
        // Sticky VSM: both states set MinWidth + MaxWidth so AdaptiveTrigger retreat resets.
        Assert.Contains("Target=\"HeroSuggestionButton.MinWidth\" Value=\"0\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"HeroSuggestionButton.MaxWidth\" Value=\"980\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"HeroSuggestionButton.MinWidth\" Value=\"216\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"HeroSuggestionButton.MaxWidth\" Value=\"216\"", cardXaml, StringComparison.Ordinal);
        // Style defaults match the narrow/short path; never hard-code a 216 min in markup default.
        Assert.Contains("Property=\"MinWidth\" Value=\"0\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"MaxWidth\" Value=\"980\"", cardXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"HorizontalAlignment\" Value=\"Stretch\"", cardXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", cardXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainPage_TaskOverviewEmptyHost_AdaptsMarginByWindowHeight()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("x:Name=\"TaskOverviewHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TaskOverviewHeightCompact\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TaskOverviewHeightComfortable\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TaskOverviewEmptyHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RightPanelHeader\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RightPanelContentStack\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"TaskOverviewEmptyHost.Margin\" Value=\"0,12\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"TaskOverviewEmptyHost.Margin\" Value=\"0,40\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RightPanelHeader.MinHeight\" Value=\"48\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RightPanelHeader.MinHeight\" Value=\"64\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RightPanelHeader.Padding\" Value=\"12,6,8,6\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RightPanelHeader.Padding\" Value=\"16,8,8,8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RightPanelContentStack.Padding\" Value=\"12\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RightPanelContentStack.Padding\" Value=\"16\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RightPanelContentStack.Spacing\" Value=\"12\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"RightPanelContentStack.Spacing\" Value=\"16\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"48\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", xaml, StringComparison.Ordinal);
    }

    
    [Fact]
    public void AcpAndMcpSettings_ListMinHeightDensifiesOnShortWindowHeights()
    {
        var acp = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml");
        var mcp = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\McpSettingsPage.xaml");

        Assert.Contains("x:Name=\"ListHeightStates\"", acp, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", acp, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AcpProfilesList\"", acp, StringComparison.Ordinal);
        Assert.Contains("Target=\"AcpProfilesList.MinHeight\" Value=\"140\"", acp, StringComparison.Ordinal);
        Assert.Contains("Target=\"AcpRemoteDirectoriesList.MinHeight\" Value=\"120\"", acp, StringComparison.Ordinal);
        Assert.Contains("Target=\"AcpProfilesList.MinHeight\" Value=\"88\"", acp, StringComparison.Ordinal);
        Assert.Contains("Target=\"AcpRemoteDirectoriesList.MinHeight\" Value=\"72\"", acp, StringComparison.Ordinal);
        Assert.Contains("AgentListItemStyleCompact", acp, StringComparison.Ordinal);
        Assert.Contains("AgentListItemStyleComfortable", acp, StringComparison.Ordinal);
        Assert.Contains("Property=\"MinHeight\" Value=\"48\"", acp, StringComparison.Ordinal);
        Assert.Contains("Property=\"MinHeight\" Value=\"64\"", acp, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"88\"", acp, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"72\"", acp, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"ListHeightStates\"", mcp, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", mcp, StringComparison.Ordinal);
        Assert.Contains("Target=\"McpServersList.MinHeight\" Value=\"96\"", mcp, StringComparison.Ordinal);
        Assert.Contains("Target=\"McpServersList.MinHeight\" Value=\"160\"", mcp, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"96\"", mcp, StringComparison.Ordinal);
        Assert.Contains("Padding=\"12\"", mcp, StringComparison.Ordinal);
        Assert.Contains("ColumnSpacing=\"12\"", mcp, StringComparison.Ordinal);

        // VSM must live on a single content root (Grid), not as a second Page child.
        Assert.Contains("<Grid>", acp, StringComparison.Ordinal);
        Assert.Contains("<Grid>", mcp, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsSettings_LiveLogHeightDensifiesOnShortWindowHeights()
    {
        var diagnostics = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml");

        Assert.Contains("x:Name=\"LiveLogHeightStates\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LiveLogTextBox\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Target=\"LiveLogTextBox.Height\" Value=\"160\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Target=\"LiveLogTextBox.Height\" Value=\"320\"", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Height=\"160\"", diagnostics, StringComparison.Ordinal);

        // VSM must live on a single content root (Grid), not as a second Page child.
        Assert.Contains("<Grid>", diagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_ComposerHeightsDensifyOnShortWindowHeights()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.Contains("x:Name=\"ComposerHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ComposerHeightCompact\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ComposerHeightComfortable\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SlashCommandsList\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"InputBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"140\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"SlashCommandsList.MaxHeight\" Value=\"140\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"InputBox.MaxHeight\" Value=\"140\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"SlashCommandsList.MaxHeight\" Value=\"220\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"InputBox.MaxHeight\" Value=\"240\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"ComposerLayoutRoot.Padding\" Value=\"12,0,12,12\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"ComposerLayoutRoot.Padding\" Value=\"20,0,20,20\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ComposerLayoutRoot\"", xaml, StringComparison.Ordinal);

        // Touch-target MinHeights stay fixed; densify growth caps and outer padding only.
        Assert.Contains("MinHeight=\"44\"", xaml, StringComparison.Ordinal);
        // Outer chrome padding lives on ComposerLayoutRoot so AdaptiveTrigger can densify it.
        Assert.DoesNotContain("Padding=\"20,0,20,20\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MiniChatView_ComposerHeightsDensifyOnShortWindowHeights()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.Contains("x:Name=\"ComposerHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ComposerHeightCompact\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ComposerHeightComfortable\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MiniChatInputBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"120\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"MiniChatInputBox.MaxHeight\" Value=\"120\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"MiniChatInputBox.MaxHeight\" Value=\"180\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RootGrid\"", xaml, StringComparison.Ordinal);

        // Keep the mini composer touch MinHeight; densify only the multi-line growth cap.
        Assert.Contains("MinHeight=\"36\"", xaml, StringComparison.Ordinal);
    }


    [Fact]
    public void BottomPanelHost_DensifiesTerminalInsetOnShortWindowHeights()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\BottomPanelHost.xaml");

        Assert.Contains("x:Name=\"BottomPanelHeightStates\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BottomPanelHeightCompact\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"BottomPanelHeightComfortable\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TerminalHostBorder\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowHeight=\"760\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"TerminalHostBorder.Margin\" Value=\"8,0,8,8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"TerminalHostBorder.Margin\" Value=\"12,0,12,12\"", xaml, StringComparison.Ordinal);
        // Compact short-height default; keep TabView MinHeight for touch targets.
        Assert.Contains("Margin=\"8,0,8,8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"48\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestedTheme=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ResizeGrip_KeepsPlatformCursorImplementationOutOfSharedControl()
    {
        var sharedControl = LoadText(@"SalmonEgg\SalmonEgg\Controls\ResizeGrip.cs");
        var windowsImplementation = LoadText(@"SalmonEgg\SalmonEgg\Platforms\Windows\ResizeGrip.Windows.cs");

        Assert.Contains("partial void ApplyPlatformCursor()", sharedControl, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.UI.Input", sharedControl, StringComparison.Ordinal);
        Assert.DoesNotContain("InputSystemCursor", sharedControl, StringComparison.Ordinal);
        Assert.Contains("InputSystemCursor.Create", windowsImplementation, StringComparison.Ordinal);
    }
}

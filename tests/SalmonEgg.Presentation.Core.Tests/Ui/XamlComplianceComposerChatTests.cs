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

public sealed class XamlComplianceComposerChatTests
{

    [Fact]
    public void ToolCallPill_UsesNativeExpanderBehavior()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml");

        Assert.Contains("<Expander", xaml);
        Assert.Contains("IsExpanded=\"{x:Bind IsExpanded, Mode=TwoWay}\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"ToolCallPill.RootButton\"", xaml);
        Assert.DoesNotContain("<ToggleButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleButtonBackgroundChecked", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatSkeleton_DoesNotOwnStoryboardAnimationState()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatSkeleton.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatSkeleton.xaml.cs");

        Assert.DoesNotContain("<Storyboard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DoubleAnimation", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded +=", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".Begin()", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".Stop()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void MiniChatView_RootSurfaceKeepsWindowBackdropVisible()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.Contains("x:Name=\"RootGrid\"", xaml);
        Assert.Contains("Background=\"Transparent\"", xaml);
        Assert.DoesNotContain("Background=\"{ThemeResource ApplicationPageBackgroundThemeBrush}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_IconButtonsAccessibleAndTouchSized()
    {
        var sendButton = FindElementByName(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml", "SendButton");
        var cancelButton = FindElementByName(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml", "CancelButton");
        var voiceStartButton = FindElementByName(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml", "VoiceInputStartButton");
        var voiceStopButton = FindElementByName(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml", "VoiceInputStopButton");

        Assert.True(
            HasAttributeByLocalName(sendButton, "AutomationProperties.Name") || HasXUid(sendButton, "SendButton"),
            "SendButton must expose an accessible name via AutomationProperties.Name or x:Uid localization.");
        Assert.True(
            HasAttributeByLocalName(cancelButton, "AutomationProperties.Name") || HasXUid(cancelButton, "CancelButton"),
            "CancelButton must expose an accessible name via AutomationProperties.Name or x:Uid localization.");
        Assert.True(
            HasAttributeByLocalName(voiceStartButton, "AutomationProperties.Name") || HasXUid(voiceStartButton, "VoiceInputStartButton"),
            "VoiceInputStartButton must expose an accessible name via AutomationProperties.Name or x:Uid localization.");
        Assert.True(
            HasAttributeByLocalName(voiceStopButton, "AutomationProperties.Name") || HasXUid(voiceStopButton, "VoiceInputStopButton"),
            "VoiceInputStopButton must expose an accessible name via AutomationProperties.Name or x:Uid localization.");
        Assert.Equal("44", GetAttributeByLocalName(sendButton, "MinWidth"));
        Assert.Equal("44", GetAttributeByLocalName(sendButton, "MinHeight"));
        Assert.Equal("44", GetAttributeByLocalName(cancelButton, "MinWidth"));
        Assert.Equal("44", GetAttributeByLocalName(cancelButton, "MinHeight"));
        Assert.Equal("44", GetAttributeByLocalName(voiceStartButton, "MinWidth"));
        Assert.Equal("44", GetAttributeByLocalName(voiceStartButton, "MinHeight"));
        Assert.Equal("44", GetAttributeByLocalName(voiceStopButton, "MinWidth"));
        Assert.Equal("44", GetAttributeByLocalName(voiceStopButton, "MinHeight"));
    }

    [Fact]
    public void ChatInputArea_SendButtonUsesCommandBinding()
    {
        var sendButton = FindElementByName(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml", "SendButton");
        var commandBinding = GetAttributeByLocalName(sendButton, "Command");
        var clickBinding = GetAttributeByLocalName(sendButton, "Click");

        Assert.NotEqual("OnSendClick", clickBinding);
        Assert.StartsWith("{x:Bind SubmitCommand", commandBinding, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_AvoidsFixedModeWidth()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.DoesNotContain("Width=\"140\"", xaml);
    }

    [Fact]
    public void ChatInputArea_UsesContainerWidthForResponsiveToolLayout()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.Contains("x:Name=\"ComposerLayoutRoot\"", xaml);
        Assert.Contains("x:Name=\"BottomToolsStrip\"", xaml);
        Assert.Contains("x:Name=\"ToolSelectorsPanel\"", xaml);
        Assert.Contains("x:Name=\"ActionButtonsPanel\"", xaml);
        Assert.Contains("<AdaptiveTrigger MinWindowWidth=\"640\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Target=\"ToolSelectorsPanel.Orientation\" Value=\"Vertical\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Target=\"ActionButtonsPanel.(Grid.Row)\" Value=\"1\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinActualWidthTrigger", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_TextsAreLocalized()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.DoesNotContain("PlaceholderText=\"向 Agent 发送消息", xaml);
        Assert.DoesNotContain("PlaceholderText=\"选择模式\"", xaml);
        Assert.DoesNotContain("Content=\"娶她\"", xaml);
        Assert.DoesNotContain("ToolTipService.ToolTip=\"“娶她”功能占位\"", xaml);
        Assert.DoesNotContain("AutomationProperties.Name=\"发送\"", xaml);
        Assert.DoesNotContain("AutomationProperties.Name=\"取消发送\"", xaml);
        Assert.Contains("x:Uid=\"ChatInputBox\"", xaml);
        Assert.Contains("x:Uid=\"ChatModeSelector\"", xaml);
        Assert.Contains("x:Uid=\"ChatModelSelector\"", xaml);
        Assert.Contains("x:Uid=\"VoiceInputStartButton\"", xaml);
        Assert.Contains("x:Uid=\"VoiceInputStopButton\"", xaml);
        Assert.Contains("x:Uid=\"SendButton\"", xaml);
        Assert.Contains("x:Uid=\"CancelButton\"", xaml);
    }

    [Fact]
    public void ChatInputArea_ExposesAgentAndProjectSlotsAsCapabilities()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");

        Assert.Contains("x:Name=\"AgentSelectorHost\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind SelectorSlots.Agent.IsVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"{x:Bind AgentSelectorAutomationId, Mode=OneWay}\"", xaml);
        Assert.Contains("ItemsSource=\"{x:Bind SelectorSlots.Agent.Items, Mode=OneWay}\"", xaml);
        Assert.Contains("SelectedItem=\"{x:Bind SelectorSlots.Agent.SelectedItem, Mode=OneWay}\"", xaml);
        Assert.Contains("x:Name=\"ProjectSelectorHost\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind SelectorSlots.Project.IsVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"{x:Bind ProjectSelectorAutomationId, Mode=OneWay}\"", xaml);
        Assert.Contains("ItemsSource=\"{x:Bind SelectorSlots.Project.Items, Mode=OneWay}\"", xaml);
        Assert.Contains("SelectedItem=\"{x:Bind SelectorSlots.Project.SelectedItem, Mode=OneWay}\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind SelectorSlots.Mode.IsVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml);
        Assert.Contains("x:Name=\"ModelSelectorHost\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind SelectorSlots.Model.IsVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"", xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"{x:Bind ModelSelectorAutomationId, Mode=OneWay}\"", xaml);
        Assert.Contains("ItemsSource=\"{x:Bind SelectorSlots.Model.Items, Mode=OneWay}\"", xaml);
        Assert.Contains("SelectedItem=\"{x:Bind SelectorSlots.Model.SelectedItem, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("ShowAgentSelector", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowModeSelector", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowProjectSelector", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowModelSelector", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_CodeBehind_TreatsDeferredSelectorsAsOptional()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");

        Assert.DoesNotContain("FindName(selectorName) as ComboBox", code, StringComparison.Ordinal);
        Assert.Contains("AgentSelectorHost,", code, StringComparison.Ordinal);
        Assert.Contains("ModeSelectorHost,", code, StringComparison.Ordinal);
        Assert.Contains("ProjectSelectorHost", code, StringComparison.Ordinal);
        Assert.Contains("ModelSelectorHost", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_ComposerBlockedStates_UseUnifiedViewModelProjection()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.Contains("Visibility=\"{x:Bind ViewModel.ShouldShowSlashCommandsUi, Mode=OneWay", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.IsTextInputEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.AreComposerToolsEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind IsSubmitButtonEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowStartButton, Mode=OneWay", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowStopButton, Mode=OneWay", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowProgressRing, Mode=OneWay", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.CanCancelPromptUi, Mode=OneWay}\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.ShowCancelPromptButton, Mode=OneWay", xaml);
        Assert.DoesNotContain("ViewModel.IsVoiceInputListening", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CanSubmitUi", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_BlockedStatusCopy_UsesLocalizedUids()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.DoesNotContain("x:Uid=\"ChatComposerPromptInFlightStatus\"", xaml);
        Assert.DoesNotContain("x:Uid=\"ChatComposerVoiceListeningStatus\"", xaml);
    }

    [Fact]
    public void ChatInputArea_DoesNotUseHardcodedWhiteForeground()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");

        Assert.DoesNotContain("Foreground=\"White\"", xaml);
    }

    [Fact]
    public void ChatStyles_DoNotUseHardcodedWhiteForeground()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Styles\ChatStyles.xaml");

        Assert.DoesNotContain("Foreground=\"White\"", xaml);
        Assert.Contains("TextFillColorPrimaryBrush", xaml);
    }

    [Fact]
    public void ChatView_DoesNotUseListViewItemContainerTransitions()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.DoesNotContain("ItemContainerTransitions=", xaml);
    }

    [Fact]
    public void StartView_ItemsPanelTemplate_DoesNotUseXBind()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.DoesNotContain("ChildrenTransitions=\"{x:Bind", xaml);
    }

    [Fact]
    public void StartView_ComposerPreservesDirectLayoutWithoutSyntheticFocusHost()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml.cs");

        Assert.Contains("<controls:ChatInputArea x:Name=\"ComposerShell\"", xaml);
        Assert.Contains("Grid.Row=\"1\"", xaml);
        Assert.DoesNotContain("x:Name=\"ComposerFocusHost\"", xaml);
        Assert.DoesNotContain("FocusEngaged=\"OnComposerFocusHostFocusEngaged\"", xaml);
        Assert.DoesNotContain("private void OnComposerFocusHostFocusEngaged(", code);
    }

    [Fact]
    public void StartView_DraftErrorInfoBarUsesNativeLayoutFlow()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("x:Name=\"StartDraftErrorInfoBar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Grid.Row=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsOpen=\"{x:Bind ViewModel.HasStartSessionDraftError, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Message=\"{x:Bind ViewModel.StartSessionDraftErrorMessage, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"24,0,24,112\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("VerticalAlignment=\"Bottom\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StartView_ComposerUsesSharedChatInputAreaWithoutPrivateInputControls()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("<controls:ChatInputArea x:Name=\"ComposerShell\"", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.IsInputEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("SelectorSlots=\"{x:Bind ViewModel.ComposerSelectorSlots, Mode=OneWay}\"", xaml);
        Assert.Contains("AgentSelectorAutomationId=\"StartView.AgentSelector\"", xaml);
        Assert.Contains("ModeSelectorAutomationId=\"StartView.ModeSelector\"", xaml);
        Assert.Contains("ProjectSelectorAutomationId=\"StartView.ProjectSelector\"", xaml);
        Assert.Contains("ModelSelectorAutomationId=\"StartView.ModelSelector\"", xaml);
        Assert.DoesNotContain("IsComposerExpanded", xaml);
        Assert.DoesNotContain("OnComposerInteractiveElementGotFocus", xaml);
        Assert.DoesNotContain("OnComposerSelectorDropDownOpened", xaml);
        Assert.DoesNotContain("x:Name=\"StartPromptBox\"", xaml);
        Assert.DoesNotContain("x:Name=\"StartAgentSelector\"", xaml);
        Assert.DoesNotContain("x:Name=\"StartModeSelector\"", xaml);
        Assert.DoesNotContain("x:Name=\"StartProjectSelector\"", xaml);
        Assert.DoesNotContain("AgentSelectorItemsSource=", xaml);
        Assert.DoesNotContain("ModeSelectorItemsSource=", xaml);
        Assert.DoesNotContain("ProjectSelectorItemsSource=", xaml);
        Assert.DoesNotContain("ModelSelectorItemsSource=", xaml);
    }

    [Fact]
    public void ChatView_UsesSharedInputAreaWithoutAgentSelectorCapability()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");

        Assert.Contains("<controls:ChatInputArea x:Name=\"ConversationInputArea\"", xaml);
        Assert.Contains("ViewModel=\"{x:Bind ViewModel, Mode=OneWay}\"", xaml);
        Assert.Contains("SelectorSlots=\"{x:Bind ViewModel.ComposerSelectorSlots, Mode=OneWay}\"", xaml);
        Assert.DoesNotContain("ShowAgentSelector=", xaml);
        Assert.DoesNotContain("ShowProjectSelector=", xaml);
        Assert.DoesNotContain("ModeSelectorItemsSource=", xaml);
        Assert.DoesNotContain("SelectedModeSelectorItem=", xaml);
        Assert.DoesNotContain("AgentSelectorAutomationId=", xaml);
        Assert.DoesNotContain("ProjectSelectorAutomationId=", xaml);
    }

    [Fact]
    public void SharedComposer_ModeSelectionUsesExplicitCommandInsteadOfTwoWaySelectedMode()
    {
        var chatInputXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var chatViewXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");
        var startViewXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml");

        Assert.Contains("ItemsSource=\"{x:Bind SelectorSlots.Mode.Items, Mode=OneWay}\"", chatInputXaml);
        Assert.Contains("SelectedItem=\"{x:Bind SelectorSlots.Mode.SelectedItem, Mode=OneWay}\"", chatInputXaml);
        Assert.Contains("SelectionChanged=\"OnModeSelectorSelectionChanged\"", chatInputXaml);
        Assert.Contains("ItemsSource=\"{x:Bind SelectorSlots.Model.Items, Mode=OneWay}\"", chatInputXaml);
        Assert.Contains("SelectedItem=\"{x:Bind SelectorSlots.Model.SelectedItem, Mode=OneWay}\"", chatInputXaml);
        Assert.Contains("SelectionChanged=\"OnModelSelectorSelectionChanged\"", chatInputXaml);
        Assert.DoesNotContain("SelectedItem=\"{x:Bind SelectedMode, Mode=TwoWay}\"", chatInputXaml, StringComparison.Ordinal);

        Assert.Contains("SelectorSlots=\"{x:Bind ViewModel.ComposerSelectorSlots, Mode=OneWay}\"", chatViewXaml);
        Assert.DoesNotContain("SelectedMode=\"{x:Bind ViewModel.SelectedMode, Mode=TwoWay}\"", chatViewXaml, StringComparison.Ordinal);

        Assert.Contains("SelectorSlots=\"{x:Bind ViewModel.ComposerSelectorSlots, Mode=OneWay}\"", startViewXaml);
        Assert.Contains("ModelSelectorAutomationId=\"StartView.ModelSelector\"", startViewXaml);
        Assert.DoesNotContain("SelectedMode=\"{x:Bind ViewModel.SelectedStartMode, Mode=TwoWay}\"", startViewXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatStyles_XBindTemplate_UsesCompiledResourceDictionary()
    {
        var appXaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");
        var chatStylesXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Styles\ChatStyles.xaml");

        Assert.DoesNotContain("Source=\"ms-appx:///Styles/ChatStyles.xaml\"", appXaml);
        Assert.Contains("x:Class=\"SalmonEgg.Styles.ChatStyles\"", chatStylesXaml);
    }

    [Fact]
    public void ChatView_AskUserAndLoadingOverlayTextsAreLocalized()
    {
        var chatXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");
        var shellXaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.DoesNotContain("Text=\"Agent 需要你的输入\"", chatXaml);
        Assert.DoesNotContain("Content=\"提交答案\"", chatXaml);
        Assert.DoesNotContain("AutomationProperties.Name=\"会话加载中\"", shellXaml);
        Assert.Contains("x:Uid=\"ChatViewAskUserTitle\"", chatXaml);
        Assert.Contains("x:Uid=\"ChatViewAskUserSubmitButton\"", chatXaml);
        Assert.Contains("x:Uid=\"ChatViewLoadingOverlay\"", shellXaml);
    }

    [Fact]
    public void MiniChatView_TextsAreLocalized()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.DoesNotContain("PlaceholderText=\"选择会话\"", xaml);
        Assert.DoesNotContain("PlaceholderText=\"输入消息\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatSessionSelector\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatReturnButton\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatInputBox\"", xaml);
        Assert.DoesNotContain("x:Uid=\"MiniChatComposerPromptInFlightStatus\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatComposerVoiceListeningStatus\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatCancelButton\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatSendButton\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatVoiceInputStartButton\"", xaml);
        Assert.Contains("x:Uid=\"MiniChatVoiceInputStopButton\"", xaml);
    }

    [Fact]
    public void ChatComposerVoiceAndModelControls_HaveLocalizedResources()
    {
        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        ];
        string[] requiredResources =
        [
            "ChatModelSelector.PlaceholderText",
            "VoiceInputStartButton.AutomationProperties.Name",
            "VoiceInputStartButton.ToolTipService.ToolTip",
            "VoiceInputStopButton.AutomationProperties.Name",
            "VoiceInputStopButton.ToolTipService.ToolTip",
            "MiniChatVoiceInputStartButton.ToolTipService.ToolTip",
            "MiniChatVoiceInputStartButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
            "MiniChatVoiceInputStopButton.ToolTipService.ToolTip",
            "MiniChatVoiceInputStopButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"
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
    public void MiniChatView_ComposerBlockedStates_UseSameProjectionAsMainComposer()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.Contains("Visibility=\"{x:Bind ViewModel.ShowCancelPromptButton, Mode=OneWay", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.CanCancelPromptUi, Mode=OneWay}\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowListeningStatus, Mode=OneWay", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowProgressRing, Mode=OneWay", xaml);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.IsTextInputEnabled, Mode=OneWay}\"", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowStartButton, Mode=OneWay", xaml);
        Assert.Contains("Visibility=\"{x:Bind ViewModel.VoiceInputUiState.ShowStopButton, Mode=OneWay", xaml);
    }

    [Fact]
    public void MiniChatView_KeepsSharedTitleBarMarkupUnoCompatible()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.DoesNotContain("<TitleBar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LeftHeader=", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RightHeader=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MiniChatView_UsesCompactSessionLabelWhilePreservingFullNameTooltip()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.Contains("Text=\"{x:Bind CompactDisplayName, Mode=OneTime}\"", xaml);
        Assert.Contains("ToolTipService.ToolTip=\"{x:Bind DisplayName, Mode=OneTime}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{x:Bind ViewModel.CurrentSessionDisplayName, Mode=OneWay}\"", xaml);
    }

    [Fact]
    public void ChatInputArea_SelectorItems_ExposeStableAutomationIds()
    {
        var chatInputXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var selectorItemVm = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\Selectors\ComposerSelectorItemViewModel.cs");

        Assert.Contains(
            "AutomationProperties.AutomationId=\"{x:Bind AutomationId, Mode=OneWay}\"",
            chatInputXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "public string AutomationId",
            selectorItemVm,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"ComposerSelectorItem.{Kind}.{ResolveAutomationSegment()}\"",
            selectorItemVm,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_SelectorItems_KeepNativeComboBoxItemsHitTestable()
    {
        var chatInputXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var chatInputCodeBehind = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");

        // 原生 hit-test 契约：不靠 IsEnabled 禁用 ComboBoxItem（会吞指针），用 Opacity 表达不可选；
        // 命令执行路径必须门控 IsSelectable；选择事实来自原生 AddedItems/SelectedIndex。
        Assert.DoesNotContain("ItemContainerStyle=\"{StaticResource ComposerSelectorComboBoxItemStyle}\"", chatInputXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Setter Property=\"IsEnabled\" Value=\"{Binding IsSelectable}\"", chatInputXaml, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"{x:Bind IsSelectable, Mode=OneWay, Converter={StaticResource BoolToOpacityConverter}}\"", chatInputXaml, StringComparison.Ordinal);
        // 门控形态可以是独立 early-return（当前）或历史复合条件；关键是执行前拒绝不可选项。
        Assert.Contains("!item.IsSelectable", chatInputCodeBehind, StringComparison.Ordinal);
        Assert.Contains("e.AddedItems", chatInputCodeBehind, StringComparison.Ordinal);
        Assert.Contains("comboBox.SelectedIndex", chatInputCodeBehind, StringComparison.Ordinal);
        Assert.Contains("DataContext: ComposerSelectorItemViewModel", chatInputCodeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("FindName(selectorName)", chatInputCodeBehind, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(_openSelectorHost, comboBox)", chatInputCodeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatView_UsesDeferredTranscriptLoadingWithoutWholePageLifecycleHack()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");
        var codeBehind = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml.cs");

        Assert.Contains("x:Name=\"ActiveConversationRoot\"", xaml);
        Assert.Contains("x:Load=\"{x:Bind ViewModel.ShouldLoadActiveConversationRoot, Mode=OneWay}\"", xaml);
        Assert.Contains("x:Load=\"{x:Bind ViewModel.ShouldLoadTranscriptSurface, Mode=OneWay}\"", xaml);
        Assert.Contains("Unloaded=\"OnMessagesListUnloaded\"", xaml);
        Assert.Contains("private void OnMessagesListUnloaded", codeBehind);
        Assert.DoesNotContain("PointerPressed=\"OnMessagesListPointerPressed\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerWheelChanged=\"OnMessagesListPointerWheelChanged\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyDown=\"OnMessagesListKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FindScrollViewer(", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper.", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void MiniChatView_UsesNativeTranscriptInteractionWithoutWholePageLifecycleHack()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");
        var codeBehind = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs");

        Assert.Contains("Unloaded=\"OnMessagesListUnloaded\"", xaml);
        Assert.Contains("private void OnMessagesListUnloaded", codeBehind);
        Assert.DoesNotContain("PointerPressed=\"OnMessagesListPointerPressed\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PointerWheelChanged=\"OnMessagesListPointerWheelChanged\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("KeyDown=\"OnMessagesListKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FindScrollViewer(", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTreeHelper.", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void TranscriptViewportHost_UsesNativeListViewBaseAsSingleViewportBoundary()
    {
        var host = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Transcript\ListViewTranscriptViewportHost.cs");

        Assert.Contains("ListViewBase", host, StringComparison.Ordinal);
        Assert.Contains("ListViewItem", host, StringComparison.Ordinal);
        Assert.Contains("Func<TranscriptVirtualizationRange?>", host, StringComparison.Ordinal);
        Assert.Contains("ClampRange(visibleRange, itemCount)", host, StringComparison.Ordinal);
        Assert.Contains("TryGetFirstVisibleIndexInRange(range, out index)", host, StringComparison.Ordinal);
        Assert.Contains("_listView.ScrollIntoView", host, StringComparison.Ordinal);
        Assert.Contains("_listView.ContainerFromIndex(index)", host, StringComparison.Ordinal);
        Assert.Contains("TransformToVisual(_listView).TransformPoint(default)", host, StringComparison.Ordinal);
        Assert.Contains("public void ScrollToEnd()", host, StringComparison.Ordinal);
        Assert.Contains("scrollViewer.VerticalOffset >= Math.Max(0, scrollViewer.ScrollableHeight - threshold)", host, StringComparison.Ordinal);
        Assert.DoesNotContain("IsLastItemVisiblyAtBottom", host, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsRepeater", host, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOrCreateElement", host, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateLayout()", host, StringComparison.Ordinal);
        Assert.DoesNotContain("BringIntoViewOptions", host, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewerViewportMonitor", host, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedChatUi_UsesPlatformShellServiceForClipboardAndUriLaunch()
    {
        var chatStylesXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Styles\ChatStyles.xaml");
        var chatStylesCode = LoadText(@"SalmonEgg\SalmonEgg\Styles\ChatStyles.xaml.cs");
        var markdownCode = LoadText(@"SalmonEgg\SalmonEgg\Controls\MarkdownTextPresenter.cs");
        var chatMessageViewModel = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatMessageViewModel.cs");
        var chatViewModel = LoadText(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatViewModel.cs");

        Assert.Contains("Command=\"{x:Bind CopyTextCommand}\"", chatStylesXaml, StringComparison.Ordinal);
        Assert.Contains("IAsyncRelayCommand<string?> CopyTextCommand", chatMessageViewModel, StringComparison.Ordinal);
        Assert.Contains("IAsyncRelayCommand<string?> OpenMarkdownLinkCommand", chatMessageViewModel, StringComparison.Ordinal);
        Assert.Contains("ConfigureShellActions", chatViewModel, StringComparison.Ordinal);
        Assert.Contains("CopyToClipboardAsync", chatViewModel, StringComparison.Ordinal);
        Assert.Contains("OpenUriAsync", chatViewModel, StringComparison.Ordinal);
        Assert.Contains("LinkCommand=\"{x:Bind OpenMarkdownLinkCommand}\"", chatStylesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"{x:Bind HasTextContent", chatStylesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService", chatStylesCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows.ApplicationModel.DataTransfer", chatStylesCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Clipboard.SetContent", chatStylesCode, StringComparison.Ordinal);
        Assert.DoesNotContain("DataPackage", chatStylesCode, StringComparison.Ordinal);

        Assert.Contains("LinkCommandProperty", markdownCode, StringComparison.Ordinal);
        Assert.DoesNotContain("IPlatformShellService", markdownCode, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService", markdownCode, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenUriAsync", markdownCode, StringComparison.Ordinal);
        Assert.DoesNotContain("using Windows.System;", markdownCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Launcher.LaunchUriAsync", markdownCode, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedViews_DoNotUseUnsupportedUiElementTransitions()
    {
        string[] xamlFiles =
        [
            @"SalmonEgg\SalmonEgg\MainPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\SettingsShellPage.xaml"
        ];

        foreach (var xamlFile in xamlFiles)
        {
            var xaml = LoadXaml(xamlFile);

            Assert.DoesNotContain(" Transitions=\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("\n          Transitions=\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MiniChatView_UsesFocusedShortcutConsumerForVoiceInput()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml.cs");
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.Contains("IGamepadShortcutConsumer", code, StringComparison.Ordinal);
        Assert.Contains("TryConsumeShortcutIntent", code, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"MiniChatInputBox\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatInputArea_ComposerDirectionalNavigation_UsesNativeBoundaryAnchorsForActions()
    {
        var code = LoadText(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml.cs");

        Assert.Contains("trailingSelector.XYFocusRight = leadingActionButton;", code, StringComparison.Ordinal);
        Assert.Contains("leadingActionButton.XYFocusLeft = trailingSelector;", code, StringComparison.Ordinal);
        Assert.Contains("RegisterPropertyChangedCallback(UIElement.VisibilityProperty", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterPropertyChangedCallback(Control.IsEnabledProperty", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusManager.TryMoveFocus", code, StringComparison.Ordinal);
    }

    [Fact]
    public void StartView_SecondaryTextUsesThemeBrushWithoutStackedOpacity()
    {
        var document = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml"));
        var startSubtitle = FindElementByUid(document, "Start_Subtitle");

        Assert.Equal("{ThemeResource TextFillColorSecondaryBrush}", startSubtitle.Attribute("Foreground")?.Value);
        Assert.Null(startSubtitle.Attribute("Opacity"));
    }

    [Fact]
    public void ChatSurfaces_SecondaryTextUsesThemeBrushWithoutStackedOpacity()
    {
        var chatXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml");
        var miniChatXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");
        var mainPageXaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");

        Assert.Contains("Text=\"{x:Bind ViewModel.TurnStatusText, Mode=OneWay}\"", chatXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\"", chatXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0.7\"", chatXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0.72\"", chatXaml, StringComparison.Ordinal);

        Assert.Contains("Text=\"{x:Bind ViewModel.TurnStatusText, Mode=OneWay}\"", miniChatXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0.7\"", miniChatXaml, StringComparison.Ordinal);

        Assert.DoesNotContain("Opacity=\"0.55\"", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind SecondaryText}\"", mainPageXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Uid=\"TaskOverviewEmptySubtitle\"", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\"", mainPageXaml, StringComparison.Ordinal);

        var chatInputXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        Assert.Contains("Text=\"{x:Bind Description, Mode=OneWay}\"", chatInputXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind ViewModel.SelectedSlashCommand.InputHint, Mode=OneWay}\"", chatInputXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0.7\"", chatInputXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0.6\"", chatInputXaml, StringComparison.Ordinal);

        var sessionsDialogXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\SessionsListDialog.xaml");
        Assert.Contains("Text=\"{x:Bind RelativeTimeText, Mode=OneWay}\"", sessionsDialogXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\"", sessionsDialogXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=\"0.6\"", sessionsDialogXaml, StringComparison.Ordinal);
    }
}

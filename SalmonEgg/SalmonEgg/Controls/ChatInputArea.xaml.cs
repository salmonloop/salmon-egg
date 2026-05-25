using System;
using System.Linq;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Domain.Models;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
using SalmonEgg.Presentation.ViewModels.Chat;
using XamlFocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace SalmonEgg.Controls;

public sealed partial class ChatInputArea : UserControl, INavigationIntentConsumer
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(ChatViewModel),
            typeof(ChatInputArea),
            new PropertyMetadata(null, OnViewModelChanged));

    public static readonly DependencyProperty SubmitCommandProperty =
        DependencyProperty.Register(
            nameof(SubmitCommand),
            typeof(ICommand),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsSubmitButtonEnabledProperty =
        DependencyProperty.Register(
            nameof(IsSubmitButtonEnabled),
            typeof(bool),
            typeof(ChatInputArea),
            new PropertyMetadata(false));

    public static readonly DependencyProperty InputBoxAutomationIdProperty =
        DependencyProperty.Register(
            nameof(InputBoxAutomationId),
            typeof(string),
            typeof(ChatInputArea),
            new PropertyMetadata("InputBox"));

    public static readonly DependencyProperty ShowAgentSelectorProperty =
        DependencyProperty.Register(
            nameof(ShowAgentSelector),
            typeof(bool),
            typeof(ChatInputArea),
            new PropertyMetadata(false));

    public static readonly DependencyProperty AgentItemsSourceProperty =
        DependencyProperty.Register(
            nameof(AgentItemsSource),
            typeof(object),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedAgentProperty =
        DependencyProperty.Register(
            nameof(SelectedAgent),
            typeof(ServerConfiguration),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty AgentSelectorItemsSourceProperty =
        DependencyProperty.Register(
            nameof(AgentSelectorItemsSource),
            typeof(object),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedAgentSelectorItemProperty =
        DependencyProperty.Register(
            nameof(SelectedAgentSelectorItem),
            typeof(ComposerSelectorItemViewModel),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty AgentSelectionCommandProperty =
        DependencyProperty.Register(
            nameof(AgentSelectionCommand),
            typeof(ICommand),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ShowModeSelectorProperty =
        DependencyProperty.Register(
            nameof(ShowModeSelector),
            typeof(bool),
            typeof(ChatInputArea),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ModeItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ModeItemsSource),
            typeof(object),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedModeProperty =
        DependencyProperty.Register(
            nameof(SelectedMode),
            typeof(SessionModeViewModel),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ModeSelectorItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ModeSelectorItemsSource),
            typeof(object),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedModeSelectorItemProperty =
        DependencyProperty.Register(
            nameof(SelectedModeSelectorItem),
            typeof(ComposerSelectorItemViewModel),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ModeSelectionCommandProperty =
        DependencyProperty.Register(
            nameof(ModeSelectionCommand),
            typeof(ICommand),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsModeSelectorEnabledProperty =
        DependencyProperty.Register(
            nameof(IsModeSelectorEnabled),
            typeof(bool),
            typeof(ChatInputArea),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ShowProjectSelectorProperty =
        DependencyProperty.Register(
            nameof(ShowProjectSelector),
            typeof(bool),
            typeof(ChatInputArea),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ProjectItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ProjectItemsSource),
            typeof(object),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedProjectIdProperty =
        DependencyProperty.Register(
            nameof(SelectedProjectId),
            typeof(string),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ProjectSelectorItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ProjectSelectorItemsSource),
            typeof(object),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedProjectSelectorItemProperty =
        DependencyProperty.Register(
            nameof(SelectedProjectSelectorItem),
            typeof(ComposerSelectorItemViewModel),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ProjectSelectionCommandProperty =
        DependencyProperty.Register(
            nameof(ProjectSelectionCommand),
            typeof(ICommand),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    public static readonly DependencyProperty AgentSelectorAutomationIdProperty =
        DependencyProperty.Register(
            nameof(AgentSelectorAutomationId),
            typeof(string),
            typeof(ChatInputArea),
            new PropertyMetadata("ChatInputArea.AgentSelector"));

    public static readonly DependencyProperty ModeSelectorAutomationIdProperty =
        DependencyProperty.Register(
            nameof(ModeSelectorAutomationId),
            typeof(string),
            typeof(ChatInputArea),
            new PropertyMetadata("ChatInputArea.ModeSelector"));

    public static readonly DependencyProperty ProjectSelectorAutomationIdProperty =
        DependencyProperty.Register(
            nameof(ProjectSelectorAutomationId),
            typeof(string),
            typeof(ChatInputArea),
            new PropertyMetadata("ChatInputArea.ProjectSelector"));

    public static readonly DependencyProperty MoveUpEscapeHandlerProperty =
        DependencyProperty.Register(
            nameof(MoveUpEscapeHandler),
            typeof(Func<bool>),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    private bool _isImeComposing;
    public event EventHandler? SelectorDropDownOpened;

    public event EventHandler? SelectorDropDownClosed;

    public ChatViewModel ViewModel
    {
        get => (ChatViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public ICommand? SubmitCommand
    {
        get => (ICommand?)GetValue(SubmitCommandProperty);
        set => SetValue(SubmitCommandProperty, value);
    }

    public bool IsSubmitButtonEnabled
    {
        get => (bool)GetValue(IsSubmitButtonEnabledProperty);
        set => SetValue(IsSubmitButtonEnabledProperty, value);
    }

    public string InputBoxAutomationId
    {
        get => (string)GetValue(InputBoxAutomationIdProperty);
        set => SetValue(InputBoxAutomationIdProperty, value);
    }

    public bool ShowAgentSelector
    {
        get => (bool)GetValue(ShowAgentSelectorProperty);
        set => SetValue(ShowAgentSelectorProperty, value);
    }

    public object? AgentItemsSource
    {
        get => GetValue(AgentItemsSourceProperty);
        set => SetValue(AgentItemsSourceProperty, value);
    }

    public ServerConfiguration? SelectedAgent
    {
        get => (ServerConfiguration?)GetValue(SelectedAgentProperty);
        set => SetValue(SelectedAgentProperty, value);
    }

    public object? AgentSelectorItemsSource
    {
        get => GetValue(AgentSelectorItemsSourceProperty);
        set => SetValue(AgentSelectorItemsSourceProperty, value);
    }

    public ComposerSelectorItemViewModel? SelectedAgentSelectorItem
    {
        get => (ComposerSelectorItemViewModel?)GetValue(SelectedAgentSelectorItemProperty);
        set => SetValue(SelectedAgentSelectorItemProperty, value);
    }

    public ICommand? AgentSelectionCommand
    {
        get => (ICommand?)GetValue(AgentSelectionCommandProperty);
        set => SetValue(AgentSelectionCommandProperty, value);
    }

    public bool ShowModeSelector
    {
        get => (bool)GetValue(ShowModeSelectorProperty);
        set => SetValue(ShowModeSelectorProperty, value);
    }

    public object? ModeItemsSource
    {
        get => GetValue(ModeItemsSourceProperty);
        set => SetValue(ModeItemsSourceProperty, value);
    }

    public SessionModeViewModel? SelectedMode
    {
        get => (SessionModeViewModel?)GetValue(SelectedModeProperty);
        set => SetValue(SelectedModeProperty, value);
    }

    public object? ModeSelectorItemsSource
    {
        get => GetValue(ModeSelectorItemsSourceProperty);
        set => SetValue(ModeSelectorItemsSourceProperty, value);
    }

    public ComposerSelectorItemViewModel? SelectedModeSelectorItem
    {
        get => (ComposerSelectorItemViewModel?)GetValue(SelectedModeSelectorItemProperty);
        set => SetValue(SelectedModeSelectorItemProperty, value);
    }

    public ICommand? ModeSelectionCommand
    {
        get => (ICommand?)GetValue(ModeSelectionCommandProperty);
        set => SetValue(ModeSelectionCommandProperty, value);
    }

    public bool IsModeSelectorEnabled
    {
        get => (bool)GetValue(IsModeSelectorEnabledProperty);
        set => SetValue(IsModeSelectorEnabledProperty, value);
    }

    public bool ShowProjectSelector
    {
        get => (bool)GetValue(ShowProjectSelectorProperty);
        set => SetValue(ShowProjectSelectorProperty, value);
    }

    public object? ProjectItemsSource
    {
        get => GetValue(ProjectItemsSourceProperty);
        set => SetValue(ProjectItemsSourceProperty, value);
    }

    public string? SelectedProjectId
    {
        get => (string?)GetValue(SelectedProjectIdProperty);
        set => SetValue(SelectedProjectIdProperty, value);
    }

    public object? ProjectSelectorItemsSource
    {
        get => GetValue(ProjectSelectorItemsSourceProperty);
        set => SetValue(ProjectSelectorItemsSourceProperty, value);
    }

    public ComposerSelectorItemViewModel? SelectedProjectSelectorItem
    {
        get => (ComposerSelectorItemViewModel?)GetValue(SelectedProjectSelectorItemProperty);
        set => SetValue(SelectedProjectSelectorItemProperty, value);
    }

    public ICommand? ProjectSelectionCommand
    {
        get => (ICommand?)GetValue(ProjectSelectionCommandProperty);
        set => SetValue(ProjectSelectionCommandProperty, value);
    }

    public string AgentSelectorAutomationId
    {
        get => (string)GetValue(AgentSelectorAutomationIdProperty);
        set => SetValue(AgentSelectorAutomationIdProperty, value);
    }

    public string ModeSelectorAutomationId
    {
        get => (string)GetValue(ModeSelectorAutomationIdProperty);
        set => SetValue(ModeSelectorAutomationIdProperty, value);
    }

    public string ProjectSelectorAutomationId
    {
        get => (string)GetValue(ProjectSelectorAutomationIdProperty);
        set => SetValue(ProjectSelectorAutomationIdProperty, value);
    }

    public Func<bool>? MoveUpEscapeHandler
    {
        get => (Func<bool>?)GetValue(MoveUpEscapeHandlerProperty);
        set => SetValue(MoveUpEscapeHandlerProperty, value);
    }

    public ChatInputArea()
    {
        InitializeComponent();
#if WINDOWS
        InputBox.TextCompositionStarted += OnInputTextCompositionStarted;
        InputBox.TextCompositionEnded += OnInputTextCompositionEnded;
#endif
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChatInputArea control)
        {
            return;
        }

        if (control.ViewModel != null && control.SubmitCommand == null)
        {
            control.SubmitCommand = control.ViewModel.SendPromptCommand;
        }
    }

    private bool IsPromptEditingAvailable()
        => ViewModel?.IsTextInputEnabled == true;

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isImeComposing || ViewModel == null || !IsPromptEditingAvailable())
        {
            return;
        }

        if (!ViewModel.ShowSlashCommands)
        {
            return;
        }

        switch (e.Key)
        {
            case Windows.System.VirtualKey.Tab:
                if (TryAcceptSelectedSlashCommandAndMoveCaretToEnd())
                {
                    e.Handled = true;
                    return;
                }
                break;
            case Windows.System.VirtualKey.Up:
                if (ViewModel.TryMoveSlashSelection(-1))
                {
                    e.Handled = true;
                    return;
                }
                break;
            case Windows.System.VirtualKey.Down:
                if (ViewModel.TryMoveSlashSelection(1))
                {
                    e.Handled = true;
                    return;
                }
                break;
            case Windows.System.VirtualKey.Enter:
                if (TryAcceptSelectedSlashCommandAndMoveCaretToEnd())
                {
                    e.Handled = true;
                    return;
                }
                break;
        }
    }

    private void OnSendAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_isImeComposing || ViewModel == null || !IsPromptEditingAvailable())
        {
            return;
        }

        // Enter sends; if slash commands menu is open, accept selection instead.
        if (ViewModel.ShowSlashCommands)
        {
            if (TryAcceptSelectedSlashCommandAndMoveCaretToEnd())
            {
                args.Handled = true;
                return;
            }
        }

        TrySendPrompt();
        args.Handled = true;
    }

    private void OnNewLineAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_isImeComposing || !IsPromptEditingAvailable())
        {
            return;
        }

        // Ctrl+Enter inserts a newline at the caret (even if IME/target platform behaves differently).
        var text = InputBox.Text ?? string.Empty;
        var start = InputBox.SelectionStart;
        var length = InputBox.SelectionLength;

        var newline = Environment.NewLine;
        if (length > 0)
        {
            text = text.Remove(start, length);
        }
        text = text.Insert(start, newline);

        InputBox.Text = text;
        InputBox.SelectionStart = start + newline.Length;
        InputBox.SelectionLength = 0;

        args.Handled = true;
    }

    private void OnInputTextCompositionStarted(object sender, TextCompositionStartedEventArgs e)
    {
        _isImeComposing = true;
    }

    private void OnInputTextCompositionEnded(object sender, TextCompositionEndedEventArgs e)
    {
        _isImeComposing = false;
    }

    private void TrySendPrompt()
    {
        var command = SubmitCommand ?? ViewModel.SendPromptCommand;
        if (command != null && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    public bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        if (ViewModel == null || XamlRoot == null)
        {
            return false;
        }

        var focusedElement = XamlFocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var focusContext = ResolveFocusContext(focusedElement);
        var action = ChatInputNavigationPolicy.Decide(
            intent,
            focusContext,
            ViewModel.ShowSlashCommands,
            IsPromptEditingAvailable(),
            _isImeComposing);

        var consumed = action switch
        {
            ChatInputNavigationAction.MoveSlashUp => ViewModel.TryMoveSlashSelection(-1),
            ChatInputNavigationAction.MoveSlashDown => ViewModel.TryMoveSlashSelection(1),
            ChatInputNavigationAction.AcceptSlashCommand => TryAcceptSelectedSlashCommandAndMoveCaretToEnd(),
            ChatInputNavigationAction.EscapeMoveUp => MoveUpEscapeHandler?.Invoke() == true,
            _ => false
        };

#if DEBUG
        App.BootLog($"ChatInputGamepad intent={intent} focus={focusContext} action={action} consumed={consumed}");
#endif

        return consumed;
    }

    private bool TryAcceptSelectedSlashCommandAndMoveCaretToEnd()
    {
        if (ViewModel == null || !IsPromptEditingAvailable())
        {
            return false;
        }

        if (!ViewModel.TryAcceptSelectedSlashCommand())
        {
            return false;
        }

        InputBox.Focus(FocusState.Programmatic);
        InputBox.SelectionStart = InputBox.Text?.Length ?? 0;
        InputBox.SelectionLength = 0;
        return true;
    }

    public bool TryFocusInputBox()
    {
        if (!IsPromptEditingAvailable())
        {
            return false;
        }

        InputBox.Focus(FocusState.Programmatic);
        InputBox.SelectionStart = InputBox.Text?.Length ?? 0;
        InputBox.SelectionLength = 0;
        return true;
    }

    private void OnSlashCommandsListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SlashCommandsList.SelectedItem == null)
        {
            return;
        }

        SlashCommandsList.ScrollIntoView(SlashCommandsList.SelectedItem);
    }

    private void OnModeSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ExecuteSelectorCommand(sender, ModeSelectionCommand);
    }

    private void OnAgentSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ExecuteSelectorCommand(sender, AgentSelectionCommand);
    }

    private void OnProjectSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ExecuteSelectorCommand(sender, ProjectSelectionCommand);
    }

    private static void ExecuteSelectorCommand(object sender, ICommand? command)
    {
        if (sender is not ComboBox comboBox
            || command is null
            || comboBox.SelectedItem is not ComposerSelectorItemViewModel item
            || item.IsPlaceholder
            || !item.IsSelectable
            || string.IsNullOrWhiteSpace(item.SemanticValue))
        {
            return;
        }

        if (command.CanExecute(item))
        {
            command.Execute(item);
        }
    }

    private void OnSelectorDropDownOpened(object sender, object e)
        => SelectorDropDownOpened?.Invoke(this, EventArgs.Empty);

    private void OnSelectorDropDownClosed(object sender, object e)
        => SelectorDropDownClosed?.Invoke(this, EventArgs.Empty);

    private ChatInputFocusContext ResolveFocusContext(DependencyObject? focusedElement)
    {
        if (!IsInControlSubtree(focusedElement))
        {
            return ChatInputFocusContext.Other;
        }

        var focusedComboBox = FindAncestorOrSelf<ComboBox>(focusedElement);
        if (focusedComboBox != null)
        {
            return ChatInputFocusContext.ModeSelector;
        }

        if (ReferenceEquals(FindAncestorOrSelf<Button>(focusedElement), SendButton))
        {
            return ChatInputFocusContext.SendButton;
        }

        if (ReferenceEquals(FindAncestorOrSelf<Button>(focusedElement), CancelButton))
        {
            return ChatInputFocusContext.CancelButton;
        }

        if (ReferenceEquals(FindAncestorOrSelf<TextBox>(focusedElement), InputBox))
        {
            return ChatInputFocusContext.InputBox;
        }

        return ChatInputFocusContext.Other;
    }

    private bool IsInControlSubtree(DependencyObject? element)
    {
        return ReferenceEquals(FindAncestorOrSelf<ChatInputArea>(element), this);
    }

    private static T? FindAncestorOrSelf<T>(DependencyObject? element)
        where T : DependencyObject
    {
        var current = element;
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return default;
    }
}

using System;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Services.Input;
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
            new PropertyMetadata(null, OnSubmitCommandChanged));

    public static readonly DependencyProperty CanSubmitUiProperty =
        DependencyProperty.Register(
            nameof(CanSubmitUi),
            typeof(bool),
            typeof(ChatInputArea),
            new PropertyMetadata(false));

    private INotifyPropertyChanged? _attachedViewModel;
    private ICommand? _attachedSubmitCommand;
    private bool _isImeComposing;

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

    public bool CanSubmitUi
    {
        get => (bool)GetValue(CanSubmitUiProperty);
        private set => SetValue(CanSubmitUiProperty, value);
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

        if (e.OldValue is ChatViewModel oldVm)
        {
            control.DetachViewModel(oldVm);
        }

        if (control.ViewModel != null && control.SubmitCommand == null)
        {
            control.SubmitCommand = control.ViewModel.SendPromptCommand;
        }

        if (control.ViewModel != null)
        {
            control.AttachViewModel(control.ViewModel);
        }

        control.UpdateCanSubmitUi();
    }

    private static void OnSubmitCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChatInputArea control)
        {
            return;
        }

        if (e.OldValue is ICommand oldCmd)
        {
            control.DetachSubmitCommand(oldCmd);
        }

        if (e.NewValue is ICommand newCmd)
        {
            control.AttachSubmitCommand(newCmd);
        }

        control.UpdateCanSubmitUi();
    }

    private void AttachViewModel(ChatViewModel vm)
    {
        if (_attachedViewModel != null)
        {
            return;
        }

        _attachedViewModel = vm;
        vm.PropertyChanged += OnAttachedViewModelPropertyChanged;
    }

    private void DetachViewModel(ChatViewModel vm)
    {
        if (_attachedViewModel == null || !ReferenceEquals(_attachedViewModel, vm))
        {
            return;
        }

        vm.PropertyChanged -= OnAttachedViewModelPropertyChanged;
        _attachedViewModel = null;
    }

    private void OnAttachedViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatViewModel.CurrentPrompt):
            case nameof(ChatViewModel.IsPromptInFlight):
            case nameof(ChatViewModel.TurnPhase):
            case nameof(ChatViewModel.IsSessionActive):
            case nameof(ChatViewModel.IsConnected):
            case nameof(ChatViewModel.CanSendPromptUi):
                UpdateCanSubmitUi();
                break;
        }
    }

    private void AttachSubmitCommand(ICommand? command)
    {
        if (command == null || _attachedSubmitCommand != null)
        {
            return;
        }

        _attachedSubmitCommand = command;
        command.CanExecuteChanged += OnSubmitCommandCanExecuteChanged;
    }

    private void DetachSubmitCommand(ICommand command)
    {
        if (_attachedSubmitCommand == null || !ReferenceEquals(_attachedSubmitCommand, command))
        {
            return;
        }

        command.CanExecuteChanged -= OnSubmitCommandCanExecuteChanged;
        _attachedSubmitCommand = null;
    }

    private void OnSubmitCommandCanExecuteChanged(object? sender, EventArgs e)
    {
        UpdateCanSubmitUi();
    }

    private void UpdateCanSubmitUi()
    {
        if (ViewModel == null)
        {
            CanSubmitUi = false;
            return;
        }

        var command = SubmitCommand ?? ViewModel.SendPromptCommand;
        var hasText = !string.IsNullOrWhiteSpace(ViewModel.CurrentPrompt);

        if (command == null)
        {
            CanSubmitUi = false;
            return;
        }

        // If using the native chat send command, keep the existing stable UI enablement logic.
        if (ReferenceEquals(command, ViewModel.SendPromptCommand))
        {
            CanSubmitUi = ViewModel.CanSendPromptUi;
            return;
        }

        // Start page: allow submission without requiring ACP to already be connected.
        var canExecute = false;
        try { canExecute = command.CanExecute(null); } catch { }

        CanSubmitUi = hasText && canExecute && ViewModel.IsInputEnabled;
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isImeComposing)
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
        }
    }

    private void OnSendAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_isImeComposing)
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
        if (_isImeComposing)
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
            InputBox.IsEnabled && ViewModel.IsInputEnabled,
            _isImeComposing);

        return action switch
        {
            ChatInputNavigationAction.MoveSlashUp => ViewModel.TryMoveSlashSelection(-1),
            ChatInputNavigationAction.MoveSlashDown => ViewModel.TryMoveSlashSelection(1),
            ChatInputNavigationAction.AcceptSlashCommand => TryAcceptSelectedSlashCommandAndMoveCaretToEnd(),
            _ => false
        };
    }

    private bool TryAcceptSelectedSlashCommandAndMoveCaretToEnd()
    {
        if (!ViewModel.TryAcceptSelectedSlashCommand())
        {
            return false;
        }

        InputBox.SelectionStart = InputBox.Text?.Length ?? 0;
        InputBox.SelectionLength = 0;
        return true;
    }

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

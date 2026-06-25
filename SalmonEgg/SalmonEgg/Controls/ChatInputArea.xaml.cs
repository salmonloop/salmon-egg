using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.ViewModels.Composer;
using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
using SalmonEgg.Presentation.ViewModels.Chat;
using XamlFocusManager = Microsoft.UI.Xaml.Input.FocusManager;
using WindowActivatedEventArgs = Microsoft.UI.Xaml.WindowActivatedEventArgs;

namespace SalmonEgg.Controls;

public sealed partial class ChatInputArea : UserControl, INavigationIntentConsumer, IGamepadShortcutConsumer
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

    public static readonly DependencyProperty SelectorSlotsProperty =
        DependencyProperty.Register(
            nameof(SelectorSlots),
            typeof(ComposerSelectorSlotsPresentation),
            typeof(ChatInputArea),
            new PropertyMetadata(
                ComposerSelectorSlotsPresentation.Empty,
                OnComposerFocusTopologyPropertyChanged));

    public static readonly DependencyProperty MoveUpEscapeHandlerProperty =
        DependencyProperty.Register(
            nameof(MoveUpEscapeHandler),
            typeof(Func<bool>),
            typeof(ChatInputArea),
            new PropertyMetadata(null));

    private bool _isImeComposing;
    private ComboBox? _openSelectorHost;
    private readonly List<(DependencyObject Target, DependencyProperty Property, long Token)> _focusRefreshCallbackTokens = [];
    private bool _focusRefreshCallbacksRegistered;
    private Button? _pendingActionBoundaryContinuationSource;
    private readonly Microsoft.UI.Xaml.Input.KeyEventHandler _inputBoxHandledKeyDownHandler;
    private bool _windowActivationHandlerAttached;
    private const int PendingActionActivationRestoreAttempts = 6;
    private DispatcherQueueTimer? _pendingActionActivationRestoreTimer;
    private Button? _pendingActionActivationRestoreButton;
    private int _pendingActionActivationRestoreRemainingAttempts;
    private ChatViewModel? _observedViewModel;

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

    public ComposerSelectorSlotsPresentation SelectorSlots
    {
        get => (ComposerSelectorSlotsPresentation)GetValue(SelectorSlotsProperty);
        set => SetValue(SelectorSlotsProperty, value);
    }

    public Func<bool>? MoveUpEscapeHandler
    {
        get => (Func<bool>?)GetValue(MoveUpEscapeHandlerProperty);
        set => SetValue(MoveUpEscapeHandlerProperty, value);
    }

    public ChatInputArea()
    {
        InitializeComponent();
        _inputBoxHandledKeyDownHandler = OnInputBoxHandledKeyDown;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        foreach (var button in EnumerateComposerActionButtons())
        {
            button.GotFocus += OnComposerActionButtonGotFocus;
            button.Click += OnComposerActionButtonClick;
        }
#if WINDOWS
        InputBox.TextCompositionStarted += OnInputTextCompositionStarted;
        InputBox.TextCompositionEnded += OnInputTextCompositionEnded;
#endif
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InputBox.AddHandler(UIElement.KeyDownEvent, _inputBoxHandledKeyDownHandler, true);
        AttachWindowActivationHandler();
        EnsureFocusRefreshCallbacksRegistered();
        ScheduleRefreshVerticalFocusTargets();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        InputBox.RemoveHandler(UIElement.KeyDownEvent, _inputBoxHandledKeyDownHandler);
        DetachWindowActivationHandler();
        StopPendingActionActivationRestore();
        DetachFocusRefreshCallbacks();
    }

    private void AttachWindowActivationHandler()
    {
        if (_windowActivationHandlerAttached || App.MainWindowInstance is null)
        {
            return;
        }

        App.MainWindowInstance.Activated += OnMainWindowActivated;
        _windowActivationHandlerAttached = true;
    }

    private void DetachWindowActivationHandler()
    {
        if (!_windowActivationHandlerAttached || App.MainWindowInstance is null)
        {
            return;
        }

        App.MainWindowInstance.Activated -= OnMainWindowActivated;
        _windowActivationHandlerAttached = false;
    }

    private void OnMainWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (string.Equals(args.WindowActivationState.ToString(), "Deactivated", StringComparison.Ordinal)
            || _pendingActionBoundaryContinuationSource is not Button pendingActionButton)
        {
            return;
        }

        if (TryAbortPendingActionBoundaryContinuationIfFocusMoved(pendingActionButton))
        {
            return;
        }

        SchedulePendingActionActivationRestore(pendingActionButton, PendingActionActivationRestoreAttempts);
    }

    private void SchedulePendingActionActivationRestore(Button pendingActionButton, int remainingAttempts)
    {
        var continuationTarget = ResolvePendingActionContinuationTarget(pendingActionButton);

        if (!ReferenceEquals(_pendingActionBoundaryContinuationSource, pendingActionButton)
            || continuationTarget is null
            || remainingAttempts <= 0)
        {
            StopPendingActionActivationRestore();
            return;
        }

        EnsurePendingActionActivationRestoreTimer();
        _pendingActionActivationRestoreButton = pendingActionButton;
        _pendingActionActivationRestoreRemainingAttempts = remainingAttempts;
        _pendingActionActivationRestoreTimer?.Stop();
        _pendingActionActivationRestoreTimer?.Start();
    }

    private void EnsurePendingActionActivationRestoreTimer()
    {
        if (_pendingActionActivationRestoreTimer is not null)
        {
            return;
        }

        _pendingActionActivationRestoreTimer = DispatcherQueue.CreateTimer();
        _pendingActionActivationRestoreTimer.Interval = TimeSpan.FromMilliseconds(50);
        _pendingActionActivationRestoreTimer.IsRepeating = true;
        _pendingActionActivationRestoreTimer.Tick += OnPendingActionActivationRestoreTick;
    }

    private void OnPendingActionActivationRestoreTick(object? sender, object e)
    {
        if (_pendingActionActivationRestoreButton is not Button pendingActionButton
            || !ReferenceEquals(_pendingActionBoundaryContinuationSource, pendingActionButton)
            || _pendingActionActivationRestoreRemainingAttempts <= 0)
        {
            StopPendingActionActivationRestore();
            return;
        }

        if (TryAbortPendingActionBoundaryContinuationIfFocusMoved(pendingActionButton))
        {
            return;
        }

        var continuationTarget = ResolvePendingActionContinuationTarget(pendingActionButton);
        if (continuationTarget is null)
        {
            StopPendingActionActivationRestore();
            return;
        }

        _pendingActionActivationRestoreRemainingAttempts--;
        var focused = continuationTarget.Focus(FocusState.Programmatic);

        if (focused)
        {
            ClearPendingActionBoundaryContinuation(pendingActionButton);
            StopPendingActionActivationRestore();
        }
    }

    private void StopPendingActionActivationRestore()
    {
        _pendingActionActivationRestoreTimer?.Stop();
        _pendingActionActivationRestoreButton = null;
        _pendingActionActivationRestoreRemainingAttempts = 0;
    }

    private void EnsureFocusRefreshCallbacksRegistered()
    {
        if (_focusRefreshCallbacksRegistered)
        {
            return;
        }

        foreach (var button in EnumerateComposerActionButtons())
        {
            _focusRefreshCallbackTokens.Add((
                button,
                UIElement.VisibilityProperty,
                button.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, OnFocusBoundaryPropertyChanged)));
        }

        _focusRefreshCallbacksRegistered = true;
    }

    private void DetachFocusRefreshCallbacks()
    {
        foreach (var (target, property, token) in _focusRefreshCallbackTokens)
        {
            target.UnregisterPropertyChangedCallback(property, token);
        }

        _focusRefreshCallbackTokens.Clear();
        _focusRefreshCallbacksRegistered = false;
    }

    private void OnFocusBoundaryPropertyChanged(DependencyObject sender, DependencyProperty dependencyProperty)
    {
        TryRestoreFocusAfterActionBoundaryChange(sender);
        ScheduleRefreshVerticalFocusTargets();
    }

    private void OnComposerActionButtonGotFocus(object sender, RoutedEventArgs e)
    {
        _pendingActionBoundaryContinuationSource = sender as Button;
    }

    private void OnComposerActionButtonClick(object sender, RoutedEventArgs e)
    {
        _pendingActionBoundaryContinuationSource = sender as Button;
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChatInputArea control)
        {
            return;
        }

        if (e.OldValue is ChatViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= control.OnViewModelPropertyChanged;
            if (ReferenceEquals(control._observedViewModel, oldViewModel))
            {
                control._observedViewModel = null;
            }
        }

        if (control.ViewModel != null && control.SubmitCommand == null)
        {
            control.SubmitCommand = control.ViewModel.SendPromptCommand;
        }

        if (e.NewValue is ChatViewModel newViewModel)
        {
            newViewModel.PropertyChanged += control.OnViewModelPropertyChanged;
            control._observedViewModel = newViewModel;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _observedViewModel)
            || _pendingActionBoundaryContinuationSource is not Button pendingActionButton)
        {
            return;
        }

        if (TryAbortPendingActionBoundaryContinuationIfFocusMoved(pendingActionButton))
        {
            return;
        }

        if (!string.Equals(e.PropertyName, nameof(ChatViewModel.CanStartVoiceInput), StringComparison.Ordinal)
            && !string.Equals(e.PropertyName, nameof(ChatViewModel.ShowVoiceInputStartButton), StringComparison.Ordinal)
            && !string.Equals(e.PropertyName, nameof(ChatViewModel.CanStopVoiceInput), StringComparison.Ordinal)
            && !string.Equals(e.PropertyName, nameof(ChatViewModel.ShowVoiceInputStopButton), StringComparison.Ordinal)
            && !string.Equals(e.PropertyName, nameof(ChatViewModel.VoiceInputErrorMessage), StringComparison.Ordinal))
        {
            return;
        }

        if (TryContinuePendingActionBoundaryChange(pendingActionButton))
        {
            return;
        }

        SchedulePendingActionActivationRestore(pendingActionButton, PendingActionActivationRestoreAttempts);
    }

    private bool IsPromptEditingAvailable()
        => ViewModel?.IsTextInputEnabled == true;

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_isImeComposing || ViewModel == null || !IsPromptEditingAvailable())
        {
            return;
        }

        if (TryHandleInputDirectionalKey(e.Key))
        {
            e.Handled = true;
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
            case Windows.System.VirtualKey.GamepadDPadUp:
                if (ViewModel.TryMoveSlashSelection(-1))
                {
                    e.Handled = true;
                    return;
                }
                break;
            case Windows.System.VirtualKey.Down:
            case Windows.System.VirtualKey.GamepadDPadDown:
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

    private void OnInputBoxHandledKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isGamepadDirectionalKey = e.Key is Windows.System.VirtualKey.GamepadDPadUp
            or Windows.System.VirtualKey.GamepadDPadDown;

        if (!e.Handled && !isGamepadDirectionalKey)
        {
            return;
        }

        if (ReferenceEquals(sender, InputBox)
            && TryHandleInputDirectionalKey(e.Key))
        {
            e.Handled = true;
        }
    }

    private bool TryHandleInputDirectionalKey(Windows.System.VirtualKey key)
    {
        if (_isImeComposing || ViewModel == null || !IsPromptEditingAvailable())
        {
            return false;
        }

        if (!ViewModel.ShowSlashCommands)
        {
            if ((key == Windows.System.VirtualKey.Up || key == Windows.System.VirtualKey.GamepadDPadUp)
                && MoveUpEscapeHandler?.Invoke() == true)
            {
                return true;
            }

            if ((key == Windows.System.VirtualKey.Down || key == Windows.System.VirtualKey.GamepadDPadDown)
                && TryFocusFirstVisibleSelector())
            {
                return true;
            }

            return false;
        }

        return key switch
        {
            Windows.System.VirtualKey.Up or Windows.System.VirtualKey.GamepadDPadUp => ViewModel.TryMoveSlashSelection(-1),
            Windows.System.VirtualKey.Down or Windows.System.VirtualKey.GamepadDPadDown => ViewModel.TryMoveSlashSelection(1),
            _ => false
        };
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

        return action switch
        {
            ChatInputNavigationAction.MoveSlashUp => ViewModel.TryMoveSlashSelection(-1),
            ChatInputNavigationAction.MoveSlashDown => ViewModel.TryMoveSlashSelection(1),
            ChatInputNavigationAction.AcceptSlashCommand => TryAcceptSelectedSlashCommandAndMoveCaretToEnd(),
            ChatInputNavigationAction.EscapeMoveUp => ConsumeOrEscapeMoveUp(),
            ChatInputNavigationAction.MoveToFirstSelector => TryFocusFirstVisibleSelector(),
            _ => false
        };
    }

    public bool TryConsumeShortcutIntent(GamepadShortcutIntent intent)
    {
        if (ViewModel == null || XamlRoot == null)
        {
            return false;
        }

        var focusedElement = XamlFocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var action = ChatVoiceShortcutPolicy.Decide(
            intent,
            ResolveVoiceShortcutFocusContext(focusedElement),
            ViewModel.CanStartVoiceInput,
            ViewModel.CanStopVoiceInput,
            ViewModel.IsVoiceInputListening,
            _isImeComposing);

        return action switch
        {
            ChatVoiceShortcutAction.StartVoiceInput => TryExecuteVoiceCommand(ViewModel.StartVoiceInputCommand),
            ChatVoiceShortcutAction.StopVoiceInput => TryExecuteVoiceCommand(ViewModel.StopVoiceInputCommand),
            _ => false
        };
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

        var focused = InputBox.Focus(FocusState.Keyboard);
        InputBox.SelectionStart = InputBox.Text?.Length ?? 0;
        InputBox.SelectionLength = 0;
        return focused;
    }

    private bool ConsumeOrEscapeMoveUp()
    {
        return MoveUpEscapeHandler?.Invoke() == true;
    }

    private void RefreshVerticalFocusTargets()
    {
        var selectors = GetVisibleSelectors().ToArray();
        var firstSelector = selectors.FirstOrDefault();
        if (firstSelector is null)
        {
            return;
        }

        InputBox.XYFocusDown = firstSelector;
        for (var i = 0; i < selectors.Length; i++)
        {
            selectors[i].XYFocusUp = InputBox;
            selectors[i].ClearValue(Control.XYFocusDownProperty);
            selectors[i].ClearValue(Control.XYFocusLeftProperty);
            selectors[i].ClearValue(Control.XYFocusRightProperty);

            if (i > 0)
            {
                selectors[i].XYFocusLeft = selectors[i - 1];
            }

            if (i + 1 < selectors.Length)
            {
                selectors[i].XYFocusRight = selectors[i + 1];
            }
        }

        var trailingSelector = GetTrailingVisibleSelector();
        var leadingActionButton = GetLeadingVisibleActionButton();
        if (trailingSelector is null || leadingActionButton is null)
        {
            return;
        }

        trailingSelector.XYFocusRight = leadingActionButton;
        leadingActionButton.XYFocusLeft = trailingSelector;
    }

    private void ScheduleRefreshVerticalFocusTargets()
    {
        RefreshVerticalFocusTargets();
        _ = DispatcherQueue.TryEnqueue(RefreshVerticalFocusTargets);
    }

    private ComboBox? GetFirstVisibleSelector()
        => GetVisibleSelectors().FirstOrDefault();

    private bool TryFocusFirstVisibleSelector()
    {
        var firstSelector = GetFirstVisibleSelector();
        var result = firstSelector is ComboBox comboBox
            && comboBox.Focus(FocusState.Programmatic);
        return result;
    }

    private ComboBox? GetTrailingVisibleSelector()
        => GetVisibleSelectors().LastOrDefault();

    private Button? GetLeadingVisibleActionButton()
    {
        return EnumerateComposerActionButtons()
            .FirstOrDefault(button => button.Visibility == Visibility.Visible
                                      && button.IsEnabled);
    }

    private IEnumerable<Button> EnumerateComposerActionButtons()
    {
        return
        [
            VoiceInputStartButton,
            VoiceInputStopButton,
            SendButton,
            CancelButton
        ];
    }

    private void TryRestoreFocusAfterActionBoundaryChange(DependencyObject sender)
    {
        if (sender is not Button actionButton
            || XamlRoot is null)
        {
            return;
        }

        var focusedElement = XamlFocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var focusedButton = FindAncestorOrSelf<Button>(focusedElement);
        if (_pendingActionBoundaryContinuationSource is Button pendingActionButton
            && !IsVisibleAndEnabled(pendingActionButton))
        {
            if (TryAbortPendingActionBoundaryContinuationIfFocusMoved(pendingActionButton))
            {
                return;
            }

            if (TryContinuePendingActionBoundaryChange(pendingActionButton))
            {
                return;
            }

            ScheduleReplacementActionContinuation(pendingActionButton, remainingAttempts: 6);
            return;
        }

        if (!ReferenceEquals(focusedButton, actionButton))
        {
            return;
        }

        if (IsVisibleAndEnabled(actionButton))
        {
            return;
        }

        if (TryContinuePendingActionBoundaryChange(actionButton))
        {
            return;
        }

        ScheduleReplacementActionContinuation(actionButton, remainingAttempts: 6);
    }

    private bool IsActionButtonFocusContinuationActive(Button pendingActionButton)
    {
        if (XamlRoot is null)
        {
            return false;
        }

        var focusedButton = FindAncestorOrSelf<Button>(XamlFocusManager.GetFocusedElement(XamlRoot) as DependencyObject);
        if (focusedButton is null)
        {
            return false;
        }

        return ReferenceEquals(focusedButton, pendingActionButton)
               || EnumerateComposerActionButtons().Any(button => ReferenceEquals(button, focusedButton));
    }

    private bool TryContinuePendingActionBoundaryChange(Button pendingActionButton)
    {
        if (TryAbortPendingActionBoundaryContinuationIfFocusMoved(pendingActionButton))
        {
            return false;
        }

        var continuationTarget = ResolvePendingActionContinuationTarget(pendingActionButton);
        if (continuationTarget is null)
        {
            return false;
        }

        var focusedButton = XamlRoot is null
            ? null
            : FindAncestorOrSelf<Button>(XamlFocusManager.GetFocusedElement(XamlRoot) as DependencyObject);

        if (ReferenceEquals(focusedButton, pendingActionButton)
            && ReferenceEquals(continuationTarget, pendingActionButton))
        {
            return false;
        }

        if (ReferenceEquals(focusedButton, continuationTarget)
            || continuationTarget.Focus(FocusState.Programmatic))
        {
            ClearPendingActionBoundaryContinuation(pendingActionButton);
            StopPendingActionActivationRestore();
            return true;
        }

        return false;
    }

    private Button? ResolvePendingActionContinuationTarget(Button pendingActionButton)
    {
        if (IsVisibleAndEnabled(pendingActionButton))
        {
            return pendingActionButton;
        }

        return EnumerateComposerActionButtons()
            .FirstOrDefault(button => !ReferenceEquals(button, pendingActionButton) && IsVisibleAndEnabled(button));
    }

    private bool FocusReplacementActionButton(Button previousActionButton)
    {
        var replacementActionButton = ResolvePendingActionContinuationTarget(previousActionButton);
        if (replacementActionButton is not null
            && !ReferenceEquals(replacementActionButton, previousActionButton)
            && replacementActionButton.Focus(FocusState.Programmatic))
        {
            return true;
        }

        return false;
    }

    private void ScheduleReplacementActionContinuation(Button previousActionButton, int remainingAttempts)
    {
        if (remainingAttempts <= 0)
        {
            ClearPendingActionBoundaryContinuation(previousActionButton);
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (TryAbortPendingActionBoundaryContinuationIfFocusMoved(previousActionButton))
            {
                return;
            }

            if (FocusReplacementActionButton(previousActionButton))
            {
                ClearPendingActionBoundaryContinuation(previousActionButton);
                return;
            }

            ScheduleReplacementActionContinuation(previousActionButton, remainingAttempts - 1);
        });
    }

    private void ClearPendingActionBoundaryContinuation(Button actionButton)
    {
        if (ReferenceEquals(_pendingActionBoundaryContinuationSource, actionButton))
        {
            _pendingActionBoundaryContinuationSource = null;
        }
    }

    private bool TryAbortPendingActionBoundaryContinuationIfFocusMoved(Button pendingActionButton)
    {
        if (IsActionButtonFocusContinuationActive(pendingActionButton))
        {
            return false;
        }

        ClearPendingActionBoundaryContinuation(pendingActionButton);
        StopPendingActionActivationRestore();
        return true;
    }

    private static void OnComposerFocusTopologyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatInputArea control)
        {
            control.ScheduleRefreshVerticalFocusTargets();
        }
    }

    private static bool IsVisibleAndEnabled(Control control)
        => control.Visibility == Visibility.Visible
           && control.IsEnabled;

    private IEnumerable<ComboBox> GetVisibleSelectors()
    {
        return new[]
            {
                GetLoadedSelector(nameof(AgentSelectorHost)),
                GetLoadedSelector(nameof(ModeSelectorHost)),
                GetLoadedSelector(nameof(ProjectSelectorHost))
            }
            .Where(selector => selector is not null
                               && selector.XamlRoot is not null
                               && selector.Visibility == Visibility.Visible
                               && selector.ActualWidth > 0
                               && selector.ActualHeight > 0
                               && selector.IsEnabled)!;
    }

    private ComboBox? GetLoadedSelector(string selectorName)
        => FindName(selectorName) as ComboBox;

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
        ExecuteSelectorCommand(sender, SelectorSlots.Mode.SelectionCommand);
    }

    private void OnAgentSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ExecuteSelectorCommand(sender, SelectorSlots.Agent.SelectionCommand);
    }

    private void OnProjectSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ExecuteSelectorCommand(sender, SelectorSlots.Project.SelectionCommand);
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
    {
        _openSelectorHost = sender as ComboBox;
        SelectorDropDownOpened?.Invoke(this, EventArgs.Empty);
    }

    private void OnSelectorDropDownClosed(object sender, object e)
    {
        if (ReferenceEquals(_openSelectorHost, sender))
        {
            _openSelectorHost = null;
        }

        SelectorDropDownClosed?.Invoke(this, EventArgs.Empty);
    }

    private ChatInputFocusContext ResolveFocusContext(DependencyObject? focusedElement)
    {
        if (!IsInControlSubtree(focusedElement))
        {
            return ChatInputFocusContext.Other;
        }

        var focusedComboBox = ResolveOwningComboBox(focusedElement);
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

    private ComboBox? ResolveOwningComboBox(DependencyObject? focusedElement)
    {
        var comboBox = FindAncestorOrSelf<ComboBox>(focusedElement);
        if (comboBox is not null)
        {
            return comboBox;
        }

        if (focusedElement is ComboBoxItem && _openSelectorHost is not null)
        {
            return _openSelectorHost;
        }

        return null;
    }

    private bool IsInControlSubtree(DependencyObject? element)
    {
        return ReferenceEquals(FindAncestorOrSelf<ChatInputArea>(element), this);
    }

    private ChatVoiceShortcutFocusContext ResolveVoiceShortcutFocusContext(DependencyObject? focusedElement)
    {
        return ReferenceEquals(FindAncestorOrSelf<TextBox>(focusedElement), InputBox)
            ? ChatVoiceShortcutFocusContext.InputBox
            : ChatVoiceShortcutFocusContext.Other;
    }

    private static bool TryExecuteVoiceCommand(ICommand? command)
    {
        if (command is null || !command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
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

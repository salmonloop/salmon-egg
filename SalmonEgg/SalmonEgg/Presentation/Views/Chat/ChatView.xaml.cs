using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SalmonEgg.Controls;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Transcript;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Chat;
using Windows.Foundation;
using XamlFocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace SalmonEgg.Presentation.Views.Chat;

public sealed partial class ChatView : Page, INavigationIntentConsumer, IGamepadContextIntentConsumer, IPrimaryContentFocusTarget
{
    public ChatShellViewModel ShellViewModel { get; }
    public ChatViewModel ViewModel => ShellViewModel.Chat;
    public ListViewTranscriptItemsSource MessagesItemsSource { get; } = new();
    public ShellLayoutViewModel LayoutVM => ShellViewModel.ShellLayout;
    private bool _isViewLoaded;
    private bool _isTrackingMessages;
    private readonly TranscriptViewportController _viewportController = new();
    private const double BottomThreshold = 10;
    private const double BottomGeometryTolerance = 2;
    private const int MaxRestoreAttempts = 32;
    private bool _wasOverlayVisible;
    private bool _resumeViewportCoordinatorAfterOverlayPending;
    private bool _isTranscriptViewportLayerActive;
    private object? _activeTranscriptMessageAnchorItem;
    private bool _isTranscriptChildControlLayerActive;
    private int? _pendingTranscriptMessageFocusIndex;
    private readonly TranscriptProjectionRestoreController _projectionRestoreController = new(MaxRestoreAttempts);
    private string _transcriptViewportAutomationState = "inactive";
    private INotifyCollectionChanged? _trackedMessageHistory;
    private readonly Microsoft.UI.Xaml.Input.KeyEventHandler _messagesListHandledKeyDownHandler;
    private readonly PointerEventHandler _messagesListHandledPointerPressedHandler;
    private readonly PointerEventHandler _messagesListHandledPointerWheelChangedHandler;
    private readonly TypedEventHandler<ListViewBase, ContainerContentChangingEventArgs> _messagesListContainerContentChangingHandler;
    private readonly RoutedEventHandler _messagesListItemGotFocusHandler;
    private ITranscriptViewportHost? _transcriptViewportHost;
    private TranscriptScrollRequestToken? _queuedNativeTranscriptScrollRequestToken;
    public ChatView()
    {
        ShellViewModel = App.ServiceProvider.GetRequiredService<ChatShellViewModel>();
        NavigationCacheMode = NavigationCacheMode.Required;
        _messagesListHandledKeyDownHandler = OnMessagesListKeyDown;
        _messagesListHandledPointerPressedHandler = OnMessagesListPointerPressed;
        _messagesListHandledPointerWheelChangedHandler = OnMessagesListPointerWheelChanged;
        _messagesListContainerContentChangingHandler = OnMessagesListContainerContentChanging;
        _messagesListItemGotFocusHandler = OnMessagesListItemGotFocus;

        this.InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnConversationInputAreaLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ChatInputArea inputArea)
        {
            inputArea.MoveUpEscapeHandler = TryFocusTranscriptViewport;
        }
    }

    private void OnConversationInputAreaUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ChatInputArea inputArea)
        {
            inputArea.MoveUpEscapeHandler = null;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isViewLoaded = true;
        _resumeViewportCoordinatorAfterOverlayPending = false;
        ClearPendingProjectionRestore();
        ClearTranscriptMessageLayerState();
        _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
        _viewportController.Load(
            CurrentViewportConversationId,
            ViewModel.IsSessionActive,
            _wasOverlayVisible,
            ViewModel.MessageHistory.Count > 0);
        if (_wasOverlayVisible)
        {
            _resumeViewportCoordinatorAfterOverlayPending = true;
            ApplyViewportActions(_viewportController.SuspendForOverlay());
        }
        else
        {
            RestoreViewportForWarmResume();
        }
        EnsureMessageTracking();
        ApplyCurrentViewportStateIfAttached();
        UpdateTranscriptViewportAutomationState();
        // ViewModel logs ACP profile load failures; shell must not silence them.
        await ViewModel.EnsureAcpProfilesLoadedAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AbandonPendingProjectionRestore("ViewUnloaded");
        ApplyViewportActions(_viewportController.Unload());
        _isViewLoaded = false;
        _resumeViewportCoordinatorAfterOverlayPending = false;
        DisposeTranscriptViewportHost();
        ClearPendingProjectionRestore();
        ClearTranscriptMessageLayerState();
        UpdateTranscriptViewportAutomationState();
        if (_isTrackingMessages)
        {
            if (_trackedMessageHistory != null)
            {
                _trackedMessageHistory.CollectionChanged -= OnMessageHistoryChanged;
                _trackedMessageHistory = null;
            }
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.ProjectionRestoreReady -= OnProjectionRestoreReady;
            MessagesItemsSource.Detach();
            _isTrackingMessages = false;
        }
    }

    private void EnsureMessageTracking()
    {
        if (_isTrackingMessages)
        {
            if (!ReferenceEquals(_trackedMessageHistory, ViewModel.MessageHistory))
            {
                if (_trackedMessageHistory != null)
                {
                    _trackedMessageHistory.CollectionChanged -= OnMessageHistoryChanged;
                }

                _trackedMessageHistory = ViewModel.MessageHistory;
                MessagesItemsSource.Attach(ViewModel.MessageHistory);
                _trackedMessageHistory.CollectionChanged += OnMessageHistoryChanged;
            }

            return;
        }

        _trackedMessageHistory = ViewModel.MessageHistory;
        MessagesItemsSource.Attach(ViewModel.MessageHistory);
        _trackedMessageHistory.CollectionChanged += OnMessageHistoryChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.ProjectionRestoreReady += OnProjectionRestoreReady;
        _isTrackingMessages = true;
    }

    private void OnMessageHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isViewLoaded)
        {
            UpdateTranscriptViewportAutomationState();
            return;
        }

        ResumeViewportCoordinatorAfterOverlayIfNeeded();

        TryApplyPendingProjectionRestore();
        ApplyViewportActions(_viewportController.OnTranscriptContentChanged(CreateViewportViewState()));

        UpdateTranscriptViewportAutomationState();
    }

    private void OnMessagesListLoaded(object sender, RoutedEventArgs e)
    {
        DisposeTranscriptViewportHost();
        var messagesList = MessagesList;
        _transcriptViewportHost = messagesList is null
            ? null
            : new ListViewTranscriptViewportHost(messagesList);
        if (_transcriptViewportHost is not null)
        {
            _transcriptViewportHost.ViewportChanged += OnMessagesListViewportChanged;
        }

#if WINDOWS
            if (messagesList is not null)
            {
                messagesList.ShowsScrollingPlaceholders = false;
            }
#endif

        if (messagesList is not null)
        {
            messagesList.ContainerContentChanging -= _messagesListContainerContentChangingHandler;
            messagesList.ContainerContentChanging += _messagesListContainerContentChangingHandler;
            messagesList.AddHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler, true);
            messagesList.AddHandler(UIElement.PointerPressedEvent, _messagesListHandledPointerPressedHandler, true);
            messagesList.AddHandler(UIElement.PointerWheelChangedEvent, _messagesListHandledPointerWheelChangedHandler, true);
        }

        ResumeViewportCoordinatorAfterOverlayIfNeeded();
        TryApplyPendingTranscriptMessageFocus();
        TryApplyPendingProjectionRestore();
        ApplyCurrentViewportState();
        TryRefreshViewportCoordinatorFromView();
        UpdateTranscriptViewportAutomationState();
    }

    private void OnMessagesListUnloaded(object sender, RoutedEventArgs e)
    {
        DisposeTranscriptViewportHost();
        if (MessagesList is not null)
        {
            MessagesList.ContainerContentChanging -= _messagesListContainerContentChangingHandler;
        }
        MessagesList?.RemoveHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler);
        MessagesList?.RemoveHandler(UIElement.PointerPressedEvent, _messagesListHandledPointerPressedHandler);
        MessagesList?.RemoveHandler(UIElement.PointerWheelChangedEvent, _messagesListHandledPointerWheelChangedHandler);
        UpdateTranscriptViewportAutomationState();
    }

    private void DisposeTranscriptViewportHost()
    {
        _queuedNativeTranscriptScrollRequestToken = null;
        if (_transcriptViewportHost is null)
        {
            return;
        }

        _transcriptViewportHost.ViewportChanged -= OnMessagesListViewportChanged;
        _transcriptViewportHost.Dispose();
        _transcriptViewportHost = null;
    }

    private void OnMessagesListViewportChanged(object? sender, EventArgs e)
    {
        TryApplyPendingTranscriptMessageFocus();
        TryApplyPendingProjectionRestore();
        ApplyViewportActions(_viewportController.OnViewportChanged(
            CreateViewportViewState(),
            TryCaptureProjectionRestoreToken()));
        UpdateTranscriptViewportAutomationState();
    }

    private void OnMessagesListPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (MessagesList is not null
            && !TranscriptPointerIntentFilter.ShouldTrackViewportIntent(e.OriginalSource, MessagesList))
        {
            return;
        }

        if (IsViewportDetachedByUser())
        {
            FocusTranscriptScroller();
            return;
        }

        if (_projectionRestoreController.HasPending)
        {
            AbandonPendingProjectionRestore("UserInterrupted");
        }

        _viewportController.MarkUserScrollIntentStarted();
        FocusTranscriptScroller();
    }

    private void OnMessagesListPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (MessagesList is not null
            && !TranscriptPointerIntentFilter.ShouldTrackViewportIntent(e.OriginalSource, MessagesList))
        {
            return;
        }

        _viewportController.MarkUserScrollIntentCompleted();
        var releaseGeneration = _viewportController.Generation;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (releaseGeneration != _viewportController.Generation
                || ViewModel.IsActivationOverlayVisible)
            {
                return;
            }

            TryRefreshViewportCoordinatorFromView();
            UpdateTranscriptViewportAutomationState();
        });
    }

    private void OnMessagesListPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (MessagesList is not null
            && !TranscriptPointerIntentFilter.ShouldTrackViewportIntent(e.OriginalSource, MessagesList))
        {
            return;
        }

        RegisterUserViewportIntent();
    }

    private void OnMessagesListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is Windows.System.VirtualKey.Up
            or Windows.System.VirtualKey.Down
            or Windows.System.VirtualKey.PageUp
            or Windows.System.VirtualKey.PageDown
            or Windows.System.VirtualKey.Home
            or Windows.System.VirtualKey.End)
        {
            RegisterUserViewportIntent();
        }
    }

    private void OnMessagesListContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not ListViewItem container)
        {
            return;
        }

        container.GotFocus -= _messagesListItemGotFocusHandler;
        container.GotFocus += _messagesListItemGotFocusHandler;
        container.ClearValue(Control.XYFocusRightProperty);

        if (FindFirstInteractiveTranscriptChild(container) is not Control firstInteractiveChild)
        {
            return;
        }

        container.XYFocusRight = firstInteractiveChild;
        firstInteractiveChild.XYFocusLeft = container;
        TryApplyPendingTranscriptMessageFocus();
    }

    private void OnMessagesListItemGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not ListViewItem container || MessagesList is null)
        {
            return;
        }

        _activeTranscriptMessageAnchorItem = MessagesList.ItemFromContainer(container);
        _isTranscriptViewportLayerActive = false;
        _isTranscriptChildControlLayerActive = false;
    }

    public bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        if (_isTranscriptViewportLayerActive && IsTranscriptMessageLayerFocusWithin())
        {
            _isTranscriptViewportLayerActive = false;
        }

        if (!_isTranscriptChildControlLayerActive
            && IsTranscriptMessageLayerFocusWithin())
        {
            if (intent == GamepadNavigationIntent.MoveUp)
            {
                return TryMoveFocusBetweenTranscriptMessages(-1);
            }

            if (intent == GamepadNavigationIntent.MoveDown)
            {
                return TryMoveFocusBetweenTranscriptMessages(1);
            }
        }

        if (intent == GamepadNavigationIntent.MoveRight)
        {
            return TryMoveFocusFromTranscriptMessageToChildControl();
        }

        if (intent == GamepadNavigationIntent.MoveLeft)
        {
            return TryMoveFocusFromTranscriptChildControlToMessage();
        }

        if (_transcriptViewportHost is null)
        {
            return false;
        }

        var consumed = ChatTranscriptNavigationIntentHandler.TryConsume(
            intent,
            _isTranscriptViewportLayerActive || IsTranscriptViewportSurfaceFocusWithin(),
            ViewModel.MessageHistory.Count,
            _transcriptViewportHost.TryScrollByItems,
            RegisterUserViewportIntent);
        if (consumed)
        {
            _isTranscriptViewportLayerActive = true;
            if (!IsTranscriptViewportSurfaceFocusWithin())
            {
                _ = TryFocusTranscriptViewportSurface(FocusState.Keyboard);
            }
        }

        return consumed;
    }

    public bool TryConsumeContextIntent(GamepadContextIntent intent)
    {
        if (_transcriptViewportHost is null)
        {
            return false;
        }

        if (!IsTranscriptContextFocusWithin())
        {
            _isTranscriptViewportLayerActive = false;
            ClearTranscriptMessageLayerState();
            return false;
        }

        var hasTranscriptContextFocus =
            IsTranscriptViewportSurfaceFocusWithin()
            || (!_isTranscriptChildControlLayerActive && IsTranscriptMessageContainerFocused());
        var consumed = ChatTranscriptContextIntentHandler.TryConsume(
            intent,
            hasTranscriptContextFocus,
            ViewModel.MessageHistory.Count,
            _transcriptViewportHost.TryScrollByPages,
            RegisterUserViewportIntent);
        if (consumed)
        {
            _isTranscriptViewportLayerActive = true;
            if (!IsTranscriptViewportSurfaceFocusWithin())
            {
                _ = TryFocusTranscriptViewportSurface(FocusState.Keyboard);
            }
        }

        return consumed;
    }

    public bool TryFocusPrimaryContentTarget()
    {
        if (ViewModel.ShouldShowConversationInputSurface
            && ConversationInputArea is not null
            && ConversationInputArea.TryFocusInputBox())
        {
            _isTranscriptViewportLayerActive = false;
            ClearTranscriptMessageLayerState();
            return true;
        }

        if (MessagesList is not null
            && ViewModel.ShouldShowTranscriptSurface
            && ViewModel.MessageHistory.Count > 0)
        {
            return TryFocusTranscriptViewportSurface(FocusState.Keyboard);
        }

        if (ViewModel.ShouldShowSessionHeader
            && CurrentSessionTitleBlock is not null)
        {
            _isTranscriptViewportLayerActive = false;
            return CurrentSessionTitleBlock.Focus(FocusState.Programmatic);
        }

        return false;
    }

    private bool TryFocusTranscriptViewport()
    {
        if (MessagesList is not null
            && ViewModel.ShouldShowTranscriptSurface
            && ViewModel.MessageHistory.Count > 0)
        {
            return TryFocusTranscriptViewportSurface(FocusState.Keyboard);
        }

        return false;
    }

    private void FocusTranscriptScroller()
    {
        _ = TryFocusTranscriptViewportSurface(FocusState.Keyboard);
    }

    private bool TryFocusTranscriptViewportSurface(FocusState focusState)
    {
        _pendingTranscriptMessageFocusIndex = null;
        if (_transcriptViewportHost?.TryFocusViewport(focusState) == true)
        {
            _isTranscriptViewportLayerActive = true;
            _isTranscriptChildControlLayerActive = false;
            return true;
        }

        if (MessagesList?.Focus(focusState) == true)
        {
            _isTranscriptViewportLayerActive = true;
            _isTranscriptChildControlLayerActive = false;
            return true;
        }

        return false;
    }

    private bool IsTranscriptViewportSurfaceFocusWithin()
    {
        if (MessagesList is null)
        {
            return false;
        }

        if (MessagesList.FocusState is FocusState.Keyboard or FocusState.Programmatic)
        {
            return true;
        }

        if (MessagesList.XamlRoot is null)
        {
            return false;
        }

        var current = XamlFocusManager.GetFocusedElement(MessagesList.XamlRoot) as DependencyObject;
        if (DependencyObjectAncestry.FindAncestorOrSelf<ListViewItem>(current) is ListViewItem itemContainer
            && DependencyObjectAncestry.IsDescendantOf(itemContainer, MessagesList))
        {
            return false;
        }

        return DependencyObjectAncestry.IsDescendantOf(current, MessagesList);
    }

    private bool IsTranscriptContextFocusWithin()
    {
        if (MessagesList is null || IsConversationInputSurfaceFocusWithin())
        {
            return false;
        }

        if (MessagesList.XamlRoot is null)
        {
            return MessagesList.FocusState is FocusState.Keyboard or FocusState.Programmatic;
        }

        var current = XamlFocusManager.GetFocusedElement(MessagesList.XamlRoot) as DependencyObject;
        return DependencyObjectAncestry.IsDescendantOf(current, MessagesList);
    }

    private bool IsConversationInputSurfaceFocusWithin()
    {
        if (ConversationInputArea is null || ConversationInputArea.XamlRoot is null)
        {
            return false;
        }

        var current = XamlFocusManager.GetFocusedElement(ConversationInputArea.XamlRoot) as DependencyObject;
        return DependencyObjectAncestry.IsDescendantOf(current, ConversationInputArea);
    }

    private bool IsTranscriptMessageLayerFocusWithin()
    {
        if (MessagesList?.XamlRoot is null)
        {
            return false;
        }

        var current = XamlFocusManager.GetFocusedElement(MessagesList.XamlRoot) as DependencyObject;
        return DependencyObjectAncestry.FindAncestorOrSelf<ListViewItem>(current) is ListViewItem itemContainer
            && DependencyObjectAncestry.IsDescendantOf(itemContainer, MessagesList);
    }

    private bool IsTranscriptMessageContainerFocused()
    {
        if (MessagesList?.XamlRoot is null)
        {
            return false;
        }

        var current = XamlFocusManager.GetFocusedElement(MessagesList.XamlRoot) as DependencyObject;
        var itemContainer = DependencyObjectAncestry.FindAncestorOrSelf<ListViewItem>(current);
        return itemContainer is not null
            && ReferenceEquals(current, itemContainer)
            && DependencyObjectAncestry.IsDescendantOf(itemContainer, MessagesList);
    }

    private static Control? FindFirstInteractiveTranscriptChild(DependencyObject root)
        => DependencyObjectAncestry.FindDescendant<Control>(
            root,
            static control =>
                control is not ListViewItem
                && control.Visibility == Visibility.Visible
                && control.IsEnabled
                && control.IsTabStop);

    private bool TryMoveFocusFromTranscriptMessageToChildControl()
    {
        if (MessagesList?.XamlRoot is null)
        {
            return false;
        }

        var current = XamlFocusManager.GetFocusedElement(MessagesList.XamlRoot) as DependencyObject;
        var focusedItemContainer = DependencyObjectAncestry.FindAncestorOrSelf<ListViewItem>(current);
        if (focusedItemContainer is null || !DependencyObjectAncestry.IsDescendantOf(focusedItemContainer, MessagesList))
        {
            return false;
        }

        if (!ReferenceEquals(current, focusedItemContainer))
        {
            return false;
        }

        if (FindFirstInteractiveTranscriptChild(focusedItemContainer) is not Control firstInteractiveChild)
        {
            return false;
        }

        _activeTranscriptMessageAnchorItem = MessagesList.ItemFromContainer(focusedItemContainer);
        var focused = TryFocusTranscriptNavigationTarget(firstInteractiveChild);
        _isTranscriptViewportLayerActive = false;
        _isTranscriptChildControlLayerActive = focused;
        return focused;
    }

    private bool TryMoveFocusBetweenTranscriptMessages(int itemDelta)
    {
        if (itemDelta == 0 || MessagesList?.XamlRoot is null || MessagesList.Items.Count <= 0)
        {
            return false;
        }

        var current = XamlFocusManager.GetFocusedElement(MessagesList.XamlRoot) as DependencyObject;
        var focusedItemContainer = DependencyObjectAncestry.FindAncestorOrSelf<ListViewItem>(current);
        var currentIndex = -1;
        if (focusedItemContainer is not null && DependencyObjectAncestry.IsDescendantOf(focusedItemContainer, MessagesList))
        {
            currentIndex = MessagesList.IndexFromContainer(focusedItemContainer);
        }

        if (currentIndex < 0 && _activeTranscriptMessageAnchorItem is not null)
        {
            currentIndex = MessagesList.Items.IndexOf(_activeTranscriptMessageAnchorItem);
        }

        if (currentIndex < 0)
        {
            return false;
        }

        var targetIndex = Math.Clamp(currentIndex + itemDelta, 0, MessagesList.Items.Count - 1);
        if (targetIndex == currentIndex)
        {
            _isTranscriptViewportLayerActive = false;
            _isTranscriptChildControlLayerActive = false;
            return true;
        }

        var transcriptViewportHost = _transcriptViewportHost;
        if (transcriptViewportHost is null)
        {
            return false;
        }

        if (!transcriptViewportHost.TryFocusItem(targetIndex, FocusState.Keyboard))
        {
            if (MessagesList.Items[targetIndex] is not object item)
            {
                return false;
            }

            _pendingTranscriptMessageFocusIndex = targetIndex;
            transcriptViewportHost.ScrollItemIntoView(targetIndex, TranscriptItemScrollAlignment.Leading);
            _activeTranscriptMessageAnchorItem = item;
            _isTranscriptViewportLayerActive = false;
            _isTranscriptChildControlLayerActive = false;
            _ = DispatcherQueue.TryEnqueue(TryApplyPendingTranscriptMessageFocus);
            return true;
        }

        _pendingTranscriptMessageFocusIndex = null;
        _activeTranscriptMessageAnchorItem = MessagesList.Items[targetIndex];
        _isTranscriptViewportLayerActive = false;
        _isTranscriptChildControlLayerActive = false;
        return true;
    }

    private void TryApplyPendingTranscriptMessageFocus()
    {
        if (_pendingTranscriptMessageFocusIndex is not int pendingIndex)
        {
            return;
        }

        if (_transcriptViewportHost?.TryFocusItem(pendingIndex, FocusState.Keyboard) == true)
        {
            _pendingTranscriptMessageFocusIndex = null;
        }
    }

    private bool TryMoveFocusFromTranscriptChildControlToMessage()
    {
        if (MessagesList?.XamlRoot is null)
        {
            return false;
        }

        var current = XamlFocusManager.GetFocusedElement(MessagesList.XamlRoot) as DependencyObject;
        var focusedItemContainer = DependencyObjectAncestry.FindAncestorOrSelf<ListViewItem>(current);
        if (focusedItemContainer is null || !DependencyObjectAncestry.IsDescendantOf(focusedItemContainer, MessagesList))
        {
            if (_isTranscriptChildControlLayerActive && TryFocusStoredTranscriptMessageAnchor())
            {
                _isTranscriptChildControlLayerActive = false;
                return true;
            }
            return false;
        }

        if (ReferenceEquals(current, focusedItemContainer))
        {
            return false;
        }

        var focused = TryFocusTranscriptNavigationTarget(focusedItemContainer);
        _isTranscriptViewportLayerActive = false;
        _isTranscriptChildControlLayerActive = false;
        return focused;
    }

    private bool TryFocusStoredTranscriptMessageAnchor()
    {
        if (MessagesList is null || _activeTranscriptMessageAnchorItem is null)
        {
            return false;
        }

        var anchorIndex = MessagesList.Items.IndexOf(_activeTranscriptMessageAnchorItem);
        if (anchorIndex < 0)
        {
            return false;
        }

        return _transcriptViewportHost?.TryFocusItem(anchorIndex, FocusState.Keyboard) == true;
    }

    private void ClearTranscriptMessageLayerState()
    {
        _activeTranscriptMessageAnchorItem = null;
        _isTranscriptChildControlLayerActive = false;
        _pendingTranscriptMessageFocusIndex = null;
    }

    private static bool TryFocusTranscriptNavigationTarget(Control target)
    {
        return target.Focus(FocusState.Keyboard)
            || target.Focus(FocusState.Programmatic);
    }

    private void RegisterUserViewportIntent()
    {
        if (_projectionRestoreController.HasPending)
        {
            AbandonPendingProjectionRestore("UserInterrupted");
        }

        if (IsViewportDetachedByUser())
        {
            FocusTranscriptScroller();
            ApplyViewportActions(_viewportController.OnUserViewportIntent(CreateViewportViewState()));
            UpdateTranscriptViewportAutomationState();
            return;
        }

        FocusTranscriptScroller();

        if (IsListViewportAtBottom())
        {
            ApplyViewportActions(_viewportController.OnUserViewportDetachIntent(
                CreateViewportViewState(),
                TryCaptureProjectionRestoreToken()));
            UpdateTranscriptViewportAutomationState();
            return;
        }

        ApplyViewportActions(_viewportController.OnUserViewportDetachIntent(
            CreateViewportViewState(),
            TryCaptureProjectionRestoreToken()));
        UpdateTranscriptViewportAutomationState();
    }

    private TranscriptProjectionRestoreToken? TryCaptureProjectionRestoreToken()
    {
        if (_transcriptViewportHost is null || ViewModel.MessageHistory.Count <= 0)
        {
            return null;
        }

        if (!_transcriptViewportHost.TryGetFirstVisibleIndex(ViewModel.MessageHistory.Count, out var firstVisibleIndex)
            || firstVisibleIndex < 0
            || firstVisibleIndex >= ViewModel.MessageHistory.Count)
        {
            return null;
        }

        return ViewModel.CreateViewportProjectionRestoreToken(ViewModel.MessageHistory[firstVisibleIndex]);
    }

    private int ResolveProjectionRestoreIndex(TranscriptProjectionRestoreToken token)
        => ViewModel.MessageHistory.IndexOfProjectionItemKey(token.ProjectionItemKey);

    private void OnProjectionRestoreReady(object? sender, EventArgs e)
    {
        ApplyViewportActions(_viewportController.OnProjectionReady(CurrentViewportConversationId));
        TryApplyPendingProjectionRestore();
        UpdateTranscriptViewportAutomationState();
    }

    private void TryRefreshViewportCoordinatorFromView()
    {
        if (!_isViewLoaded
            || _transcriptViewportHost is null
            || ViewModel.IsActivationOverlayVisible
            || !ViewModel.IsSessionActive
            || string.IsNullOrWhiteSpace(ViewModel.CurrentSessionId))
        {
            return;
        }

        ApplyViewportActions(_viewportController.OnViewportChanged(
            CreateViewportViewState(),
            TryCaptureProjectionRestoreToken()));
    }

    private TranscriptViewportViewState CreateViewportViewState()
    {
        var hasMessages = ViewModel.MessageHistory.Count > 0;
        return new TranscriptViewportViewState(
            HasMessages: hasMessages,
            IsAtBottom: IsListViewportAtBottom());
    }

    private void ApplyViewportActions(IReadOnlyList<TranscriptViewportControllerAction> actions)
    {
        foreach (var action in actions)
        {
            ApplyViewportAction(action);
        }
    }

    private void ApplyViewportAction(TranscriptViewportControllerAction action)
    {
        switch (action.Kind)
        {
            case TranscriptViewportControllerActionKind.ScrollIntoView:
                if (!string.IsNullOrWhiteSpace(action.ItemKey))
                {
                    var index = ViewModel.MessageHistory.IndexOfProjectionItemKey(action.ItemKey);
                    if (index >= 0)
                    {
                        _transcriptViewportHost?.ScrollItemIntoView(index);
                    }
                }
                break;
            case TranscriptViewportControllerActionKind.ScrollTranscriptToEnd:
                if (action.ScrollRequestToken.Generation >= 0)
                {
                    IssueNativeTranscriptScrollRequest(action.ScrollRequestToken);
                }
                else
                {
                    RequestScrollToEnd();
                }
                break;

            case TranscriptViewportControllerActionKind.RequestRestore:
                if (action.RestoreToken is { } restoreToken)
                {
                    QueueProjectionOwnedRestore(restoreToken, action.Generation);
                }
                break;
        }
    }

    private void ActivateViewportForCurrentSession(TranscriptViewportActivationKind activationKind)
    {
        ApplyViewportActions(_viewportController.ActivateCurrentConversation(
            CurrentViewportConversationId,
            ViewModel.IsSessionActive,
            ViewModel.IsActivationOverlayVisible,
            ViewModel.MessageHistory.Count > 0,
            activationKind));
    }

    private bool IsViewportDetachedByUser()
        => _viewportController.State is TranscriptViewportState.DetachedByUser;

    private string CurrentViewportConversationId => ViewModel.CurrentSessionId ?? string.Empty;

    private void QueueProjectionOwnedRestore(TranscriptProjectionRestoreToken token, int generation)
    {
        _projectionRestoreController.Queue(token, generation);
        TryApplyPendingProjectionRestore();
    }

    private void TryApplyPendingProjectionRestore()
    {
        if (_transcriptViewportHost is null || !_isViewLoaded)
        {
            return;
        }

        ApplyProjectionRestoreResult(_projectionRestoreController.TryApply(
            _transcriptViewportHost,
            ViewModel.MessageHistory.Count,
            CurrentViewportConversationId,
            _viewportController.Generation,
            ResolveProjectionRestoreIndex));
    }

    private void AbandonPendingProjectionRestore(string reason)
    {
        ApplyProjectionRestoreResult(_projectionRestoreController.Abandon(CurrentViewportConversationId, reason));
    }

    private void ClearPendingProjectionRestore()
    {
        _projectionRestoreController.Clear();
    }

    private void ApplyProjectionRestoreResult(TranscriptProjectionRestoreResult result)
    {
        switch (result.Kind)
        {
            case TranscriptProjectionRestoreResultKind.Retry:
                _projectionRestoreController.TryScheduleRetry(DispatcherQueue, TryApplyPendingProjectionRestore);
                break;

            case TranscriptProjectionRestoreResultKind.Confirmed:
                if (result.Token is { } token)
                {
                    ApplyViewportActions(_viewportController.OnRestoreConfirmed(token, result.Generation));
                }
                break;

            case TranscriptProjectionRestoreResultKind.Unavailable:
                ApplyViewportActions(_viewportController.OnRestoreUnavailable(
                    result.ConversationId ?? CurrentViewportConversationId,
                    result.Generation,
                    ViewModel.MessageHistory.Count > 0));
                break;

            case TranscriptProjectionRestoreResultKind.Abandoned:
                ApplyViewportActions(_viewportController.OnRestoreAbandoned(
                    result.ConversationId ?? CurrentViewportConversationId,
                    result.Generation));
                break;
        }
    }

    private void RequestScrollToEnd()
    {
        if (_transcriptViewportHost is not null && ViewModel.MessageHistory.Count > 0)
        {
            _transcriptViewportHost.ScrollToEnd();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId))
        {
            ResetAutoScrollStateForConversationChange();
            _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
            ApplyCurrentViewportStateIfAttached();
            UpdateTranscriptViewportAutomationState();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.IsSessionActive))
        {
            ResetAutoScrollStateForConversationChange();
            _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
            ApplyCurrentViewportStateIfAttached();
            UpdateTranscriptViewportAutomationState();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.MessageHistory))
        {
            EnsureMessageTracking();
            ResetAutoScrollStateForConversationChange();
            ApplyCurrentViewportStateIfAttached();
            UpdateTranscriptViewportAutomationState();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.IsActivationOverlayVisible))
        {
            HandleOverlayVisibilityChanged();
            UpdateTranscriptViewportAutomationState();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.PresentedSessionHeaderDisplayName))
        {
            Bindings.Update();
            return;
        }
    }

    private void ApplyCurrentViewportState()
    {
        var actions = _viewportController.OnViewportChanged(CreateViewportViewState());
        ApplyViewportActions(actions);
    }

    private void ApplyCurrentViewportStateIfAttached()
    {
        if (!IsViewportDetachedByUser())
        {
            ApplyCurrentViewportState();
        }
    }

    private void IssueNativeTranscriptScrollRequest(TranscriptScrollRequestToken requestToken)
    {
        if (_transcriptViewportHost is null
            || ViewModel.MessageHistory.Count <= 0
            || !_viewportController.MatchesActiveScrollRequest(requestToken))
        {
            return;
        }

        if (_queuedNativeTranscriptScrollRequestToken == requestToken)
        {
            return;
        }

        _queuedNativeTranscriptScrollRequestToken = requestToken;
        RequestScrollToEnd();

        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (_queuedNativeTranscriptScrollRequestToken == requestToken)
            {
                _queuedNativeTranscriptScrollRequestToken = null;
            }

            if (!_isViewLoaded
                || _transcriptViewportHost is null
                || ViewModel.MessageHistory.Count <= 0
                || !_viewportController.MatchesActiveScrollRequest(requestToken))
            {
                return;
            }

            RequestScrollToEnd();
            ScheduleTranscriptScrollRequestObservation(requestToken);
        }))
        {
            _queuedNativeTranscriptScrollRequestToken = null;
        }
    }

    private void ScheduleTranscriptScrollRequestObservation(TranscriptScrollRequestToken requestToken)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isViewLoaded
                || _transcriptViewportHost is null
                || ViewModel.MessageHistory.Count <= 0
                || !_viewportController.MatchesActiveScrollRequest(requestToken))
            {
                return;
            }

            ApplyViewportActions(_viewportController.OnActiveScrollObservation());
            ApplyCurrentViewportState();
            TryRefreshViewportCoordinatorFromView();
            UpdateTranscriptViewportAutomationState();
        });
    }

    private bool IsListViewportAtBottom()
    {
        if (_transcriptViewportHost is null)
        {
            return false;
        }

        var itemCount = ViewModel.MessageHistory.Count;
        if (itemCount <= 0)
        {
            return true;
        }

        return _transcriptViewportHost.IsAtBottom(itemCount, BottomThreshold, BottomGeometryTolerance);
    }

    private void ResetAutoScrollStateForConversationChange()
    {
        AbandonPendingProjectionRestore("ConversationChanged");
        ClearPendingProjectionRestore();
        if (ViewModel.IsActivationOverlayVisible)
        {
            _resumeViewportCoordinatorAfterOverlayPending = true;
            UpdateTranscriptViewportAutomationState();
            return;
        }

        if (_resumeViewportCoordinatorAfterOverlayPending)
        {
            ResumeViewportCoordinatorAfterOverlayIfNeeded();
            UpdateTranscriptViewportAutomationState();
            return;
        }

        _resumeViewportCoordinatorAfterOverlayPending = false;
        ApplyViewportActions(_viewportController.OnConversationChanged(
            CurrentViewportConversationId,
            ViewModel.IsSessionActive,
            ViewModel.IsActivationOverlayVisible,
            ViewModel.MessageHistory.Count > 0));
        UpdateTranscriptViewportAutomationState();
    }

    private void HandleOverlayVisibilityChanged()
    {
        var isOverlayVisible = ViewModel.IsActivationOverlayVisible;
        var overlayJustDismissed = _wasOverlayVisible && !isOverlayVisible;
        _wasOverlayVisible = isOverlayVisible;

        if (isOverlayVisible)
        {
            _resumeViewportCoordinatorAfterOverlayPending = true;
            ApplyViewportActions(_viewportController.SuspendForOverlay());
            UpdateTranscriptViewportAutomationState();
            return;
        }

        if (!overlayJustDismissed)
        {
            return;
        }

        ResumeViewportCoordinatorAfterOverlayIfNeeded();
    }

    private void ResumeViewportCoordinatorAfterOverlayIfNeeded()
    {
        if (!_resumeViewportCoordinatorAfterOverlayPending
            || ViewModel.IsActivationOverlayVisible
            || !_isViewLoaded
            || !ViewModel.IsSessionActive
            || _transcriptViewportHost is null
            || string.IsNullOrWhiteSpace(ViewModel.CurrentSessionId))
        {
            return;
        }

        _resumeViewportCoordinatorAfterOverlayPending = false;
        ActivateViewportForCurrentSession(TranscriptViewportActivationKind.OverlayResume);
        TryApplyPendingProjectionRestore();
        ApplyCurrentViewportStateIfAttached();
        TryRefreshViewportCoordinatorFromView();
        UpdateTranscriptViewportAutomationState();
    }

    private void RestoreViewportForWarmResume()
    {
        if (!_isViewLoaded
            || !ViewModel.IsSessionActive
            || ViewModel.IsActivationOverlayVisible
            || string.IsNullOrWhiteSpace(ViewModel.CurrentSessionId))
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isViewLoaded
                || !ViewModel.IsSessionActive
                || ViewModel.IsActivationOverlayVisible
                || string.IsNullOrWhiteSpace(ViewModel.CurrentSessionId))
            {
                return;
            }

            ActivateViewportForCurrentSession(TranscriptViewportActivationKind.WarmReturn);
            TryApplyPendingProjectionRestore();
            ApplyCurrentViewportStateIfAttached();
            TryRefreshViewportCoordinatorFromView();
            UpdateTranscriptViewportAutomationState();
        });
    }

    private void UpdateTranscriptViewportAutomationState()
    {
        var state = ResolveTranscriptViewportAutomationState();
        UpdateTranscriptViewportDebugProbe(state);
        if (TranscriptViewportStateProbe is not null)
        {
            TranscriptViewportStateProbe.Text = state;
            AutomationProperties.SetName(TranscriptViewportStateProbe, state);
        }

        if (string.Equals(_transcriptViewportAutomationState, state, StringComparison.Ordinal))
        {
            return;
        }

        _transcriptViewportAutomationState = state;
    }

    private void UpdateTranscriptViewportDebugProbe(string state)
    {
        if (TranscriptViewportDebugProbe is null)
        {
            return;
        }

        var conversationState = string.IsNullOrWhiteSpace(CurrentViewportConversationId)
            ? null
            : _viewportController.GetConversationState(CurrentViewportConversationId);
        var debug = $"state={state};coord={_viewportController.State};attached={_viewportController.IsAutoFollowAttached};current={CurrentViewportConversationId};generation={_viewportController.Generation};scrollIntentPending={_viewportController.UserScrollIntentPending};scrollIntentCompleted={_viewportController.UserScrollIntentCompleted};restoreConversation={_projectionRestoreController.PendingConversationId ?? "<none>"};restoreGeneration={_projectionRestoreController.PendingGeneration};restoreToken={(conversationState?.RestoreToken?.ProjectionItemKey ?? "<none>")}";
        TranscriptViewportDebugProbe.Text = debug;
        AutomationProperties.SetName(TranscriptViewportDebugProbe, debug);
    }

    private string ResolveTranscriptViewportAutomationState()
    {
        if (!_isViewLoaded || !ViewModel.IsSessionActive)
        {
            return "inactive";
        }

        if (ViewModel.IsActivationOverlayVisible)
        {
            return "loading";
        }

        if (ViewModel.MessageHistory.Count == 0)
        {
            return "empty";
        }

        return _viewportController.State switch
        {
            TranscriptViewportState.Following => "bottom",
            TranscriptViewportState.DetachedByUser => "not_bottom",
            TranscriptViewportState.Suspended => "loading",
            _ => "untracked",
        };
    }

}

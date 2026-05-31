using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SalmonEgg.Controls;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Transcript;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using SalmonEgg.Presentation.Core.Services.Input;
using Windows.Foundation;
using XamlFocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace SalmonEgg.Presentation.Views.Chat;

public sealed partial class ChatView : Page, INavigationIntentConsumer, IPrimaryContentFocusTarget
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
        private bool _restoreDetachedViewportAfterOverlay;
        private string? _restoreDetachedViewportConversationId;
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
            _restoreDetachedViewportAfterOverlay = false;
            _restoreDetachedViewportConversationId = null;
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
            BeginLayoutLoadingIfPendingMessages();
            TryIssueTranscriptScrollRequestIfAttached();
            UpdateTranscriptViewportAutomationState();
            try
            {
                await ViewModel.EnsureAcpProfilesLoadedAsync();
            }
            catch
            {
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            AbandonPendingProjectionRestore("ViewUnloaded");
            ApplyViewportActions(_viewportController.Unload());
            _isViewLoaded = false;
            _restoreDetachedViewportAfterOverlay = false;
            _restoreDetachedViewportConversationId = null;
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

            BeginLayoutLoadingIfPendingMessages();
            TryApplyPendingProjectionRestore();
            ApplyViewportActions(_viewportController.OnMessagesAppended(
                e.NewItems?.Count ?? 0,
                CreateViewportViewState()));
            RefreshLayoutLoadingState(HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));

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
            BeginLayoutLoadingIfPendingMessages();
            TryApplyPendingTranscriptMessageFocus();
            TryApplyPendingProjectionRestore();
            TryIssueTranscriptScrollRequest();
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
            var lastItemContainerGenerated = HasLastItemContainerGenerated(ViewModel.MessageHistory.Count);
            RefreshLayoutLoadingState(lastItemContainerGenerated);

            TryApplyPendingTranscriptMessageFocus();
            TryApplyPendingProjectionRestore();
            ApplyViewportActions(_viewportController.OnViewportChanged(
                CreateViewportViewState(lastItemContainerGenerated),
                TryCaptureProjectionRestoreToken()));
            RefreshLayoutLoadingState(lastItemContainerGenerated);
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
                _viewportController.MarkDetachedViewportInteractionStarted();
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
            if (FindAncestorOrSelf<ListViewItem>(current) is ListViewItem itemContainer
                && IsDescendantOf(itemContainer, MessagesList))
            {
                return false;
            }

            while (current is not null)
            {
                if (ReferenceEquals(current, MessagesList))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private bool IsTranscriptMessageLayerFocusWithin()
        {
            if (MessagesList?.XamlRoot is null)
            {
                return false;
            }

            var current = XamlFocusManager.GetFocusedElement(MessagesList.XamlRoot) as DependencyObject;
            return FindAncestorOrSelf<ListViewItem>(current) is ListViewItem itemContainer
                && IsDescendantOf(itemContainer, MessagesList);
        }

        private static Control? FindFirstInteractiveTranscriptChild(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is Control control
                    && control is not ListViewItem
                    && control.Visibility == Visibility.Visible
                    && control.IsEnabled
                    && control.IsTabStop)
                {
                    return control;
                }

                var nested = FindFirstInteractiveTranscriptChild(child);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }

        private bool TryMoveFocusFromTranscriptMessageToChildControl()
        {
            if (MessagesList?.XamlRoot is null)
            {
                return false;
            }

            var current = XamlFocusManager.GetFocusedElement(MessagesList.XamlRoot) as DependencyObject;
            var focusedItemContainer = FindAncestorOrSelf<ListViewItem>(current);
            if (focusedItemContainer is null || !IsDescendantOf(focusedItemContainer, MessagesList))
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
            var focusedItemContainer = FindAncestorOrSelf<ListViewItem>(current);
            var currentIndex = -1;
            if (focusedItemContainer is not null && IsDescendantOf(focusedItemContainer, MessagesList))
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

            if (MessagesList.ContainerFromIndex(targetIndex) is not ListViewItem targetContainer)
            {
                if (MessagesList.Items[targetIndex] is not object item)
                {
                    return false;
                }

                _pendingTranscriptMessageFocusIndex = targetIndex;
                MessagesList.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
                _activeTranscriptMessageAnchorItem = item;
                _isTranscriptViewportLayerActive = false;
                _isTranscriptChildControlLayerActive = false;
                _ = DispatcherQueue.TryEnqueue(TryApplyPendingTranscriptMessageFocus);
                return true;
            }

            _pendingTranscriptMessageFocusIndex = null;
            _activeTranscriptMessageAnchorItem = MessagesList.ItemFromContainer(targetContainer);
            _isTranscriptViewportLayerActive = false;
            _isTranscriptChildControlLayerActive = false;
            return TryFocusTranscriptNavigationTarget(targetContainer);
        }

        private void TryApplyPendingTranscriptMessageFocus()
        {
            if (_pendingTranscriptMessageFocusIndex is not int pendingIndex
                || MessagesList?.ContainerFromIndex(pendingIndex) is not ListViewItem pendingContainer)
            {
                return;
            }

            if (TryFocusTranscriptNavigationTarget(pendingContainer))
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
            var focusedItemContainer = FindAncestorOrSelf<ListViewItem>(current);
            if (focusedItemContainer is null || !IsDescendantOf(focusedItemContainer, MessagesList))
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

            if (MessagesList.ContainerFromItem(_activeTranscriptMessageAnchorItem) is not ListViewItem container)
            {
                return false;
            }

            return TryFocusTranscriptNavigationTarget(container);
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

        private static T? FindAncestorOrSelf<T>(DependencyObject? start)
            where T : class
        {
            var current = start;
            while (current is not null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return default;
        }

        private static bool IsDescendantOf(DependencyObject? current, DependencyObject ancestor)
        {
            var node = current;
            while (node is not null)
            {
                if (ReferenceEquals(node, ancestor))
                {
                    return true;
                }

                node = VisualTreeHelper.GetParent(node);
            }

            return false;
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
            RefreshLayoutLoadingState(HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
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

        private void OnProjectionRestoreReady(object? sender, ProjectionRestoreReadyEventArgs e)
        {
            ApplyViewportActions(_viewportController.OnProjectionReady(e.ConversationId, e.ProjectionEpoch));
            TryApplyPendingProjectionRestore();
            UpdateTranscriptViewportAutomationState();
        }

        private void TryRefreshViewportCoordinatorFromView(bool? lastItemContainerGenerated = null)
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
                CreateViewportViewState(lastItemContainerGenerated),
                TryCaptureProjectionRestoreToken()));
        }

        private TranscriptViewportViewState CreateViewportViewState(bool? lastItemContainerGenerated = null)
        {
            var messageCount = ViewModel.MessageHistory.Count;
            var hasMessages = messageCount > 0;
            return new TranscriptViewportViewState(
                IsViewReady: _isViewLoaded
                    && _transcriptViewportHost is not null
                    && !ViewModel.IsActivationOverlayVisible
                    && ViewModel.IsSessionActive
                    && !string.IsNullOrWhiteSpace(ViewModel.CurrentSessionId),
                IsViewportReady: hasMessages && (lastItemContainerGenerated ?? HasLastItemContainerGenerated(messageCount)),
                HasMessages: hasMessages,
                IsAtBottom: IsListViewportAtBottom(),
                IsLastItemVisibleAtBottom: hasMessages && IsLastItemVisiblyAtBottom(messageCount));
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
                case TranscriptViewportControllerActionKind.ScrollLastMessageIntoView:
                    if (action.ScrollRequestToken.Generation >= 0)
                    {
                        IssueNativeTranscriptScrollRequest(action.ScrollRequestToken);
                    }
                    else
                    {
                        RequestScrollToBottom();
                    }
                    break;

                case TranscriptViewportControllerActionKind.RequestRestore:
                    if (action.RestoreToken is { } restoreToken)
                    {
                        QueueProjectionOwnedRestore(restoreToken, action.Generation);
                    }
                    break;

                case TranscriptViewportControllerActionKind.StopProgrammaticScroll:
                    ClearPendingProjectionRestore();
                    break;

                case TranscriptViewportControllerActionKind.AutoFollowDetached:
                    ClearPendingProjectionRestore();
                    break;

                case TranscriptViewportControllerActionKind.AutoFollowAttached:
                    ClearPendingProjectionRestore();
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
        {
            return _viewportController.State is TranscriptViewportState.DetachedByUser
                or TranscriptViewportState.DetachedPendingRestore
                or TranscriptViewportState.DetachedRestoring;
        }

        private string CurrentViewportConversationId => ViewModel.CurrentSessionId ?? string.Empty;

        private void QueueProjectionOwnedRestore(TranscriptProjectionRestoreToken token, int generation)
        {
            _projectionRestoreController.Queue(token, generation);
            _viewportController.MarkProjectionRestoreQueued();
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
                        result.Reason ?? "RestoreUnavailable"));
                    break;

                case TranscriptProjectionRestoreResultKind.Abandoned:
                    ApplyViewportActions(_viewportController.OnRestoreAbandoned(
                        result.ConversationId ?? CurrentViewportConversationId,
                        result.Generation,
                        result.Reason ?? "RestoreAbandoned"));
                    break;
            }
        }

        private void RequestScrollToBottom()
        {
            if (_transcriptViewportHost is not null && ViewModel.MessageHistory.Count > 0)
            {
                _transcriptViewportHost.ScrollItemIntoView(
                    ViewModel.MessageHistory.Count - 1,
                    TranscriptItemScrollAlignment.Leading);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId))
            {
                ResetAutoScrollStateForConversationChange();
                _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
                TryIssueTranscriptScrollRequestIfAttached();
                BeginLayoutLoadingIfPendingMessages();
                UpdateTranscriptViewportAutomationState();
                return;
            }

            if (e.PropertyName == nameof(ChatViewModel.IsSessionActive))
            {
                if (_viewportController.State is TranscriptViewportState.DetachedPendingRestore
                    or TranscriptViewportState.DetachedRestoring)
                {
                    BeginLayoutLoadingIfPendingMessages();
                    TryApplyPendingProjectionRestore();
                    TryRefreshViewportCoordinatorFromView();
                    UpdateTranscriptViewportAutomationState();
                    return;
                }

                ResetAutoScrollStateForConversationChange();
                _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
                TryIssueTranscriptScrollRequestIfAttached();
                BeginLayoutLoadingIfPendingMessages();
                UpdateTranscriptViewportAutomationState();
                return;
            }

            if (e.PropertyName == nameof(ChatViewModel.MessageHistory))
            {
                EnsureMessageTracking();
                if (_viewportController.State is TranscriptViewportState.DetachedPendingRestore
                    or TranscriptViewportState.DetachedRestoring)
                {
                    BeginLayoutLoadingIfPendingMessages();
                    TryApplyPendingProjectionRestore();
                    TryRefreshViewportCoordinatorFromView();
                    UpdateTranscriptViewportAutomationState();
                    return;
                }

                ResetAutoScrollStateForConversationChange();
                TryIssueTranscriptScrollRequestIfAttached();
                BeginLayoutLoadingIfPendingMessages();
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

        private void BeginLayoutLoadingIfPendingMessages()
        {
            RefreshLayoutLoadingState();
        }

        private void RefreshLayoutLoadingState(bool lastItemContainerGenerated = false)
        {
            ViewModel.IsLayoutLoading = InitialLayoutLoadingPolicy.ShouldKeepLoading(
                isSessionActive: ViewModel.IsSessionActive,
                messageCount: ViewModel.MessageHistory.Count,
                hasPendingInitialScroll: _viewportController.HasPendingSettle,
                lastItemContainerGenerated: lastItemContainerGenerated,
                isHydrating: ViewModel.IsHydrating,
                isRemoteHydrationPending: ViewModel.IsRemoteHydrationPending);
            UpdateTranscriptViewportAutomationState();
        }

        private bool TryIssueTranscriptScrollRequest()
        {
            var actions = _viewportController.OnViewportChanged(CreateViewportViewState());
            ApplyViewportActions(actions);
            RefreshLayoutLoadingState(HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
            UpdateTranscriptViewportAutomationState();
            return actions.Count > 0;
        }

        private void TryIssueTranscriptScrollRequestIfAttached()
        {
            if (!IsViewportDetachedByUser())
            {
                TryIssueTranscriptScrollRequest();
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

            RequestScrollToBottom();

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isViewLoaded
                    || _transcriptViewportHost is null
                    || ViewModel.MessageHistory.Count <= 0
                    || !_viewportController.MatchesActiveScrollRequest(requestToken))
                {
                    return;
                }

                RequestScrollToBottom();
                ScheduleTranscriptScrollRequestObservation(requestToken);
            });
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

                var lastItemContainerGenerated = HasLastItemContainerGenerated(ViewModel.MessageHistory.Count);
                ObserveActiveTranscriptScrollFromLayout(lastItemContainerGenerated);
                TryIssueTranscriptScrollRequest();
                TryRefreshViewportCoordinatorFromView(lastItemContainerGenerated);
                UpdateTranscriptViewportAutomationState();
            });
        }

        private bool HasLastItemContainerGenerated(int itemCount)
        {
            if (_transcriptViewportHost is null || itemCount <= 0)
            {
                return false;
            }

            return _transcriptViewportHost.HasRealizedItem(itemCount - 1);
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

        private bool ObserveActiveTranscriptScrollFromLayout(bool? lastItemContainerGenerated = null)
        {
            var actions = _viewportController.OnActiveScrollObservation(CreateViewportViewState(lastItemContainerGenerated));
            ApplyViewportActions(actions);
            return actions.Count > 0;
        }

        private bool IsLastItemVisiblyAtBottom(int itemCount)
        {
            if (_transcriptViewportHost is null || itemCount <= 0)
            {
                return false;
            }

            return _transcriptViewportHost.IsLastItemVisiblyAtBottom(itemCount, BottomThreshold, BottomGeometryTolerance);
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

            _restoreDetachedViewportAfterOverlay = false;
            _restoreDetachedViewportConversationId = null;
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
                if (IsViewportDetachedByUser())
                {
                    _restoreDetachedViewportAfterOverlay = true;
                    _restoreDetachedViewportConversationId = CurrentViewportConversationId;
                }
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
            if (_restoreDetachedViewportAfterOverlay
                && !string.Equals(_restoreDetachedViewportConversationId, CurrentViewportConversationId, StringComparison.Ordinal))
            {
                _restoreDetachedViewportAfterOverlay = false;
                _restoreDetachedViewportConversationId = null;
            }

            _restoreDetachedViewportAfterOverlay = false;
            _restoreDetachedViewportConversationId = null;
            ActivateViewportForCurrentSession(TranscriptViewportActivationKind.OverlayResume);
            TryApplyPendingProjectionRestore();
            TryIssueTranscriptScrollRequestIfAttached();
            TryRefreshViewportCoordinatorFromView();
            RefreshLayoutLoadingState(HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
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
                TryIssueTranscriptScrollRequestIfAttached();
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

            var transition = _viewportController.LastTransition;
            var conversationState = string.IsNullOrWhiteSpace(CurrentViewportConversationId)
                ? null
                : _viewportController.GetConversationState(CurrentViewportConversationId);
            var debug = $"state={state};coord={_viewportController.State};attached={_viewportController.IsAutoFollowAttached};current={CurrentViewportConversationId};generation={_viewportController.Generation};transition={(transition?.Reason ?? "<none>")};attachPending={_viewportController.AttachToBottomIntentPending};scrollIntentPending={_viewportController.UserScrollIntentPending};scrollIntentCompleted={_viewportController.UserScrollIntentCompleted};restoreConversation={_projectionRestoreController.PendingConversationId ?? "<none>"};restoreGeneration={_projectionRestoreController.PendingGeneration};restoreToken={(conversationState?.RestoreToken?.ProjectionItemKey ?? "<none>")}";
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
                TranscriptViewportState.Idle => "untracked",
                TranscriptViewportState.Settling => "pending",
                TranscriptViewportState.Following => "bottom",
                TranscriptViewportState.DetachedByUser => "not_bottom",
                TranscriptViewportState.DetachedPendingRestore => "pending",
                TranscriptViewportState.DetachedRestoring => "pending",
                TranscriptViewportState.Suspended => "loading",
                _ => "untracked",
            };
        }

}

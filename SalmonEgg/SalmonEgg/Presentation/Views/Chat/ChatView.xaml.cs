using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;

namespace SalmonEgg.Presentation.Views.Chat
{
    public sealed partial class ChatView : Page
    {
        public ChatShellViewModel ShellViewModel { get; }
        public ChatViewModel ViewModel => ShellViewModel.Chat;
        public ShellLayoutViewModel LayoutVM => ShellViewModel.ShellLayout;
        public UiMotion Motion => UiMotion.Current;
        private bool _isViewLoaded;
        private bool _isTrackingMessages;
        private readonly InitialScrollGate _initialScrollGate = new();
        private bool _userScrolledUp;
        private const double BottomThreshold = 10;
        private const int MaxInitialScrollAttempts = 8;
        private ScrollViewer? _scrollViewer;
        private bool _suspendAutoScrollTracking;

        public ChatView()
        {
            ShellViewModel = App.ServiceProvider.GetRequiredService<ChatShellViewModel>();

            this.InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isViewLoaded = true;
            _userScrolledUp = false;
            _initialScrollGate.MarkPending();
            EnsureMessageTracking();
            BeginLayoutLoadingIfPendingMessages();
            RequestInitialScroll();
            try
            {
                // Restore may already be running from the singleton VM; calling again is safe.
                await ViewModel.RestoreConversationsAsync();
                await ViewModel.EnsureAcpProfilesLoadedAsync();
            }
            catch
            {
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _isViewLoaded = false;
            DetachScrollViewer();
            _initialScrollGate.CancelInFlight();
            if (_isTrackingMessages)
            {
                ViewModel.MessageHistory.CollectionChanged -= OnMessageHistoryChanged;
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _isTrackingMessages = false;
            }
        }

        private void EnsureMessageTracking()
        {
            if (_isTrackingMessages)
            {
                return;
            }

            ViewModel.MessageHistory.CollectionChanged += OnMessageHistoryChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _isTrackingMessages = true;
        }

        private void OnMessageHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_isViewLoaded)
            {
                return;
            }

            if (ViewModel.IsSessionActive && ViewModel.MessageHistory.Count > 0 && !_initialScrollGate.HasPending)
            {
                _initialScrollGate.MarkPending();
            }

            BeginLayoutLoadingIfPendingMessages();

            if (_initialScrollGate.HasPending && _userScrolledUp)
            {
                _initialScrollGate.ClearPending();
                RefreshLayoutLoadingState();
                return;
            }

            if (RequestInitialScroll())
            {
                return;
            }

            if (!_userScrolledUp)
            {
                RequestScrollToBottom();
            }
        }

        private void OnMessagesListLoaded(object sender, RoutedEventArgs e)
        {
            DetachScrollViewer();
            _scrollViewer = FindScrollViewer(MessagesList);
            if (_scrollViewer != null)
            {
                _scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
                _scrollViewer.PointerPressed += ScrollViewer_PointerPressed;
                _scrollViewer.PointerWheelChanged += ScrollViewer_PointerWheelChanged;
            }

            BeginLayoutLoadingIfPendingMessages();
            RequestInitialScroll();
        }

        private void OnMessagesListLayoutUpdated(object? sender, object e)
        {
            var lastItemContainerGenerated = HasLastItemContainerGenerated(ViewModel.MessageHistory.Count);
            RefreshLayoutLoadingState(lastItemContainerGenerated);

            if (!_initialScrollGate.HasPending || _userScrolledUp)
            {
                return;
            }

            if (TryCompletePendingInitialScroll(lastItemContainerGenerated))
            {
                return;
            }

            if (lastItemContainerGenerated)
            {
                RequestInitialScroll();
            }
        }

        private void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_scrollViewer == null) return;

            // Only evaluate at-bottom when the scroll has settled (not intermediate).
            // During virtualization layout changes, intermediate frames have transient
            // ScrollableHeight values that cause false at-bottom detection.
            if (e.IsIntermediate)
            {
                return;
            }

            if (_initialScrollGate.HasPending && !_userScrolledUp && TryCompletePendingInitialScroll())
            {
                return;
            }

            if (_suspendAutoScrollTracking)
            {
                return;
            }

            _userScrolledUp = !IsScrollViewerAtBottom();
        }

        private void ScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            StopInitialScrollForManualInteraction();
        }

        private void ScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            StopInitialScrollForManualInteraction();

            var point = e.GetCurrentPoint(_scrollViewer);
            if (point.Properties.MouseWheelDelta > 0)
            {
                _userScrolledUp = true;
            }
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject element)
        {
            if (element is ScrollViewer sv) return sv;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }

            return null;
        }

        private void RequestScrollToBottom()
        {
            if (MessagesList != null && ViewModel.MessageHistory.Count > 0)
            {
                MessagesList.ScrollIntoView(ViewModel.MessageHistory.Last());
            }

            if (_scrollViewer != null)
            {
                _scrollViewer.ChangeView(null, _scrollViewer.ScrollableHeight, null);
                return;
            }

            // Fallback: use internal ScrollIntoView if ScrollViewer is not found yet.
            if (MessagesList != null && ViewModel.MessageHistory.Count > 0)
            {
                MessagesList.ScrollIntoView(ViewModel.MessageHistory.Last());
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId)
                || e.PropertyName == nameof(ChatViewModel.IsSessionActive))
            {
                _initialScrollGate.MarkPending();
                BeginLayoutLoadingIfPendingMessages();
                RequestInitialScroll();
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
                hasPendingInitialScroll: _initialScrollGate.HasPending,
                lastItemContainerGenerated: lastItemContainerGenerated);
        }

        private bool RequestInitialScroll(int attempt = 0)
        {
            if (!_isViewLoaded || MessagesList is null)
            {
                return false;
            }

            if (ViewModel.MessageHistory.Count <= 0)
            {
                _initialScrollGate.ClearPending();
                RefreshLayoutLoadingState();
                return false;
            }

            if (!_initialScrollGate.TrySchedule(ViewModel.MessageHistory.Count))
            {
                return false;
            }

            var requestGeneration = _initialScrollGate.Generation;

            if (!DispatcherQueue.TryEnqueue(() => ExecuteInitialScrollAttempt(requestGeneration, attempt)))
            {
                _initialScrollGate.CancelInFlight();
                return false;
            }

            return true;
        }

        private void ExecuteInitialScrollAttempt(int requestGeneration, int attempt)
        {
            if (!_isViewLoaded || MessagesList is null || requestGeneration != _initialScrollGate.Generation)
            {
                _initialScrollGate.CancelInFlight();
                return;
            }

            var count = ViewModel.MessageHistory.Count;
            if (count <= 0)
            {
                _initialScrollGate.ClearPending();
                RefreshLayoutLoadingState();
                return;
            }

            if (_userScrolledUp)
            {
                _initialScrollGate.ClearPending();
                RefreshLayoutLoadingState();
                return;
            }

            _suspendAutoScrollTracking = true;
            var reachedBottom = TryScrollInitialLoadToBottom(count);

            if (requestGeneration != _initialScrollGate.Generation)
            {
                _initialScrollGate.CancelInFlight();
                return;
            }

            var outcome = InitialScrollAttemptPolicy.Decide(
                hasMessages: count > 0,
                autoScrollEnabled: !_userScrolledUp,
                reachedBottom: reachedBottom,
                attempt: attempt,
                maxAttempts: MaxInitialScrollAttempts);

            switch (outcome)
            {
                case InitialScrollAttemptOutcome.Complete:
                    _ = _initialScrollGate.TryComplete(true);
                    ReleaseAutoScrollTracking();
                    break;
                case InitialScrollAttemptOutcome.Retry:
                    _initialScrollGate.CancelInFlight();
                    _userScrolledUp = false;
                    RequestInitialScroll(attempt + 1);
                    break;
                default:
                    _initialScrollGate.CancelInFlight();
                    RefreshLayoutLoadingState(HasLastItemContainerGenerated(count));
                    break;
            }
        }

        private bool TryScrollInitialLoadToBottom(int itemCount)
        {
            if (MessagesList is null || _scrollViewer is null)
            {
                return false;
            }

            MessagesList.ScrollIntoView(ViewModel.MessageHistory[itemCount - 1]);

            // Avoid synchronous UpdateLayout(); rely on virtualizer's async layout pass
            // and the retry mechanism in InitialScrollAttemptPolicy.
            _scrollViewer.ChangeView(null, _scrollViewer.ScrollableHeight, null);
            return IsInitialScrollReadyAndAtBottom(itemCount);
        }

        private bool IsInitialScrollReadyAndAtBottom(int itemCount)
        {
            if (MessagesList is null || itemCount <= 0)
            {
                return false;
            }

            if (!HasLastItemContainerGenerated(itemCount))
            {
                return false;
            }

            return IsScrollViewerAtBottom();
        }

        private bool HasLastItemContainerGenerated(int itemCount)
        {
            if (MessagesList is null || itemCount <= 0)
            {
                return false;
            }

            return MessagesList.ContainerFromIndex(itemCount - 1) is not null;
        }

        private bool IsScrollViewerAtBottom()
        {
            if (_scrollViewer is null)
            {
                return false;
            }

            return _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - BottomThreshold;
        }

        private bool TryCompletePendingInitialScroll(bool? lastItemContainerGenerated = null)
        {
            if (!_initialScrollGate.HasPending || MessagesList is null || _scrollViewer is null)
            {
                return false;
            }

            var itemCount = ViewModel.MessageHistory.Count;
            if (itemCount <= 0)
            {
                return false;
            }

            var hasLastItemContainer = lastItemContainerGenerated ?? HasLastItemContainerGenerated(itemCount);
            if (!hasLastItemContainer || !IsScrollViewerAtBottom())
            {
                return false;
            }

            _ = _initialScrollGate.TryComplete(true);
            ReleaseAutoScrollTracking();
            RefreshLayoutLoadingState(true);
            return true;
        }

        private void DetachScrollViewer()
        {
            if (_scrollViewer is null)
            {
                return;
            }

            _scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
            _scrollViewer.PointerPressed -= ScrollViewer_PointerPressed;
            _scrollViewer.PointerWheelChanged -= ScrollViewer_PointerWheelChanged;
            _scrollViewer = null;
        }


        private void StopInitialScrollForManualInteraction()
        {
            if (!_initialScrollGate.HasPending)
            {
                return;
            }

            _suspendAutoScrollTracking = false;
            _userScrolledUp = true;
            _initialScrollGate.ClearPending();
            RefreshLayoutLoadingState();
        }

        private void ReleaseAutoScrollTracking()
        {
            _ = DispatcherQueue.TryEnqueue(() => _suspendAutoScrollTracking = false);
        }

        private void OnSessionNameClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel.BeginEditSessionNameCommand.CanExecute(null))
            {
                ViewModel.BeginEditSessionNameCommand.Execute(null);

                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    SessionNameEditor.Focus(FocusState.Programmatic);
                    SessionNameEditor.SelectAll();
                });
            }
        }

        private void OnSessionNameEditorLostFocus(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsEditingSessionName)
            {
                return;
            }

            if (ViewModel.CommitSessionNameEditCommand.CanExecute(null))
            {
                ViewModel.CommitSessionNameEditCommand.Execute(null);
            }
        }

        private void OnSessionNameEditorKeyDown(object sender, KeyRoutedEventArgs e)
        {
            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    if (ViewModel.CommitSessionNameEditCommand.CanExecute(null))
                    {
                        ViewModel.CommitSessionNameEditCommand.Execute(null);
                        e.Handled = true;
                    }
                    break;
                case Windows.System.VirtualKey.Escape:
                    if (ViewModel.CancelSessionNameEditCommand.CanExecute(null))
                    {
                        ViewModel.CancelSessionNameEditCommand.Execute(null);
                        e.Handled = true;
                    }
                    break;
            }
        }
    }
}

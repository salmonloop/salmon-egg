using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using Windows.Foundation;

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
        private bool _suspendAutoScrollTracking;
        private bool _manualScrollIntentPending;
        private bool _wasOverlayVisible;
        private bool _scrollToBottomScheduled;
        private int _scrollScheduleGeneration;
        private string _transcriptViewportAutomationState = "inactive";
        private ObservableCollection<ChatMessageViewModel>? _trackedMessageHistory;

        public ChatView()
        {
            ShellViewModel = App.ServiceProvider.GetRequiredService<ChatShellViewModel>();
            NavigationCacheMode = NavigationCacheMode.Required;

            this.InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isViewLoaded = true;
            unchecked { _scrollScheduleGeneration++; }
            _userScrolledUp = false;
            _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
            _initialScrollGate.MarkPending();
            EnsureMessageTracking();
            BeginLayoutLoadingIfPendingMessages();
            RequestInitialScroll();
            RestoreViewportForWarmResume();
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
            _isViewLoaded = false;
            unchecked { _scrollScheduleGeneration++; }
            _scrollToBottomScheduled = false;
            _initialScrollGate.CancelInFlight();
            UpdateTranscriptViewportAutomationState();
            if (_isTrackingMessages)
            {
                if (_trackedMessageHistory != null)
                {
                    _trackedMessageHistory.CollectionChanged -= OnMessageHistoryChanged;
                    _trackedMessageHistory = null;
                }
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
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
                    _trackedMessageHistory.CollectionChanged += OnMessageHistoryChanged;
                }

                return;
            }

            _trackedMessageHistory = ViewModel.MessageHistory;
            _trackedMessageHistory.CollectionChanged += OnMessageHistoryChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _isTrackingMessages = true;
        }

        private void OnMessageHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_isViewLoaded)
            {
                UpdateTranscriptViewportAutomationState();
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
                ScheduleScrollToBottom();
            }

            UpdateTranscriptViewportAutomationState();
        }

        private void OnMessagesListLoaded(object sender, RoutedEventArgs e)
        {
            BeginLayoutLoadingIfPendingMessages();
            RequestInitialScroll();
            UpdateTranscriptViewportAutomationState();
        }

        private void OnMessagesListUnloaded(object sender, RoutedEventArgs e)
        {
            UpdateTranscriptViewportAutomationState();
        }

        private void OnMessagesListLayoutUpdated(object? sender, object e)
        {
            var lastItemContainerGenerated = HasLastItemContainerGenerated(ViewModel.MessageHistory.Count);
            RefreshLayoutLoadingState(lastItemContainerGenerated);

            if (_manualScrollIntentPending && !_initialScrollGate.HasPending && !_suspendAutoScrollTracking)
            {
                _userScrolledUp = !IsListViewportAtBottom();
                _manualScrollIntentPending = false;
            }

            if (!_initialScrollGate.HasPending || _userScrolledUp)
            {
                UpdateTranscriptViewportAutomationState();
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

            UpdateTranscriptViewportAutomationState();
        }

        private void OnMessagesListPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _manualScrollIntentPending = true;
            StopInitialScrollForManualInteraction();
        }

        private void OnMessagesListPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            _manualScrollIntentPending = true;
            StopInitialScrollForManualInteraction();
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
                _manualScrollIntentPending = true;
                StopInitialScrollForManualInteraction();
            }
        }

        private void RequestScrollToBottom()
        {
            if (MessagesList != null && ViewModel.MessageHistory.Count > 0)
            {
                MessagesList.ScrollIntoView(ViewModel.MessageHistory.Last());
            }
        }

        private void ScheduleScrollToBottom()
        {
            if (_scrollToBottomScheduled)
            {
                return;
            }

            var scheduleGeneration = _scrollScheduleGeneration;
            var scheduledConversationId = ViewModel.CurrentSessionId;
            _scrollToBottomScheduled = true;
            if (!DispatcherQueue.TryEnqueue(() =>
                {
                    _scrollToBottomScheduled = false;
                    if (!_isViewLoaded
                        || scheduleGeneration != _scrollScheduleGeneration
                        || _userScrolledUp
                        || !ViewModel.IsSessionActive
                        || ViewModel.MessageHistory.Count <= 0
                        || !string.Equals(ViewModel.CurrentSessionId, scheduledConversationId, StringComparison.Ordinal))
                    {
                        return;
                    }

                    RequestScrollToBottom();
                }))
            {
                _scrollToBottomScheduled = false;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId)
                || e.PropertyName == nameof(ChatViewModel.IsSessionActive))
            {
                ResetAutoScrollStateForConversationChange();
                _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
                _initialScrollGate.MarkPending();
                BeginLayoutLoadingIfPendingMessages();
                RequestInitialScroll();
                UpdateTranscriptViewportAutomationState();
                return;
            }

            if (e.PropertyName == nameof(ChatViewModel.MessageHistory))
            {
                EnsureMessageTracking();
                ResetAutoScrollStateForConversationChange();
                _initialScrollGate.MarkPending();
                BeginLayoutLoadingIfPendingMessages();
                RequestInitialScroll();
                UpdateTranscriptViewportAutomationState();
                return;
            }

            if (e.PropertyName == nameof(ChatViewModel.IsActivationOverlayVisible))
            {
                HandleOverlayVisibilityChanged();
                UpdateTranscriptViewportAutomationState();
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
                lastItemContainerGenerated: lastItemContainerGenerated,
                isHydrating: ViewModel.IsHydrating,
                isRemoteHydrationPending: ViewModel.IsRemoteHydrationPending);
            UpdateTranscriptViewportAutomationState();
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
                UpdateTranscriptViewportAutomationState();
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
                UpdateTranscriptViewportAutomationState();
                return false;
            }

            return true;
        }

        private void ExecuteInitialScrollAttempt(int requestGeneration, int attempt)
        {
            if (!_isViewLoaded || MessagesList is null || requestGeneration != _initialScrollGate.Generation)
            {
                _initialScrollGate.CancelInFlight();
                UpdateTranscriptViewportAutomationState();
                return;
            }

            var count = ViewModel.MessageHistory.Count;
            if (count <= 0)
            {
                _initialScrollGate.ClearPending();
                RefreshLayoutLoadingState();
                UpdateTranscriptViewportAutomationState();
                return;
            }

            if (_userScrolledUp)
            {
                _initialScrollGate.ClearPending();
                RefreshLayoutLoadingState();
                UpdateTranscriptViewportAutomationState();
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
                    // Stop the initial-load cycle after the retry budget is exhausted.
                    // Leaving the gate pending would let LayoutUpdated restart the whole
                    // sequence from attempt 0 forever and starve the UI thread.
                    _initialScrollGate.ClearPending();
                    ReleaseAutoScrollTracking();
                    RefreshLayoutLoadingState(HasLastItemContainerGenerated(count));
                    UpdateTranscriptViewportAutomationState();
                    break;
            }
        }

        private bool TryScrollInitialLoadToBottom(int itemCount)
        {
            if (MessagesList is null)
            {
                return false;
            }

            MessagesList.ScrollIntoView(ViewModel.MessageHistory[itemCount - 1]);

            // Avoid synchronous UpdateLayout(); rely on virtualizer's async layout pass
            // and the retry mechanism in InitialScrollAttemptPolicy.
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

            return IsListViewportAtBottom();
        }

        private bool HasLastItemContainerGenerated(int itemCount)
        {
            if (MessagesList is null || itemCount <= 0)
            {
                return false;
            }

            return MessagesList.ContainerFromIndex(itemCount - 1) is not null;
        }

        private bool IsListViewportAtBottom()
        {
            if (MessagesList is null)
            {
                return false;
            }

            var itemCount = ViewModel.MessageHistory.Count;
            if (itemCount <= 0)
            {
                return true;
            }

            if (MessagesList.ContainerFromIndex(itemCount - 1) is not FrameworkElement lastItemContainer)
            {
                return false;
            }

            Point relativeOrigin = lastItemContainer.TransformToVisual(MessagesList).TransformPoint(default);
            var lastItemBottom = relativeOrigin.Y + lastItemContainer.ActualHeight;
            var viewportBottom = MessagesList.ActualHeight - BottomThreshold;
            return lastItemBottom <= viewportBottom;
        }

        private bool TryCompletePendingInitialScroll(bool? lastItemContainerGenerated = null)
        {
            if (!_initialScrollGate.HasPending || MessagesList is null)
            {
                return false;
            }

            var itemCount = ViewModel.MessageHistory.Count;
            if (itemCount <= 0)
            {
                return false;
            }

            var hasLastItemContainer = lastItemContainerGenerated ?? HasLastItemContainerGenerated(itemCount);
            if (!hasLastItemContainer || !IsListViewportAtBottom())
            {
                return false;
            }

            _ = _initialScrollGate.TryComplete(true);
            ReleaseAutoScrollTracking();
            RefreshLayoutLoadingState(true);
            UpdateTranscriptViewportAutomationState();
            return true;
        }


        private void StopInitialScrollForManualInteraction()
        {
            if (!_initialScrollGate.HasPending)
            {
                return;
            }

            _suspendAutoScrollTracking = false;
            _initialScrollGate.ClearPending();
            RefreshLayoutLoadingState();
            UpdateTranscriptViewportAutomationState();
        }

        private void ReleaseAutoScrollTracking()
        {
            _ = DispatcherQueue.TryEnqueue(() => _suspendAutoScrollTracking = false);
        }

        private void ResetAutoScrollStateForConversationChange()
        {
            unchecked { _scrollScheduleGeneration++; }
            _userScrolledUp = false;
            _suspendAutoScrollTracking = false;
            _manualScrollIntentPending = false;
            UpdateTranscriptViewportAutomationState();
        }

        private void HandleOverlayVisibilityChanged()
        {
            var isOverlayVisible = ViewModel.IsActivationOverlayVisible;
            var overlayJustDismissed = _wasOverlayVisible && !isOverlayVisible;
            _wasOverlayVisible = isOverlayVisible;

            if (!overlayJustDismissed
                || _userScrolledUp
                || !_isViewLoaded
                || MessagesList is null
                || ViewModel.MessageHistory.Count <= 0)
            {
                return;
            }

            _initialScrollGate.MarkPending();
            RefreshLayoutLoadingState(HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
            RequestScrollToBottom();
            RequestInitialScroll();
            UpdateTranscriptViewportAutomationState();
        }

        private void RestoreViewportForWarmResume()
        {
            if (!_isViewLoaded
                || !ViewModel.IsSessionActive
                || ViewModel.IsActivationOverlayVisible
                || ViewModel.MessageHistory.Count <= 0)
            {
                return;
            }

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isViewLoaded
                    || !ViewModel.IsSessionActive
                    || ViewModel.IsActivationOverlayVisible
                    || ViewModel.MessageHistory.Count <= 0)
                {
                    return;
                }

                _userScrolledUp = false;
                _initialScrollGate.MarkPending();
                RequestScrollToBottom();
                RequestInitialScroll();
                UpdateTranscriptViewportAutomationState();
            });
        }

        private void UpdateTranscriptViewportAutomationState()
        {
            var state = ResolveTranscriptViewportAutomationState();
            if (string.Equals(_transcriptViewportAutomationState, state, StringComparison.Ordinal))
            {
                return;
            }

            _transcriptViewportAutomationState = state;
            if (TranscriptViewportStateProbe is null)
            {
                return;
            }

            TranscriptViewportStateProbe.Text = state;
            AutomationProperties.SetName(TranscriptViewportStateProbe, state);
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

            if (_initialScrollGate.HasPending)
            {
                return "pending";
            }

            if (!HasLastItemContainerGenerated(ViewModel.MessageHistory.Count))
            {
                return "untracked";
            }

            return IsListViewportAtBottom() ? "bottom" : "not_bottom";
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

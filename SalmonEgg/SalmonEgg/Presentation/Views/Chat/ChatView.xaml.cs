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
        private readonly TranscriptScrollSettler _transcriptScrollSettler = new(maxReadyButNotBottomFailures: MaxInitialScrollAttempts);
        private bool _userScrolledUp;
        private const double BottomThreshold = 10;
        private const double BottomGeometryTolerance = 2;
        private const int MaxInitialScrollAttempts = 8;
        private bool _suspendAutoScrollTracking;
        private bool _manualScrollIntentPending;
        private bool _wasOverlayVisible;
        private bool _scrollToBottomScheduled;
        private int _scrollScheduleGeneration;
        private int _activeTranscriptScrollGeneration = -1;
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
            BeginTranscriptSettleRound();
            EnsureMessageTracking();
            BeginLayoutLoadingIfPendingMessages();
            TryIssueTranscriptScrollRequest();
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
            _activeTranscriptScrollGeneration = -1;
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

            if (ViewModel.IsSessionActive && ViewModel.MessageHistory.Count > 0 && !_transcriptScrollSettler.HasPendingWork)
            {
                BeginTranscriptSettleRound();
            }

            BeginLayoutLoadingIfPendingMessages();

            if (_transcriptScrollSettler.HasPendingWork && _userScrolledUp)
            {
                AbortTranscriptSettleRound();
                RefreshLayoutLoadingState();
                return;
            }

            if (TryIssueTranscriptScrollRequest())
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
            TryIssueTranscriptScrollRequest();
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

            if (_manualScrollIntentPending && !_transcriptScrollSettler.HasPendingWork && !_suspendAutoScrollTracking)
            {
                _userScrolledUp = !IsListViewportAtBottom();
                _manualScrollIntentPending = false;
            }

            if (!_transcriptScrollSettler.HasPendingWork || _userScrolledUp)
            {
                UpdateTranscriptViewportAutomationState();
                return;
            }

            TryAdvanceTranscriptSettleFromLayout(lastItemContainerGenerated);
            TryIssueTranscriptScrollRequest();

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
                BeginTranscriptSettleRound();
                BeginLayoutLoadingIfPendingMessages();
                TryIssueTranscriptScrollRequest();
                UpdateTranscriptViewportAutomationState();
                return;
            }

            if (e.PropertyName == nameof(ChatViewModel.MessageHistory))
            {
                EnsureMessageTracking();
                ResetAutoScrollStateForConversationChange();
                BeginTranscriptSettleRound();
                BeginLayoutLoadingIfPendingMessages();
                TryIssueTranscriptScrollRequest();
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
                hasPendingInitialScroll: _transcriptScrollSettler.HasPendingWork,
                lastItemContainerGenerated: lastItemContainerGenerated,
                isHydrating: ViewModel.IsHydrating,
                isRemoteHydrationPending: ViewModel.IsRemoteHydrationPending);
            UpdateTranscriptViewportAutomationState();
        }

        private bool TryIssueTranscriptScrollRequest()
        {
            if (_activeTranscriptScrollGeneration >= 0)
            {
                return false;
            }

            var decision = _transcriptScrollSettler.TryIssueScrollRequest(
                ViewModel.CurrentSessionId,
                hasMessages: ViewModel.MessageHistory.Count > 0,
                isReady: CanIssueTranscriptScrollRequest());

            switch (decision.Action)
            {
                case TranscriptScrollAction.IssueScrollRequest:
                    _activeTranscriptScrollGeneration = decision.Generation;
                    _suspendAutoScrollTracking = true;
                    IssueNativeTranscriptScrollRequest();
                    RefreshLayoutLoadingState(HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
                    UpdateTranscriptViewportAutomationState();
                    return true;

                case TranscriptScrollAction.Completed:
                case TranscriptScrollAction.Aborted:
                case TranscriptScrollAction.Exhausted:
                    _activeTranscriptScrollGeneration = -1;
                    ReleaseAutoScrollTracking();
                    RefreshLayoutLoadingState(HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
                    UpdateTranscriptViewportAutomationState();
                    return false;

                default:
                    return false;
            }
        }

        private bool CanIssueTranscriptScrollRequest()
        {
            return _isViewLoaded
                && MessagesList is not null
                && ViewModel.IsSessionActive
                && ViewModel.MessageHistory.Count > 0
                && !string.IsNullOrWhiteSpace(ViewModel.CurrentSessionId);
        }

        private void IssueNativeTranscriptScrollRequest()
        {
            if (MessagesList is null || ViewModel.MessageHistory.Count <= 0)
            {
                return;
            }

            MessagesList.ScrollIntoView(ViewModel.MessageHistory[^1]);
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

            if (!HasLastItemContainerGenerated(itemCount))
            {
                return false;
            }

            if (MessagesList.ContainerFromIndex(itemCount - 1) is not ListViewItem lastItemContainer)
            {
                return false;
            }

            var anchor = lastItemContainer.ContentTemplateRoot as FrameworkElement ?? lastItemContainer;
            Point relativeOrigin = anchor.TransformToVisual(MessagesList).TransformPoint(default);
            var lastItemBottom = relativeOrigin.Y + anchor.ActualHeight;
            var viewportBottom = MessagesList.ActualHeight - BottomThreshold;
            return lastItemBottom <= viewportBottom + BottomGeometryTolerance;
        }

        private bool TryAdvanceTranscriptSettleFromLayout(bool? lastItemContainerGenerated = null)
        {
            if (_activeTranscriptScrollGeneration < 0)
            {
                return false;
            }

            var observation = ResolveTranscriptScrollObservation(lastItemContainerGenerated);
            if (observation == TranscriptScrollSettleObservation.NotReadyYet)
            {
                return false;
            }

            return ApplyTranscriptSettleDecision(
                ReportTranscriptSettleObservation(observation),
                HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
        }

        private TranscriptScrollSettleObservation ResolveTranscriptScrollObservation(bool? lastItemContainerGenerated = null)
        {
            var itemCount = ViewModel.MessageHistory.Count;
            if (itemCount <= 0)
            {
                return TranscriptScrollSettleObservation.NotReadyYet;
            }

            var hasLastItemContainer = lastItemContainerGenerated ?? HasLastItemContainerGenerated(itemCount);
            if (!hasLastItemContainer)
            {
                return TranscriptScrollSettleObservation.NotReadyYet;
            }

            return IsListViewportAtBottom()
                ? TranscriptScrollSettleObservation.AtBottom
                : TranscriptScrollSettleObservation.ReadyButNotAtBottom;
        }

        private TranscriptScrollDecision ReportTranscriptSettleObservation(TranscriptScrollSettleObservation observation)
        {
            if (_activeTranscriptScrollGeneration < 0)
            {
                return default;
            }

            var generation = _activeTranscriptScrollGeneration;
            _activeTranscriptScrollGeneration = -1;
            return _transcriptScrollSettler.ReportSettled(ViewModel.CurrentSessionId, generation, observation);
        }

        private bool ApplyTranscriptSettleDecision(TranscriptScrollDecision decision, bool lastItemContainerGenerated)
        {
            switch (decision.Action)
            {
                case TranscriptScrollAction.Completed:
                case TranscriptScrollAction.Aborted:
                case TranscriptScrollAction.Exhausted:
                    ReleaseAutoScrollTracking();
                    RefreshLayoutLoadingState(lastItemContainerGenerated);
                    UpdateTranscriptViewportAutomationState();
                    return true;

                default:
                    return false;
            }
        }

        private void StopInitialScrollForManualInteraction()
        {
            if (!_transcriptScrollSettler.HasPendingWork)
            {
                return;
            }

            _suspendAutoScrollTracking = false;
            _activeTranscriptScrollGeneration = -1;
            ApplyTranscriptSettleDecision(
                _transcriptScrollSettler.AbortForUserInteraction(),
                HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
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
            _activeTranscriptScrollGeneration = -1;
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

            BeginTranscriptSettleRound();
            RefreshLayoutLoadingState(HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
            TryIssueTranscriptScrollRequest();
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
                BeginTranscriptSettleRound();
                TryIssueTranscriptScrollRequest();
                UpdateTranscriptViewportAutomationState();
            });
        }

        private void BeginTranscriptSettleRound()
        {
            _activeTranscriptScrollGeneration = -1;
            _transcriptScrollSettler.BeginRound(ViewModel.CurrentSessionId);
        }

        private void AbortTranscriptSettleRound()
        {
            _activeTranscriptScrollGeneration = -1;
            ApplyTranscriptSettleDecision(
                _transcriptScrollSettler.AbortForUserInteraction(),
                HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
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

            if (_transcriptScrollSettler.HasPendingWork)
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

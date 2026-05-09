using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Transcript;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;

namespace SalmonEgg.Presentation.Views.Chat;

public sealed partial class ChatView : Page
{
        public ChatShellViewModel ShellViewModel { get; }
        public ChatViewModel ViewModel => ShellViewModel.Chat;
        public ShellLayoutViewModel LayoutVM => ShellViewModel.ShellLayout;
        public UiMotion Motion => UiMotion.Current;
        private bool _isViewLoaded;
        private bool _isTrackingMessages;
        private readonly TranscriptViewportOrchestrator _viewportOrchestrator = new();
        private const double BottomThreshold = 10;
        private const double BottomGeometryTolerance = 2;
        private const int MaxRestoreAttempts = 8;
        private bool _wasOverlayVisible;
        private bool _restoreDetachedViewportAfterOverlay;
        private string? _restoreDetachedViewportConversationId;
        private bool _resumeViewportCoordinatorAfterOverlayPending;
        private readonly TranscriptProjectionRestoreController _projectionRestoreController = new(MaxRestoreAttempts);
        private string _transcriptViewportAutomationState = "inactive";
        private INotifyCollectionChanged? _trackedMessageHistory;
        private readonly Microsoft.UI.Xaml.Input.KeyEventHandler _messagesListHandledKeyDownHandler;
        private readonly PointerEventHandler _messagesListHandledPointerPressedHandler;
        private readonly PointerEventHandler _messagesListHandledPointerWheelChangedHandler;
        private ITranscriptViewportHost? _transcriptViewportHost;
        private bool _isSessionHeaderLayoutHooked;
        private const double SessionHeaderMediumWidthThreshold = 720;
        private const double SessionHeaderWideWidthThreshold = 1080;

        public ChatView()
        {
            ShellViewModel = App.ServiceProvider.GetRequiredService<ChatShellViewModel>();
            NavigationCacheMode = NavigationCacheMode.Required;
            _messagesListHandledKeyDownHandler = OnMessagesListKeyDown;
            _messagesListHandledPointerPressedHandler = OnMessagesListPointerPressed;
            _messagesListHandledPointerWheelChangedHandler = OnMessagesListPointerWheelChanged;

            this.InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isViewLoaded = true;
            _viewportOrchestrator.StartLifecycleGeneration();
            _restoreDetachedViewportAfterOverlay = false;
            _restoreDetachedViewportConversationId = null;
            _resumeViewportCoordinatorAfterOverlayPending = false;
            _viewportOrchestrator.ResetInteractionState();
            ClearPendingProjectionRestore();
            ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind.ColdEnter);
            _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
            if (_wasOverlayVisible)
            {
                _resumeViewportCoordinatorAfterOverlayPending = true;
                InvalidateViewportCoordinator();
            }
            BeginTranscriptSettleRound();
            EnsureMessageTracking();
            BeginLayoutLoadingIfPendingMessages();
            TryIssueTranscriptScrollRequest();
            RestoreViewportForWarmResume();
            UpdateTranscriptViewportAutomationState();
            HookSessionHeaderLayoutState();
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
            InvalidateViewportCoordinator();
            _isViewLoaded = false;
            _viewportOrchestrator.StartLifecycleGeneration();
            _restoreDetachedViewportAfterOverlay = false;
            _restoreDetachedViewportConversationId = null;
            _resumeViewportCoordinatorAfterOverlayPending = false;
            _viewportOrchestrator.ResetInteractionState();
            _viewportOrchestrator.ResetScheduledScrollState();
            DisposeTranscriptViewportHost();
            ClearPendingProjectionRestore();
            UnhookSessionHeaderLayoutState();
            ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind.ColdEnter);
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

            if (ViewModel.IsSessionActive
                && ViewModel.MessageHistory.Count > 0
                && !_viewportOrchestrator.HasPendingSettle
                && !IsViewportDetachedByUser())
            {
                BeginTranscriptSettleRound();
            }

            BeginLayoutLoadingIfPendingMessages();
            TryApplyPendingProjectionRestore();

            if (_viewportOrchestrator.HasPendingSettle && IsViewportDetachedByUser())
            {
                AbortTranscriptSettleRound();
                RefreshLayoutLoadingState();
                return;
            }

            if (TryIssueTranscriptScrollRequest())
            {
                return;
            }

            ApplyViewportCommand(_viewportOrchestrator.Handle(
                _viewportOrchestrator.CreateTranscriptAppendedEvent(
                    CurrentViewportConversationId,
                    e.NewItems?.Count ?? 0)));

            UpdateTranscriptViewportAutomationState();
        }

        private void OnMessagesListLoaded(object sender, RoutedEventArgs e)
        {
            DisposeTranscriptViewportHost();
            _transcriptViewportHost = MessagesList is null
                ? null
                : new ListViewTranscriptViewportHost(MessagesList);
            if (_transcriptViewportHost is not null)
            {
                _transcriptViewportHost.ViewportChanged += OnMessagesListViewportChanged;
            }

#if WINDOWS
            MessagesList.ShowsScrollingPlaceholders = false;
#endif

            MessagesList?.AddHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler, true);
            MessagesList?.AddHandler(UIElement.PointerPressedEvent, _messagesListHandledPointerPressedHandler, true);
            MessagesList?.AddHandler(UIElement.PointerWheelChangedEvent, _messagesListHandledPointerWheelChangedHandler, true);
            ResumeViewportCoordinatorAfterOverlayIfNeeded();
            BeginLayoutLoadingIfPendingMessages();
            TryApplyPendingProjectionRestore();
            TryIssueTranscriptScrollRequest();
            TryRefreshViewportCoordinatorFromView();
            UpdateTranscriptViewportAutomationState();
        }

        private void OnMessagesListUnloaded(object sender, RoutedEventArgs e)
        {
            DisposeTranscriptViewportHost();
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

            if (_viewportOrchestrator.HasPendingSettle && IsViewportDetachedByUser())
            {
                AbortTranscriptSettleRound();
                RefreshLayoutLoadingState(lastItemContainerGenerated);
                TryRefreshViewportCoordinatorFromView(lastItemContainerGenerated);
                UpdateTranscriptViewportAutomationState();
                return;
            }

            if (_viewportOrchestrator.HasPendingSettle)
            {
                TryAdvanceTranscriptSettleFromLayout(lastItemContainerGenerated);
                TryIssueTranscriptScrollRequest();
            }

            TryApplyPendingProjectionRestore();
            TryRefreshViewportCoordinatorFromView(lastItemContainerGenerated);
            UpdateTranscriptViewportAutomationState();
        }

        private void OnMessagesListPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (IsViewportDetachedByUser())
            {
                _viewportOrchestrator.MarkDetachedViewportInteractionStarted();
                FocusTranscriptScroller();
                return;
            }

            if (_projectionRestoreController.HasPending)
            {
                AbandonPendingProjectionRestore("UserInterrupted");
            }

            _viewportOrchestrator.MarkUserScrollIntentStarted();
            FocusTranscriptScroller();
        }

        private void OnMessagesListPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _viewportOrchestrator.MarkUserScrollIntentCompleted();
            var releaseGeneration = _viewportOrchestrator.Generation;
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (releaseGeneration != _viewportOrchestrator.Generation
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

        private void FocusTranscriptScroller()
        {
            if (MessagesList is not null)
            {
                _ = MessagesList.Focus(FocusState.Programmatic);
            }
        }

        private void RegisterUserViewportIntent()
        {
            if (_projectionRestoreController.HasPending)
            {
                AbandonPendingProjectionRestore("UserInterrupted");
            }

            if (IsViewportDetachedByUser())
            {
                RegisterUserViewportAttachIntent();
                UpdateTranscriptViewportAutomationState();
                return;
            }

            FocusTranscriptScroller();

            if (IsListViewportAtBottom())
            {
                _viewportOrchestrator.MarkUserScrollIntentStarted();
                StopInitialScrollForManualInteraction();
                UpdateTranscriptViewportAutomationState();
                return;
            }

            RegisterUserViewportDetachment();
            UpdateTranscriptViewportAutomationState();
        }

        private void RegisterUserViewportAttachIntent()
        {
            FocusTranscriptScroller();
            _viewportOrchestrator.MarkAttachToBottomIntent();
            if (IsListViewportAtBottom())
            {
                RegisterUserViewportAttachment();
            }
        }

        private void RegisterUserViewportDetachment()
        {
            _viewportOrchestrator.ClearAttachIntentOnly();
            AbandonPendingProjectionRestore("UserInterrupted");

            TranscriptViewportCommand command;
            if (TryCaptureProjectionRestoreToken() is { } restoreToken)
            {
                command = _viewportOrchestrator.Handle(
                    _viewportOrchestrator.CreateUserDetachedEvent(CurrentViewportConversationId, restoreToken));
            }
            else
            {
                command = _viewportOrchestrator.Handle(
                    _viewportOrchestrator.CreateUserIntentScrollEvent(CurrentViewportConversationId));
            }

            ApplyViewportCommand(command);
            StopInitialScrollForManualInteraction();
        }

        private void RegisterUserViewportAttachment()
        {
            _viewportOrchestrator.ClearAttachIntent();
            AbandonPendingProjectionRestore("UserAttached");
            ApplyViewportCommand(_viewportOrchestrator.Handle(
                _viewportOrchestrator.CreateUserAttachedEvent(CurrentViewportConversationId)));
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
            if (!_isViewLoaded
                || !ViewModel.IsSessionActive
                || string.IsNullOrWhiteSpace(e.ConversationId))
            {
                return;
            }

            ApplyViewportCommand(_viewportOrchestrator.Handle(
                _viewportOrchestrator.CreateProjectionReadyEvent(
                    e.ConversationId,
                    e.ProjectionEpoch)));
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

            var fact = new TranscriptViewportFact(
                HasItems: ViewModel.MessageHistory.Count > 0,
                IsReady: lastItemContainerGenerated ?? HasLastItemContainerGenerated(ViewModel.MessageHistory.Count),
                IsAtBottom: IsListViewportAtBottom(),
                IsProgrammaticScrollInFlight: _viewportOrchestrator.IsProgrammaticScrollInFlight);
            var observation = _viewportOrchestrator.ObserveViewportFact(
                CurrentViewportConversationId,
                fact,
                TryCaptureProjectionRestoreToken());
            ApplyViewportCommand(observation.Command);
        }

        private void ApplyViewportCommand(TranscriptViewportCommand command)
        {
            switch (command.Kind)
            {
                case TranscriptViewportCommandKind.IssueScrollToBottom:
                    ScheduleScrollToBottom();
                    break;

                case TranscriptViewportCommandKind.RequestRestore:
                    if (command.RestoreToken is { } restoreToken)
                    {
                        QueueProjectionOwnedRestore(restoreToken, command.Generation);
                    }
                    break;

                case TranscriptViewportCommandKind.StopProgrammaticScroll:
                    _viewportOrchestrator.StopProgrammaticScroll();
                    ClearPendingProjectionRestore();
                    if (_viewportOrchestrator.HasPendingSettle)
                    {
                        AbortTranscriptSettleRound();
                    }
                    break;

                case TranscriptViewportCommandKind.MarkAutoFollowDetached:
                    _viewportOrchestrator.ClearAttachIntentOnly();
                    _viewportOrchestrator.ClearScrollToBottomScheduled();
                    ClearPendingProjectionRestore();
                    break;

                case TranscriptViewportCommandKind.MarkAutoFollowAttached:
                    _viewportOrchestrator.ClearAttachIntentOnly();
                    ClearPendingProjectionRestore();
                    break;
            }
        }

        private void ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind activationKind)
        {
            ApplyViewportCommand(_viewportOrchestrator.Activate(
                _isViewLoaded && ViewModel.IsSessionActive
                    ? CurrentViewportConversationId
                    : string.Empty,
                activationKind));
        }

        private void InvalidateViewportCoordinator()
        {
            if (string.IsNullOrWhiteSpace(CurrentViewportConversationId))
            {
                return;
            }

            ApplyViewportCommand(_viewportOrchestrator.InvalidateContext(CurrentViewportConversationId));
        }

        private bool IsViewportDetachedByUser()
        {
            return _viewportOrchestrator.State is TranscriptViewportState.DetachedByUser
                or TranscriptViewportState.DetachedPendingRestore
                or TranscriptViewportState.DetachedRestoring;
        }

        private string CurrentViewportConversationId => ViewModel.CurrentSessionId ?? string.Empty;

        private void QueueProjectionOwnedRestore(TranscriptProjectionRestoreToken token, int generation)
        {
            _projectionRestoreController.Queue(token, generation);
            _viewportOrchestrator.MarkProjectionRestoreQueued();
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
                _viewportOrchestrator.Generation,
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
                        ReleaseAutoScrollTracking();
                        ApplyViewportCommand(_viewportOrchestrator.Handle(
                            _viewportOrchestrator.CreateRestoreConfirmedEvent(
                                token.ConversationId,
                                result.Generation,
                                token)));
                    }
                    break;

                case TranscriptProjectionRestoreResultKind.Unavailable:
                    ReleaseAutoScrollTracking();
                    ApplyViewportCommand(_viewportOrchestrator.Handle(
                        _viewportOrchestrator.CreateRestoreUnavailableEvent(
                            result.ConversationId ?? CurrentViewportConversationId,
                            result.Generation,
                            result.Reason ?? "RestoreUnavailable")));
                    break;

                case TranscriptProjectionRestoreResultKind.Abandoned:
                    ReleaseAutoScrollTracking();
                    ApplyViewportCommand(_viewportOrchestrator.Handle(
                        _viewportOrchestrator.CreateRestoreAbandonedEvent(
                            result.ConversationId ?? CurrentViewportConversationId,
                            result.Generation,
                            result.Reason ?? "RestoreAbandoned")));
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

        private void ScheduleScrollToBottom()
        {
            if (!_viewportOrchestrator.TryBeginScrollToBottomSchedule(ViewModel.CurrentSessionId, out var scheduleToken))
            {
                return;
            }

            if (!DispatcherQueue.TryEnqueue(() =>
                {
                    _viewportOrchestrator.ReleaseScrollToBottomSchedule(scheduleToken);
                    if (!_isViewLoaded
                        || !ViewModel.IsSessionActive
                        || ViewModel.MessageHistory.Count <= 0
                        || !_viewportOrchestrator.CanExecuteScrollToBottomSchedule(scheduleToken, ViewModel.CurrentSessionId))
                    {
                        return;
                    }

                    RequestScrollToBottom();
                }))
            {
                _viewportOrchestrator.ReleaseScrollToBottomSchedule(scheduleToken);
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId))
            {
                ResetAutoScrollStateForConversationChange();
                _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
                if (!IsViewportDetachedByUser())
                {
                    BeginTranscriptSettleRound();
                }
                BeginLayoutLoadingIfPendingMessages();
                TryIssueTranscriptScrollRequest();
                UpdateTranscriptViewportAutomationState();
                return;
            }

            if (e.PropertyName == nameof(ChatViewModel.IsSessionActive))
            {
                if (_viewportOrchestrator.State is TranscriptViewportState.DetachedPendingRestore
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
                if (!IsViewportDetachedByUser())
                {
                    BeginTranscriptSettleRound();
                }
                BeginLayoutLoadingIfPendingMessages();
                TryIssueTranscriptScrollRequest();
                UpdateTranscriptViewportAutomationState();
                return;
            }

            if (e.PropertyName == nameof(ChatViewModel.MessageHistory))
            {
                EnsureMessageTracking();
                if (_viewportOrchestrator.State is TranscriptViewportState.DetachedPendingRestore
                    or TranscriptViewportState.DetachedRestoring)
                {
                    BeginLayoutLoadingIfPendingMessages();
                    TryApplyPendingProjectionRestore();
                    TryRefreshViewportCoordinatorFromView();
                    UpdateTranscriptViewportAutomationState();
                    return;
                }

                ResetAutoScrollStateForConversationChange();
                if (!IsViewportDetachedByUser())
                {
                    BeginTranscriptSettleRound();
                }
                BeginLayoutLoadingIfPendingMessages();
                TryIssueTranscriptScrollRequest();
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
                hasPendingInitialScroll: _viewportOrchestrator.HasPendingSettle,
                lastItemContainerGenerated: lastItemContainerGenerated,
                isHydrating: ViewModel.IsHydrating,
                isRemoteHydrationPending: ViewModel.IsRemoteHydrationPending);
            UpdateTranscriptViewportAutomationState();
        }

        private bool TryIssueTranscriptScrollRequest()
        {
            var decision = _viewportOrchestrator.TryIssueScrollRequest(
                ViewModel.CurrentSessionId,
                hasMessages: ViewModel.MessageHistory.Count > 0,
                isReady: CanIssueTranscriptScrollRequest());

            switch (decision.Action)
            {
                case TranscriptScrollAction.IssueScrollRequest:
                    IssueNativeTranscriptScrollRequest();
                    RefreshLayoutLoadingState(HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
                    TryRefreshViewportCoordinatorFromView();
                    UpdateTranscriptViewportAutomationState();
                    return true;

                case TranscriptScrollAction.Completed:
                case TranscriptScrollAction.Aborted:
                case TranscriptScrollAction.Exhausted:
                    ReleaseAutoScrollTracking();
                    RefreshLayoutLoadingState(HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
                    TryRefreshViewportCoordinatorFromView();
                    UpdateTranscriptViewportAutomationState();
                    return false;

                default:
                    return false;
            }
        }

        private bool CanIssueTranscriptScrollRequest()
        {
            return _isViewLoaded
                && _transcriptViewportHost is not null
                && !ViewModel.IsActivationOverlayVisible
                && ViewModel.IsSessionActive
                && ViewModel.MessageHistory.Count > 0
                && !string.IsNullOrWhiteSpace(ViewModel.CurrentSessionId);
        }

        private void IssueNativeTranscriptScrollRequest()
        {
            if (_transcriptViewportHost is null || ViewModel.MessageHistory.Count <= 0)
            {
                return;
            }

            if (!_viewportOrchestrator.TryCaptureActiveScrollRequestToken(ViewModel.CurrentSessionId, out var requestToken))
            {
                return;
            }

            RequestScrollToBottom();

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isViewLoaded
                    || _transcriptViewportHost is null
                    || ViewModel.MessageHistory.Count <= 0
                    || !_viewportOrchestrator.MatchesActiveScrollRequest(requestToken, ViewModel.CurrentSessionId))
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
                    || !_viewportOrchestrator.MatchesActiveScrollRequest(requestToken, ViewModel.CurrentSessionId))
                {
                    return;
                }

                var lastItemContainerGenerated = HasLastItemContainerGenerated(ViewModel.MessageHistory.Count);
                TryAdvanceTranscriptSettleFromLayout(lastItemContainerGenerated);
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

        private bool TryAdvanceTranscriptSettleFromLayout(bool? lastItemContainerGenerated = null)
        {
            if (!_viewportOrchestrator.HasActiveScrollGeneration)
            {
                return false;
            }

            var observation = ResolveTranscriptScrollObservation(lastItemContainerGenerated);
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

            return IsListViewportAtBottom() && IsLastItemVisiblyAtBottom(itemCount)
                ? TranscriptScrollSettleObservation.AtBottom
                : TranscriptScrollSettleObservation.ReadyButNotAtBottom;
        }

        private bool IsLastItemVisiblyAtBottom(int itemCount)
        {
            if (_transcriptViewportHost is null || itemCount <= 0)
            {
                return false;
            }

            return _transcriptViewportHost.IsLastItemVisiblyAtBottom(itemCount, BottomThreshold, BottomGeometryTolerance);
        }

        private TranscriptScrollDecision ReportTranscriptSettleObservation(TranscriptScrollSettleObservation observation)
        {
            if (!_viewportOrchestrator.HasActiveScrollGeneration)
            {
                return default;
            }

            return _viewportOrchestrator.ReportSettled(ViewModel.CurrentSessionId, observation);
        }

        private bool ApplyTranscriptSettleDecision(TranscriptScrollDecision decision, bool lastItemContainerGenerated)
        {
            switch (decision.Action)
            {
                case TranscriptScrollAction.IssueScrollRequest:
                    IssueNativeTranscriptScrollRequest();
                    RefreshLayoutLoadingState(lastItemContainerGenerated);
                    UpdateTranscriptViewportAutomationState();
                    return true;

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
            _viewportOrchestrator.StopInitialScrollForManualInteraction();

            if (!_viewportOrchestrator.HasPendingSettle)
            {
                UpdateTranscriptViewportAutomationState();
                return;
            }

            ApplyTranscriptSettleDecision(
                _viewportOrchestrator.AbortSettleForUserInteraction(),
                HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
            RefreshLayoutLoadingState();
            UpdateTranscriptViewportAutomationState();
        }

        private void ReleaseAutoScrollTracking()
        {
            _viewportOrchestrator.ReleaseAutoScrollTracking();
        }

        private void ResetAutoScrollStateForConversationChange()
        {
            AbandonPendingProjectionRestore("ConversationChanged");
            InvalidateViewportCoordinator();
            _viewportOrchestrator.ResetForConversationChange();
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
            _viewportOrchestrator.ClearUserScrollIntent();
            ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind.WarmReturn);
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
                _viewportOrchestrator.ResetInteractionState();
                _resumeViewportCoordinatorAfterOverlayPending = true;
                InvalidateViewportCoordinator();
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
                || string.IsNullOrWhiteSpace(ViewModel.CurrentSessionId)
                || ViewModel.MessageHistory.Count <= 0)
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
            ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind.OverlayResume);

            if (!IsViewportDetachedByUser())
            {
                BeginTranscriptSettleRound();
            }
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

                ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind.WarmReturn);
                if (!IsViewportDetachedByUser())
                {
                    BeginTranscriptSettleRound();
                }
                TryIssueTranscriptScrollRequest();
                TryRefreshViewportCoordinatorFromView();
                UpdateTranscriptViewportAutomationState();
            });
        }

        private void BeginTranscriptSettleRound()
        {
            _viewportOrchestrator.BeginSettleRound(ViewModel.CurrentSessionId);
        }

        private void AbortTranscriptSettleRound()
        {
            ApplyTranscriptSettleDecision(
                _viewportOrchestrator.AbortSettleForUserInteraction(),
                HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
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

            var transition = _viewportOrchestrator.LastTransition;
            var debug = $"state={state};coord={_viewportOrchestrator.State};attached={_viewportOrchestrator.IsAutoFollowAttached};current={CurrentViewportConversationId};generation={_viewportOrchestrator.Generation};transition={(transition?.Reason ?? "<none>")};attachPending={_viewportOrchestrator.AttachToBottomIntentPending};scrollIntentPending={_viewportOrchestrator.UserScrollIntentPending};scrollIntentCompleted={_viewportOrchestrator.UserScrollIntentCompleted};restoreConversation={_projectionRestoreController.PendingConversationId ?? "<none>"};restoreGeneration={_projectionRestoreController.PendingGeneration}";
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

            return _viewportOrchestrator.State switch
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

        private void OnSessionHeaderRootLoaded(object sender, RoutedEventArgs e)
            => HookSessionHeaderLayoutState();

        private void OnSessionHeaderRootUnloaded(object sender, RoutedEventArgs e)
            => UnhookSessionHeaderLayoutState();

        private void OnSessionHeaderRootSizeChanged(object sender, SizeChangedEventArgs e)
            => UpdateSessionHeaderLayoutState(e.NewSize.Width);

        private void HookSessionHeaderLayoutState()
        {
            if (_isSessionHeaderLayoutHooked || SessionHeaderRoot is null)
            {
                return;
            }

            SessionHeaderRoot.SizeChanged += OnSessionHeaderRootSizeChanged;
            _isSessionHeaderLayoutHooked = true;
            UpdateSessionHeaderLayoutState(SessionHeaderRoot.ActualWidth);
        }

        private void UnhookSessionHeaderLayoutState()
        {
            if (!_isSessionHeaderLayoutHooked || SessionHeaderRoot is null)
            {
                return;
            }

            SessionHeaderRoot.SizeChanged -= OnSessionHeaderRootSizeChanged;
            _isSessionHeaderLayoutHooked = false;
        }

        private void UpdateSessionHeaderLayoutState(double availableWidth)
        {
            if (availableWidth <= 0)
            {
                return;
            }

            if (availableWidth >= SessionHeaderWideWidthThreshold)
            {
                ApplyWideSessionHeaderLayout();
                return;
            }

            if (availableWidth >= SessionHeaderMediumWidthThreshold)
            {
                ApplyMediumSessionHeaderLayout();
                return;
            }

            ApplyNarrowSessionHeaderLayout();
        }

        private void ApplyNarrowSessionHeaderLayout()
        {
            SessionHeaderMetaGrid.ColumnSpacing = 8;
            SessionHeaderMetaGrid.RowSpacing = 4;
            SessionHeaderAgentColumn.Width = new GridLength(0);
            SessionHeaderAgentRow.Height = GridLength.Auto;
            Grid.SetRow(SessionHeaderAgentDisplay, 1);
            Grid.SetColumn(SessionHeaderAgentDisplay, 0);
            Grid.SetColumnSpan(SessionHeaderAgentDisplay, 2);
            SessionHeaderAgentDisplay.HorizontalAlignment = HorizontalAlignment.Right;
            SessionHeaderAgentDisplay.Margin = new Thickness(0);
            SessionHeaderAgentDisplay.MaxWidth = 320;
        }

        private void ApplyMediumSessionHeaderLayout()
        {
            SessionHeaderMetaGrid.ColumnSpacing = 12;
            SessionHeaderMetaGrid.RowSpacing = 0;
            SessionHeaderAgentColumn.Width = GridLength.Auto;
            SessionHeaderAgentRow.Height = new GridLength(0);
            Grid.SetRow(SessionHeaderAgentDisplay, 0);
            Grid.SetColumn(SessionHeaderAgentDisplay, 1);
            Grid.SetColumnSpan(SessionHeaderAgentDisplay, 1);
            SessionHeaderAgentDisplay.HorizontalAlignment = HorizontalAlignment.Right;
            SessionHeaderAgentDisplay.Margin = new Thickness(0);
            SessionHeaderAgentDisplay.MaxWidth = 180;
        }

        private void ApplyWideSessionHeaderLayout()
        {
            ApplyMediumSessionHeaderLayout();
            SessionHeaderAgentDisplay.MaxWidth = 240;
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

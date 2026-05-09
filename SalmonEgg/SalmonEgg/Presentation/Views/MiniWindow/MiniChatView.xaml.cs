using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
#if WINDOWS
using Microsoft.UI;
#endif
using SalmonEgg.Presentation.Transcript;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Views.MiniWindow;

public sealed partial class MiniChatView : Page
{
    public ChatShellViewModel ShellViewModel { get; }
    public ChatViewModel ViewModel => ShellViewModel.Chat;
    private bool _isLoaded;
    private bool _isMessagesListLoaded;
    private bool _isTrackingViewModel;
    private INotifyCollectionChanged? _trackedMessageHistory;
    private readonly TranscriptViewportOrchestrator _viewportOrchestrator = new();
    private const double BottomThreshold = 10;
    private const double BottomGeometryTolerance = 2;
    private const int MaxRestoreAttempts = 8;
    private bool _wasOverlayVisible;
    private bool _restoreDetachedViewportAfterOverlay;
    private string? _restoreDetachedViewportConversationId;
    private bool _resumeViewportCoordinatorAfterOverlayPending;
    private readonly TranscriptProjectionRestoreController _projectionRestoreController = new(MaxRestoreAttempts);
    private readonly Microsoft.UI.Xaml.Input.KeyEventHandler _messagesListHandledKeyDownHandler;
    private readonly PointerEventHandler _messagesListHandledPointerPressedHandler;
    private readonly PointerEventHandler _messagesListHandledPointerWheelChangedHandler;
    private ITranscriptViewportHost? _transcriptViewportHost;
#if WINDOWS
    private Microsoft.UI.Xaml.Controls.TitleBar? _nativeTitleBarControl;
#endif

    public MiniChatView()
    {
        ShellViewModel = App.ServiceProvider.GetRequiredService<ChatShellViewModel>();
        _messagesListHandledKeyDownHandler = OnMessagesListKeyDown;
        _messagesListHandledPointerPressedHandler = OnMessagesListPointerPressed;
        _messagesListHandledPointerWheelChangedHandler = OnMessagesListPointerWheelChanged;
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public FrameworkElement EnsureNativeTitleBarElement()
    {
#if WINDOWS
        if (_nativeTitleBarControl is not null)
        {
            return _nativeTitleBarControl;
        }

        if (!ReferenceEquals(MiniTitleBar.Child, MiniTitleBarFallbackLayout))
        {
            return MiniTitleBar;
        }

        DetachElementFromVisualParent(MiniTitleBarContent);
        DetachElementFromVisualParent(MiniTitleBarReturnButton);

        MiniTitleBar.Child = null;
        _nativeTitleBarControl = new Microsoft.UI.Xaml.Controls.TitleBar
        {
            Background = new SolidColorBrush(Colors.Transparent),
            IsBackButtonVisible = false,
            IsPaneToggleButtonVisible = false,
            Content = MiniTitleBarContent,
            RightHeader = MiniTitleBarReturnButton,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        MiniTitleBar.Child = _nativeTitleBarControl;
        return _nativeTitleBarControl;
#else
        return MiniTitleBar;
#endif
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
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
        EnsureViewModelTracking();
        TryIssueTranscriptScrollRequest();
        RestoreViewportForWarmResume();

        try
        {
            await ViewModel.RestoreConversationsAsync();
        }
        catch
        {
        }

        TryIssueTranscriptScrollRequest();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AbandonPendingProjectionRestore("ViewUnloaded");
        InvalidateViewportCoordinator();
        _isLoaded = false;
        _isMessagesListLoaded = false;
        _viewportOrchestrator.StartLifecycleGeneration();
        _restoreDetachedViewportAfterOverlay = false;
        _restoreDetachedViewportConversationId = null;
        _resumeViewportCoordinatorAfterOverlayPending = false;
        _viewportOrchestrator.ResetInteractionState();
        _viewportOrchestrator.ResetScheduledScrollState();
        DisposeTranscriptViewportHost();
        ClearPendingProjectionRestore();
        ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind.ColdEnter);
        DetachViewModelTracking();
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

        _isMessagesListLoaded = true;
        MessagesList?.AddHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler, true);
        MessagesList?.AddHandler(UIElement.PointerPressedEvent, _messagesListHandledPointerPressedHandler, true);
        MessagesList?.AddHandler(UIElement.PointerWheelChangedEvent, _messagesListHandledPointerWheelChangedHandler, true);
        ResumeViewportCoordinatorAfterOverlayIfNeeded();
        TryApplyPendingProjectionRestore();
        TryIssueTranscriptScrollRequest();
        TryRefreshViewportCoordinatorFromView();
    }

    private void OnMessagesListUnloaded(object sender, RoutedEventArgs e)
    {
        DisposeTranscriptViewportHost();
        MessagesList?.RemoveHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler);
        MessagesList?.RemoveHandler(UIElement.PointerPressedEvent, _messagesListHandledPointerPressedHandler);
        MessagesList?.RemoveHandler(UIElement.PointerWheelChangedEvent, _messagesListHandledPointerWheelChangedHandler);
        _isMessagesListLoaded = false;
    }

    private void EnsureViewModelTracking()
    {
        if (_isTrackingViewModel)
        {
            if (!ReferenceEquals(_trackedMessageHistory, ViewModel.MessageHistory))
            {
                if (_trackedMessageHistory is not null)
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
        _isTrackingViewModel = true;
    }

    private void DetachViewModelTracking()
    {
        if (!_isTrackingViewModel)
        {
            return;
        }

        if (_trackedMessageHistory is not null)
        {
            _trackedMessageHistory.CollectionChanged -= OnMessageHistoryChanged;
            _trackedMessageHistory = null;
        }

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.ProjectionRestoreReady -= OnProjectionRestoreReady;
        _isTrackingViewModel = false;
    }

    private void OnMessageHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        ResumeViewportCoordinatorAfterOverlayIfNeeded();
        TryApplyPendingProjectionRestore();

        if (ViewModel.IsSessionActive
            && ViewModel.MessageHistory.Count > 0
            && !_viewportOrchestrator.HasPendingSettle
            && !IsViewportDetachedByUser())
        {
            BeginTranscriptSettleRound();
        }

        if (_viewportOrchestrator.HasPendingSettle && IsViewportDetachedByUser())
        {
            AbortTranscriptSettleRound();
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
            TryIssueTranscriptScrollRequest();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.IsSessionActive))
        {
            if (_viewportOrchestrator.State is TranscriptViewportState.DetachedPendingRestore
                or TranscriptViewportState.DetachedRestoring)
            {
                TryApplyPendingProjectionRestore();
                TryRefreshViewportCoordinatorFromView();
                return;
            }

            ResetAutoScrollStateForConversationChange();
            _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
            if (!IsViewportDetachedByUser())
            {
                BeginTranscriptSettleRound();
            }
            TryIssueTranscriptScrollRequest();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.MessageHistory))
        {
            EnsureViewModelTracking();
            if (_viewportOrchestrator.State is TranscriptViewportState.DetachedPendingRestore
                or TranscriptViewportState.DetachedRestoring)
            {
                TryApplyPendingProjectionRestore();
                TryRefreshViewportCoordinatorFromView();
                return;
            }

            ResetAutoScrollStateForConversationChange();
            if (!IsViewportDetachedByUser())
            {
                BeginTranscriptSettleRound();
            }
            TryIssueTranscriptScrollRequest();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.IsActivationOverlayVisible))
        {
            HandleOverlayVisibilityChanged();
        }
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
        if (_viewportOrchestrator.HasPendingSettle && IsViewportDetachedByUser())
        {
            AbortTranscriptSettleRound();
            TryRefreshViewportCoordinatorFromView(lastItemContainerGenerated);
            return;
        }

        if (_viewportOrchestrator.HasPendingSettle)
        {
            TryAdvanceTranscriptSettleFromLayout(lastItemContainerGenerated);
            TryIssueTranscriptScrollRequest();
        }

        TryApplyPendingProjectionRestore();
        TryRefreshViewportCoordinatorFromView(lastItemContainerGenerated);
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
            return;
        }

        if (MessagesList is not null)
        {
            _ = MessagesList.Focus(FocusState.Programmatic);
        }

        if (IsListViewportAtBottom())
        {
            _viewportOrchestrator.MarkUserScrollIntentStarted();
            StopInitialScrollForManualInteraction();
            return;
        }

        RegisterUserViewportDetachment();
    }

    private void RegisterUserViewportAttachIntent()
    {
        if (MessagesList is not null)
        {
            _ = MessagesList.Focus(FocusState.Programmatic);
        }

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
        if (!_isLoaded
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
    }

    private void TryRefreshViewportCoordinatorFromView(bool? lastItemContainerGenerated = null)
    {
        if (!_isLoaded
            || !_isMessagesListLoaded
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
            _isLoaded && ViewModel.IsSessionActive
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
        if (_transcriptViewportHost is null || !_isLoaded)
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
                TryRefreshViewportCoordinatorFromView();
                return true;

            case TranscriptScrollAction.Completed:
            case TranscriptScrollAction.Aborted:
            case TranscriptScrollAction.Exhausted:
                ReleaseAutoScrollTracking();
                TryRefreshViewportCoordinatorFromView();
                return false;

            default:
                return false;
        }
    }

    private bool CanIssueTranscriptScrollRequest()
    {
        return _isLoaded
            && _isMessagesListLoaded
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

        _transcriptViewportHost.ScrollItemIntoView(
            ViewModel.MessageHistory.Count - 1,
            TranscriptItemScrollAlignment.Leading);
        ScheduleTranscriptScrollRequestObservation(requestToken);
    }

    private void ScheduleTranscriptScrollRequestObservation(TranscriptScrollRequestToken requestToken)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isLoaded
                || !_isMessagesListLoaded
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
        });
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
                if (!_isLoaded
                    || !_isMessagesListLoaded
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

    private bool HasLastItemContainerGenerated(int itemCount)
    {
        if (!_isMessagesListLoaded || itemCount <= 0)
        {
            return false;
        }

        return _transcriptViewportHost is not null
            && _transcriptViewportHost.HasRealizedItem(itemCount - 1);
    }

    private bool IsListViewportAtBottom()
    {
        if (!_isMessagesListLoaded || _transcriptViewportHost is null)
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

    private bool IsLastItemVisiblyAtBottom(int itemCount)
    {
        if (!_isMessagesListLoaded || _transcriptViewportHost is null || itemCount <= 0)
        {
            return false;
        }

        return _transcriptViewportHost.IsLastItemVisiblyAtBottom(itemCount, BottomThreshold, BottomGeometryTolerance);
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
                return true;

            case TranscriptScrollAction.Completed:
            case TranscriptScrollAction.Aborted:
            case TranscriptScrollAction.Exhausted:
                ReleaseAutoScrollTracking();
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
            return;
        }

        ApplyTranscriptSettleDecision(
            _viewportOrchestrator.AbortSettleForUserInteraction(),
            HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
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
            return;
        }

        if (_resumeViewportCoordinatorAfterOverlayPending)
        {
            ResumeViewportCoordinatorAfterOverlayIfNeeded();
            return;
        }

        _restoreDetachedViewportAfterOverlay = false;
        _restoreDetachedViewportConversationId = null;
        _resumeViewportCoordinatorAfterOverlayPending = false;
        _viewportOrchestrator.ClearUserScrollIntent();
        ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind.WarmReturn);
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
            || !_isLoaded
            || !ViewModel.IsSessionActive
            || !_isMessagesListLoaded
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
        TryIssueTranscriptScrollRequest();
        TryRefreshViewportCoordinatorFromView();
    }

    private void RestoreViewportForWarmResume()
    {
        if (!_isLoaded
            || !ViewModel.IsSessionActive
            || ViewModel.IsActivationOverlayVisible
            || ViewModel.MessageHistory.Count <= 0)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isLoaded
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
        });
    }

    private void OnMessagesListPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsViewportDetachedByUser())
        {
            _viewportOrchestrator.MarkDetachedViewportInteractionStarted();
            if (MessagesList is not null)
            {
                _ = MessagesList.Focus(FocusState.Programmatic);
            }

            return;
        }

        if (_projectionRestoreController.HasPending)
        {
            AbandonPendingProjectionRestore("UserInterrupted");
        }

        _viewportOrchestrator.MarkUserScrollIntentStarted();
        if (MessagesList is not null)
        {
            _ = MessagesList.Focus(FocusState.Programmatic);
        }
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

#if WINDOWS
    private static void DetachElementFromVisualParent(FrameworkElement element)
    {
        if (element.Parent is Panel panel)
        {
            panel.Children.Remove(element);
            return;
        }

        if (element.Parent is Border border && ReferenceEquals(border.Child, element))
        {
            border.Child = null;
        }
    }
#endif
}

using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
#if WINDOWS
using Microsoft.UI;
#endif
using Windows.Foundation;
using SalmonEgg.Presentation.Behaviors;
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
    private ObservableCollection<ChatMessageViewModel>? _trackedMessageHistory;
    private readonly TranscriptScrollSettler _transcriptScrollSettler = new(maxReadyButNotBottomFailures: MaxInitialScrollAttempts);
    private readonly TranscriptViewportCoordinator _viewportCoordinator = new();
    private const double BottomThreshold = 10;
    private const double BottomGeometryTolerance = 2;
    private const int MaxInitialScrollAttempts = 8;
    private const int MaxRestoreAnchorAttempts = 8;
    private bool _attachToBottomIntentPending;
    private bool _pointerScrollIntentPending;
    private bool _pointerScrollReleasePending;
    private bool _suspendAutoScrollTracking;
    private bool _wasOverlayVisible;
    private bool _restoreDetachedViewportAfterOverlay;
    private string? _restoreDetachedViewportConversationId;
    private bool _resumeViewportCoordinatorAfterOverlayPending;
    private int _activeTranscriptScrollGeneration = -1;
    private bool _scrollToBottomScheduled;
    private int _scrollScheduleGeneration;
    private long _messagesListViewportTokenCallback;
    private TranscriptViewportAnchor? _pendingRestoreAnchor;
    private string? _pendingRestoreConversationId;
    private int _pendingRestoreGeneration = -1;
    private int _pendingRestoreAttemptCount;
    private readonly Microsoft.UI.Xaml.Input.KeyEventHandler _messagesListHandledKeyDownHandler;
#if WINDOWS
    private Microsoft.UI.Xaml.Controls.TitleBar? _nativeTitleBarControl;
#endif

    public MiniChatView()
    {
        ShellViewModel = App.ServiceProvider.GetRequiredService<ChatShellViewModel>();
        _messagesListHandledKeyDownHandler = OnMessagesListKeyDown;
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
        unchecked { _scrollScheduleGeneration++; }
        _restoreDetachedViewportAfterOverlay = false;
        _restoreDetachedViewportConversationId = null;
        _resumeViewportCoordinatorAfterOverlayPending = false;
        _attachToBottomIntentPending = false;
        _pointerScrollIntentPending = false;
        _pointerScrollReleasePending = false;
        ClearPendingRestoreAnchor();
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
        InvalidateViewportCoordinator();
        _isLoaded = false;
        _isMessagesListLoaded = false;
        unchecked { _scrollScheduleGeneration++; }
        _restoreDetachedViewportAfterOverlay = false;
        _restoreDetachedViewportConversationId = null;
        _resumeViewportCoordinatorAfterOverlayPending = false;
        _attachToBottomIntentPending = false;
        _pointerScrollIntentPending = false;
        _pointerScrollReleasePending = false;
        _scrollToBottomScheduled = false;
        _activeTranscriptScrollGeneration = -1;
        UnregisterViewportMonitor();
        ClearPendingRestoreAnchor();
        ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind.ColdEnter);
        DetachViewModelTracking();
    }

    private void OnMessagesListLoaded(object sender, RoutedEventArgs e)
    {
        _isMessagesListLoaded = true;
        MessagesList?.AddHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler, true);
        RegisterViewportMonitor();
        ResumeViewportCoordinatorAfterOverlayIfNeeded();
        TryApplyPendingRestoreAnchor();
        TryIssueTranscriptScrollRequest();
        TryRefreshViewportCoordinatorFromView();
    }

    private void OnMessagesListUnloaded(object sender, RoutedEventArgs e)
    {
        MessagesList?.RemoveHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler);
        UnregisterViewportMonitor();
        _isMessagesListLoaded = false;
    }

    private void OnMessagesListLayoutUpdated(object? sender, object e)
    {
        var lastItemContainerGenerated = HasLastItemContainerGenerated(ViewModel.MessageHistory.Count);
        TryApplyPendingRestoreAnchor();

        if (_transcriptScrollSettler.HasPendingWork && IsViewportDetachedByUser())
        {
            AbortTranscriptSettleRound();
            TryRefreshViewportCoordinatorFromView(lastItemContainerGenerated);
            return;
        }

        if (_transcriptScrollSettler.HasPendingWork)
        {
            TryAdvanceTranscriptSettleFromLayout(lastItemContainerGenerated);
            TryIssueTranscriptScrollRequest();
        }

        TryRefreshViewportCoordinatorFromView(lastItemContainerGenerated);
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
        _isTrackingViewModel = false;
    }

    private void OnMessageHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        ResumeViewportCoordinatorAfterOverlayIfNeeded();

        if (ViewModel.IsSessionActive
            && ViewModel.MessageHistory.Count > 0
            && !_transcriptScrollSettler.HasPendingWork
            && !IsViewportDetachedByUser())
        {
            BeginTranscriptSettleRound();
        }

        if (_transcriptScrollSettler.HasPendingWork && IsViewportDetachedByUser())
        {
            AbortTranscriptSettleRound();
            return;
        }

        if (TryIssueTranscriptScrollRequest())
        {
            return;
        }

        ApplyViewportCommand(_viewportCoordinator.Handle(new TranscriptViewportEvent.TranscriptAppended(
            CurrentViewportConversationId,
            _scrollScheduleGeneration,
            e.NewItems?.Count ?? 0)));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId)
            || e.PropertyName == nameof(ChatViewModel.IsSessionActive))
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

        if (e.PropertyName == nameof(ChatViewModel.MessageHistory))
        {
            EnsureViewModelTracking();
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

    private void RegisterViewportMonitor()
    {
        if (MessagesList is null || _messagesListViewportTokenCallback != 0)
        {
            return;
        }

        _messagesListViewportTokenCallback = MessagesList.RegisterPropertyChangedCallback(
            ScrollViewerViewportMonitor.ViewportChangeTokenProperty,
            OnMessagesListViewportChanged);
    }

    private void UnregisterViewportMonitor()
    {
        if (MessagesList is null || _messagesListViewportTokenCallback == 0)
        {
            return;
        }

        MessagesList.UnregisterPropertyChangedCallback(
            ScrollViewerViewportMonitor.ViewportChangeTokenProperty,
            _messagesListViewportTokenCallback);
        _messagesListViewportTokenCallback = 0;
    }

    private void OnMessagesListViewportChanged(DependencyObject sender, DependencyProperty dp)
    {
        TryRefreshViewportCoordinatorFromView();
    }

    private void RegisterUserViewportIntent()
    {
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
            _pointerScrollIntentPending = true;
            _pointerScrollReleasePending = false;
            StopInitialScrollForManualInteraction();
            return;
        }

        _pointerScrollIntentPending = false;
        _pointerScrollReleasePending = false;
        RegisterUserViewportDetachment();
    }

    private void RegisterUserViewportAttachIntent()
    {
        if (MessagesList is not null)
        {
            _ = MessagesList.Focus(FocusState.Programmatic);
        }

        _attachToBottomIntentPending = true;
        _pointerScrollReleasePending = true;
        if (IsListViewportAtBottom())
        {
            RegisterUserViewportAttachment();
        }
    }

    private void RegisterUserViewportDetachment()
    {
        _attachToBottomIntentPending = false;
        ClearPendingRestoreAnchor();

        TranscriptViewportCommand command;
        if (TryCaptureViewportAnchor() is { } anchor)
        {
            command = _viewportCoordinator.Handle(new TranscriptViewportEvent.UserDetached(
                CurrentViewportConversationId,
                _scrollScheduleGeneration,
                anchor));
        }
        else
        {
            command = _viewportCoordinator.Handle(new TranscriptViewportEvent.UserIntentScroll(
                CurrentViewportConversationId,
                _scrollScheduleGeneration));
        }

        ApplyViewportCommand(command);
        StopInitialScrollForManualInteraction();
    }

    private void RegisterUserViewportAttachment()
    {
        _attachToBottomIntentPending = false;
        _pointerScrollReleasePending = false;
        ClearPendingRestoreAnchor();
        ApplyViewportCommand(_viewportCoordinator.Handle(new TranscriptViewportEvent.UserAttached(
            CurrentViewportConversationId,
            _scrollScheduleGeneration)));
    }

    private TranscriptViewportAnchor? TryCaptureViewportAnchor()
    {
        if (MessagesList is null || ViewModel.MessageHistory.Count <= 0)
        {
            return null;
        }

        var firstVisibleIndex = ResolveFirstVisibleIndex();
        if (firstVisibleIndex < 0 || firstVisibleIndex >= ViewModel.MessageHistory.Count)
        {
            return null;
        }

        var message = ViewModel.MessageHistory[firstVisibleIndex];
        if (string.IsNullOrWhiteSpace(message.Id))
        {
            return null;
        }

        return new TranscriptViewportAnchor(
            message.Id,
            TranscriptViewportAnchorKind.FirstVisibleItem,
            ResolveRelativeOffsetWithinAnchor(firstVisibleIndex),
            ViewModel.MessageHistory.Count,
            ViewModel.MessageHistory.Count - firstVisibleIndex - 1,
            ResolveAnchorContentSignature(message));
    }

    private int ResolveFirstVisibleIndex()
    {
        if (MessagesList is null)
        {
            return -1;
        }

        for (var index = 0; index < ViewModel.MessageHistory.Count; index++)
        {
            if (MessagesList.ContainerFromIndex(index) is not ListViewItem container)
            {
                continue;
            }

            var anchor = container.ContentTemplateRoot as FrameworkElement ?? container;
            var relativeOrigin = anchor.TransformToVisual(MessagesList).TransformPoint(default);
            if (relativeOrigin.Y + anchor.ActualHeight >= 0)
            {
                return index;
            }
        }

        return -1;
    }

    private double ResolveRelativeOffsetWithinAnchor(int index)
    {
        if (MessagesList?.ContainerFromIndex(index) is not ListViewItem container)
        {
            return 0d;
        }

        var anchor = container.ContentTemplateRoot as FrameworkElement ?? container;
        return anchor.TransformToVisual(MessagesList).TransformPoint(default).Y;
    }

    private int ResolveMessageIndex(string messageId)
    {
        for (var index = 0; index < ViewModel.MessageHistory.Count; index++)
        {
            if (string.Equals(ViewModel.MessageHistory[index].Id, messageId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private int ResolveAnchorIndex(TranscriptViewportAnchor anchor)
    {
        var index = ResolveMessageIndex(anchor.MessageId);
        if (index >= 0)
        {
            return index;
        }

        if (ViewModel.MessageHistory.Count <= 0)
        {
            return -1;
        }

        var fallbackIndex = ViewModel.MessageHistory.Count - anchor.DistanceFromEnd - 1;
        fallbackIndex = Math.Clamp(fallbackIndex, 0, ViewModel.MessageHistory.Count - 1);
        if (string.IsNullOrWhiteSpace(anchor.ContentSignature))
        {
            return fallbackIndex;
        }

        var candidateSignature = ResolveAnchorContentSignature(ViewModel.MessageHistory[fallbackIndex]);
        return string.Equals(candidateSignature, anchor.ContentSignature, StringComparison.Ordinal)
            ? fallbackIndex
            : -1;
    }

    private static string? ResolveAnchorContentSignature(ChatMessageViewModel message)
    {
        if (!string.IsNullOrWhiteSpace(message.TextContent))
        {
            return message.TextContent;
        }

        if (!string.IsNullOrWhiteSpace(message.Title))
        {
            return message.Title;
        }

        return null;
    }

    private void TryRefreshViewportCoordinatorFromView(bool? lastItemContainerGenerated = null)
    {
        if (!_isLoaded
            || !_isMessagesListLoaded
            || MessagesList is null
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
            IsProgrammaticScrollInFlight: _suspendAutoScrollTracking || _scrollToBottomScheduled || _activeTranscriptScrollGeneration >= 0);
        if (IsViewportDetachedByUser()
            && _attachToBottomIntentPending
            && !fact.IsProgrammaticScrollInFlight)
        {
            if (fact.IsAtBottom)
            {
                RegisterUserViewportAttachment();
                return;
            }

            if (_pointerScrollReleasePending)
            {
                _attachToBottomIntentPending = false;
                _pointerScrollReleasePending = false;
            }
        }

        if (_pointerScrollIntentPending && !fact.IsProgrammaticScrollInFlight)
        {
            if (!fact.IsAtBottom)
            {
                _pointerScrollIntentPending = false;
                _pointerScrollReleasePending = false;
                RegisterUserViewportDetachment();
            }
            else if (_pointerScrollReleasePending)
            {
                _pointerScrollIntentPending = false;
                _pointerScrollReleasePending = false;
            }
        }

        ApplyViewportCommand(_viewportCoordinator.Handle(new TranscriptViewportEvent.ViewportFactChanged(
            CurrentViewportConversationId,
            _scrollScheduleGeneration,
            fact)));
    }

    private void ApplyViewportCommand(TranscriptViewportCommand command)
    {
        switch (command.Kind)
        {
            case TranscriptViewportCommandKind.IssueScrollToBottom:
                ScheduleScrollToBottom();
                break;

            case TranscriptViewportCommandKind.RestoreAnchor:
                if (command.Anchor is { } anchor)
                {
                    TryRestoreViewportAnchor(anchor);
                }
                break;

            case TranscriptViewportCommandKind.StopProgrammaticScroll:
                unchecked { _scrollScheduleGeneration++; }
                _scrollToBottomScheduled = false;
                _activeTranscriptScrollGeneration = -1;
                ClearPendingRestoreAnchor();
                if (_transcriptScrollSettler.HasPendingWork)
                {
                    AbortTranscriptSettleRound();
                }
                break;

            case TranscriptViewportCommandKind.MarkAutoFollowDetached:
                _attachToBottomIntentPending = false;
                _scrollToBottomScheduled = false;
                ClearPendingRestoreAnchor();
                break;

            case TranscriptViewportCommandKind.MarkAutoFollowAttached:
                _attachToBottomIntentPending = false;
                ClearPendingRestoreAnchor();
                break;
        }
    }

    private void ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind activationKind)
    {
        ApplyViewportCommand(_viewportCoordinator.Handle(new TranscriptViewportEvent.SessionActivated(
            _isLoaded && ViewModel.IsSessionActive
                ? CurrentViewportConversationId
                : string.Empty,
            _scrollScheduleGeneration,
            activationKind)));
    }

    private void InvalidateViewportCoordinator()
    {
        if (string.IsNullOrWhiteSpace(CurrentViewportConversationId))
        {
            return;
        }

        ApplyViewportCommand(_viewportCoordinator.Handle(new TranscriptViewportEvent.ConversationContextInvalidated(
            CurrentViewportConversationId,
            _scrollScheduleGeneration)));
    }

    private bool IsViewportDetachedByUser()
    {
        return _viewportCoordinator.State == TranscriptViewportState.DetachedByUser;
    }

    private string CurrentViewportConversationId => ViewModel.CurrentSessionId ?? string.Empty;

    private void TryRestoreViewportAnchor(TranscriptViewportAnchor anchor)
    {
        if (MessagesList is null)
        {
            return;
        }

        _pendingRestoreAnchor = anchor;
        _pendingRestoreConversationId = CurrentViewportConversationId;
        _pendingRestoreGeneration = _scrollScheduleGeneration;
        _pendingRestoreAttemptCount = 0;
        _suspendAutoScrollTracking = true;
        _scrollToBottomScheduled = false;

        var index = ResolveAnchorIndex(anchor);
        if (index < 0 || index >= ViewModel.MessageHistory.Count)
        {
            return;
        }

        MessagesList.ScrollIntoView(ViewModel.MessageHistory[index]);
        TryApplyPendingRestoreAnchor();
    }

    private void TryApplyPendingRestoreAnchor()
    {
        if (_pendingRestoreAnchor is not { } anchor
            || MessagesList is null
            || !_isLoaded)
        {
            return;
        }

        if (!string.Equals(CurrentViewportConversationId, _pendingRestoreConversationId, StringComparison.Ordinal)
            || _scrollScheduleGeneration != _pendingRestoreGeneration)
        {
            ClearPendingRestoreAnchor();
            ReleaseAutoScrollTracking();
            return;
        }

        var index = ResolveAnchorIndex(anchor);
        if (index < 0 || index >= ViewModel.MessageHistory.Count)
        {
            return;
        }

        if (MessagesList.ContainerFromIndex(index) is not ListViewItem container)
        {
            if (++_pendingRestoreAttemptCount >= MaxRestoreAnchorAttempts)
            {
                ClearPendingRestoreAnchor();
                ReleaseAutoScrollTracking();
            }

            return;
        }

        var currentRelativeOffset = ResolveRelativeOffsetWithinAnchor(index);
        if (Math.Abs(currentRelativeOffset - anchor.RelativeOffsetWithinAnchor) <= 1d)
        {
            ClearPendingRestoreAnchor();
            ReleaseAutoScrollTracking();
            return;
        }

        var verticalOffset = ScrollViewerViewportMonitor.GetVerticalOffset(MessagesList);
        var scrollViewer = ScrollViewerViewportMonitor.GetAttachedScrollViewer(MessagesList);
        if (verticalOffset < 0 || scrollViewer is null)
        {
            if (++_pendingRestoreAttemptCount >= MaxRestoreAnchorAttempts)
            {
                ClearPendingRestoreAnchor();
                ReleaseAutoScrollTracking();
            }

            return;
        }

        if (++_pendingRestoreAttemptCount >= MaxRestoreAnchorAttempts)
        {
            ClearPendingRestoreAnchor();
            ReleaseAutoScrollTracking();
            return;
        }

        var targetVerticalOffset = Math.Max(0d, verticalOffset + currentRelativeOffset - anchor.RelativeOffsetWithinAnchor);
        scrollViewer.ChangeView(null, targetVerticalOffset, null, true);
    }

    private void ClearPendingRestoreAnchor()
    {
        _pendingRestoreAnchor = null;
        _pendingRestoreConversationId = null;
        _pendingRestoreGeneration = -1;
        _pendingRestoreAttemptCount = 0;
    }

    private bool TryIssueTranscriptScrollRequest()
    {
        if (_activeTranscriptScrollGeneration >= 0 || IsViewportDetachedByUser())
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
                TryRefreshViewportCoordinatorFromView();
                return true;

            case TranscriptScrollAction.Completed:
            case TranscriptScrollAction.Aborted:
            case TranscriptScrollAction.Exhausted:
                _activeTranscriptScrollGeneration = -1;
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
            && MessagesList is not null
            && !ViewModel.IsActivationOverlayVisible
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

    private void RequestScrollToBottom()
    {
        try
        {
            if (ViewModel.MessageHistory.Count > 0)
            {
                MessagesList.ScrollIntoView(ViewModel.MessageHistory[^1]);
            }
        }
        catch
        {
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
                if (!_isLoaded
                    || !_isMessagesListLoaded
                    || scheduleGeneration != _scrollScheduleGeneration
                    || !_viewportCoordinator.IsAutoFollowAttached
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

    private bool HasLastItemContainerGenerated(int itemCount)
    {
        if (!_isMessagesListLoaded || itemCount <= 0)
        {
            return false;
        }

        return MessagesList.ContainerFromIndex(itemCount - 1) is not null;
    }

    private bool IsListViewportAtBottom()
    {
        if (!_isMessagesListLoaded || MessagesList is null)
        {
            return false;
        }

        var itemCount = ViewModel.MessageHistory.Count;
        if (itemCount <= 0)
        {
            return true;
        }

        var monitoredScrollableHeight = ScrollViewerViewportMonitor.GetScrollableHeight(MessagesList);
        if (monitoredScrollableHeight >= 0)
        {
            var monitoredVerticalOffset = ScrollViewerViewportMonitor.GetVerticalOffset(MessagesList);
            return monitoredScrollableHeight - monitoredVerticalOffset <= GetBottomViewportTolerance();
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

    private double GetBottomViewportTolerance()
    {
        return BottomThreshold + BottomGeometryTolerance + (MessagesList?.Padding.Bottom ?? 0);
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
                return true;

            default:
                return false;
        }
    }

    private void StopInitialScrollForManualInteraction()
    {
        _scrollToBottomScheduled = false;
        _suspendAutoScrollTracking = false;

        if (!_transcriptScrollSettler.HasPendingWork)
        {
            return;
        }

        _activeTranscriptScrollGeneration = -1;
        ApplyTranscriptSettleDecision(
            _transcriptScrollSettler.AbortForUserInteraction(),
            HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
    }

    private void ReleaseAutoScrollTracking()
    {
        _ = DispatcherQueue.TryEnqueue(() => _suspendAutoScrollTracking = false);
    }

    private void ResetAutoScrollStateForConversationChange()
    {
        InvalidateViewportCoordinator();
        unchecked { _scrollScheduleGeneration++; }
        _activeTranscriptScrollGeneration = -1;
        _suspendAutoScrollTracking = false;
        _scrollToBottomScheduled = false;
        _attachToBottomIntentPending = false;
        _pointerScrollIntentPending = false;
        _pointerScrollReleasePending = false;
        ClearPendingRestoreAnchor();
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
        _pointerScrollIntentPending = false;
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
            _attachToBottomIntentPending = false;
            _pointerScrollIntentPending = false;
            _pointerScrollReleasePending = false;
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
            || MessagesList is null
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
            _attachToBottomIntentPending = true;
            _pointerScrollReleasePending = false;
            if (MessagesList is not null)
            {
                _ = MessagesList.Focus(FocusState.Programmatic);
            }

            return;
        }

        _pointerScrollIntentPending = true;
        _pointerScrollReleasePending = false;
        if (MessagesList is not null)
        {
            _ = MessagesList.Focus(FocusState.Programmatic);
        }
    }

    private void OnMessagesListPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _pointerScrollReleasePending = true;
        var releaseGeneration = _scrollScheduleGeneration;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (releaseGeneration != _scrollScheduleGeneration
                || ViewModel.IsActivationOverlayVisible)
            {
                return;
            }

            if (IsViewportDetachedByUser())
            {
                TryRefreshViewportCoordinatorFromView();
                if (_attachToBottomIntentPending)
                {
                    _attachToBottomIntentPending = false;
                    _pointerScrollReleasePending = false;
                }

                return;
            }

            if (!_pointerScrollIntentPending
                || !_pointerScrollReleasePending)
            {
                return;
            }

            if (IsListViewportAtBottom())
            {
                _pointerScrollIntentPending = false;
                _pointerScrollReleasePending = false;
            }
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

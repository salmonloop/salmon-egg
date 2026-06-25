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
    public ListViewTranscriptItemsSource MessagesItemsSource { get; } = new();
    private bool _isLoaded;
    private bool _isMessagesListLoaded;
    private bool _isTrackingViewModel;
    private INotifyCollectionChanged? _trackedMessageHistory;
    private readonly TranscriptViewportController _viewportController = new();
    private const double BottomThreshold = 10;
    private const double BottomGeometryTolerance = 2;
    private const int MaxRestoreAttempts = 32;
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
        _restoreDetachedViewportAfterOverlay = false;
        _restoreDetachedViewportConversationId = null;
        _resumeViewportCoordinatorAfterOverlayPending = false;
        ClearPendingProjectionRestore();
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
        EnsureViewModelTracking();
        TryIssueTranscriptScrollRequestIfAttached();

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
        ApplyViewportActions(_viewportController.Unload());
        _isLoaded = false;
        _isMessagesListLoaded = false;
        _restoreDetachedViewportAfterOverlay = false;
        _restoreDetachedViewportConversationId = null;
        _resumeViewportCoordinatorAfterOverlayPending = false;
        DisposeTranscriptViewportHost();
        ClearPendingProjectionRestore();
        DetachViewModelTracking();
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

        _isMessagesListLoaded = true;
        messagesList?.AddHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler, true);
        messagesList?.AddHandler(UIElement.PointerPressedEvent, _messagesListHandledPointerPressedHandler, true);
        messagesList?.AddHandler(UIElement.PointerWheelChangedEvent, _messagesListHandledPointerWheelChangedHandler, true);
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
        MessagesItemsSource.Detach();
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
        ApplyViewportActions(_viewportController.OnMessagesAppended(
            e.NewItems?.Count ?? 0,
            CreateViewportViewState()));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId))
        {
            ResetAutoScrollStateForConversationChange();
            _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
            TryIssueTranscriptScrollRequestIfAttached();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.IsSessionActive))
        {
            if (_viewportController.State is TranscriptViewportState.DetachedPendingRestore
                or TranscriptViewportState.DetachedRestoring)
            {
                TryApplyPendingProjectionRestore();
                TryRefreshViewportCoordinatorFromView();
                return;
            }

            ResetAutoScrollStateForConversationChange();
            _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
            TryIssueTranscriptScrollRequestIfAttached();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.MessageHistory))
        {
            EnsureViewModelTracking();
            if (_viewportController.State is TranscriptViewportState.DetachedPendingRestore
                or TranscriptViewportState.DetachedRestoring)
            {
                TryApplyPendingProjectionRestore();
                TryRefreshViewportCoordinatorFromView();
                return;
            }

            ResetAutoScrollStateForConversationChange();
            TryIssueTranscriptScrollRequestIfAttached();
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
        TryApplyPendingProjectionRestore();
        ApplyViewportActions(_viewportController.OnViewportChanged(
            CreateViewportViewState(lastItemContainerGenerated),
            TryCaptureProjectionRestoreToken()));
    }

    private void RegisterUserViewportIntent()
    {
        if (_projectionRestoreController.HasPending)
        {
            AbandonPendingProjectionRestore("UserInterrupted");
        }

        if (IsViewportDetachedByUser())
        {
            if (MessagesList is not null)
            {
                _ = MessagesList.Focus(FocusState.Programmatic);
            }

            ApplyViewportActions(_viewportController.OnUserViewportIntent(CreateViewportViewState()));
            return;
        }

        if (MessagesList is not null)
        {
            _ = MessagesList.Focus(FocusState.Programmatic);
        }

        if (IsListViewportAtBottom())
        {
            ApplyViewportActions(_viewportController.OnUserViewportDetachIntent(
                CreateViewportViewState(),
                TryCaptureProjectionRestoreToken()));
            return;
        }

        ApplyViewportActions(_viewportController.OnUserViewportDetachIntent(
            CreateViewportViewState(),
            TryCaptureProjectionRestoreToken()));
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

        ApplyViewportActions(_viewportController.OnViewportChanged(
            CreateViewportViewState(lastItemContainerGenerated),
            TryCaptureProjectionRestoreToken()));
    }

    private TranscriptViewportViewState CreateViewportViewState(bool? lastItemContainerGenerated = null)
    {
        var messageCount = ViewModel.MessageHistory.Count;
        var hasMessages = messageCount > 0;
        return new TranscriptViewportViewState(
            IsViewReady: _isLoaded
                && _isMessagesListLoaded
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
            case TranscriptViewportControllerActionKind.AutoFollowDetached:
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
        if (_transcriptViewportHost is null || !_isLoaded)
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

    private bool TryIssueTranscriptScrollRequest()
    {
        var actions = _viewportController.OnViewportChanged(CreateViewportViewState());
        ApplyViewportActions(actions);
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
                || !_viewportController.MatchesActiveScrollRequest(requestToken))
            {
                return;
            }

            var lastItemContainerGenerated = HasLastItemContainerGenerated(ViewModel.MessageHistory.Count);
            ObserveActiveTranscriptScrollFromLayout(lastItemContainerGenerated);
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

    private bool ObserveActiveTranscriptScrollFromLayout(bool? lastItemContainerGenerated = null)
    {
        var actions = _viewportController.OnActiveScrollObservation(CreateViewportViewState(lastItemContainerGenerated));
        ApplyViewportActions(actions);
        return actions.Count > 0;
    }

    private void ResetAutoScrollStateForConversationChange()
    {
        AbandonPendingProjectionRestore("ConversationChanged");
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
        ApplyViewportActions(_viewportController.OnConversationChanged(
            CurrentViewportConversationId,
            ViewModel.IsSessionActive,
            ViewModel.IsActivationOverlayVisible,
            ViewModel.MessageHistory.Count > 0));
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
    }

    private void RestoreViewportForWarmResume()
    {
        if (!_isLoaded
            || !ViewModel.IsSessionActive
            || ViewModel.IsActivationOverlayVisible
            || string.IsNullOrWhiteSpace(ViewModel.CurrentSessionId))
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isLoaded
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
        });
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

        _viewportController.MarkUserScrollIntentStarted();
        if (MessagesList is not null)
        {
            _ = MessagesList.Focus(FocusState.Programmatic);
        }
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

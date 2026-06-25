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
        public ListViewTranscriptItemsSource MessagesItemsSource { get; } = new();
        public ShellLayoutViewModel LayoutVM => ShellViewModel.ShellLayout;
        public UiMotion Motion => UiMotion.Current;
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
        private readonly TranscriptProjectionRestoreController _projectionRestoreController = new(MaxRestoreAttempts);
        private string _transcriptViewportAutomationState = "inactive";
        private INotifyCollectionChanged? _trackedMessageHistory;
        private readonly Microsoft.UI.Xaml.Input.KeyEventHandler _messagesListHandledKeyDownHandler;
        private readonly PointerEventHandler _messagesListHandledPointerPressedHandler;
        private readonly PointerEventHandler _messagesListHandledPointerWheelChangedHandler;
        private ITranscriptViewportHost? _transcriptViewportHost;
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

            messagesList?.AddHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler, true);
            messagesList?.AddHandler(UIElement.PointerPressedEvent, _messagesListHandledPointerPressedHandler, true);
            messagesList?.AddHandler(UIElement.PointerWheelChangedEvent, _messagesListHandledPointerWheelChangedHandler, true);
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

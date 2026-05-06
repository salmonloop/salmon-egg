using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SalmonEgg.Presentation.Behaviors;
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
        private readonly TranscriptViewportCoordinator _viewportCoordinator = new();
        private const double BottomThreshold = 10;
        private const double BottomGeometryTolerance = 2;
        private const int MaxInitialScrollAttempts = 8;
        private const int MaxRestoreAttempts = 8;
        private bool _attachToBottomIntentPending;
        private bool _pointerScrollIntentPending;
        private bool _pointerScrollReleasePending;
        private bool _suspendAutoScrollTracking;
        private bool _wasOverlayVisible;
        private bool _restoreDetachedViewportAfterOverlay;
        private string? _restoreDetachedViewportConversationId;
        private bool _resumeViewportCoordinatorAfterOverlayPending;
        private bool _scrollToBottomScheduled;
        private int _scrollScheduleGeneration;
        private int _scheduledScrollRequestVersion;
        private int _activeTranscriptScrollGeneration = -1;
        private TranscriptProjectionRestoreToken? _pendingRestoreToken;
        private string? _pendingRestoreConversationId;
        private int _pendingRestoreGeneration = -1;
        private int _pendingRestoreAttemptCount;
        private int _pendingRestoreResolvedIndex = -1;
        private int _pendingRestoreRequestedMaterializationIndex = -1;
        private double _pendingRestoreRequestedVerticalOffset = double.NaN;
        private string _transcriptViewportAutomationState = "inactive";
        private ObservableCollection<ChatMessageViewModel>? _trackedMessageHistory;
        private long _messagesListViewportTokenCallback;
        private readonly Microsoft.UI.Xaml.Input.KeyEventHandler _messagesListHandledKeyDownHandler;
        private readonly PointerEventHandler _messagesListHandledPointerPressedHandler;
        private readonly PointerEventHandler _messagesListHandledPointerWheelChangedHandler;

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
            unchecked { _scrollScheduleGeneration++; }
            _restoreDetachedViewportAfterOverlay = false;
            _restoreDetachedViewportConversationId = null;
            _resumeViewportCoordinatorAfterOverlayPending = false;
            _attachToBottomIntentPending = false;
            _pointerScrollIntentPending = false;
            _pointerScrollReleasePending = false;
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
            unchecked { _scrollScheduleGeneration++; }
            _restoreDetachedViewportAfterOverlay = false;
            _restoreDetachedViewportConversationId = null;
            _resumeViewportCoordinatorAfterOverlayPending = false;
            _attachToBottomIntentPending = false;
            _pointerScrollIntentPending = false;
            _pointerScrollReleasePending = false;
            _scrollToBottomScheduled = false;
            _activeTranscriptScrollGeneration = -1;
            ClearPendingProjectionRestore();
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
                && !_transcriptScrollSettler.HasPendingWork
                && !IsViewportDetachedByUser())
            {
                BeginTranscriptSettleRound();
            }

            BeginLayoutLoadingIfPendingMessages();

            if (_transcriptScrollSettler.HasPendingWork && IsViewportDetachedByUser())
            {
                AbortTranscriptSettleRound();
                RefreshLayoutLoadingState();
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

            UpdateTranscriptViewportAutomationState();
        }

        private void OnMessagesListLoaded(object sender, RoutedEventArgs e)
        {
            MessagesList?.AddHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler, true);
            MessagesList?.AddHandler(UIElement.PointerPressedEvent, _messagesListHandledPointerPressedHandler, true);
            MessagesList?.AddHandler(UIElement.PointerWheelChangedEvent, _messagesListHandledPointerWheelChangedHandler, true);
            RegisterViewportMonitor();
            ResumeViewportCoordinatorAfterOverlayIfNeeded();
            BeginLayoutLoadingIfPendingMessages();
            TryApplyPendingProjectionRestore();
            TryIssueTranscriptScrollRequest();
            TryRefreshViewportCoordinatorFromView();
            UpdateTranscriptViewportAutomationState();
        }

        private void OnMessagesListUnloaded(object sender, RoutedEventArgs e)
        {
            MessagesList?.RemoveHandler(UIElement.KeyDownEvent, _messagesListHandledKeyDownHandler);
            MessagesList?.RemoveHandler(UIElement.PointerPressedEvent, _messagesListHandledPointerPressedHandler);
            MessagesList?.RemoveHandler(UIElement.PointerWheelChangedEvent, _messagesListHandledPointerWheelChangedHandler);
            UnregisterViewportMonitor();
            UpdateTranscriptViewportAutomationState();
        }

        private void OnMessagesListLayoutUpdated(object? sender, object e)
        {
            var lastItemContainerGenerated = HasLastItemContainerGenerated(ViewModel.MessageHistory.Count);
            TryApplyPendingProjectionRestore();
            RefreshLayoutLoadingState(lastItemContainerGenerated);

            if (_transcriptScrollSettler.HasPendingWork && IsViewportDetachedByUser())
            {
                AbortTranscriptSettleRound();
                RefreshLayoutLoadingState(lastItemContainerGenerated);
                TryRefreshViewportCoordinatorFromView(lastItemContainerGenerated);
                UpdateTranscriptViewportAutomationState();
                return;
            }

            if (_transcriptScrollSettler.HasPendingWork)
            {
                TryAdvanceTranscriptSettleFromLayout(lastItemContainerGenerated);
                TryIssueTranscriptScrollRequest();
            }

            TryRefreshViewportCoordinatorFromView(lastItemContainerGenerated);

            UpdateTranscriptViewportAutomationState();
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
            TryApplyPendingProjectionRestore();
            TryRefreshViewportCoordinatorFromView();
            UpdateTranscriptViewportAutomationState();
        }

        private void OnMessagesListPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (IsViewportDetachedByUser())
            {
                _attachToBottomIntentPending = true;
                _pointerScrollReleasePending = false;
                FocusTranscriptScroller();
                return;
            }

            if (_pendingRestoreToken is not null)
            {
                AbandonPendingProjectionRestore("UserInterrupted");
            }

            _pointerScrollIntentPending = true;
            _pointerScrollReleasePending = false;
            FocusTranscriptScroller();
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
                    UpdateTranscriptViewportAutomationState();
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

        private void FocusTranscriptScroller()
        {
            if (MessagesList is not null)
            {
                _ = MessagesList.Focus(FocusState.Programmatic);
            }
        }

        private void RegisterUserViewportIntent()
        {
            if (_pendingRestoreToken is not null)
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
                _pointerScrollIntentPending = true;
                _pointerScrollReleasePending = false;
                StopInitialScrollForManualInteraction();
                UpdateTranscriptViewportAutomationState();
                return;
            }

            _pointerScrollIntentPending = false;
            _pointerScrollReleasePending = false;
            RegisterUserViewportDetachment();
            UpdateTranscriptViewportAutomationState();
        }

        private void RegisterUserViewportAttachIntent()
        {
            FocusTranscriptScroller();
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
            AbandonPendingProjectionRestore("UserInterrupted");

            TranscriptViewportCommand command;
            if (TryCaptureProjectionRestoreToken() is { } restoreToken)
            {
                command = _viewportCoordinator.Handle(new TranscriptViewportEvent.UserDetached(
                    CurrentViewportConversationId,
                    _scrollScheduleGeneration,
                    restoreToken));
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
            AbandonPendingProjectionRestore("UserAttached");
            ApplyViewportCommand(_viewportCoordinator.Handle(new TranscriptViewportEvent.UserAttached(
                CurrentViewportConversationId,
                _scrollScheduleGeneration)));
        }

        private TranscriptProjectionRestoreToken? TryCaptureProjectionRestoreToken()
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
            return ViewModel.CreateViewportProjectionRestoreToken(
                message,
                ResolveRelativeOffsetWithinAnchor(firstVisibleIndex));
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

        private int ResolveProjectionRestoreIndex(TranscriptProjectionRestoreToken token)
        {
            for (var index = 0; index < ViewModel.MessageHistory.Count; index++)
            {
                if (string.Equals(
                        ViewModel.MessageHistory[index].ProjectionItemKey,
                        token.ProjectionItemKey,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private void OnProjectionRestoreReady(object? sender, ProjectionRestoreReadyEventArgs e)
        {
            if (!_isViewLoaded
                || !ViewModel.IsSessionActive
                || string.IsNullOrWhiteSpace(e.ConversationId))
            {
                return;
            }

            ApplyViewportCommand(_viewportCoordinator.Handle(new TranscriptViewportEvent.ProjectionReady(
                e.ConversationId,
                _scrollScheduleGeneration,
                e.ProjectionEpoch)));
            TryApplyPendingProjectionRestore();
            UpdateTranscriptViewportAutomationState();
        }

        private void TryRefreshViewportCoordinatorFromView(bool? lastItemContainerGenerated = null)
        {
            if (!_isViewLoaded
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

            if (_pointerScrollIntentPending)
            {
                if (!fact.IsAtBottom)
                {
                    if (fact.IsProgrammaticScrollInFlight)
                    {
                        StopInitialScrollForManualInteraction();
                    }

                    _pointerScrollIntentPending = false;
                    _pointerScrollReleasePending = false;
                    RegisterUserViewportDetachment();
                    return;
                }
                else if (!fact.IsProgrammaticScrollInFlight
                    && _pointerScrollReleasePending)
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

                case TranscriptViewportCommandKind.RequestRestore:
                    if (command.RestoreToken is { } restoreToken)
                    {
                        QueueProjectionOwnedRestore(restoreToken, command.Generation);
                    }
                    break;

                case TranscriptViewportCommandKind.StopProgrammaticScroll:
                    unchecked { _scrollScheduleGeneration++; }
                    _scrollToBottomScheduled = false;
                    _activeTranscriptScrollGeneration = -1;
                    ClearPendingProjectionRestore();
                    if (_transcriptScrollSettler.HasPendingWork)
                    {
                        AbortTranscriptSettleRound();
                    }
                    break;

                case TranscriptViewportCommandKind.MarkAutoFollowDetached:
                    _attachToBottomIntentPending = false;
                    _scrollToBottomScheduled = false;
                    ClearPendingProjectionRestore();
                    break;

                case TranscriptViewportCommandKind.MarkAutoFollowAttached:
                    _attachToBottomIntentPending = false;
                    ClearPendingProjectionRestore();
                    break;
            }
        }

        private void ActivateViewportCoordinatorForCurrentSession(TranscriptViewportActivationKind activationKind)
        {
            ApplyViewportCommand(_viewportCoordinator.Handle(new TranscriptViewportEvent.SessionActivated(
                _isViewLoaded && ViewModel.IsSessionActive
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
            return _viewportCoordinator.State is TranscriptViewportState.DetachedByUser
                or TranscriptViewportState.DetachedPendingRestore
                or TranscriptViewportState.DetachedRestoring;
        }

        private string CurrentViewportConversationId => ViewModel.CurrentSessionId ?? string.Empty;

        private void QueueProjectionOwnedRestore(TranscriptProjectionRestoreToken token, int generation)
        {
            _pendingRestoreToken = token;
            _pendingRestoreConversationId = token.ConversationId;
            _pendingRestoreGeneration = generation;
            _pendingRestoreAttemptCount = 0;
            _pendingRestoreResolvedIndex = -1;
            _suspendAutoScrollTracking = true;
            _scrollToBottomScheduled = false;
            TryApplyPendingProjectionRestore();
        }

        private void TryApplyPendingProjectionRestore()
        {
            if (_pendingRestoreToken is not { } token
                || MessagesList is null
                || !_isViewLoaded)
            {
                return;
            }

            if (!string.Equals(CurrentViewportConversationId, token.ConversationId, StringComparison.Ordinal)
                || _scrollScheduleGeneration != _pendingRestoreGeneration)
            {
                AbandonPendingProjectionRestore("RestoreContextChanged");
                return;
            }

            var index = ResolvePendingProjectionRestoreIndex(token);
            if (index < 0 || index >= ViewModel.MessageHistory.Count)
            {
                ReportPendingProjectionRestoreUnavailable("ProjectionItemMissing");
                return;
            }

            if (MessagesList.ContainerFromIndex(index) is not ListViewItem)
            {
                if (_pendingRestoreRequestedMaterializationIndex == index)
                {
                    return;
                }

                if (++_pendingRestoreAttemptCount >= MaxRestoreAttempts)
                {
                    ReportPendingProjectionRestoreUnavailable("ProjectionItemNotMaterialized");
                    return;
                }

                _pendingRestoreRequestedMaterializationIndex = index;
                MessagesList.ScrollIntoView(ViewModel.MessageHistory[index]);
                return;
            }

            _pendingRestoreRequestedMaterializationIndex = -1;
            var currentRelativeOffset = ResolveRelativeOffsetWithinAnchor(index);
            if (Math.Abs(currentRelativeOffset - token.OffsetHint) <= 1d)
            {
                ConfirmPendingProjectionRestore(token);
                return;
            }

            var verticalOffset = ScrollViewerViewportMonitor.GetVerticalOffset(MessagesList);
            var scrollViewer = ScrollViewerViewportMonitor.GetAttachedScrollViewer(MessagesList);
            if (verticalOffset < 0 || scrollViewer is null)
            {
                if (++_pendingRestoreAttemptCount >= MaxRestoreAttempts)
                {
                    ReportPendingProjectionRestoreUnavailable("ScrollViewerUnavailable");
                }

                return;
            }

            if (!double.IsNaN(_pendingRestoreRequestedVerticalOffset))
            {
                if (Math.Abs(verticalOffset - _pendingRestoreRequestedVerticalOffset) > 1d)
                {
                    return;
                }

                _pendingRestoreRequestedVerticalOffset = double.NaN;
            }

            var targetVerticalOffset = Math.Max(0d, verticalOffset + currentRelativeOffset - token.OffsetHint);
            if (Math.Abs(targetVerticalOffset - verticalOffset) <= 1d)
            {
                ReportPendingProjectionRestoreUnavailable("OffsetHintUnresolved");
                return;
            }

            if (++_pendingRestoreAttemptCount >= MaxRestoreAttempts)
            {
                ReportPendingProjectionRestoreUnavailable("OffsetHintUnresolved");
                return;
            }

            _pendingRestoreRequestedVerticalOffset = targetVerticalOffset;
            scrollViewer.ChangeView(null, targetVerticalOffset, null, true);
        }

        private int ResolvePendingProjectionRestoreIndex(TranscriptProjectionRestoreToken token)
        {
            if (_pendingRestoreResolvedIndex >= 0
                && _pendingRestoreResolvedIndex < ViewModel.MessageHistory.Count
                && string.Equals(
                    ViewModel.MessageHistory[_pendingRestoreResolvedIndex].ProjectionItemKey,
                    token.ProjectionItemKey,
                    StringComparison.Ordinal))
            {
                return _pendingRestoreResolvedIndex;
            }

            _pendingRestoreResolvedIndex = ResolveProjectionRestoreIndex(token);
            return _pendingRestoreResolvedIndex;
        }

        private void ConfirmPendingProjectionRestore(TranscriptProjectionRestoreToken token)
        {
            var generation = _pendingRestoreGeneration;
            ClearPendingProjectionRestore();
            ReleaseAutoScrollTracking();
            ApplyViewportCommand(_viewportCoordinator.Handle(new TranscriptViewportEvent.RestoreConfirmed(
                token.ConversationId,
                generation,
                token)));
        }

        private void ReportPendingProjectionRestoreUnavailable(string reason)
        {
            var conversationId = _pendingRestoreConversationId ?? CurrentViewportConversationId;
            var generation = _pendingRestoreGeneration;
            ClearPendingProjectionRestore();
            ReleaseAutoScrollTracking();
            ApplyViewportCommand(_viewportCoordinator.Handle(new TranscriptViewportEvent.RestoreUnavailable(
                conversationId,
                generation,
                reason)));
        }

        private void AbandonPendingProjectionRestore(string reason)
        {
            if (_pendingRestoreToken is null)
            {
                ClearPendingProjectionRestore();
                return;
            }

            var conversationId = _pendingRestoreConversationId ?? CurrentViewportConversationId;
            var generation = _pendingRestoreGeneration;
            ClearPendingProjectionRestore();
            ReleaseAutoScrollTracking();
            ApplyViewportCommand(_viewportCoordinator.Handle(new TranscriptViewportEvent.RestoreAbandoned(
                conversationId,
                generation,
                reason)));
        }

        private void ClearPendingProjectionRestore()
        {
            _pendingRestoreToken = null;
            _pendingRestoreConversationId = null;
            _pendingRestoreGeneration = -1;
            _pendingRestoreAttemptCount = 0;
            _pendingRestoreResolvedIndex = -1;
            _pendingRestoreRequestedMaterializationIndex = -1;
            _pendingRestoreRequestedVerticalOffset = double.NaN;
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
            var scheduledRequestVersion = _scheduledScrollRequestVersion;
            var scheduledConversationId = ViewModel.CurrentSessionId;
            _scrollToBottomScheduled = true;
            if (!DispatcherQueue.TryEnqueue(() =>
                {
                    _scrollToBottomScheduled = false;
                    if (!_isViewLoaded
                        || scheduleGeneration != _scrollScheduleGeneration
                        || scheduledRequestVersion != _scheduledScrollRequestVersion
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
                if (_viewportCoordinator.State is TranscriptViewportState.DetachedPendingRestore
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
                if (_viewportCoordinator.State is TranscriptViewportState.DetachedPendingRestore
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
                    RefreshLayoutLoadingState(HasLastItemContainerGenerated(ViewModel.MessageHistory.Count));
                    TryRefreshViewportCoordinatorFromView();
                    UpdateTranscriptViewportAutomationState();
                    return true;

                case TranscriptScrollAction.Completed:
                case TranscriptScrollAction.Aborted:
                case TranscriptScrollAction.Exhausted:
                    _activeTranscriptScrollGeneration = -1;
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

            var requestGeneration = _activeTranscriptScrollGeneration;
            var requestConversationId = ViewModel.CurrentSessionId;
            RequestScrollToBottom();

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isViewLoaded
                    || MessagesList is null
                    || ViewModel.MessageHistory.Count <= 0
                    || requestGeneration < 0
                    || _activeTranscriptScrollGeneration != requestGeneration
                    || !string.Equals(ViewModel.CurrentSessionId, requestConversationId, StringComparison.Ordinal))
                {
                    return;
                }

                RequestScrollToBottom();
            });
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

            var monitoredScrollableHeight = ScrollViewerViewportMonitor.GetScrollableHeight(MessagesList);
            if (monitoredScrollableHeight >= 0)
            {
                var monitoredVerticalOffset = ScrollViewerViewportMonitor.GetVerticalOffset(MessagesList);
                return monitoredScrollableHeight - monitoredVerticalOffset <= GetBottomViewportTolerance();
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

            return IsLastItemVisiblyAtBottom(itemCount)
                ? TranscriptScrollSettleObservation.AtBottom
                : TranscriptScrollSettleObservation.ReadyButNotAtBottom;
        }

        private bool IsLastItemVisiblyAtBottom(int itemCount)
        {
            if (MessagesList is null || itemCount <= 0)
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
            _suspendAutoScrollTracking = false;
            _scrollToBottomScheduled = false;
            unchecked { _scheduledScrollRequestVersion++; }

            if (!_transcriptScrollSettler.HasPendingWork)
            {
                UpdateTranscriptViewportAutomationState();
                return;
            }

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
            AbandonPendingProjectionRestore("ConversationChanged");
            InvalidateViewportCoordinator();
            unchecked { _scrollScheduleGeneration++; }
            _activeTranscriptScrollGeneration = -1;
            _suspendAutoScrollTracking = false;
            _scrollToBottomScheduled = false;
            unchecked { _scheduledScrollRequestVersion++; }
            _attachToBottomIntentPending = false;
            _pointerScrollIntentPending = false;
            _pointerScrollReleasePending = false;
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
            _pointerScrollIntentPending = false;
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
                _attachToBottomIntentPending = false;
                _pointerScrollIntentPending = false;
                _pointerScrollReleasePending = false;
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

            var transition = _viewportCoordinator.LastTransition;
            var debug = $"state={state};coord={_viewportCoordinator.State};attached={_viewportCoordinator.IsAutoFollowAttached};current={CurrentViewportConversationId};generation={_scrollScheduleGeneration};transition={(transition?.Reason ?? "<none>")};attachPending={_attachToBottomIntentPending};detachPending={_pointerScrollIntentPending};releasePending={_pointerScrollReleasePending};restoreConversation={_pendingRestoreConversationId ?? "<none>"};restoreGeneration={_pendingRestoreGeneration}";
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

            return _viewportCoordinator.State switch
            {
                TranscriptViewportState.Idle => "untracked",
                TranscriptViewportState.Settling => "pending",
                TranscriptViewportState.Following => "bottom",
                TranscriptViewportState.DetachedByUser => "not_bottom",
                TranscriptViewportState.DetachedPendingRestore => "not_bottom",
                TranscriptViewportState.DetachedRestoring => "not_bottom",
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
}

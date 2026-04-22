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
    private bool _userScrolledUp;
    private readonly TranscriptScrollSettler _transcriptScrollSettler = new(maxReadyButNotBottomFailures: MaxInitialScrollAttempts);
    private const double BottomThreshold = 10;
    private const double BottomGeometryTolerance = 2;
    private const int MaxInitialScrollAttempts = 8;
    private bool _suspendAutoScrollTracking;
    private bool _manualScrollIntentPending;
    private bool _wasOverlayVisible;
    private int _activeTranscriptScrollGeneration = -1;
    private bool _scrollToBottomScheduled;
    private int _scrollScheduleGeneration;
#if WINDOWS
    private Microsoft.UI.Xaml.Controls.TitleBar? _nativeTitleBarControl;
#endif

    public MiniChatView()
    {
        ShellViewModel = App.ServiceProvider.GetRequiredService<ChatShellViewModel>();
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
        _userScrolledUp = false;
        _manualScrollIntentPending = false;
        _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
        BeginTranscriptSettleRound();
        EnsureViewModelTracking();
        TryIssueTranscriptScrollRequest();

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
        _isLoaded = false;
        _isMessagesListLoaded = false;
        unchecked { _scrollScheduleGeneration++; }
        _scrollToBottomScheduled = false;
        _activeTranscriptScrollGeneration = -1;
        DetachViewModelTracking();
    }

    private void OnMessagesListLoaded(object sender, RoutedEventArgs e)
    {
        _isMessagesListLoaded = true;
        TryIssueTranscriptScrollRequest();
    }

    private void OnMessagesListUnloaded(object sender, RoutedEventArgs e)
    {
        _isMessagesListLoaded = false;
    }

    private void OnMessagesListLayoutUpdated(object? sender, object e)
    {
        var lastItemContainerGenerated = HasLastItemContainerGenerated(ViewModel.MessageHistory.Count);

        if (_manualScrollIntentPending && !_transcriptScrollSettler.HasPendingWork && !_suspendAutoScrollTracking)
        {
            _userScrolledUp = !IsListViewportAtBottom();
            _manualScrollIntentPending = false;
        }

        if (!_transcriptScrollSettler.HasPendingWork || _userScrolledUp)
        {
            return;
        }

        TryAdvanceTranscriptSettleFromLayout(lastItemContainerGenerated);
        TryIssueTranscriptScrollRequest();
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

        if (ViewModel.IsSessionActive && ViewModel.MessageHistory.Count > 0 && !_transcriptScrollSettler.HasPendingWork)
        {
            BeginTranscriptSettleRound();
        }

        if (_transcriptScrollSettler.HasPendingWork && _userScrolledUp)
        {
            AbortTranscriptSettleRound();
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
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId)
            || e.PropertyName == nameof(ChatViewModel.IsSessionActive))
        {
            ResetAutoScrollStateForConversationChange();
            _wasOverlayVisible = ViewModel.IsActivationOverlayVisible;
            BeginTranscriptSettleRound();
            TryIssueTranscriptScrollRequest();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.MessageHistory))
        {
            EnsureViewModelTracking();
            ResetAutoScrollStateForConversationChange();
            BeginTranscriptSettleRound();
            TryIssueTranscriptScrollRequest();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.IsActivationOverlayVisible))
        {
            HandleOverlayVisibilityChanged();
        }
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
                return true;

            case TranscriptScrollAction.Completed:
            case TranscriptScrollAction.Aborted:
            case TranscriptScrollAction.Exhausted:
                _activeTranscriptScrollGeneration = -1;
                ReleaseAutoScrollTracking();
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
    }

    private void HandleOverlayVisibilityChanged()
    {
        var isOverlayVisible = ViewModel.IsActivationOverlayVisible;
        var overlayJustDismissed = _wasOverlayVisible && !isOverlayVisible;
        _wasOverlayVisible = isOverlayVisible;

        if (!overlayJustDismissed
            || _userScrolledUp
            || !_isLoaded
            || MessagesList is null
            || ViewModel.MessageHistory.Count <= 0)
        {
            return;
        }

        BeginTranscriptSettleRound();
        TryIssueTranscriptScrollRequest();
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

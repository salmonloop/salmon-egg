using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Views.MiniWindow;

public sealed partial class MiniChatView : Page
{
    public ChatViewModel ViewModel { get; }
    private bool _isLoaded;
    private bool _isMessagesListLoaded;
    private bool _isTrackingViewModel;
    private ScrollViewer? _scrollViewer;
    private bool _userScrolledUp;
    private readonly InitialScrollGate _initialScrollGate = new();
    private const double BottomThreshold = 10;
    private const int MaxInitialScrollAttempts = 8;
    private bool _suspendAutoScrollTracking;

    public MiniChatView()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public FrameworkElement TitleBarElement => MiniTitleBar;

    public IReadOnlyList<FrameworkElement> TitleBarInteractiveElements
        => new FrameworkElement[]
        {
            MiniTitleBarSessionSelector,
            MiniTitleBarReturnButton
        };

    public void SetTitleBarInsets(double leftInset, double rightInset)
    {
        MiniTitleBarLeftInsetColumn.Width = new GridLength(Math.Max(0, leftInset));
        MiniTitleBarRightInsetColumn.Width = new GridLength(Math.Max(0, rightInset));
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        _userScrolledUp = false;
        _initialScrollGate.MarkPending();
        EnsureViewModelTracking();
        RequestInitialScroll();

        try
        {
            await ViewModel.RestoreConversationsAsync();
        }
        catch
        {
        }

        RequestInitialScroll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _isMessagesListLoaded = false;
        _initialScrollGate.CancelInFlight();
        DetachViewModelTracking();
        DetachScrollViewer();
    }

    private void OnMessagesListLoaded(object sender, RoutedEventArgs e)
    {
        _isMessagesListLoaded = true;
        EnsureScrollViewerAttached();
        RequestInitialScroll();
    }

    private void OnMessagesListLayoutUpdated(object? sender, object e)
    {
        if (!_initialScrollGate.HasPending || _userScrolledUp)
        {
            return;
        }

        var lastItemContainerGenerated = HasLastItemContainerGenerated(ViewModel.MessageHistory.Count);
        if (TryCompletePendingInitialScroll(lastItemContainerGenerated))
        {
            return;
        }

        if (lastItemContainerGenerated)
        {
            RequestInitialScroll();
        }
    }

    private void EnsureViewModelTracking()
    {
        if (_isTrackingViewModel)
        {
            return;
        }

        ViewModel.MessageHistory.CollectionChanged += OnMessageHistoryChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _isTrackingViewModel = true;
    }

    private void DetachViewModelTracking()
    {
        if (!_isTrackingViewModel)
        {
            return;
        }

        ViewModel.MessageHistory.CollectionChanged -= OnMessageHistoryChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _isTrackingViewModel = false;
    }

    private void OnScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scrollViewer == null)
        {
            return;
        }

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

    private void OnScrollViewerPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        StopInitialScrollForManualInteraction();
    }

    private void OnScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        StopInitialScrollForManualInteraction();

        var point = e.GetCurrentPoint(_scrollViewer);
        if (point.Properties.MouseWheelDelta > 0)
        {
            _userScrolledUp = true;
        }
    }

    private void OnMessageHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        if (_initialScrollGate.HasPending && _userScrolledUp)
        {
            _initialScrollGate.ClearPending();
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId)
            || e.PropertyName == nameof(ChatViewModel.IsSessionActive))
        {
            _initialScrollGate.MarkPending();
            RequestInitialScroll();
        }
    }

    private bool RequestInitialScroll(int attempt = 0)
    {
        if (!_isLoaded || MessagesList is null)
        {
            return false;
        }

        if (ViewModel.MessageHistory.Count <= 0)
        {
            _initialScrollGate.ClearPending();
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
        if (!_isLoaded || MessagesList is null || requestGeneration != _initialScrollGate.Generation)
        {
            _initialScrollGate.CancelInFlight();
            return;
        }

        var count = ViewModel.MessageHistory.Count;
        if (count <= 0)
        {
            _initialScrollGate.ClearPending();
            return;
        }

        if (_userScrolledUp)
        {
            _initialScrollGate.ClearPending();
            return;
        }

        _suspendAutoScrollTracking = true;
        var reachedBottom = TryScrollToBottomForInitialLoad();
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
                break;
        }
    }

    private bool TryScrollToBottomForInitialLoad()
    {
        if (!_isMessagesListLoaded)
        {
            return false;
        }

        var count = ViewModel.MessageHistory.Count;
        if (count <= 0)
        {
            return false;
        }

        EnsureScrollViewerAttached();
        if (_scrollViewer == null)
        {
            return false;
        }

        MessagesList.ScrollIntoView(ViewModel.MessageHistory[count - 1]);

        // Avoid synchronous UpdateLayout(); rely on virtualizer's async layout pass
        // and the retry mechanism in InitialScrollAttemptPolicy.
        _scrollViewer.ChangeView(null, _scrollViewer.ScrollableHeight, null);
        return IsInitialScrollReadyAndAtBottom(count);
    }

    private void RequestScrollToBottom()
    {
        try
        {
            if (ViewModel.MessageHistory.Count > 0)
            {
                MessagesList.ScrollIntoView(ViewModel.MessageHistory[^1]);
            }

            EnsureScrollViewerAttached();
            if (_scrollViewer != null)
            {
                _scrollViewer.ChangeView(null, _scrollViewer.ScrollableHeight, null);
                return;
            }

        }
        catch
        {
        }
    }

    private void EnsureScrollViewerAttached()
    {
        var scrollViewer = FindScrollViewer(MessagesList);
        if (ReferenceEquals(_scrollViewer, scrollViewer))
        {
            return;
        }

        if (_scrollViewer != null)
        {
            _scrollViewer.ViewChanged -= OnScrollViewerViewChanged;
        }

        _scrollViewer = scrollViewer;
        if (_scrollViewer != null)
        {
            _scrollViewer.ViewChanged += OnScrollViewerViewChanged;
            _scrollViewer.PointerPressed += OnScrollViewerPointerPressed;
            _scrollViewer.PointerWheelChanged += OnScrollViewerPointerWheelChanged;
        }
    }

    private void DetachScrollViewer()
    {
        if (_scrollViewer == null)
        {
            return;
        }

        _scrollViewer.ViewChanged -= OnScrollViewerViewChanged;
        _scrollViewer.PointerPressed -= OnScrollViewerPointerPressed;
        _scrollViewer.PointerWheelChanged -= OnScrollViewerPointerWheelChanged;
        _scrollViewer = null;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer sv)
        {
            return sv;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            var result = FindScrollViewer(child);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private bool HasLastItemContainerGenerated(int itemCount)
    {
        if (!_isMessagesListLoaded || itemCount <= 0)
        {
            return false;
        }

        return MessagesList.ContainerFromIndex(itemCount - 1) is not null;
    }

    private bool IsInitialScrollReadyAndAtBottom(int itemCount)
    {
        if (!HasLastItemContainerGenerated(itemCount))
        {
            return false;
        }

        return IsScrollViewerAtBottom();
    }

    private bool IsScrollViewerAtBottom()
    {
        if (_scrollViewer == null)
        {
            return false;
        }

        return _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - BottomThreshold;
    }

    private bool TryCompletePendingInitialScroll(bool? lastItemContainerGenerated = null)
    {
        if (!_initialScrollGate.HasPending || _scrollViewer == null)
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
        return true;
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
    }

    private void ReleaseAutoScrollTracking()
    {
        _ = DispatcherQueue.TryEnqueue(() => _suspendAutoScrollTracking = false);
    }
}

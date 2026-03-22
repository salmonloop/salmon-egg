using System;
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
    private bool _autoScroll = true;
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        _autoScroll = true;
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

        if (_suspendAutoScrollTracking && _initialScrollGate.HasPending)
        {
            return;
        }

        var verticalOffset = _scrollViewer.VerticalOffset;
        var maxOffset = _scrollViewer.ScrollableHeight;
        _autoScroll = verticalOffset >= maxOffset - BottomThreshold;
    }

    private void OnScrollViewerPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        StopInitialScrollForManualInteraction();
    }

    private void OnScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        StopInitialScrollForManualInteraction();
    }

    private void OnMessageHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        if (_initialScrollGate.HasPending && !_autoScroll)
        {
            _initialScrollGate.ClearPending();
            return;
        }

        if (RequestInitialScroll())
        {
            return;
        }

        if (_autoScroll)
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

        if (!_autoScroll)
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
            autoScrollEnabled: _autoScroll,
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
                _autoScroll = true;
                RequestInitialScroll(attempt + 1);
                break;
            default:
                _initialScrollGate.ClearPending();
                ReleaseAutoScrollTracking();
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
        MessagesList.UpdateLayout();
        _scrollViewer.ChangeView(null, _scrollViewer.ScrollableHeight, null);
        return _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - BottomThreshold;
    }

    private void RequestScrollToBottom()
    {
        try
        {
            EnsureScrollViewerAttached();
            if (_scrollViewer != null)
            {
                _scrollViewer.ChangeView(null, _scrollViewer.ScrollableHeight, null);
                return;
            }

            if (_isMessagesListLoaded && ViewModel.MessageHistory.Count > 0)
            {
                MessagesList.ScrollIntoView(ViewModel.MessageHistory[^1]);
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

    private void StopInitialScrollForManualInteraction()
    {
        if (!_initialScrollGate.HasPending)
        {
            return;
        }

        _suspendAutoScrollTracking = false;
        _autoScroll = false;
        _initialScrollGate.ClearPending();
    }

    private void ReleaseAutoScrollTracking()
    {
        _ = DispatcherQueue.TryEnqueue(() => _suspendAutoScrollTracking = false);
    }
}

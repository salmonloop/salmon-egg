using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
#if WINDOWS
using Microsoft.UI;
#endif
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Transcript;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Views.MiniWindow;

public sealed partial class MiniChatView : Page, IGamepadShortcutConsumer, IGamepadContextIntentConsumer
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
    private readonly TranscriptProjectionRestoreController _projectionRestoreController = new(MaxRestoreAttempts);
    private readonly Microsoft.UI.Xaml.Input.KeyEventHandler _messagesListHandledKeyDownHandler;
    private readonly PointerEventHandler _messagesListHandledPointerPressedHandler;
    private readonly PointerEventHandler _messagesListHandledPointerWheelChangedHandler;
    private ITranscriptViewportHost? _transcriptViewportHost;
    private TranscriptScrollRequestToken? _queuedNativeTranscriptScrollRequestToken;
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        ClearPendingProjectionRestore();
        _viewportController.Load(
            CurrentViewportConversationId,
            ViewModel.IsSessionActive,
            ViewModel.IsActivationOverlayVisible);
        EnsureViewModelTracking();
        TryResumeViewportAfterOverlay();
        TryActivateViewportAfterLoad();
        ApplyCurrentViewportStateIfAttached();
        ApplyCurrentViewportState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AbandonPendingProjectionRestore("ViewUnloaded");
        ApplyViewportActions(_viewportController.Unload());
        _isLoaded = false;
        _isMessagesListLoaded = false;
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
        TryResumeViewportAfterOverlay();
        TryActivateViewportAfterLoad();
        TryApplyPendingProjectionRestore();
        ApplyCurrentViewportState();
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

        TryResumeViewportAfterOverlay();
        TryApplyPendingProjectionRestore();
        ApplyViewportActions(_viewportController.OnTranscriptContentChanged(CreateViewportViewState()));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId))
        {
            HandleViewportConversationContextChanged();
            ApplyCurrentViewportStateIfAttached();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.IsSessionActive))
        {
            HandleViewportConversationContextChanged();
            ApplyCurrentViewportStateIfAttached();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.MessageHistory))
        {
            EnsureViewModelTracking();
            HandleViewportConversationContextChanged();
            ApplyCurrentViewportStateIfAttached();
            return;
        }

        if (e.PropertyName == nameof(ChatViewModel.IsActivationOverlayVisible))
        {
            HandleOverlayVisibilityChanged();
        }
    }

    public bool TryConsumeShortcutIntent(GamepadShortcutIntent intent)
    {
        if (XamlRoot is null)
        {
            return false;
        }

        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var action = ChatVoiceShortcutPolicy.Decide(
            intent,
            ResolveVoiceShortcutFocusContext(focusedElement),
            ViewModel.CanStartVoiceInput,
            ViewModel.CanStopVoiceInput,
            ViewModel.IsVoiceInputListening,
            isImeComposing: false);

        return action switch
        {
            ChatVoiceShortcutAction.StartVoiceInput => TryExecuteVoiceCommand(ViewModel.StartVoiceInputCommand),
            ChatVoiceShortcutAction.StopVoiceInput => TryExecuteVoiceCommand(ViewModel.StopVoiceInputCommand),
            _ => false
        };
    }

    public bool TryConsumeContextIntent(GamepadContextIntent intent)
    {
        if (_transcriptViewportHost is null)
        {
            return false;
        }

        if (!IsTranscriptContextFocusWithin())
        {
            return false;
        }

        var hasTranscriptContextFocus =
            IsTranscriptViewportSurfaceFocusWithin()
            || IsTranscriptMessageContainerFocused();
        var consumed = ChatTranscriptContextIntentHandler.TryConsume(
            intent,
            hasTranscriptContextFocus,
            ViewModel.MessageHistory.Count,
            _transcriptViewportHost.TryScrollByPages,
            RegisterUserViewportIntent);

        if (consumed)
        {
            if (!IsTranscriptViewportSurfaceFocusWithin())
            {
                _ = TryFocusTranscriptScroller(FocusState.Keyboard);
            }
        }

        return consumed;
    }

    private void DisposeTranscriptViewportHost()
    {
        _queuedNativeTranscriptScrollRequestToken = null;
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
        TryApplyPendingProjectionRestore();
        ApplyViewportActions(_viewportController.OnViewportChanged(
            CreateViewportViewState(),
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
            _ = TryFocusTranscriptScroller(FocusState.Programmatic);

            ApplyViewportActions(_viewportController.OnUserViewportIntent(CreateViewportViewState()));
            return;
        }

        _ = TryFocusTranscriptScroller(FocusState.Programmatic);

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

    private bool IsTranscriptViewportSurfaceFocusWithin()
    {
        if (MessagesList is null || MessagesList.XamlRoot is null)
        {
            return false;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(MessagesList.XamlRoot) as DependencyObject;
        if (DependencyObjectAncestry.FindAncestorOrSelf<ListViewItem>(current) is ListViewItem
            {
            } itemContainer
            && DependencyObjectAncestry.IsDescendantOf(itemContainer, MessagesList))
        {
            return false;
        }

        return DependencyObjectAncestry.IsDescendantOf(current, MessagesList);
    }

    private bool IsTranscriptMessageContainerFocused()
    {
        if (MessagesList?.XamlRoot is null)
        {
            return false;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(MessagesList.XamlRoot) as DependencyObject;
        var itemContainer = DependencyObjectAncestry.FindAncestorOrSelf<ListViewItem>(current);
        return itemContainer is not null
            && ReferenceEquals(current, itemContainer)
            && DependencyObjectAncestry.IsDescendantOf(itemContainer, MessagesList);
    }

    private bool IsTranscriptContextFocusWithin()
    {
        if (MessagesList is null)
        {
            return false;
        }

        if (MessagesList.XamlRoot is null)
        {
            return MessagesList.FocusState is FocusState.Keyboard or FocusState.Programmatic;
        }

        var current = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(MessagesList.XamlRoot) as DependencyObject;
        return DependencyObjectAncestry.IsDescendantOf(current, MessagesList);
    }

    private bool TryFocusTranscriptScroller(FocusState focusState)
    {
        if (_transcriptViewportHost?.TryFocusViewport(focusState) == true)
        {
            return true;
        }

        return MessagesList?.Focus(focusState) == true;
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

    private void OnProjectionRestoreReady(object? sender, EventArgs e)
    {
        ApplyViewportActions(_viewportController.OnProjectionReady(CurrentViewportConversationId));
        TryApplyPendingProjectionRestore();
    }

    private void TryRefreshViewportCoordinatorFromView()
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
            CreateViewportViewState(),
            TryCaptureProjectionRestoreToken()));
    }

    private TranscriptViewportViewState CreateViewportViewState()
    {
        var hasMessages = ViewModel.MessageHistory.Count > 0;
        return new TranscriptViewportViewState(
            HasMessages: hasMessages,
            IsAtBottom: IsListViewportAtBottom());
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
            case TranscriptViewportControllerActionKind.ScrollIntoView:
                if (!string.IsNullOrWhiteSpace(action.ItemKey))
                {
                    var index = ViewModel.MessageHistory.IndexOfProjectionItemKey(action.ItemKey);
                    if (index >= 0)
                    {
                        _transcriptViewportHost?.ScrollItemIntoView(index);
                    }
                }
                break;
            case TranscriptViewportControllerActionKind.ScrollTranscriptToEnd:
                if (action.ScrollRequestToken.Generation >= 0)
                {
                    IssueNativeTranscriptScrollRequest(action.ScrollRequestToken);
                }
                else
                {
                    RequestScrollToEnd();
                }
                break;

            case TranscriptViewportControllerActionKind.RequestRestore:
                if (action.RestoreToken is { } restoreToken)
                {
                    QueueProjectionOwnedRestore(restoreToken, action.Generation);
                }
                break;
        }
    }

    private bool IsViewportDetachedByUser()
        => _viewportController.State is TranscriptViewportState.DetachedByUser;

    private string CurrentViewportConversationId => ViewModel.CurrentSessionId ?? string.Empty;

    private void QueueProjectionOwnedRestore(TranscriptProjectionRestoreToken token, int generation)
    {
        _projectionRestoreController.Queue(token, generation);
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
                    ViewModel.MessageHistory.Count > 0));
                break;

            case TranscriptProjectionRestoreResultKind.Abandoned:
                ApplyViewportActions(_viewportController.OnRestoreAbandoned(
                    result.ConversationId ?? CurrentViewportConversationId,
                    result.Generation));
                break;
        }
    }

    private void ApplyCurrentViewportState()
    {
        var actions = _viewportController.OnViewportChanged(CreateViewportViewState());
        ApplyViewportActions(actions);
    }

    private void ApplyCurrentViewportStateIfAttached()
    {
        if (!IsViewportDetachedByUser())
        {
            ApplyCurrentViewportState();
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

        if (_queuedNativeTranscriptScrollRequestToken == requestToken)
        {
            return;
        }

        _queuedNativeTranscriptScrollRequestToken = requestToken;
        RequestScrollToEnd();
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (_queuedNativeTranscriptScrollRequestToken == requestToken)
            {
                _queuedNativeTranscriptScrollRequestToken = null;
            }

            if (!_isLoaded
                || !_isMessagesListLoaded
                || _transcriptViewportHost is null
                || ViewModel.MessageHistory.Count <= 0
                || !_viewportController.MatchesActiveScrollRequest(requestToken))
            {
                return;
            }

            RequestScrollToEnd();
            ScheduleTranscriptScrollRequestObservation(requestToken);
        }))
        {
            _queuedNativeTranscriptScrollRequestToken = null;
        }
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

            ApplyViewportActions(_viewportController.OnActiveScrollObservation());
            ApplyCurrentViewportState();
            TryRefreshViewportCoordinatorFromView();
        });
    }

    private void RequestScrollToEnd()
    {
        if (_transcriptViewportHost is not null && ViewModel.MessageHistory.Count > 0)
        {
            _transcriptViewportHost.ScrollToEnd();
        }
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


    private void HandleViewportConversationContextChanged()
    {
        AbandonPendingProjectionRestore("ConversationChanged");
        ClearPendingProjectionRestore();
        ApplyViewportActions(_viewportController.OnConversationChanged(
            CurrentViewportConversationId,
            ViewModel.IsSessionActive,
            ViewModel.IsActivationOverlayVisible,
            ViewModel.MessageHistory.Count > 0));
        TryResumeViewportAfterOverlay();
        TryActivateViewportAfterLoad();
    }

    private void HandleOverlayVisibilityChanged()
    {
        ApplyViewportActions(_viewportController.OnOverlayVisibilityChanged(
            ViewModel.IsActivationOverlayVisible));
        TryResumeViewportAfterOverlay();
        TryActivateViewportAfterLoad();
    }

    private void TryResumeViewportAfterOverlay()
    {
        if (!_isLoaded
            || !_isMessagesListLoaded
            || _transcriptViewportHost is null)
        {
            return;
        }

        if (!_viewportController.TryResumeAfterOverlay(
            CurrentViewportConversationId,
            ViewModel.IsSessionActive,
            ViewModel.IsActivationOverlayVisible,
            ViewModel.MessageHistory.Count > 0,
            out var actions))
        {
            return;
        }

        ApplyViewportActions(actions);
        TryApplyPendingProjectionRestore();
        ApplyCurrentViewportStateIfAttached();
        TryRefreshViewportCoordinatorFromView();
    }

    private void TryActivateViewportAfterLoad()
    {
        if (!_isLoaded
            || !_isMessagesListLoaded
            || _transcriptViewportHost is null)
        {
            return;
        }

        if (!_viewportController.TryActivateAfterLoad(
            CurrentViewportConversationId,
            ViewModel.IsSessionActive,
            ViewModel.IsActivationOverlayVisible,
            ViewModel.MessageHistory.Count > 0,
            out var actions))
        {
            return;
        }

        ApplyViewportActions(actions);
        TryApplyPendingProjectionRestore();
        ApplyCurrentViewportStateIfAttached();
        TryRefreshViewportCoordinatorFromView();
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
            _ = TryFocusTranscriptScroller(FocusState.Programmatic);

            return;
        }

        if (_projectionRestoreController.HasPending)
        {
            AbandonPendingProjectionRestore("UserInterrupted");
        }

        _viewportController.MarkUserScrollIntentStarted();
        _ = TryFocusTranscriptScroller(FocusState.Programmatic);
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

    private ChatVoiceShortcutFocusContext ResolveVoiceShortcutFocusContext(DependencyObject? focusedElement)
    {
        return ReferenceEquals(DependencyObjectAncestry.FindAncestorOrSelf<TextBox>(focusedElement), MiniChatInputBox)
            ? ChatVoiceShortcutFocusContext.InputBox
            : ChatVoiceShortcutFocusContext.Other;
    }

    private static bool TryExecuteVoiceCommand(ICommand? command)
    {
        if (command is null || !command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
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

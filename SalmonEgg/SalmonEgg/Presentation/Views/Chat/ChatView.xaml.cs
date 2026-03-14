using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.Views.Chat
{
    public sealed partial class ChatView : Page
    {
        public ChatViewModel ViewModel { get; }
        public UiMotion Motion => UiMotion.Current;
        private readonly IShellNavigationService _shellNavigation;
        private readonly IFloatingChatWindowService _floatingWindowService;
        private bool _isViewLoaded;
        private bool _isTrackingMessages;
        private bool _pendingInitialScroll = true;
        private bool _isMotionSubscribed;
        private bool _isFloatingHost;
        private bool _isFloatingServiceSubscribed;
        private ScrollViewer? _messagesScrollViewer;
        private bool _isUserScrollLocked;
        private bool _isAutoScrolling;
        private const double ScrollBottomThreshold = 48;

        public bool IsFloatingHost => _isFloatingHost;
        public Visibility MainHeaderVisibility => _isFloatingHost ? Visibility.Collapsed : Visibility.Visible;
        public Visibility FloatingHeaderVisibility => _isFloatingHost ? Visibility.Visible : Visibility.Collapsed;

        public ChatView()
        {
            // 从全局服务容器获取 ViewModel 以确保状态在导航间持久化
            ViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();
            _shellNavigation = App.ServiceProvider.GetRequiredService<IShellNavigationService>();
            _floatingWindowService = App.ServiceProvider.GetRequiredService<IFloatingChatWindowService>();

            this.InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var options = e.Parameter as ChatViewHostOptions;
            SetFloatingHost(options?.IsFloatingHost == true);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isViewLoaded = true;
            SubscribeMotion();
            EnsureMessageTracking();
            RequestInitialScroll();
            ApplyTerminalLayout();
            SubscribeFloatingWindowService();
            AttachMessagesScrollViewer();
            try
            {
                // Restore may already be running from the singleton VM; calling again is safe.
                await ViewModel.RestoreConversationsAsync();
                await ViewModel.EnsureAcpProfilesLoadedAsync();
            }
            catch
            {
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _isViewLoaded = false;
            UnsubscribeMotion();
            UnsubscribeFloatingWindowService();
            if (_messagesScrollViewer != null)
            {
                _messagesScrollViewer.ViewChanged -= OnMessagesViewChanged;
                _messagesScrollViewer = null;
            }
            if (_isTrackingMessages)
            {
                ViewModel.MessageHistory.CollectionChanged -= OnMessageHistoryChanged;
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _isTrackingMessages = false;
            }
        }

        private void SubscribeMotion()
        {
            if (_isMotionSubscribed)
            {
                return;
            }

            UiMotion.Current.PropertyChanged += OnUiMotionPropertyChanged;
            _isMotionSubscribed = true;
            ApplyListTransitions();
        }

        private void UnsubscribeMotion()
        {
            if (!_isMotionSubscribed)
            {
                return;
            }

            UiMotion.Current.PropertyChanged -= OnUiMotionPropertyChanged;
            _isMotionSubscribed = false;
        }

        private void OnUiMotionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UiMotion.ListItemTransitions))
            {
                ApplyListTransitions();
            }
        }

        private void ApplyListTransitions()
        {
#if WINDOWS
            if (MessagesList != null)
            {
                MessagesList.ItemContainerTransitions = UiMotion.Current.ListItemTransitions;
            }
#endif
        }

        private void EnsureMessageTracking()
        {
            if (_isTrackingMessages)
            {
                return;
            }

            ViewModel.MessageHistory.CollectionChanged += OnMessageHistoryChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _isTrackingMessages = true;
        }

        private void OnMessageHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (!_isViewLoaded)
            {
                return;
            }

            if (_pendingInitialScroll)
            {
                RequestInitialScroll();
            }

            if (!_isUserScrollLocked)
            {
                ScrollMessagesToBottom();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId) ||
                e.PropertyName == nameof(ChatViewModel.IsSessionActive))
            {
                _pendingInitialScroll = true;
                RequestInitialScroll();
            }

            if (e.PropertyName == nameof(ChatViewModel.IsTerminalVisible) ||
                e.PropertyName == nameof(ChatViewModel.TerminalPanelHeight))
            {
                ApplyTerminalLayout();
            }
        }

        private void RequestInitialScroll()
        {
            if (!_pendingInitialScroll || MessagesList is null)
            {
                return;
            }

            if (ViewModel.MessageHistory.Count == 0)
            {
                return;
            }

            _pendingInitialScroll = false;

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                MessagesList.UpdateLayout();
                ScrollMessagesToBottom();
            });
        }

        private void AttachMessagesScrollViewer()
        {
            if (_messagesScrollViewer != null || MessagesList == null)
            {
                return;
            }

            _messagesScrollViewer = FindScrollViewer(MessagesList);
            if (_messagesScrollViewer != null)
            {
                _messagesScrollViewer.ViewChanged += OnMessagesViewChanged;
            }
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer viewer)
                {
                    return viewer;
                }

                var found = FindScrollViewer(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void OnMessagesViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_messagesScrollViewer == null)
            {
                return;
            }

            if (_isAutoScrolling && !e.IsIntermediate)
            {
                _isAutoScrolling = false;
            }

            if (_isAutoScrolling)
            {
                return;
            }

            var atBottom = _messagesScrollViewer.VerticalOffset >= _messagesScrollViewer.ScrollableHeight - ScrollBottomThreshold;
            _isUserScrollLocked = !atBottom;
        }

        private void ScrollMessagesToBottom()
        {
            if (_messagesScrollViewer != null)
            {
                _isAutoScrolling = true;
                _messagesScrollViewer.ChangeView(null, _messagesScrollViewer.ScrollableHeight, null);
                return;
            }

            if (MessagesList != null && ViewModel.MessageHistory.Count > 0)
            {
                var last = ViewModel.MessageHistory[^1];
                MessagesList.ScrollIntoView(last);
            }
        }

        private void OnSessionNameClick(object sender, RoutedEventArgs e)
        {
            if (IsFloatingHost)
            {
                return;
            }

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

        private void OnGoToSettingsClick(object sender, RoutedEventArgs e)
        {
            _shellNavigation.NavigateToSettings("General");
        }

        private void OnDeleteMessageClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is ChatMessageViewModel message)
            {
                ViewModel.DeleteMessageCommand.Execute(message);
            }
        }

        private void OnTerminalInputKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            if (ViewModel.SendTerminalInputCommand.CanExecute(null))
            {
                ViewModel.SendTerminalInputCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void OnTerminalThumbDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (TerminalRowDefinition == null)
            {
                return;
            }

            var height = TerminalRowDefinition.ActualHeight - e.VerticalChange;
            if (height < 120)
            {
                height = 120;
            }

            TerminalRowDefinition.Height = new GridLength(height);
        }

        private void OnTerminalThumbDragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (TerminalRowDefinition == null)
            {
                return;
            }

            var height = TerminalRowDefinition.ActualHeight;
            if (height < 120)
            {
                height = 120;
            }

            ViewModel.TerminalPanelHeight = height;
        }

        private void ApplyTerminalLayout()
        {
            if (TerminalRowDefinition == null || TerminalSplitterRowDefinition == null)
            {
                return;
            }

            if (!ViewModel.IsTerminalVisible)
            {
                TerminalRowDefinition.Height = new GridLength(0);
                TerminalSplitterRowDefinition.Height = new GridLength(0);
                return;
            }

            var height = ViewModel.TerminalPanelHeight;
            if (height < 120)
            {
                height = 120;
            }

            TerminalRowDefinition.Height = new GridLength(height);
            TerminalSplitterRowDefinition.Height = new GridLength(6);
        }

        private void OnReturnToMainClick(object sender, RoutedEventArgs e)
        {
            _shellNavigation.NavigateToChat();
            _floatingWindowService.Hide();
        }

        private void OnFloatingAlwaysOnTopClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle)
            {
                _floatingWindowService.IsAlwaysOnTop = toggle.IsChecked == true;
                toggle.IsChecked = _floatingWindowService.IsAlwaysOnTop;
                return;
            }

            _floatingWindowService.IsAlwaysOnTop = !_floatingWindowService.IsAlwaysOnTop;
        }

        private void SetFloatingHost(bool value)
        {
            if (_isFloatingHost == value)
            {
                return;
            }

            _isFloatingHost = value;
            Bindings.Update();
            SyncAlwaysOnTopToggle();
        }

        private void SubscribeFloatingWindowService()
        {
            if (_isFloatingServiceSubscribed || !_isFloatingHost)
            {
                return;
            }

            _floatingWindowService.AlwaysOnTopChanged += OnFloatingAlwaysOnTopChanged;
            _isFloatingServiceSubscribed = true;
            SyncAlwaysOnTopToggle();
        }

        private void UnsubscribeFloatingWindowService()
        {
            if (!_isFloatingServiceSubscribed)
            {
                return;
            }

            _floatingWindowService.AlwaysOnTopChanged -= OnFloatingAlwaysOnTopChanged;
            _isFloatingServiceSubscribed = false;
        }

        private void OnFloatingAlwaysOnTopChanged(object? sender, bool isAlwaysOnTop)
        {
            SyncAlwaysOnTopToggle();
        }

        private void SyncAlwaysOnTopToggle()
        {
            if (!IsFloatingHost || AlwaysOnTopToggle == null)
            {
                return;
            }

            AlwaysOnTopToggle.IsChecked = _floatingWindowService.IsAlwaysOnTop;
        }
    }
}

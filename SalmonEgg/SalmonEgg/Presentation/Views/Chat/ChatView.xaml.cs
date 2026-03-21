using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.Core.ViewModels.ShellLayout;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.Views.Chat
{
    public sealed partial class ChatView : Page
    {
        public ChatShellViewModel ShellViewModel { get; }
        public ChatViewModel ViewModel => ShellViewModel.Chat;
        public ShellLayoutViewModel LayoutVM => ShellViewModel.ShellLayout;
        public UiMotion Motion => UiMotion.Current;
        private readonly IShellNavigationService _shellNavigation;
        private bool _isViewLoaded;
        private bool _isTrackingMessages;
        private readonly InitialScrollGate _initialScrollGate = new();
        private bool _isMotionSubscribed;
        private bool _autoScroll = true;
        private ScrollViewer? _scrollViewer;

        public ChatView()
        {
            ShellViewModel = App.ServiceProvider.GetRequiredService<ChatShellViewModel>();
            _shellNavigation = App.ServiceProvider.GetRequiredService<IShellNavigationService>();

            this.InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isViewLoaded = true;
            SubscribeMotion();
            EnsureMessageTracking();
            RequestInitialScroll();
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
            _initialScrollGate.CancelInFlight();
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

            if (RequestInitialScroll())
            {
                return;
            }

            if (_autoScroll)
            {
                RequestScrollToBottom();
            }
        }

        private void OnMessagesListLoaded(object sender, RoutedEventArgs e)
        {
            _scrollViewer = FindScrollViewer(MessagesList);
            if (_scrollViewer != null)
            {
                _scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
            }
        }

        private void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_scrollViewer == null) return;

            // If user is at the bottom, enable auto-scroll.
            // Otherwise, if they scrolled up, disable it.
            var verticalOffset = _scrollViewer.VerticalOffset;
            var maxOffset = _scrollViewer.ScrollableHeight;

            // Use a small threshold (10px) to account for precision issues.
            _autoScroll = verticalOffset >= maxOffset - 10;
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject element)
        {
            if (element is ScrollViewer sv) return sv;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                var result = FindScrollViewer(child);
                if (result != null) return result;
            }

            return null;
        }

        private void RequestScrollToBottom()
        {
            if (_scrollViewer != null)
            {
                _scrollViewer.ChangeView(null, _scrollViewer.ScrollableHeight, null);
                return;
            }

            // Fallback: use internal ScrollIntoView if ScrollViewer is not found yet.
            if (MessagesList != null && ViewModel.MessageHistory.Count > 0)
            {
                MessagesList.ScrollIntoView(ViewModel.MessageHistory.Last());
            }
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId) ||
                e.PropertyName == nameof(ChatViewModel.IsSessionActive))
            {
                _initialScrollGate.MarkPending();
                RequestInitialScroll();
            }
        }

        private bool RequestInitialScroll()
        {
            if (MessagesList is null)
            {
                return false;
            }

            if (!_initialScrollGate.TrySchedule(ViewModel.MessageHistory.Count))
            {
                return false;
            }

            if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isViewLoaded || MessagesList is null)
                {
                    _initialScrollGate.CancelInFlight();
                    return;
                }

                var count = ViewModel.MessageHistory.Count;
                if (!_initialScrollGate.TryComplete(count))
                {
                    return;
                }

                var last = ViewModel.MessageHistory[count - 1];
                MessagesList.UpdateLayout();
                MessagesList.ScrollIntoView(last);
            }))
            {
                _initialScrollGate.CancelInFlight();
                return false;
            }

            return true;
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

        private void OnGoToSettingsClick(object sender, RoutedEventArgs e)
        {
            _ = _shellNavigation.NavigateToSettings("General");
        }
    }
}

using System;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.Views.Chat
{
    public sealed partial class ChatView : Page
    {
        public ChatViewModel ViewModel { get; }
        private readonly IShellNavigationService _shellNavigation;
        private bool _isViewLoaded;
        private bool _isTrackingMessages;
        private bool _pendingInitialScroll = true;

        public ChatView()
        {
            // 从全局服务容器获取 ViewModel 以确保状态在导航间持久化
            ViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();
            _shellNavigation = App.ServiceProvider.GetRequiredService<IShellNavigationService>();

            this.InitializeComponent();

#if WINDOWS
            if (MessagesList != null)
            {
                MessagesList.ItemContainerTransitions = UiMotion.Current.ListItemTransitions;
            }
#endif

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isViewLoaded = true;
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
            if (_isTrackingMessages)
            {
                ViewModel.MessageHistory.CollectionChanged -= OnMessageHistoryChanged;
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _isTrackingMessages = false;
            }
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
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatViewModel.CurrentSessionId) ||
                e.PropertyName == nameof(ChatViewModel.IsSessionActive))
            {
                _pendingInitialScroll = true;
                RequestInitialScroll();
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
                var last = ViewModel.MessageHistory[^1];
                MessagesList.ScrollIntoView(last);
            });
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
            _shellNavigation.NavigateToSettings("General");
        }
    }
}

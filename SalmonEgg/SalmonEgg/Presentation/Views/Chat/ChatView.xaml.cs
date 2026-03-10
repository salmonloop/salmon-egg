using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.Services;

namespace SalmonEgg.Presentation.Views.Chat
{
    public sealed partial class ChatView : Page
    {
        public ChatViewModel ViewModel { get; }
        private readonly IShellNavigationService _shellNavigation;

        public ChatView()
        {
            // 从全局服务容器获取 ViewModel 以确保状态在导航间持久化
            ViewModel = App.ServiceProvider.GetRequiredService<ChatViewModel>();
            _shellNavigation = App.ServiceProvider.GetRequiredService<IShellNavigationService>();

            this.InitializeComponent();

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Restore may already be running from the singleton VM; calling again is safe.
                await ViewModel.RestoreConversationsAsync();
                await ViewModel.TryAutoConnectAsync();
                await ViewModel.EnsureAcpProfilesLoadedAsync();
            }
            catch
            {
            }
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

        private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (ViewModel.ShowSlashCommands)
            {
                switch (e.Key)
                {
                    case Windows.System.VirtualKey.Tab:
                    case Windows.System.VirtualKey.Enter:
                        if (e.Key == Windows.System.VirtualKey.Enter && ctrlPressed)
                        {
                            break;
                        }

                        if (ViewModel.TryAcceptSelectedSlashCommand())
                        {
                            if (sender is TextBox tb)
                            {
                                tb.SelectionStart = tb.Text?.Length ?? 0;
                            }
                            e.Handled = true;
                            return;
                        }
                        break;
                    case Windows.System.VirtualKey.Up:
                        if (ViewModel.TryMoveSlashSelection(-1))
                        {
                            e.Handled = true;
                            return;
                        }
                        break;
                    case Windows.System.VirtualKey.Down:
                        if (ViewModel.TryMoveSlashSelection(1))
                        {
                            e.Handled = true;
                            return;
                        }
                        break;
                }
            }

            // Enter sends; Ctrl+Enter inserts newline (TextBox handles it by default when AcceptsReturn=true).
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (!ctrlPressed)
                {
                    if (ViewModel.SendPromptCommand != null && ViewModel.SendPromptCommand.CanExecute(null))
                    {
                        ViewModel.SendPromptCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
        }

        private void OnGoToSettingsClick(object sender, RoutedEventArgs e)
        {
            _shellNavigation.NavigateToSettings("General");
        }
    }
}

using Microsoft.UI.Xaml.Controls;
using UnoAcpClient.Domain.Models;
using UnoAcpClient.Presentation.ViewModels.Chat;

namespace UnoAcpClient.Presentation.Views.Chat
{
    public sealed partial class ChatView : Page
    {
        public ChatViewModel ViewModel { get; }

        public ChatView() : this(null)
        {
        }

        public ChatView(ChatViewModel? viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel ?? App.ServiceProvider.GetRequiredService<ChatViewModel>();
        }

        private void OnTransportTypeChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton && radioButton.CommandParameter is string transportTypeStr)
            {
                var transportType = transportTypeStr switch
                {
                    "Stdio" => TransportType.Stdio,
                    "WebSocket" => TransportType.WebSocket,
                    "HttpSse" => TransportType.HttpSse,
                    _ => TransportType.Stdio
                };

                if (ViewModel?.TransportConfig != null)
                {
                    ViewModel.TransportConfig.SelectedTransportType = transportType;
                }
            }
        }
    }
}

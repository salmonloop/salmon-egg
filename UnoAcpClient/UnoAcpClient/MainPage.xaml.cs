using UnoAcpClient.Presentation.Views.Chat;

namespace UnoAcpClient;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();
        // 直接加载 ChatView 作为内容
        this.Content = new ChatView();
    }
}

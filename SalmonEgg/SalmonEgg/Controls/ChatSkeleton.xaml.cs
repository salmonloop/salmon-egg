using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SalmonEgg.Controls
{
    public sealed partial class ChatSkeleton : UserControl
    {
        public ChatSkeleton()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
            => ShimmerStoryboard.Begin();

        private void OnUnloaded(object sender, RoutedEventArgs e)
            => ShimmerStoryboard.Stop();
    }
}

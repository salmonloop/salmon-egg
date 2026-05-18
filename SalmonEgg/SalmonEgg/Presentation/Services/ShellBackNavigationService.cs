using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Core.Services.Navigation;

namespace SalmonEgg.Presentation.Services;

public sealed class ShellBackNavigationService : IShellBackNavigationService
{
    public bool TryGoBack()
    {
        return GetShell()?.TryGoBack() == true;
    }

    private static MainPage? GetShell()
    {
        if (App.MainWindowInstance?.Content is not Frame rootFrame)
        {
            return null;
        }

        return rootFrame.Content as MainPage;
    }
}

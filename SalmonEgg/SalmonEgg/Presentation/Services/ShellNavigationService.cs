using Microsoft.UI.Xaml.Controls;

namespace SalmonEgg.Presentation.Services;

public sealed class ShellNavigationService : IShellNavigationService
{
    public void NavigateToSettings(string key)
    {
        GetShell()?.NavigateToSettingsSubPage(key);
    }

    public void NavigateToChat()
    {
        GetShell()?.NavigateToChat();
    }

    public void NavigateToStart()
    {
        GetShell()?.NavigateToStart();
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

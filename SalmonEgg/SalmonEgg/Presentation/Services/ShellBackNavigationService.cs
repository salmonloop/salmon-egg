using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Core.Services.Navigation;

namespace SalmonEgg.Presentation.Services;

public sealed class ShellBackNavigationService : IShellBackNavigationService
{
    public bool TryGoBack()
    {
        // Gamepad Back is intentionally routed through the shell's custom product semantics
        // instead of being treated as a generic title-bar back-button click.
        return GetShell()?.TryHandleGamepadBack() == true;
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

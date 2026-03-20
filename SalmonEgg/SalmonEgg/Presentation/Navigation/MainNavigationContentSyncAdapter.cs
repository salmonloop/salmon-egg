using System;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Views.Chat;
using SalmonEgg.Presentation.Views.Start;

namespace SalmonEgg.Presentation.Navigation;

/// <summary>
/// UI-only adapter that maps concrete shell pages back to semantic navigation content.
/// It keeps MainPage from interpreting shell selection rules directly.
/// </summary>
public sealed class MainNavigationContentSyncAdapter
{
    private readonly INavigationCoordinator _navigationCoordinator;

    public MainNavigationContentSyncAdapter(
        INavigationCoordinator navigationCoordinator)
    {
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
    }

    public void OnFrameNavigated(Type? pageType)
    {
        SyncFromVisibleContent(pageType);
    }

    private void SyncFromVisibleContent(Type? pageType)
    {
        if (!TryResolveContent(pageType, out var content))
        {
            return;
        }

        _navigationCoordinator.SyncSelectionFromShellContent(content);
    }

    internal static bool TryResolveContent(Type? pageType, out ShellNavigationContent content)
    {
        if (pageType == typeof(ChatView))
        {
            content = ShellNavigationContent.Chat;
            return true;
        }

        if (pageType == typeof(StartView))
        {
            content = ShellNavigationContent.Start;
            return true;
        }

        if (pageType == typeof(SalmonEgg.Presentation.Views.SettingsShellPage))
        {
            content = ShellNavigationContent.Settings;
            return true;
        }

        content = ShellNavigationContent.None;
        return false;
    }
}

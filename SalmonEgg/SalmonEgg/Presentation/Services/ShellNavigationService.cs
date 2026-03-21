using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Core.Services;

namespace SalmonEgg.Presentation.Services;

public sealed class ShellNavigationService : IShellNavigationService, IActivationTokenShellNavigationService
{
    public ValueTask<ShellNavigationResult> NavigateToSettings(string key)
    {
        var shell = GetShell();
        if (shell is null)
        {
            return ValueTask.FromResult(ShellNavigationResult.Failed("ShellUnavailable"));
        }

        shell.NavigateToSettingsSubPage(key);
        return ValueTask.FromResult(ShellNavigationResult.Success());
    }

    public ValueTask<ShellNavigationResult> NavigateToSettings(string key, long activationToken)
        => NavigateToSettings(key);

    public ValueTask<ShellNavigationResult> NavigateToChat()
    {
        var shell = GetShell();
        if (shell is null)
        {
            return ValueTask.FromResult(ShellNavigationResult.Failed("ShellUnavailable"));
        }

        shell.NavigateToChat();
        return ValueTask.FromResult(ShellNavigationResult.Success());
    }

    public ValueTask<ShellNavigationResult> NavigateToChat(long activationToken)
        => NavigateToChat();

    public ValueTask<ShellNavigationResult> NavigateToStart()
    {
        var shell = GetShell();
        if (shell is null)
        {
            return ValueTask.FromResult(ShellNavigationResult.Failed("ShellUnavailable"));
        }

        shell.NavigateToStart();
        return ValueTask.FromResult(ShellNavigationResult.Success());
    }

    public ValueTask<ShellNavigationResult> NavigateToStart(long activationToken)
        => NavigateToStart();

    private static MainPage? GetShell()
    {
        if (App.MainWindowInstance?.Content is not Frame rootFrame)
        {
            return null;
        }

        return rootFrame.Content as MainPage;
    }
}

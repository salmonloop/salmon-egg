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

        return shell.NavigateToSettingsSubPageAsync(key);
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

        return shell.NavigateToChatAsync();
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

        return shell.NavigateToStartAsync();
    }

    public ValueTask<ShellNavigationResult> NavigateToStart(long activationToken)
        => NavigateToStart();

    public ValueTask<ShellNavigationResult> NavigateToDiscoverSessions()
    {
        var shell = GetShell();
        if (shell is null)
        {
            return ValueTask.FromResult(ShellNavigationResult.Failed("ShellUnavailable"));
        }

        return shell.NavigateToDiscoverSessionsAsync();
    }

    public ValueTask<ShellNavigationResult> NavigateToDiscoverSessions(long activationToken)
        => NavigateToDiscoverSessions();

    private static MainPage? GetShell()
    {
        if (App.MainWindowInstance?.Content is not Frame rootFrame)
        {
            return null;
        }

        return rootFrame.Content as MainPage;
    }
}

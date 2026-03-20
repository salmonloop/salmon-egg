namespace SalmonEgg.Presentation.Services;

/// <summary>
/// Thin UI-only navigation facade so pages don't reach into the visual tree to find MainPage.
/// </summary>
public interface IShellNavigationService
{
    ValueTask<ShellNavigationResult> NavigateToSettings(string key);
    ValueTask<ShellNavigationResult> NavigateToChat();
    ValueTask<ShellNavigationResult> NavigateToStart();
}

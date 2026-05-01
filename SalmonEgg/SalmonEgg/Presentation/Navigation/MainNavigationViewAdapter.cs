using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Navigation;

/// <summary>
/// UI-only adapter that maps NavigationView UI events to semantic navigation intents.
/// It must not own a secondary visual selection or pane state machine.
/// </summary>
public sealed class MainNavigationViewAdapter
{
    private readonly MainNavigationViewModel _viewModel;
    private readonly INavigationCoordinator _navigationCoordinator;

    public MainNavigationViewAdapter(
        MainNavigationViewModel viewModel,
        INavigationCoordinator navigationCoordinator)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
    }

    public Task<bool> HandleItemInvokedAsync(NavigationViewItemInvokedEventArgs args)
        => HandleItemInvokedCoreAsync(args);

    public Task HandleSelectionChangedAsync(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            return _navigationCoordinator.ActivateSettingsAsync("General");
        }

        return Task.CompletedTask;
    }

    private Task<bool> HandleItemInvokedCoreAsync(NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            return _navigationCoordinator.ActivateSettingsAsync("General").ContinueWith(_ => true);
        }

        if (args.InvokedItemContainer is not NavigationViewItem navItem || navItem.Tag is not string tag)
        {
            return Task.FromResult(false);
        }

        if (NavItemTag.TryParseProject(tag, out _))
        {
            // Non-leaf project items are not navigation destinations. Let the native
            // NavigationView hierarchy handle expand/collapse without translating the
            // click into a semantic selection change.
            return Task.FromResult(true);
        }

        if (string.Equals(tag, NavItemTag.AddProject, StringComparison.Ordinal))
        {
            _ = _viewModel.AddProjectItem.AddProjectCommand.ExecuteAsync(null);
            return Task.FromResult(true);
        }

        if (NavItemTag.TryParseMore(tag, out var moreProjectId))
        {
            _ = _viewModel.ShowAllSessionsForProjectAsync(moreProjectId);
            return Task.FromResult(true);
        }

        if (string.Equals(tag, NavItemTag.Start, StringComparison.Ordinal))
        {
            return _navigationCoordinator.ActivateStartAsync().ContinueWith(_ => true);
        }

        if (string.Equals(tag, NavItemTag.DiscoverSessions, StringComparison.Ordinal))
        {
            return _navigationCoordinator.ActivateDiscoverSessionsAsync().ContinueWith(_ => true);
        }

        if (NavItemTag.TryParseSession(tag, out var sessionId))
        {
            var sessionProjectId = (navItem.DataContext as SessionNavItemViewModel)?.ProjectId
                ?? _viewModel.TryGetProjectIdForSession(sessionId);

            _ = _navigationCoordinator.ActivateSessionAsync(sessionId, sessionProjectId);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}

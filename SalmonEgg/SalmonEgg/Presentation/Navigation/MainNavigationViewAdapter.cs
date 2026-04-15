using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Application.Common.Shell;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Navigation;

/// <summary>
/// UI-only adapter that projects navigation selection state onto NavigationView.
/// It absorbs control-specific selection quirks without becoming another state source.
/// </summary>
public sealed class MainNavigationViewAdapter
{
    private readonly NavigationView _navigationView;
    private readonly MainNavigationViewModel _viewModel;
    private readonly INavigationCoordinator _navigationCoordinator;
    private NavigationViewPanePresentationState _panePresentationState = NavigationViewPanePresentationState.Default;

    public MainNavigationViewAdapter(
        NavigationView navigationView,
        MainNavigationViewModel viewModel,
        INavigationCoordinator navigationCoordinator)
    {
        _navigationView = navigationView ?? throw new ArgumentNullException(nameof(navigationView));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
    }

    public async Task<bool> HandleItemInvokedAsync(NavigationViewItemInvokedEventArgs args)
    {
        return await HandleItemInvokedCoreAsync(args).ConfigureAwait(true);
    }

    public void ApplyPaneProjection(bool isPaneOpen)
    {
        if (_navigationView.IsPaneOpen != isPaneOpen)
        {
            _navigationView.IsPaneOpen = isPaneOpen;
        }
    }

    public bool HandlePanePresentationChanged(
        bool isPaneOpen,
        bool isDisplayModeChanged,
        NavigationViewPanePresentationMode displayMode,
        bool desiredPaneOpen)
    {
        var decision = NavigationViewPanePresentationPolicy.Evaluate(
            _panePresentationState,
            isPaneOpen,
            isDisplayModeChanged,
            displayMode,
            desiredPaneOpen);
        _panePresentationState = decision.NextState;

        if (decision.ShouldApplyPaneProjection)
        {
            ApplyPaneProjection(desiredPaneOpen);
        }

        return decision.ShouldReportPaneOpenIntent;
    }

    private async Task<bool> HandleItemInvokedCoreAsync(NavigationViewItemInvokedEventArgs args)
    {
        if (ReferenceEquals(args.InvokedItemContainer, _navigationView.SettingsItem))
        {
            await _navigationCoordinator.ActivateSettingsAsync("General").ConfigureAwait(true);
            return true;
        }

        if (args.InvokedItemContainer is not NavigationViewItem navItem || navItem.Tag is not string tag)
        {
            return false;
        }

        if (string.Equals(tag, NavItemTag.Start, StringComparison.Ordinal))
        {
            await _navigationCoordinator.ActivateStartAsync().ConfigureAwait(true);
            return true;
        }

        if (string.Equals(tag, NavItemTag.DiscoverSessions, StringComparison.Ordinal))
        {
            await _navigationCoordinator.ActivateDiscoverSessionsAsync().ConfigureAwait(true);
            return true;
        }

        if (NavItemTag.TryParseSession(tag, out var sessionId))
        {
            var sessionProjectId = (args.InvokedItemContainer as FrameworkElement)?.DataContext is SessionNavItemViewModel sessionItem
                ? sessionItem.ProjectId
                : _viewModel.TryGetProjectIdForSession(sessionId);

            // Never await remote session activation on the NavigationView UI event pipeline.
            // If we await here, the UI thread stays occupied until activation completes,
            // which causes multi-second "click has no response" freezes.
            _ = _navigationCoordinator.ActivateSessionAsync(sessionId, sessionProjectId);
            return true;
        }

        if (NavItemTag.TryParseProject(tag, out _))
        {
            // Non-leaf project items are not navigation destinations. Let the native
            // NavigationView hierarchy handle expand/collapse without translating the
            // click into a semantic selection change.
            return true;
        }

        if (string.Equals(tag, NavItemTag.AddProject, StringComparison.Ordinal))
        {
            _ = _viewModel.AddProjectItem.AddProjectCommand.ExecuteAsync(null);
            return true;
        }

        if (NavItemTag.TryParseMore(tag, out var moreProjectId))
        {
            _ = _viewModel.ShowAllSessionsForProjectAsync(moreProjectId);
            return true;
        }

        return false;
    }
}

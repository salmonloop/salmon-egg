using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly MainNavigationViewModel _viewModel;
    private readonly INavigationCoordinator _navigationCoordinator;

    /// <summary>
    /// Set to <c>true</c> while <see cref="HandleItemInvokedAsync"/> is executing.
    /// When active, <see cref="ApplySelection"/> defers to the next dispatcher tick
    /// instead of immediately setting <see cref="NavigationView.SelectedItem"/>,
    /// allowing the control's own click-driven selection pipeline to run its
    /// native indicator slide animation without being preempted by a
    /// programmatic <c>SelectedItem</c> assignment from the ViewModel round-trip.
    /// </summary>
    private bool _isProcessingItemInvoked;

    public MainNavigationViewAdapter(
        NavigationView navigationView,
        DispatcherQueue dispatcherQueue,
        MainNavigationViewModel viewModel,
        INavigationCoordinator navigationCoordinator)
    {
        _navigationView = navigationView ?? throw new ArgumentNullException(nameof(navigationView));
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _navigationCoordinator = navigationCoordinator ?? throw new ArgumentNullException(nameof(navigationCoordinator));
    }

    public void ApplySelection()
    {
        // While processing a user click (ItemInvoked), the NavigationView is
        // executing its own selection pipeline that includes the native indicator
        // slide animation. Setting SelectedItem programmatically here would
        // preempt that animation. Defer to the next dispatcher tick so the
        // control can finish its own selection first.
        if (_isProcessingItemInvoked)
        {
            ApplySelectionDeferred();
            return;
        }

        ApplySelectionCore();
    }

    public void ApplySelectionDeferred()
    {
        _ = _dispatcherQueue.TryEnqueue(ApplySelectionCore);
    }

    public async Task<bool> HandleItemInvokedAsync(NavigationViewItemInvokedEventArgs args)
    {
        _isProcessingItemInvoked = true;
        try
        {
            return await HandleItemInvokedCoreAsync(args).ConfigureAwait(true);
        }
        finally
        {
            _isProcessingItemInvoked = false;
        }
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

        if (NavItemTag.TryParseSession(tag, out var sessionId))
        {
            var sessionProjectId = (args.InvokedItemContainer as FrameworkElement)?.DataContext is SessionNavItemViewModel sessionItem
                ? sessionItem.ProjectId
                : _viewModel.TryGetProjectIdForSession(sessionId);

            await _navigationCoordinator.ActivateSessionAsync(sessionId, sessionProjectId).ConfigureAwait(true);
            return true;
        }

        if (NavItemTag.TryParseProject(tag, out _))
        {
            // The native NavigationViewItem with MenuItemsSource handles expand/collapse
            // automatically when the content area is clicked. IsExpanded TwoWay binding
            // in ProjectNavTemplate synchronizes the result back to the ViewModel.
            // Calling ToggleProjectExpanded here would cause a double-toggle (cancel out).
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

    private void ApplySelectionCore()
    {
        if (_viewModel.IsSettingsSelected)
        {
            SetSelectedSettingsItemDeferred();
            return;
        }

        var target = _viewModel.ProjectedControlSelectedItem;
        if (target is null)
        {
            return;
        }

        if (ReferenceEquals(_navigationView.SelectedItem, target))
        {
            return;
        }

        _navigationView.SelectedItem = target;
    }

    private void SetSelectedSettingsItemDeferred()
    {
        if (_navigationView.SettingsItem is null)
        {
            return;
        }

        if (ReferenceEquals(_navigationView.SelectedItem, _navigationView.SettingsItem))
        {
            return;
        }

        _ = _dispatcherQueue.TryEnqueue(() =>
        {
            if (_navigationView.SettingsItem is null)
            {
                return;
            }

            if (ReferenceEquals(_navigationView.SelectedItem, _navigationView.SettingsItem))
            {
                return;
            }

            _navigationView.SelectedItem = _navigationView.SettingsItem;
        });
    }
}

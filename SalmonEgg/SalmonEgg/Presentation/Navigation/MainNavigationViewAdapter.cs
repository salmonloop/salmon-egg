using System;
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
    private readonly SelectionProjectionApplyGate _selectionApplyGate = new();

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
        var decision = _selectionApplyGate.RequestApply();
        if (decision is SelectionProjectionApplyDecision.Defer)
        {
            return;
        }

        ApplySelectionCore();
    }

    public void ApplySelectionDeferred()
    {
        if (!_selectionApplyGate.TryScheduleDeferredApply())
        {
            return;
        }

        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                _selectionApplyGate.ReleaseScheduledDeferredApply();
                ApplySelection();
            }))
        {
            _selectionApplyGate.ReleaseScheduledDeferredApply();
        }
    }

    public async Task<bool> HandleItemInvokedAsync(NavigationViewItemInvokedEventArgs args)
    {
        _selectionApplyGate.BeginInteraction();
        try
        {
            return await HandleItemInvokedCoreAsync(args).ConfigureAwait(true);
        }
        finally
        {
            if (_selectionApplyGate.EndInteraction())
            {
                ApplySelection();
            }
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

            // Apply the user's latest intent immediately so projection does not briefly
            // snap back to the previous session while activation is still in flight.
            //
            // IMPORTANT: We MUST call ActivateSessionAsync BEFORE SelectSession.
            // ActivateSessionAsync (via NavigationCoordinator) calls PrimeSessionSwitchPreview,
            // which sets the ViewModel's preview loading state. If we SelectSession first,
            // the ViewModel might briefly project the session as "Active" but "Not Loading"
            // (since the preview hasn't started yet), causing the "flash of empty chat".
            var activationTask = _navigationCoordinator.ActivateSessionAsync(sessionId, sessionProjectId);
            _viewModel.SelectSession(sessionId);
            await activationTask.ConfigureAwait(true);
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

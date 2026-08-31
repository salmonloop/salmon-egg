using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class ShellStartupNavigationService : IShellStartupNavigationService
{
    private readonly MainNavigationViewModel _navigationViewModel;
    private readonly IShellNavigationRuntimeState _runtimeState;
    private readonly IActivationTokenShellNavigationService _shellNavigationService;
    private readonly ISettingsSectionSelectionStore _settingsSelectionStore;
    private readonly ILogger<ShellStartupNavigationService> _logger;
    private readonly SemaphoreSlim _activationGate = new(1, 1);
    private bool _activationCompleted;

    public ShellStartupNavigationService(
        MainNavigationViewModel navigationViewModel,
        IShellNavigationRuntimeState runtimeState,
        IActivationTokenShellNavigationService shellNavigationService,
        ISettingsSectionSelectionStore settingsSelectionStore,
        ILogger<ShellStartupNavigationService>? logger = null)
    {
        _navigationViewModel = navigationViewModel ?? throw new ArgumentNullException(nameof(navigationViewModel));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _shellNavigationService = shellNavigationService ?? throw new ArgumentNullException(nameof(shellNavigationService));
        _settingsSelectionStore = settingsSelectionStore ?? throw new ArgumentNullException(nameof(settingsSelectionStore));
        _logger = logger ?? NullLogger<ShellStartupNavigationService>.Instance;
    }

    public async Task ActivateInitialContentAsync()
    {
        _navigationViewModel.RebuildTree();
        var content = ShellNavigationContent.Start;
        await _activationGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_activationCompleted || !IsPristineStartupState())
            {
                content = ResolveContentToRestore();
                var restoreResult = await RestoreAuthoritativeContentAsync(content).ConfigureAwait(true);
                if (restoreResult.Succeeded)
                {
                    _activationCompleted = true;
                    return;
                }

                _logger.LogWarning(
                    "Shell content restore failed. content={Content} reason={Reason}",
                    content,
                    restoreResult.FailureReason ?? "NavigationRejected");
                return;
            }

            // Route through the navigation VM owner so cold-start Start activation
            // failures surface the same localized ShowInfo used by later shell entry points.
            var activated = await _navigationViewModel.ActivateStartAsync().ConfigureAwait(true);
            if (activated)
            {
                _activationCompleted = true;
                return;
            }

            _logger.LogWarning(
                "Initial shell navigation activation failed. content={Content} reason={Reason}",
                ShellNavigationContent.Start,
                "ActivationRejected");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Shell content activation threw. content={Content} reason={Reason}",
                content,
                ex.GetType().Name);
        }
        finally
        {
            _activationGate.Release();
        }
    }

    private bool IsPristineStartupState()
        => _runtimeState.CurrentShellContent == ShellNavigationContent.Start
           && _runtimeState.PendingShellContent is null;

    private ShellNavigationContent ResolveContentToRestore()
        => _runtimeState.PendingShellContent ?? _runtimeState.CurrentShellContent;

    private ValueTask<ShellNavigationResult> RestoreAuthoritativeContentAsync(ShellNavigationContent content)
    {
        var activationToken = _runtimeState.LatestActivationToken;
        return content switch
        {
            ShellNavigationContent.Chat => _shellNavigationService.NavigateToChat(activationToken),
            ShellNavigationContent.Settings => _shellNavigationService.NavigateToSettings(
                _settingsSelectionStore.CurrentSectionKey,
                activationToken),
            ShellNavigationContent.DiscoverSessions => _shellNavigationService.NavigateToDiscoverSessions(activationToken),
            ShellNavigationContent.Start or ShellNavigationContent.None => _shellNavigationService.NavigateToStart(activationToken),
            _ => _shellNavigationService.NavigateToStart(activationToken)
        };
    }
}

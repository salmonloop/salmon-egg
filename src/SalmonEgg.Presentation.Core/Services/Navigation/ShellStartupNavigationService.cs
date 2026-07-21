using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;

namespace SalmonEgg.Presentation.Core.Services;

public sealed class ShellStartupNavigationService : IShellStartupNavigationService
{
    private readonly MainNavigationViewModel _navigationViewModel;
    private readonly ILogger<ShellStartupNavigationService> _logger;
    private int _activationInFlight;
    private bool _activationCompleted;

    public ShellStartupNavigationService(
        MainNavigationViewModel navigationViewModel,
        ILogger<ShellStartupNavigationService>? logger = null)
    {
        _navigationViewModel = navigationViewModel ?? throw new ArgumentNullException(nameof(navigationViewModel));
        _logger = logger ?? NullLogger<ShellStartupNavigationService>.Instance;
    }

    public async Task ActivateInitialContentAsync()
    {
        if (_activationCompleted)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _activationInFlight, 1, 0) != 0)
        {
            return;
        }

        try
        {
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
                "Initial shell navigation activation threw. content={Content} reason={Reason}",
                ShellNavigationContent.Start,
                ex.GetType().Name);
        }
        finally
        {
            Interlocked.Exchange(ref _activationInFlight, 0);
        }
    }
}

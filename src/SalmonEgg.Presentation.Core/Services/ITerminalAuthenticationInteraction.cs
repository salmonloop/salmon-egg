using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.ViewModels.Chat;

namespace SalmonEgg.Presentation.Core.Services;

/// <summary>Native consent and interactive terminal surfaces. No command or environment is displayed.</summary>
public interface ITerminalAuthenticationInteraction
{
    Task<bool> ConfirmAsync(TerminalAuthenticationViewModel viewModel, CancellationToken cancellationToken);

    Task ShowSessionAsync(TerminalAuthenticationViewModel viewModel, CancellationToken cancellationToken);
}

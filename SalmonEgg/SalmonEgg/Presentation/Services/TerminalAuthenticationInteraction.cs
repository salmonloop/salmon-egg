using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.ViewModels.Chat;
using SalmonEgg.Presentation.Views.Chat;

namespace SalmonEgg.Presentation.Services;

public sealed class TerminalAuthenticationInteraction : ITerminalAuthenticationInteraction
{
    private readonly AppActivationSignalSource _activation;
    private readonly IUiDispatcher _dispatcher;

    public TerminalAuthenticationInteraction(AppActivationSignalSource activation, IUiDispatcher dispatcher)
    {
        _activation = activation ?? throw new ArgumentNullException(nameof(activation));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public Task<bool> ConfirmAsync(TerminalAuthenticationViewModel viewModel, CancellationToken cancellationToken)
        => ShowAsync(new ContentDialog
        {
            Title = viewModel.Title,
            Content = new TextBlock { Text = viewModel.ConsentMessage, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = viewModel.ContinueButtonText,
            CloseButtonText = viewModel.CancelButtonText,
            DefaultButton = ContentDialogButton.Close
        }, cancellationToken);

    public async Task ShowSessionAsync(TerminalAuthenticationViewModel viewModel, CancellationToken cancellationToken)
        => _ = await ShowAsync(new TerminalAuthenticationDialog(viewModel), cancellationToken).ConfigureAwait(true);

    private async Task<bool> ShowAsync(ContentDialog dialog, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_activation.ActiveWindow?.Content is not FrameworkElement { XamlRoot: { } root }) return false;
        ContentDialogHost.AttachToXamlRoot(dialog, root);
        using var registration = cancellationToken.Register(() => _dispatcher.Enqueue(dialog.Hide));
        cancellationToken.ThrowIfCancellationRequested();
        var result = await dialog.ShowAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return result == ContentDialogResult.Primary;
    }
}

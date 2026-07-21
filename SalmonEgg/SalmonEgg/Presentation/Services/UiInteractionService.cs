using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.Utilities;
using SalmonEgg.Presentation.Views.Navigation;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Services;

public sealed class UiInteractionService : IUiInteractionService
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();
    private readonly IFolderPickerService _folderPicker;

    public UiInteractionService(IFolderPickerService folderPicker)
    {
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
    }

    public bool CanPickFolder => _folderPicker.IsSupported;

    public async Task ShowInfoAsync(string message)
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = ResolveResourceString("UiInteractionInfoDialogTitle", "Notice"),
            Content = message ?? string.Empty,
            CloseButtonText = ResolveResourceString("UiInteractionConfirmButtonText", "OK")
        };
        ContentDialogHost.AttachToXamlRoot(dialog, xamlRoot);

        await dialog.ShowAsync();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText, string closeButtonText)
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = title ?? string.Empty,
            Content = message ?? string.Empty,
            PrimaryButtonText = string.IsNullOrWhiteSpace(primaryButtonText)
                ? ResolveResourceString("UiInteractionConfirmButtonText", "OK")
                : primaryButtonText,
            CloseButtonText = string.IsNullOrWhiteSpace(closeButtonText)
                ? ResolveResourceString("UiInteractionCancelButtonText", "Cancel")
                : closeButtonText,
            DefaultButton = ContentDialogButton.Primary
        };
        ContentDialogHost.AttachToXamlRoot(dialog, xamlRoot);

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public async Task<string?> PromptTextAsync(string title, string primaryButtonText, string closeButtonText, string initialText)
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null)
        {
            return null;
        }

        var input = new TextBox
        {
            Text = initialText ?? string.Empty,
            MinWidth = 320,
            TextWrapping = TextWrapping.NoWrap
        };

        var dialog = new ContentDialog
        {
            Title = title ?? string.Empty,
            Content = input,
            PrimaryButtonText = string.IsNullOrWhiteSpace(primaryButtonText)
                ? ResolveResourceString("UiInteractionConfirmButtonText", "OK")
                : primaryButtonText,
            CloseButtonText = string.IsNullOrWhiteSpace(closeButtonText)
                ? ResolveResourceString("UiInteractionCancelButtonText", "Cancel")
                : closeButtonText,
            DefaultButton = ContentDialogButton.Primary
        };
        ContentDialogHost.AttachToXamlRoot(dialog, xamlRoot);

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return input.Text?.Trim();
    }

    public async Task<string?> PickFolderAsync()
    {
        if (!_folderPicker.IsSupported)
        {
            return null;
        }

        try
        {
            var pickedFolder = await _folderPicker.PickFolderAsync().ConfigureAwait(true);
            if (pickedFolder is null)
            {
                // Native folder pickers return null when the user cancels; cancellation is not a
                // picker failure and must not cascade into a manual path prompt.
                return null;
            }

            if (!string.IsNullOrWhiteSpace(pickedFolder))
            {
                return pickedFolder;
            }
        }
        catch
        {
            // Supported native picker failures keep the user on the same explicit path input flow.
            return await PromptTextAsync(
                title: ResolveResourceString("UiInteractionPickFolderTitle", "Add project"),
                primaryButtonText: ResolveResourceString("UiInteractionConfirmButtonText", "OK"),
                closeButtonText: ResolveResourceString("UiInteractionCancelButtonText", "Cancel"),
                initialText: "").ConfigureAwait(true);
        }

        return null;
    }

    public async Task ShowSessionsListDialogAsync(string title, IReadOnlyList<SessionNavItemViewModel> sessions, Action<string> onPickSession)
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null)
        {
            return;
        }
        var dialog = new SessionsListDialog(string.IsNullOrWhiteSpace(title) ? string.Empty : title, sessions);
        ContentDialogHost.AttachToXamlRoot(dialog, xamlRoot);

        await dialog.ShowAsync();

        if (!string.IsNullOrWhiteSpace(dialog.PickedSessionId))
        {
            // Let pick-session failures surface to the caller so navigation owners can
            // log/recover; the UI shell must not silently drop activation errors.
            onPickSession(dialog.PickedSessionId!);
        }
    }

    public async Task<RemoteProjectSelectionResult> ShowRemoteProjectSelectionAsync(RemoteProjectSelectionViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null)
        {
            return RemoteProjectSelectionResult.Cancel;
        }

        var dialog = new RemoteProjectSelectionDialog(viewModel);
        ContentDialogHost.AttachToXamlRoot(dialog, xamlRoot);

        var result = await dialog.ShowAsync();
        dialog.ApplyResult(result);
        return dialog.Result;
    }

    private static XamlRoot? GetXamlRoot()
    {
        try
        {
            if (App.MainWindowInstance?.Content is Frame rootFrame)
            {
                if (rootFrame.Content is FrameworkElement shell)
                {
                    return shell.XamlRoot;
                }

                return rootFrame.XamlRoot;
            }

            return (App.MainWindowInstance?.Content as FrameworkElement)?.XamlRoot;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveResourceString(string resourceKey, string fallback)
    {
        var value = ResourceLoader.GetString(resourceKey);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

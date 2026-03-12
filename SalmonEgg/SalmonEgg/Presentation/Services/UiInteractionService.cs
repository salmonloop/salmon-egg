using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.ViewModels.Navigation;
using SalmonEgg.Presentation.Views.Navigation;

namespace SalmonEgg.Presentation.Services;

public sealed class UiInteractionService : IUiInteractionService
{
    public async Task ShowInfoAsync(string message)
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "提示",
            Content = message ?? string.Empty,
            CloseButtonText = "确定"
        };

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
            XamlRoot = xamlRoot,
            Title = title ?? string.Empty,
            Content = message ?? string.Empty,
            PrimaryButtonText = string.IsNullOrWhiteSpace(primaryButtonText) ? "确定" : primaryButtonText,
            CloseButtonText = string.IsNullOrWhiteSpace(closeButtonText) ? "取消" : closeButtonText,
            DefaultButton = ContentDialogButton.Primary
        };

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
            XamlRoot = xamlRoot,
            Title = title ?? string.Empty,
            Content = input,
            PrimaryButtonText = primaryButtonText ?? "确定",
            CloseButtonText = closeButtonText ?? "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        return input.Text?.Trim();
    }

    public async Task<string?> PickFolderAsync()
    {
#if WINDOWS
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");

            var window = App.MainWindowInstance;
            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch
        {
            // Fall back to manual input below.
        }
#endif

        return await PromptTextAsync(
            title: "添加项目",
            primaryButtonText: "确定",
            closeButtonText: "取消",
            initialText: "").ConfigureAwait(true);
    }

    public async Task ShowSessionsListDialogAsync(string title, IReadOnlyList<SessionNavItemViewModel> sessions, Action<string> onPickSession)
    {
        var xamlRoot = GetXamlRoot();
        if (xamlRoot == null)
        {
            return;
        }
        var dialog = new SessionsListDialog(title ?? "会话", sessions)
        {
            XamlRoot = xamlRoot
        };

        await dialog.ShowAsync();

        if (!string.IsNullOrWhiteSpace(dialog.PickedSessionId))
        {
            try { onPickSession(dialog.PickedSessionId!); } catch { }
        }
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
}

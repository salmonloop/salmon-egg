using System;
using Microsoft.UI.Xaml.Controls;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Windows.ApplicationModel.Resources;

namespace SalmonEgg.Presentation.Views.Navigation;

/// <summary>
/// Pure view over <see cref="RemoteProjectSelectionViewModel"/>. Renders the populated list or
/// the empty state and translates the user's button choice into a UI-free
/// <see cref="RemoteProjectSelectionResult"/>. It owns no project, path or navigation state;
/// selection lives on the view model and identity flows only as the stable directory id.
/// </summary>
public sealed partial class RemoteProjectSelectionDialog : ContentDialog
{
    private static readonly ResourceLoader ResourceLoader = ResourceLoader.GetForViewIndependentUse();

    public RemoteProjectSelectionViewModel ViewModel { get; }

    public RemoteProjectSelectionDialog(RemoteProjectSelectionViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();

        Title = ResolveResourceString("RemoteProjectSelectionDialogTitle", "选择远程项目");
        CloseButtonText = ResolveResourceString("UiInteractionCancelButtonText", "取消");

        if (ViewModel.HasProjects)
        {
            // Populated: primary confirms the current selection (gated on CanConfirm), the
            // secondary button routes to the authoritative remote-path settings.
            PrimaryButtonText = ResolveResourceString("RemoteProjectSelectionAddButton", "添加");
            SecondaryButtonText = ResolveResourceString("RemoteProjectSelectionManageButton", "管理远程项目…");
            IsPrimaryButtonEnabled = ViewModel.CanConfirm;
        }
        else
        {
            // Empty state: nothing to add, so the primary action takes the user to settings.
            PrimaryButtonText = ResolveResourceString("RemoteProjectSelectionGoToSettingsButton", "前往设置");
            IsPrimaryButtonEnabled = true;
        }

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnDialogClosed;
    }

    /// <summary>The UI-free outcome, mapped from the button pressed and the current state.</summary>
    public RemoteProjectSelectionResult Result { get; private set; } = RemoteProjectSelectionResult.Cancel;

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!ViewModel.HasProjects)
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(RemoteProjectSelectionViewModel.CanConfirm), StringComparison.Ordinal))
        {
            IsPrimaryButtonEnabled = ViewModel.CanConfirm;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The list is the only selection surface; project it back onto the view model's
        // stable-id selection state rather than tracking a second selected item here.
        ViewModel.SelectedDirectoryId = (RemoteProjectList.SelectedItem as RemoteProjectOptionViewModel)?.DirectoryId;
    }

    private void OnDialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnDialogClosed;
    }

    /// <summary>
    /// Maps a dialog button result to the semantic outcome. Called by the hosting service after
    /// <c>ShowAsync</c>; the primary button means "add" only in the populated state.
    /// </summary>
    public void ApplyResult(ContentDialogResult result)
    {
        Result = result switch
        {
            ContentDialogResult.Primary when ViewModel.HasProjects && ViewModel.CanConfirm
                => new RemoteProjectSelectionResult.Confirmed(ViewModel.SelectedDirectoryId!),
            ContentDialogResult.Primary when !ViewModel.HasProjects
                => RemoteProjectSelectionResult.Manage,
            ContentDialogResult.Secondary
                => RemoteProjectSelectionResult.Manage,
            _ => RemoteProjectSelectionResult.Cancel
        };
    }

    private static string ResolveResourceString(string resourceKey, string fallback)
    {
        var value = ResourceLoader.GetString(resourceKey);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

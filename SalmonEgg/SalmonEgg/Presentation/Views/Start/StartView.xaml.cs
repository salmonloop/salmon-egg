using System;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Models;
using SalmonEgg.Presentation.ViewModels.Start;

namespace SalmonEgg.Presentation.Views.Start;

public sealed partial class StartView : Page
{
    private bool _isViewLoaded;
    private int _composerPopupOpenCount;
    private bool _isComposerPopupClosePending;

    public StartViewModel ViewModel { get; }
    public UiMotion Motion => UiMotion.Current;

    public StartView()
    {
        ViewModel = App.ServiceProvider.GetRequiredService<StartViewModel>();

        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isViewLoaded = true;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.OnComposerLoaded();
        ApplyComposerStageVisualState(useTransitions: false);

        try
        {
            await ViewModel.Chat.RestoreConversationsAsync();
            await ViewModel.Chat.EnsureAcpProfilesLoadedAsync();
        }
        catch
        {
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isViewLoaded = false;
        _composerPopupOpenCount = 0;
        _isComposerPopupClosePending = false;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.OnComposerUnloaded();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(StartViewModel.ComposerStage), StringComparison.Ordinal))
        {
            ApplyComposerStageVisualState(useTransitions: true);
        }
    }

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInComposerSubtree(source))
        {
            return;
        }

        ViewModel.OnComposerOutsidePointerPressed();
    }

    private void OnComposerShellPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ViewModel.OnComposerActivated();
    }

    private void OnComposerInteractiveElementGotFocus(object sender, RoutedEventArgs e)
    {
        ViewModel.OnComposerFocusEntered();
    }

    private void OnComposerInteractiveElementLostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isViewLoaded)
            {
                return;
            }

            if (_composerPopupOpenCount > 0 || _isComposerPopupClosePending)
            {
                return;
            }

            var focusedElement = GetFocusedElement();

            if (!IsInComposerSubtree(focusedElement))
            {
                ViewModel.OnComposerFocusExited();
            }
        });
    }

    private void OnComposerSelectorDropDownOpened(object sender, object e)
    {
        _composerPopupOpenCount++;
        ViewModel.OnComposerPopupOpened();
    }

    private void OnComposerSelectorDropDownClosed(object sender, object e)
    {
        _composerPopupOpenCount = Math.Max(0, _composerPopupOpenCount - 1);

        if (_composerPopupOpenCount > 0)
        {
            return;
        }

        _isComposerPopupClosePending = true;

        if (!DispatcherQueue.TryEnqueue(ReconcileComposerPopupClosed))
        {
            ReconcileComposerPopupClosed();
        }
    }

    private void ApplyComposerStageVisualState(bool useTransitions)
    {
        var stateName = ViewModel.ComposerStage switch
        {
            StartComposerStage.Collapsed => "StageCollapsed",
            StartComposerStage.Primed => "StagePrimed",
            StartComposerStage.Interacting => "StageInteracting",
            StartComposerStage.PopupEngaged => "StagePopupEngaged",
            StartComposerStage.ExpandedIdle => "StageExpandedIdle",
            StartComposerStage.Submitting => "StageSubmitting",
            _ => "StageCollapsed",
        };

        VisualStateManager.GoToState(
            this,
            stateName,
            useTransitions);
    }

    private void ReconcileComposerPopupClosed()
    {
        if (!_isViewLoaded)
        {
            return;
        }

        // WinUI/Uno closes the ComboBox popup before focus is fully settled.
        // Reconcile against the post-close focused element so popup selection
        // does not emit a transient collapse when focus stays in the composer.
        var focusWithinComposer = IsInComposerSubtree(GetFocusedElement());
        _isComposerPopupClosePending = false;
        ViewModel.OnComposerPopupClosedWithFocusState(focusWithinComposer);
    }

    private DependencyObject? GetFocusedElement()
        => XamlRoot is null
            ? null
            : Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;

    private bool IsInComposerSubtree(DependencyObject? focusedElement)
    {
        var current = focusedElement;
        while (current is not null)
        {
            if (ReferenceEquals(current, ComposerShell))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}

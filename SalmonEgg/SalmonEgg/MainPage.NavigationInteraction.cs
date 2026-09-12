using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Models.Navigation;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Windows.System;

namespace SalmonEgg;

public sealed partial class MainPage
{
    private readonly HashSet<uint> _navigationPressedPointers = new();
    private readonly HashSet<VirtualKey> _navigationPressedKeys = new();
    private UIElement? _navigationInteractionRoot;

    private void AttachNavigationInteraction()
    {
        if (_navigationInteractionRoot is not null || XamlRoot?.Content is not UIElement root) return;
        _navigationInteractionRoot = root;
        MainNavView.AddHandler(PointerPressedEvent, new PointerEventHandler(OnNavigationPointerPressed), true);
        MainNavView.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnNavigationPreviewKeyDown), true);
        root.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnNavigationPointerEnded), true);
        root.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnNavigationPointerEnded), true);
        root.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(OnNavigationPointerEnded), true);
        root.AddHandler(KeyUpEvent, new KeyEventHandler(OnNavigationKeyUp), true);
        root.GotFocus += OnNavigationNativeGotFocus;
        MainNavView.Expanding += OnNavigationGroupExpanding;
        MainNavView.Collapsed += OnNavigationGroupCollapsed;
        _appActivationSignalSource.ActivityChanged += OnNavigationInteractionActivityChanged;
        PublishNativeNavigationFocus();
    }

    private void DetachNavigationInteraction()
    {
        if (_navigationInteractionRoot is not { } root) return;
        MainNavView.RemoveHandler(PointerPressedEvent, new PointerEventHandler(OnNavigationPointerPressed));
        MainNavView.RemoveHandler(PreviewKeyDownEvent, new KeyEventHandler(OnNavigationPreviewKeyDown));
        root.RemoveHandler(PointerReleasedEvent, new PointerEventHandler(OnNavigationPointerEnded));
        root.RemoveHandler(PointerCanceledEvent, new PointerEventHandler(OnNavigationPointerEnded));
        root.RemoveHandler(PointerCaptureLostEvent, new PointerEventHandler(OnNavigationPointerEnded));
        root.RemoveHandler(KeyUpEvent, new KeyEventHandler(OnNavigationKeyUp));
        root.GotFocus -= OnNavigationNativeGotFocus;
        MainNavView.Expanding -= OnNavigationGroupExpanding;
        MainNavView.Collapsed -= OnNavigationGroupCollapsed;
        _appActivationSignalSource.ActivityChanged -= OnNavigationInteractionActivityChanged;
        _navigationInteractionRoot = null;
        NavVM.SetFocusedConversationId(null);
        ClearNavigationInteraction();
    }

    private void OnNavigationPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var container = FindNavigationInteractionContainer(args.OriginalSource as DependencyObject);
        if (container is null || !args.GetCurrentPoint(MainNavView).Properties.IsLeftButtonPressed) return;
        _navigationPressedPointers.Add(args.Pointer.PointerId);
        UpdateNavigationInteractionHold();
    }

    private void OnNavigationPointerEnded(object sender, PointerRoutedEventArgs args)
    {
        if (!_navigationPressedPointers.Remove(args.Pointer.PointerId)) return;
        UpdateNavigationInteractionHold();
    }

    private void OnNavigationPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var container = FindNavigationInteractionContainer(args.OriginalSource as DependencyObject);
        if (container is null) return;
        _navigationPressedKeys.Add(args.Key);
        UpdateNavigationInteractionHold();
    }

    private void OnNavigationKeyUp(object sender, KeyRoutedEventArgs args)
    {
        if (!_navigationPressedKeys.Remove(args.Key)) return;
        UpdateNavigationInteractionHold();
    }

    private void OnNavigationNativeGotFocus(object sender, RoutedEventArgs args) => PublishNativeNavigationFocus();

    private void PublishNativeNavigationFocus()
    {
        var focused = XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as DependencyObject : null;
        var container = FindNavigationInteractionContainer(focused);
        NavVM.SetFocusedConversationId(container?.DataContext is SessionNavItemViewModel row
            && IsDescendantOf(container, MainNavView) ? row.SessionId : null);
    }

    private void OnNavigationGroupExpanding(NavigationView sender, NavigationViewItemExpandingEventArgs args)
        => PersistNativeNavigationExpansion(args.ExpandingItemContainer);

    private void OnNavigationGroupCollapsed(NavigationView sender, NavigationViewItemCollapsedEventArgs args)
        => PersistNativeNavigationExpansion(args.CollapsedItemContainer);

    private void PersistNativeNavigationExpansion(NavigationViewItemBase? candidate)
    {
        // Native ExpandCollapse includes mouse, keyboard and UIA. Initial template values arrive
        // before Loaded; compact/flyout auto-collapse is not a change to the expanded-pane preference.
        // Child collection updates do not change IsExpanded in Uno 6.7.103 NavigationViewItem.
        if (candidate is not NavigationViewItem { IsLoaded: true, DataContext: StatusGroupNavItemViewModel group } container
            || MainNavView.DisplayMode != NavigationViewDisplayMode.Expanded || !MainNavView.IsPaneOpen
            || !ReferenceEquals(container.XamlRoot, MainNavView.XamlRoot) || !NavVM.Items.Contains(group)) return;
        group.ApplyUserExpandedPreference(container.IsExpanded);
    }

    private void OnNavigationInteractionActivityChanged(object? sender, EventArgs args)
    {
        if (!_appActivationSignalSource.IsActive || !ReferenceEquals(_appActivationSignalSource.ActiveWindow, App.MainWindowInstance))
        {
            ClearNavigationInteraction();
        }
    }

    private void ClearNavigationInteraction()
    {
        _navigationPressedPointers.Clear();
        _navigationPressedKeys.Clear();
        UpdateNavigationInteractionHold();
    }

    private void UpdateNavigationInteractionHold()
        => NavVM.SetTransitionsFrozen(_navigationPressedPointers.Count > 0 || _navigationPressedKeys.Count > 0);

    private NavigationViewItem? FindNavigationInteractionContainer(DependencyObject? source)
    {
        for (var element = source; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (ReferenceEquals(element, MainNavView)) return null;
            if (element is NavigationViewItem container && container.Tag is string tag
                && (NavItemTag.TryParseSession(tag, out _) || NavItemTag.TryParseStatusGroup(tag, out _)
                    || NavItemTag.TryParseProject(tag, out _))) return container;
        }

        return null;
    }
}

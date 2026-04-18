using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Services.Input;
using ExpandCollapseState = Microsoft.UI.Xaml.Automation.ExpandCollapseState;
using FocusDirection = Microsoft.UI.Xaml.Input.FocusNavigationDirection;
using XamlFocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class MainShellGamepadNavigationDispatcher : IGamepadNavigationDispatcher
{
    private readonly ILogger<MainShellGamepadNavigationDispatcher> _logger;

    public MainShellGamepadNavigationDispatcher(ILogger<MainShellGamepadNavigationDispatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool TryDispatch(GamepadNavigationIntent intent)
    {
        if (TryConsumeNavigationIntent(intent))
        {
            return true;
        }

        return intent switch
        {
            GamepadNavigationIntent.MoveUp => TryMoveFocus(FocusDirection.Up),
            GamepadNavigationIntent.MoveDown => TryMoveFocus(FocusDirection.Down),
            GamepadNavigationIntent.MoveLeft => TryMoveFocus(FocusDirection.Left),
            GamepadNavigationIntent.MoveRight => TryMoveFocus(FocusDirection.Right),
            GamepadNavigationIntent.Activate => TryActivateFocusedElement(),
            GamepadNavigationIntent.Back => TryHandleBack(),
            _ => false
        };
    }

    private static bool TryConsumeNavigationIntent(GamepadNavigationIntent intent)
    {
        var current = GetFocusedElement();
        while (current != null)
        {
            if (current is INavigationIntentConsumer consumer
                && consumer.TryConsumeNavigationIntent(intent))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private bool TryActivateFocusedElement()
    {
        var focusedElement = GetFocusedElement();
        if (focusedElement is null)
        {
            return false;
        }

        if (TryInvoke(focusedElement))
        {
            return true;
        }

        if (TryToggle(focusedElement))
        {
            return true;
        }

        if (TryExpandOrCollapse(focusedElement))
        {
            return true;
        }

        return TrySelect(focusedElement);
    }

    private bool TryHandleBack()
    {
        if (TryCloseTopPopup())
        {
            return true;
        }

        var contentFrame = FindNamedDescendant<Frame>(GetRootElement(), "ContentFrame");
        if (contentFrame?.CanGoBack == true)
        {
            contentFrame.GoBack();
            return true;
        }

        var backButton = FindNamedDescendant<Button>(GetRootElement(), "TitleBarBackButton");
        if (backButton?.IsEnabled == true)
        {
            return TryInvoke(backButton);
        }

        return false;
    }

    private bool TryCloseTopPopup()
    {
        var root = GetRootElement();
        var xamlRoot = root?.XamlRoot;
        if (xamlRoot is null)
        {
            return false;
        }

        var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot);
        if (popups.Count > 0)
        {
            var popup = popups[popups.Count - 1];
            if (popup.Child is FrameworkElement child)
            {
                var dialog = FindDescendant<ContentDialog>(child);
                if (dialog != null)
                {
                    dialog.Hide();
                    return true;
                }
            }

            popup.IsOpen = false;
            return true;
        }

        return false;
    }

    private static DependencyObject? GetFocusedElement()
    {
        var root = GetRootElement();
        if (root?.XamlRoot is null)
        {
            return null;
        }

        return XamlFocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
    }

    private bool TryInvoke(DependencyObject element)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            return false;
        }

        var peer = FrameworkElementAutomationPeer.FromElement(frameworkElement)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(frameworkElement);
        if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invokeProvider)
        {
            invokeProvider.Invoke();
            return true;
        }

        return false;
    }

    private bool TrySelect(DependencyObject element)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            return false;
        }

        var peer = FrameworkElementAutomationPeer.FromElement(frameworkElement)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(frameworkElement);
        if (peer?.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selectionItemProvider)
        {
            selectionItemProvider.Select();
            return true;
        }

        if (frameworkElement is SelectorItem selectorItem && selectorItem.Parent is Selector selector)
        {
            selector.SelectedItem = selectorItem.DataContext;
            return true;
        }

        _logger.LogDebug("Gamepad activate ignored because focused element {ElementType} does not expose an invoke or selection pattern.", frameworkElement.GetType().Name);
        return false;
    }

    private bool TryToggle(DependencyObject element)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            return false;
        }

        var peer = FrameworkElementAutomationPeer.FromElement(frameworkElement)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(frameworkElement);
        if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggleProvider)
        {
            toggleProvider.Toggle();
            return true;
        }

        return false;
    }

    private bool TryExpandOrCollapse(DependencyObject element)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            return false;
        }

        var peer = FrameworkElementAutomationPeer.FromElement(frameworkElement)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(frameworkElement);
        if (peer?.GetPattern(PatternInterface.ExpandCollapse) is not IExpandCollapseProvider expandCollapseProvider)
        {
            return false;
        }

        switch (expandCollapseProvider.ExpandCollapseState)
        {
            case ExpandCollapseState.Collapsed:
            case ExpandCollapseState.PartiallyExpanded:
                expandCollapseProvider.Expand();
                return true;
            case ExpandCollapseState.Expanded:
                expandCollapseProvider.Collapse();
                return true;
            default:
                return false;
        }
    }

    private static FrameworkElement? GetRootElement()
    {
        return App.MainWindowInstance?.Content as FrameworkElement;
    }

    private static bool TryMoveFocus(FocusDirection direction)
    {
        var searchRoot = GetNavigationSearchRoot();
        if (searchRoot is null)
        {
            return false;
        }

        var options = new FindNextElementOptions
        {
            SearchRoot = searchRoot
        };
        return XamlFocusManager.TryMoveFocus(direction, options);
    }

    private static DependencyObject? GetNavigationSearchRoot()
    {
        var current = GetFocusedElement() ?? GetRootElement();
        if (current is null)
        {
            return null;
        }

        while (true)
        {
            var parent = VisualTreeHelper.GetParent(current);
            if (parent is null)
            {
                return current;
            }

            current = parent;
        }
    }

    private static T? FindNamedDescendant<T>(DependencyObject? root, string name)
        where T : FrameworkElement
    {
        var match = FindDescendant<T>(root, element => string.Equals(element.Name, name, StringComparison.Ordinal));
        return match;
    }

    private static T? FindDescendant<T>(DependencyObject? root)
        where T : DependencyObject
    {
        return FindDescendant<T>(root, static _ => true);
    }

    private static T? FindDescendant<T>(DependencyObject? root, Predicate<T> predicate)
        where T : DependencyObject
    {
        if (root is null)
        {
            return default;
        }

        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is T match && predicate(match))
            {
                return match;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < childCount; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }

        return default;
    }
}

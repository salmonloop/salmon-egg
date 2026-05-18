using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Services.Input;
using SalmonEgg.Presentation.Core.Services.Navigation;
using ExpandCollapseState = Microsoft.UI.Xaml.Automation.ExpandCollapseState;
using FocusDirection = Microsoft.UI.Xaml.Input.FocusNavigationDirection;
using XamlFocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class MainShellGamepadNavigationDispatcher : IGamepadNavigationDispatcher
{
    private readonly ILogger<MainShellGamepadNavigationDispatcher> _logger;
    private readonly IShellBackNavigationService _shellBackNavigation;

    public MainShellGamepadNavigationDispatcher(
        ILogger<MainShellGamepadNavigationDispatcher> logger,
        IShellBackNavigationService shellBackNavigation)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _shellBackNavigation = shellBackNavigation ?? throw new ArgumentNullException(nameof(shellBackNavigation));
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
            GamepadNavigationIntent.Back => _shellBackNavigation.TryGoBack(),
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

}

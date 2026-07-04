using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using XamlFocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace SalmonEgg.Presentation.Services.Input;

/// <summary>
/// Main-shell implementation of <see cref="IShellFocusScope"/>. This is the
/// only place the shell talks to <see cref="XamlFocusManager"/>, the outer
/// window content or <see cref="VisualTreeHelper"/> when resolving the
/// focused element for gamepad intent dispatchers.
/// </summary>
public sealed class MainShellFocusScope : IShellFocusScope
{
    public DependencyObject? GetFocusedElement()
    {
        if (App.MainWindowInstance?.Content is not FrameworkElement { XamlRoot: { } xamlRoot })
        {
            return null;
        }

        return XamlFocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
    }

    public DependencyObject? GetCurrentRootContent()
    {
        if (App.MainWindowInstance?.Content is not FrameworkElement { XamlRoot: not null } rootContent)
        {
            return null;
        }

        if (rootContent is Frame rootFrame && rootFrame.Content is DependencyObject frameContent)
        {
            return frameContent;
        }

        return rootContent;
    }

    public IEnumerable<DependencyObject> EnumerateAncestors(DependencyObject? start)
    {
        var current = start;
        while (current is not null)
        {
            yield return current;
            current = VisualTreeHelper.GetParent(current);
        }
    }
}

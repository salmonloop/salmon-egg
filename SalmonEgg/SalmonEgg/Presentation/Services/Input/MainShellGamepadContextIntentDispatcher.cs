using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using SalmonEgg.Presentation.Core.Services.Input;
using XamlFocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class MainShellGamepadContextIntentDispatcher : IGamepadContextIntentDispatcher
{
    public bool TryDispatch(GamepadContextIntent intent)
    {
        var current = GetFocusedElement();
        while (current is not null)
        {
            if (current is IGamepadContextIntentConsumer consumer
                && consumer.TryConsumeContextIntent(intent))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static DependencyObject? GetFocusedElement()
    {
        var root = App.MainWindowInstance?.Content as FrameworkElement;
        return root?.XamlRoot is null
            ? null
            : XamlFocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
    }
}

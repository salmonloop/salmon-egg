using Microsoft.UI.Input;
using WinUIKeyEventArgs = Microsoft.UI.Input.KeyEventArgs;

namespace SalmonEgg.Controls;

public sealed partial class ShortcutRecorder
{
    private InputKeyboardSource? _keyboardSource;

    partial void AttachSystemKeyCapture()
    {
        if (XamlRoot?.ContentIsland is null)
        {
            return;
        }

        _keyboardSource ??= InputKeyboardSource.GetForIsland(XamlRoot.ContentIsland);
        _keyboardSource.SystemKeyDown -= OnSystemKeyDown;
        _keyboardSource.SystemKeyDown += OnSystemKeyDown;
    }

    partial void DetachSystemKeyCapture()
    {
        if (_keyboardSource is null)
        {
            return;
        }

        _keyboardSource.SystemKeyDown -= OnSystemKeyDown;
    }

    private void OnSystemKeyDown(InputKeyboardSource sender, WinUIKeyEventArgs args)
    {
        HandleSystemKeyDown(args.VirtualKey, out var handled);
        args.Handled = handled;
    }
}

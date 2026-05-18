using System;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using SalmonEgg.Presentation.Core.Services.Shortcuts;

namespace SalmonEgg.Controls;

public sealed partial class ShortcutRecorder : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty GestureProperty =
        DependencyProperty.Register(
            nameof(Gesture),
            typeof(string),
            typeof(ShortcutRecorder),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(
            nameof(PlaceholderText),
            typeof(string),
            typeof(ShortcutRecorder),
            new PropertyMetadata("Click to record", OnVisualPropertyChanged));

    public static readonly DependencyProperty RecordingTextProperty =
        DependencyProperty.Register(
            nameof(RecordingText),
            typeof(string),
            typeof(ShortcutRecorder),
            new PropertyMetadata("Press shortcut", OnVisualPropertyChanged));

    public static readonly DependencyProperty RecorderAutomationIdProperty =
        DependencyProperty.Register(
            nameof(RecorderAutomationId),
            typeof(string),
            typeof(ShortcutRecorder),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    private bool _isRecording;
    private AppShortcutModifiers _pressedModifiers;

    public ShortcutRecorder()
    {
        InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Gesture
    {
        get => (string)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public string RecordingText
    {
        get => (string)GetValue(RecordingTextProperty);
        set => SetValue(RecordingTextProperty, value);
    }

    public string RecorderAutomationId
    {
        get => (string)GetValue(RecorderAutomationIdProperty);
        set => SetValue(RecorderAutomationIdProperty, value);
    }

    public string DisplayText => _isRecording
        ? RecordingText
        : string.IsNullOrWhiteSpace(Gesture)
            ? PlaceholderText
            : Gesture;

    public string AutomationName => DisplayText;

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var recorder = (ShortcutRecorder)d;
        recorder.OnPropertyChanged(nameof(DisplayText));
        recorder.OnPropertyChanged(nameof(AutomationName));
    }

    private void OnRecorderButtonClick(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            return;
        }

        StartRecording();
    }

    private void OnRecorderButtonLostFocus(object sender, RoutedEventArgs e)
    {
        StopRecording();
    }

    private void OnRecorderButtonPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isRecording && CanStartRecordingFromKey(e.Key))
        {
            StartRecording();
        }

        if (!_isRecording)
        {
            return;
        }

        UpdatePressedModifier(e.Key, isDown: true);

        if (TryHandleVirtualKey(e.Key, out var handled))
        {
            e.Handled = handled;
        }
    }

    private void OnRecorderButtonKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (!_isRecording)
        {
            return;
        }

        UpdatePressedModifier(e.Key, isDown: false);
    }

    private void StartRecording()
    {
        _pressedModifiers = AppShortcutModifiers.None;
        _isRecording = true;
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(AutomationName));
        _ = RecorderButton.Focus(FocusState.Keyboard);
        AttachSystemKeyCapture();
    }

    private void StopRecording()
    {
        if (!_isRecording)
        {
            return;
        }

        _isRecording = false;
        DetachSystemKeyCapture();
        _pressedModifiers = AppShortcutModifiers.None;
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(AutomationName));
    }

    partial void AttachSystemKeyCapture();

    partial void DetachSystemKeyCapture();

    private bool TryHandleVirtualKey(VirtualKey key, out bool handled)
    {
        handled = false;

        if (key == VirtualKey.Tab)
        {
            StopRecording();
            return false;
        }

        if (key == VirtualKey.Escape)
        {
            StopRecording();
            handled = true;
            return true;
        }

        if (key is VirtualKey.Back or VirtualKey.Delete)
        {
            Gesture = string.Empty;
            StopRecording();
            handled = true;
            return true;
        }

        if (IsModifierKey(key))
        {
            handled = true;
            return true;
        }

        if (!TryBuildGestureText(key, out var gestureText))
        {
            handled = true;
            return true;
        }

        Gesture = gestureText;
        StopRecording();
        handled = true;
        return true;
    }

    private static bool IsModifierKey(VirtualKey key)
        => key is VirtualKey.Control
            or VirtualKey.LeftControl
            or VirtualKey.RightControl
            or VirtualKey.Shift
            or VirtualKey.LeftShift
            or VirtualKey.RightShift
            or VirtualKey.Menu
            or VirtualKey.LeftMenu
            or VirtualKey.RightMenu;

    private static bool CanStartRecordingFromKey(VirtualKey key)
    {
        if (key is VirtualKey.Tab or VirtualKey.Enter or VirtualKey.Space)
        {
            return false;
        }

        return IsModifierKey(key)
            || key is VirtualKey.Escape or VirtualKey.Back or VirtualKey.Delete
            || TryGetKeyToken(key, out _);
    }

    private bool TryBuildGestureText(VirtualKey key, out string gestureText)
    {
        gestureText = string.Empty;
        if (!TryGetKeyToken(key, out var keyToken))
        {
            return false;
        }

        var modifiers = GetCurrentModifiers();
        if (modifiers == AppShortcutModifiers.None)
        {
            return false;
        }

        var candidate = modifiers switch
        {
            AppShortcutModifiers.Control => $"Ctrl+{keyToken}",
            AppShortcutModifiers.Alt => $"Alt+{keyToken}",
            AppShortcutModifiers.Shift => $"Shift+{keyToken}",
            AppShortcutModifiers.Control | AppShortcutModifiers.Alt => $"Ctrl+Alt+{keyToken}",
            AppShortcutModifiers.Control | AppShortcutModifiers.Shift => $"Ctrl+Shift+{keyToken}",
            AppShortcutModifiers.Alt | AppShortcutModifiers.Shift => $"Alt+Shift+{keyToken}",
            AppShortcutModifiers.Control | AppShortcutModifiers.Alt | AppShortcutModifiers.Shift => $"Ctrl+Alt+Shift+{keyToken}",
            _ => string.Empty
        };

        if (!AppShortcutGesture.TryParse(candidate, out var parsed))
        {
            return false;
        }

        gestureText = parsed.ToString();
        return true;
    }

    private static bool TryGetKeyToken(VirtualKey key, out string token)
    {
        token = string.Empty;
        if (key is >= VirtualKey.A and <= VirtualKey.Z)
        {
            token = ((char)key).ToString();
            return true;
        }

        if (key is >= VirtualKey.Number0 and <= VirtualKey.Number9)
        {
            token = ((int)key - (int)VirtualKey.Number0).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (key is >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9)
        {
            token = ((int)key - (int)VirtualKey.NumberPad0).ToString(CultureInfo.InvariantCulture);
            return true;
        }

        var name = key.ToString();
        if (name.Length > 1 &&
            name[0] == 'F' &&
            int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            token = name;
            return true;
        }

        return false;
    }

    private AppShortcutModifiers GetCurrentModifiers()
        => _pressedModifiers;

    private void UpdatePressedModifier(VirtualKey key, bool isDown)
    {
        ApplyModifierKeyState(key, isDown);
    }

    private void HandleSystemKeyDown(VirtualKey key, out bool handled)
    {
        handled = false;
        if (!_isRecording && CanStartRecordingFromKey(key))
        {
            StartRecording();
        }

        if (!_isRecording)
        {
            return;
        }

        UpdatePressedModifier(key, isDown: true);

        if (TryHandleVirtualKey(key, out var virtualKeyHandled))
        {
            handled = virtualKeyHandled;
        }
    }

    private void ApplyModifierKeyState(VirtualKey key, bool isDown)
    {
        var modifier = key switch
        {
            VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl => AppShortcutModifiers.Control,
            VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu => AppShortcutModifiers.Alt,
            VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift => AppShortcutModifiers.Shift,
            _ => AppShortcutModifiers.None
        };

        if (modifier == AppShortcutModifiers.None)
        {
            return;
        }

        _pressedModifiers = isDown
            ? _pressedModifiers | modifier
            : _pressedModifiers & ~modifier;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

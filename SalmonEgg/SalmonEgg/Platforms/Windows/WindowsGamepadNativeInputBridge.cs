using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Services.Input;

public sealed class WindowsGamepadNativeInputBridge : IGamepadNativeInputBridge
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_UP = 0x26;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_DOWN = 0x28;

    private readonly ILogger<WindowsGamepadNativeInputBridge> _logger;

    public WindowsGamepadNativeInputBridge(ILogger<WindowsGamepadNativeInputBridge> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool TryDispatch(GamepadNavigationIntent intent)
    {
        if (!TryMapVirtualKey(intent, out var virtualKey))
        {
            return false;
        }

        if (!IsAppWindowForeground())
        {
            return false;
        }

        var inputs = new[]
        {
            CreateKeyboardInput(virtualKey, 0),
            CreateKeyboardInput(virtualKey, KEYEVENTF_KEYUP)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == inputs.Length)
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        _logger.LogWarning(
            "Gamepad native input bridge failed to inject keyboard input. Intent={Intent} Sent={Sent} Requested={Requested} LastError={LastError}",
            intent,
            sent,
            inputs.Length,
            error);

        if (sent == 1)
        {
            _ = SendKeyUp(virtualKey);
        }

        return false;
    }

    private static bool TryMapVirtualKey(GamepadNavigationIntent intent, out ushort virtualKey)
    {
        virtualKey = intent switch
        {
            GamepadNavigationIntent.MoveUp => VK_UP,
            GamepadNavigationIntent.MoveDown => VK_DOWN,
            GamepadNavigationIntent.MoveLeft => VK_LEFT,
            GamepadNavigationIntent.MoveRight => VK_RIGHT,
            GamepadNavigationIntent.Activate => VK_RETURN,
            _ => 0
        };

        return virtualKey != 0;
    }

    private static INPUT CreateKeyboardInput(ushort virtualKey, uint flags)
        => new()
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = flags
                }
            }
        };

    private bool SendKeyUp(ushort virtualKey)
    {
        var cleanup = new[] { CreateKeyboardInput(virtualKey, KEYEVENTF_KEYUP) };
        var sent = SendInput(1, cleanup, Marshal.SizeOf<INPUT>());
        if (sent == 1)
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        _logger.LogWarning(
            "Gamepad native input bridge failed to clean up keyboard input. VirtualKey={VirtualKey} LastError={LastError}",
            virtualKey,
            error);
        return false;
    }

    private static bool IsAppWindowForeground()
    {
        var window = App.MainWindowInstance;
        if (window is null)
        {
            return false;
        }

        var appHwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        return appHwnd != IntPtr.Zero && GetForegroundWindow() == appHwnd;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}

using System;
using System.Runtime.InteropServices;

public static class NativeDesktopAcceptance
{
    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort Key;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        // INPUT's union is sized for MOUSEINPUT, even when only keyboard input is used.
        [FieldOffset(0)] public long MouseAlignment;
        [FieldOffset(24)] public UIntPtr MouseExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);

    [DllImport("user32.dll")]
    public static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll")]
    public static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index,
        System.Text.StringBuilder name, int length, out int needed);

    public static string Name(IntPtr handle)
    {
        var name = new System.Text.StringBuilder(256);
        return GetUserObjectInformation(handle, 2, name, name.Capacity * 2, out _)
            ? name.ToString() : "<unavailable>";
    }

    public static bool TypeCharacter(char value)
    {
        var down = new Input { Type = 1, Data = new InputUnion {
            Keyboard = new KeyboardInput { Scan = value, Flags = 4 } } };
        var up = down;
        up.Data.Keyboard.Flags |= 2;
        return SendInput(2, new[] { down, up }, Marshal.SizeOf<Input>()) == 2;
    }

    public static bool[] RunKeyboardWindow()
    {
        using var form = new System.Windows.Forms.Form {
            Text = "SalmonEgg hosted desktop acceptance", Width = 500, Height = 160 };
        using var input = new System.Windows.Forms.TextBox { Dock = System.Windows.Forms.DockStyle.Fill };
        using var timer = new System.Windows.Forms.Timer { Interval = 100 };
        form.Controls.Add(input);
        var sent = false;
        var entered = false;
        var received = false;
        var started = DateTime.UtcNow;
        // PowerShell delegates cannot run while ShowDialog blocks their pipeline. Keep both the
        // native message pump and callbacks inside managed code so this tests the desktop, not PS reentry.
        form.Shown += (_, _) => {
            form.Activate();
            input.Focus();
            timer.Start();
            Console.WriteLine("[desktop] window shown; waiting for native keyboard input");
        };
        timer.Tick += (_, _) => {
            if (!sent && input.Focused) {
                sent = true;
                entered = TypeCharacter('x');
                Console.WriteLine("[desktop] SendInput result=" + entered);
            }
            received = input.Text == "x";
            if (received || (DateTime.UtcNow - started).TotalSeconds >= 15) form.Close();
        };
        try { form.ShowDialog(); }
        finally { timer.Stop(); }
        return new[] { entered, received };
    }
}

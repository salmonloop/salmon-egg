param(
    [string] $ArtifactsDirectory = "artifacts/windows-packaged-gui"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This gate requires an actual Windows desktop.' }

New-Item -ItemType Directory -Force -Path $ArtifactsDirectory | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
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
}
'@

$process = [System.Diagnostics.Process]::GetCurrentProcess()
$inputDesktop = [NativeDesktopAcceptance]::OpenInputDesktop(0, $false, 0x0100)
$evidence = [ordered]@{
    processId = $process.Id
    sessionId = $process.SessionId
    userInteractive = [Environment]::UserInteractive
    windowStation = [NativeDesktopAcceptance]::Name([NativeDesktopAcceptance]::GetProcessWindowStation())
    inputDesktop = $(if ($inputDesktop -ne [IntPtr]::Zero) { [NativeDesktopAcceptance]::Name($inputDesktop) } else { '<unavailable>' })
    pointerOrKeyboardSettingsChanged = $false
    passed = $false
}
if ($inputDesktop -ne [IntPtr]::Zero) { [void][NativeDesktopAcceptance]::CloseDesktop($inputDesktop) }

$form = [System.Windows.Forms.Form]::new()
$input = [System.Windows.Forms.TextBox]::new()
$input.Dock = [System.Windows.Forms.DockStyle]::Fill
$form.Controls.Add($input)
$form.Text = 'SalmonEgg hosted desktop acceptance'
$form.Width = 500
$form.Height = 160
$timer = [System.Windows.Forms.Timer]::new()
$timer.Interval = 100
$state = @{ sent = $false; started = [DateTime]::UtcNow; entered = $false }
$form.Add_Shown({
    [void]$form.Activate()
    [void]$input.Focus()
    $timer.Start()
})
$timer.Add_Tick({
    if (-not $state.sent -and $input.Focused) {
        $state.sent = $true
        $state.entered = [NativeDesktopAcceptance]::TypeCharacter('x')
    }
    if ($input.Text -eq 'x' -or ([DateTime]::UtcNow - $state.started).TotalSeconds -ge 15) {
        $form.Close()
    }
})
try {
    [void]$form.ShowDialog()
    $evidence.nativeInputSent = $state.entered
    $evidence.nativeTextReceived = $input.Text -eq 'x'
    $evidence.passed = $evidence.nativeInputSent -and $evidence.nativeTextReceived
    $evidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactsDirectory 'desktop-preflight.json')
    $evidence | ConvertTo-Json
    if (-not $evidence.passed) {
        throw 'This runner did not deliver native keyboard input to its visible window. Do not treat package compilation as GUI acceptance.'
    }
}
finally {
    $timer.Stop()
    $timer.Dispose()
    $form.Dispose()
}

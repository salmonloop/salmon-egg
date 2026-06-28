using System;
using System.IO;
using System.Runtime.InteropServices;

#if WINDOWS
namespace SalmonEgg.Platforms.Windows;

public sealed class TrayIconManager : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_COMMAND = 0x0111;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int MF_STRING = 0x00000000;
    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_RETURNCMD = 0x0100;
    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x00000010;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;
    private const int IDI_APPLICATION = 0x7F00;

    private const int CmdOpen = 1001;
    private const int CmdExit = 1002;

    private readonly IntPtr _hwnd;
    private readonly Action _onOpen;
    private readonly Action _onExit;
    private readonly uint _callbackMessage;
    private readonly WndProc _newWndProc;
    private readonly IntPtr _oldWndProc;
    private readonly NOTIFYICONDATA _notifyData;
    private readonly bool _ownsIconHandle;
    private bool _isDisposed;

    public TrayIconManager(IntPtr hwnd, string tooltip, Action onOpen, Action onExit)
    {
        _hwnd = hwnd;
        _onOpen = onOpen ?? throw new ArgumentNullException(nameof(onOpen));
        _onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));
        _callbackMessage = WM_USER + 1;
        _newWndProc = WindowProc;
        _oldWndProc = SetWindowLongPtr(hwnd, GWL_WNDPROC, _newWndProc);
        var iconHandle = LoadApplicationIcon(out _ownsIconHandle);

        _notifyData = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = _callbackMessage,
            hIcon = iconHandle,
            szTip = string.IsNullOrWhiteSpace(tooltip) ? "Salmon Egg" : tooltip
        };

        Shell_NotifyIcon(NIM_ADD, ref _notifyData);
        Shell_NotifyIcon(NIM_MODIFY, ref _notifyData);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            var data = _notifyData;
            Shell_NotifyIcon(NIM_DELETE, ref data);
        }
        catch
        {
        }

        try
        {
            if (_oldWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_hwnd, GWL_WNDPROC, _oldWndProc);
            }
        }
        catch
        {
        }

        if (_ownsIconHandle && _notifyData.hIcon != IntPtr.Zero)
        {
            DestroyIcon(_notifyData.hIcon);
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _callbackMessage)
        {
            var action = (int)lParam;
            if (action == WM_LBUTTONUP)
            {
                _onOpen();
                return IntPtr.Zero;
            }

            if (action == WM_RBUTTONUP)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        if (msg == WM_COMMAND)
        {
            var cmd = wParam.ToInt32() & 0xffff;
            if (cmd == CmdOpen)
            {
                _onOpen();
                return IntPtr.Zero;
            }

            if (cmd == CmdExit)
            {
                _onExit();
                return IntPtr.Zero;
            }
        }

        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MF_STRING, CmdOpen, "打开");
        AppendMenu(menu, MF_STRING, CmdExit, "退出");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);

        var cmd = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        if (cmd == CmdOpen)
        {
            _onOpen();
        }
        else if (cmd == CmdExit)
        {
            _onExit();
        }
    }

    private static IntPtr LoadApplicationIcon(out bool ownsHandle)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "Windows", "icon.ico");
        if (File.Exists(iconPath))
        {
            var handle = LoadImage(
                IntPtr.Zero,
                iconPath,
                IMAGE_ICON,
                GetSystemMetrics(SM_CXSMICON),
                GetSystemMetrics(SM_CYSMICON),
                LR_LOADFROMFILE);
            if (handle != IntPtr.Zero)
            {
                ownsHandle = true;
                return handle;
            }
        }

        ownsHandle = false;
        return LoadIcon(IntPtr.Zero, new IntPtr(IDI_APPLICATION));
    }

    private const int GWL_WNDPROC = -4;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, int uType, int cxDesired, int cyDesired, int fuLoad);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hmenu, int fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProc newProc);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newProc);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
#endif

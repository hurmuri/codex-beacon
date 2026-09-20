using System.Runtime.InteropServices;

namespace CodexBeacon;

internal sealed class TrayIcon : IDisposable
{
    private const uint WmApp = 0x8000;
    private const uint CallbackMessage = WmApp + 73;
    private const uint WmCommand = 0x0111;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint NinSelect = 0x0400;
    private const uint NinKeySelect = 0x0401;
    private const uint NimAdd = 0;
    private const uint NimDelete = 2;
    private const uint NimSetVersion = 4;
    private const uint NifMessage = 1;
    private const uint NifIcon = 2;
    private const uint NifTip = 4;
    private const uint NotifyIconVersion4 = 4;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private const uint LrDefaultSize = 0x0040;
    private const uint MfString = 0;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const int SwRestore = 9;
    private const int SwHide = 0;
    private const uint OpenCommand = 1001;
    private const uint ExitCommand = 1002;

    private readonly IntPtr _windowHandle;
    private readonly Action _show;
    private readonly Action _exit;
    private readonly WindowProcedure _windowProcedure;
    private readonly IntPtr _messageWindowHandle;
    private readonly IntPtr _moduleHandle;
    private readonly string _windowClassName;
    private readonly uint _taskbarCreatedMessage;
    private readonly IntPtr _iconHandle;
    private bool _disposed;

    public TrayIcon(IntPtr windowHandle, Action show, Action exit)
    {
        _windowHandle = windowHandle;
        _show = show;
        _exit = exit;
        _windowProcedure = WindowProc;
        _moduleHandle = GetModuleHandleW(null);
        _windowClassName = $"CodexBeacon.Tray.{Environment.ProcessId}.{Guid.NewGuid():N}";
        var windowClass = new WindowClass
        {
            cbSize = (uint)Marshal.SizeOf<WindowClass>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            hInstance = _moduleHandle,
            lpszClassName = _windowClassName
        };
        if (RegisterClassExW(ref windowClass) == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Unable to register the tray message window.");
        _messageWindowHandle = CreateWindowExW(0, _windowClassName, "", 0,
            0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, _moduleHandle, IntPtr.Zero);
        if (_messageWindowHandle == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Unable to create the tray message window.");
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        _iconHandle = LoadImageW(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
        AddIcon();
    }

    public static void ShowWindow(IntPtr windowHandle)
    {
        NativeShowWindow(windowHandle, SwRestore);
        SetForegroundWindow(windowHandle);
    }

    public static void HideWindow(IntPtr windowHandle) => NativeShowWindow(windowHandle, SwHide);

    private void AddIcon()
    {
        if (_iconHandle == IntPtr.Zero)
        {
            AppLog.Error("Tray", "Unable to load Assets\\app-icon.ico for the system tray.");
            return;
        }

        var data = CreateData();
        if (!Shell_NotifyIconW(NimAdd, ref data))
        {
            AppLog.Error("Tray", "Windows rejected the system tray icon registration.");
            return;
        }

        data.uTimeoutOrVersion = NotifyIconVersion4;
        Shell_NotifyIconW(NimSetVersion, ref data);
    }

    private NotifyIconData CreateData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        hWnd = _messageWindowHandle,
        uID = 1,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = CallbackMessage,
        hIcon = _iconHandle,
        szTip = "Codex Beacon"
    };

    private IntPtr WindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            AddIcon();
            return IntPtr.Zero;
        }

        if (message == CallbackMessage)
        {
            var trayMessage = (uint)(lParam.ToInt64() & 0xffff);
            if (trayMessage is WmLButtonUp or WmLButtonDoubleClick or NinSelect or NinKeySelect)
            {
                AppLog.Info("Tray", "Open requested from the system tray.");
                _show();
            }
            else if (trayMessage is WmRButtonUp or WmContextMenu)
                ShowContextMenu();
            return IntPtr.Zero;
        }

        if (message == WmCommand)
        {
            var command = (uint)(wParam.ToInt64() & 0xffff);
            if (command == OpenCommand) _show();
            if (command == ExitCommand) _exit();
        }

        return DefWindowProcW(windowHandle, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenuW(menu, MfString, OpenCommand, Localization.Get("TrayOpen"));
            AppendMenuW(menu, MfSeparator, 0, null);
            AppendMenuW(menu, MfString, ExitCommand, Localization.Get("TrayExit"));
            GetCursorPos(out var cursor);
            SetForegroundWindow(_messageWindowHandle);
            var command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCommand,
                cursor.X, cursor.Y, 0, _messageWindowHandle, IntPtr.Zero);
            if (command == OpenCommand) _show();
            if (command == ExitCommand) _exit();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = CreateData();
        Shell_NotifyIconW(NimDelete, ref data);
        if (_messageWindowHandle != IntPtr.Zero) DestroyWindow(_messageWindowHandle);
        if (_moduleHandle != IntPtr.Zero) UnregisterClassW(_windowClassName, _moduleHandle);
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClassW(string className, IntPtr instance);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, uint id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr windowHandle, IntPtr rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);
}

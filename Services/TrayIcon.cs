using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace IpGuard;

public sealed record TrayMenuItem(
    int Id,
    string Text,
    bool Checked = false,
    bool Enabled = true,
    bool IsSeparator = false,
    IReadOnlyList<TrayMenuItem>? Submenu = null,
    bool IsDefault = false);

/// <summary>Classic Shell_NotifyIcon tray icon with a native Win32 popup menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private const int WM_TRAYICON = 0x8000 + 1;
    private const int NIM_ADD = 0;
    private const int NIM_MODIFY = 1;
    private const int NIM_DELETE = 2;
    private const int NIF_MESSAGE = 0x1;
    private const int NIF_ICON = 0x2;
    private const int NIF_TIP = 0x4;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_CHECKED = 0x0008;
    private const uint MF_GRAYED = 0x0001;
    private const uint MF_POPUP = 0x0010;

    private readonly IntPtr _owner;
    private readonly HwndSource _source;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private bool _added;
    private bool _vpnOn;
    private string _tooltip = "IP Guard";

    public event Action? LeftClick;
    public event Action? MenuRequested;
    public event Action<int>? CommandSelected;

    public TrayIcon(IntPtr owner)
    {
        _owner = owner;
        _source = new HwndSource(new HwndSourceParameters("IpGuardTrayWindow")
        {
            WindowStyle = 0,
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        });
        _source.AddHook(WndProc);
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _icon = LoadStateIcon(_vpnOn);
        if (_icon == IntPtr.Zero)
        {
            _icon = LoadAppIcon();
        }

        AddIcon();
    }

    /// <summary>Accent icon while the VPN is on, gray icon when it is off.</summary>
    public void SetVpnOn(bool vpnOn)
    {
        if (_vpnOn == vpnOn || _icon == IntPtr.Zero)
        {
            return;
        }

        var next = LoadStateIcon(vpnOn);
        if (next == IntPtr.Zero)
        {
            return;
        }

        _vpnOn = vpnOn;
        var previous = _icon;
        _icon = next;
        Log.Info($"Tray icon: {(vpnOn ? "VPN on (accent)" : "VPN off (gray)")}");

        if (_added)
        {
            var data = CreateData(NIF_ICON);
            NativeMethods.Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        NativeMethods.DestroyIcon(previous);
    }

    private static IntPtr LoadStateIcon(bool vpnOn)
    {
        var resource = vpnOn ? "Assets/tray-on.ico" : "Assets/tray-off.ico";
        try
        {
            var info = Application.GetResourceStream(new Uri(resource, UriKind.Relative));
            if (info is null)
            {
                return IntPtr.Zero;
            }

            using var stream = info.Stream;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            if (bytes.Length < 22 || BitConverter.ToUInt16(bytes, 2) != 1)
            {
                return IntPtr.Zero;
            }

            int count = BitConverter.ToUInt16(bytes, 4);
            if (count < 1)
            {
                return IntPtr.Zero;
            }

            var size = (int)BitConverter.ToUInt32(bytes, 6 + 8);
            var offset = (int)BitConverter.ToUInt32(bytes, 6 + 12);
            if (size <= 0 || offset + size > bytes.Length)
            {
                return IntPtr.Zero;
            }

            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var pointer = pinned.AddrOfPinnedObject() + offset;
                return NativeMethods.CreateIconFromResourceEx(pointer, (uint)size, true, 0x00030000, 0, 0, 0);
            }
            finally
            {
                pinned.Free();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("State icon: " + ex.Message);
            return IntPtr.Zero;
        }
    }

    private static IntPtr LoadAppIcon()
    {
        try
        {
            if (NativeMethods.ExtractIconEx(StartupService.ExecutablePath, 0, out var large, out var small, 1) > 0)
            {
                if (large != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(large);
                }

                if (small != IntPtr.Zero)
                {
                    return small;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("ExtractIconEx: " + ex.Message);
        }

        return NativeMethods.LoadIcon(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
    }

    private NOTIFYICONDATA CreateData(int flags)
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _icon,
            szTip = Truncate(_tooltip, 127),
            szInfo = "",
            szInfoTitle = ""
        };
    }

    private void AddIcon()
    {
        var data = CreateData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added = NativeMethods.Shell_NotifyIcon(NIM_ADD, ref data);
        if (!_added)
        {
            Log.Warn("Shell_NotifyIcon(NIM_ADD) failed");
        }
    }

    public void SetTooltip(string text)
    {
        _tooltip = string.IsNullOrWhiteSpace(text) ? "IP Guard" : text;
        if (!_added)
        {
            return;
        }

        var data = CreateData(NIF_TIP);
        NativeMethods.Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void ShowMenu(IReadOnlyList<TrayMenuItem> items)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            BuildMenu(menu, items);
            NativeMethods.SetMenuDefaultItem(menu, 1, 0);
            if (!NativeMethods.GetCursorPos(out var point))
            {
                return;
            }

            NativeMethods.SetForegroundWindow(_owner);
            var command = NativeMethods.TrackPopupMenu(
                menu,
                TPM_RETURNCMD | TPM_NONOTIFY | TPM_LEFTALIGN | TPM_BOTTOMALIGN,
                point.X,
                point.Y,
                0,
                _owner,
                IntPtr.Zero);

            NativeMethods.PostMessage(_owner, 0x0000, IntPtr.Zero, IntPtr.Zero);
            if (command != 0)
            {
                CommandSelected?.Invoke(command);
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static void BuildMenu(IntPtr menu, IReadOnlyList<TrayMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                NativeMethods.AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
                continue;
            }

            if (item.Submenu is { Count: > 0 })
            {
                var submenu = NativeMethods.CreatePopupMenu();
                BuildMenu(submenu, item.Submenu);
                NativeMethods.AppendMenu(menu, MF_POPUP | (item.Enabled ? 0u : MF_GRAYED), submenu, item.Text);
                continue;
            }

            var flags = MF_STRING
                        | (item.Checked ? MF_CHECKED : 0u)
                        | (item.Enabled ? 0u : MF_GRAYED);
            NativeMethods.AppendMenu(menu, flags, new IntPtr(item.Id), item.Text);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMessage = lParam.ToInt32() & 0xFFFF;
            switch (mouseMessage)
            {
                case WM_LBUTTONUP:
                    handled = true;
                    LeftClick?.Invoke();
                    return IntPtr.Zero;
                case WM_RBUTTONUP:
                    handled = true;
                    Application.Current?.Dispatcher.BeginInvoke(() => MenuRequested?.Invoke());
                    return IntPtr.Zero;
            }
        }
        else if ((uint)msg == _taskbarCreated)
        {
            // Explorer restarted: put the icon back.
            _added = false;
            AddIcon();
        }

        return IntPtr.Zero;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    public void Dispose()
    {
        try
        {
            if (_added)
            {
                var data = CreateData(0);
                NativeMethods.Shell_NotifyIcon(NIM_DELETE, ref data);
                _added = false;
            }

            _source.RemoveHook(WndProc);
            _source.Dispose();

            if (_icon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_icon);
                _icon = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Tray dispose: " + ex.Message);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public int dwState;
        public int dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public int uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }
}

internal static class NativeMethods
{
    [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
    private static extern void FlushMenuThemes();

    /// <summary>Windows 10 1903+ / Windows 11: draw Win32 menus (the tray menu) in dark mode.</summary>
    public static void TryEnableDarkMenus()
    {
        try
        {
            SetPreferredAppMode(2); // ForceDark
            FlushMenuThemes();
        }
        catch
        {
            // older Windows: menus stay light, which is fine
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(int message, ref TrayIcon.NOTIFYICONDATA data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconFromResourceEx(IntPtr presbits, uint dwResSize, bool fIcon,
        uint dwVer, int cxDesired, int cyDesired, uint flags);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenu(IntPtr menu, uint flags, IntPtr id, string? text);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    public static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out TrayIcon.POINT point);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

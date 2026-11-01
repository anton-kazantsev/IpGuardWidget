using System.Runtime.InteropServices;
using System.Windows;

namespace IpGuard;

/// <summary>
/// Monitor work areas in WPF DIPs (each monitor scaled by its own DPI) plus physical-pixel
/// helpers. Window.Left/Top are DIPs, but Windows places the HWND in physical pixels, so a
/// position saved on a 125% monitor does not survive a naive DIP round-trip.
/// </summary>
internal static class Monitors
{
    private static IReadOnlyList<Rect>? _workAreas;
    private static IReadOnlyList<Rect>? _physicalWorkAreas;

    public static IReadOnlyList<Rect> WorkAreas => _workAreas ??= LoadWorkAreas();

    /// <summary>Monitor work areas in real screen pixels.</summary>
    public static IReadOnlyList<Rect> PhysicalWorkAreas => _physicalWorkAreas ??= LoadPhysicalWorkAreas();

    public static Rect PrimaryWorkArea
    {
        get
        {
            foreach (var area in PhysicalWorkAreas)
            {
                if (area.Contains(0, 0))
                {
                    return area;
                }
            }

            return PhysicalWorkAreas[0];
        }
    }

    private static IReadOnlyList<Rect> LoadPhysicalWorkAreas()
    {
        var areas = new List<Rect>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
                {
                    var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(monitor, ref info))
                    {
                        var work = info.rcWork;
                        areas.Add(new Rect(work.Left, work.Top,
                            work.Right - work.Left, work.Bottom - work.Top));
                    }

                    return true;
                },
                IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warn("EnumDisplayMonitors (physical): " + ex.Message);
        }

        if (areas.Count == 0)
        {
            areas.Add(SystemParameters.WorkArea);
        }

        return areas;
    }

    /// <summary>True when most of the window is physically inside a monitor.</summary>
    public static bool IsPhysicalRectVisible(Rect windowRect)
    {
        var needWidth = windowRect.Width * 0.6;
        var needHeight = windowRect.Height * 0.6;

        foreach (var work in PhysicalWorkAreas)
        {
            var visible = Rect.Intersect(windowRect, work);
            if (!visible.IsEmpty && visible.Width >= needWidth && visible.Height >= needHeight)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<Rect> LoadWorkAreas()
    {
        var areas = new List<Rect>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
                {
                    var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(monitor, ref info))
                    {
                        var work = info.rcWork;
                        var scale = DpiScale(monitor);
                        areas.Add(new Rect(
                            work.Left / scale,
                            work.Top / scale,
                            (work.Right - work.Left) / scale,
                            (work.Bottom - work.Top) / scale));
                    }

                    return true;
                },
                IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warn("EnumDisplayMonitors: " + ex.Message);
        }

        if (areas.Count == 0)
        {
            areas.Add(SystemParameters.WorkArea);
        }

        return areas;
    }

    /// <summary>DPI scale (1.0 = 96 dpi) of the monitor under a physical screen point.</summary>
    public static double ScaleAt(double physicalX, double physicalY)
    {
        try
        {
            var point = new POINT { X = (int)physicalX, Y = (int)physicalY };
            var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
            return DpiScale(monitor);
        }
        catch
        {
            return 1.0;
        }
    }

    private static double DpiScale(IntPtr monitor)
    {
        try
        {
            if (GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return dpiX / 96.0;
            }
        }
        catch
        {
            // older Windows: 100%
        }

        return 1.0;
    }

    /// <summary>Keeps the window inside the monitor it overlaps the most (window rect in DIPs).</summary>
    public static Rect ClampToWorkArea(double left, double top, double width, double height)
    {
        var rect = new Rect(left, top, width, height);
        Rect? best = null;
        var bestArea = 0.0;

        foreach (var work in WorkAreas)
        {
            var intersection = Rect.Intersect(rect, work);
            var area = intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
            if (area > bestArea)
            {
                bestArea = area;
                best = work;
            }
        }

        var target = best ?? WorkAreas[0];
        var clampedLeft = Math.Clamp(left, target.Left, Math.Max(target.Left, target.Right - width));
        var clampedTop = Math.Clamp(top, target.Top, Math.Max(target.Top, target.Bottom - height));
        return new Rect(clampedLeft, clampedTop, width, height);
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}

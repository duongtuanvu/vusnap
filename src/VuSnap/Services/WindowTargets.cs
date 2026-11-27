// Adapted from Snapboard WindowEnumerator.cs, MIT, Copyright (c) 2026 FlowDesk.
// Upstream: https://github.com/Flowdesktech/Snapboard/blob/379651086cd7ce6eb50733925ffe4c74f006fdab/Snapboard/Helpers/WindowEnumerator.cs
using System.Runtime.InteropServices;
using System.Text;
using SD = System.Drawing;

namespace VuSnap.Services;

public static class WindowTargets
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr arg);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder title, int maxCount);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    private const int DWMWA_CLOAKED = 14;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public sealed record Target(IntPtr Handle, SD.Rectangle Bounds, uint ProcessId, string Title)
    {
        public string Display => $"{Title}  ({Bounds.Width} × {Bounds.Height})";
    }
    public static IntPtr Foreground() => GetForegroundWindow();

    public static bool TryGetVisible(IntPtr hwnd, out SD.Rectangle bounds)
    {
        bounds = SD.Rectangle.Empty;
        if (hwnd == IntPtr.Zero || hwnd == GetShellWindow() || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0 || pid == Environment.ProcessId) return false;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        var name = new StringBuilder(128);
        GetClassName(hwnd, name, name.Capacity);
        if (name.ToString() is "WorkerW" or "Progman" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        return TryGetWindowBounds(hwnd, out bounds) && bounds.Width >= 8 && bounds.Height >= 8;
    }

    public static Target? Identify(IntPtr hwnd)
    {
        if (!TryGetVisible(hwnd, out var rect) || GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return null;
        var title = new StringBuilder(512);
        GetWindowText(hwnd, title, title.Capacity);
        return new Target(hwnd, rect, pid, string.IsNullOrWhiteSpace(title.ToString()) ? Loc.T("wgc.untitled") : title.ToString());
    }

    public static bool IsCurrent(Target target) => TryGetVisible(target.Handle, out _) &&
        GetWindowThreadProcessId(target.Handle, out uint pid) != 0 && pid == target.ProcessId;

    public static IReadOnlyList<Target> Enumerate()
    {
        var result = new List<Target>();
        EnumWindows((hwnd, _) =>
        {
            if (Identify(hwnd) is { } target) result.Add(target);
            return result.Count < 512;
        }, IntPtr.Zero);
        return result;
    }

    public static bool TryGetWindowBounds(IntPtr hwnd, out SD.Rectangle bounds)
    {
        // Prefer the DWM-reported "extended frame" rect — that excludes the
        // invisible resize padding Windows adds around most top-level windows.
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0)
        {
            bounds = SD.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (bounds.Width > 0 && bounds.Height > 0) return true;
        }
        if (GetWindowRect(hwnd, out var r2))
        {
            bounds = SD.Rectangle.FromLTRB(r2.Left, r2.Top, r2.Right, r2.Bottom);
            return bounds.Width > 0 && bounds.Height > 0;
        }
        bounds = SD.Rectangle.Empty;
        return false;
    }

}

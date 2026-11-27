using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace VuSnap.Services;

/// <summary>Quản lý bảng màu Light / Dark chuẩn UI/UX Pro cho toàn bộ cửa sổ WPF.</summary>
public static class ThemeManager
{
    public static bool IsDark(string? theme) =>
        theme == "Dark" || (theme == "System" && !SystemUsesLightTheme());

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        }
        catch { return true; }
    }

    private static SolidColorBrush Solid(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public static void Apply(string? theme, ResourceDictionary? target = null)
    {
        var dark = IsDark(theme);
        var res = target ?? Application.Current?.Resources;
        if (res is null) return;

        res["WindowBrush"] = dark ? Solid(20, 23, 41) : Solid(255, 255, 255);
        res["NavBgBrush"] = dark ? Solid(17, 20, 36) : Solid(250, 251, 253);
        res["PanelBrush"] = dark ? Solid(28, 32, 56) : Solid(245, 247, 253);
        res["SubtleSurfaceBrush"] = dark ? Solid(36, 41, 69) : Solid(238, 241, 251);
        res["InputBrush"] = dark ? Solid(20, 23, 41) : Solid(255, 255, 255);
        res["HeaderBrush"] = dark ? Solid(35, 40, 71) : Solid(237, 240, 250);
        res["ButtonBrush"] = dark ? Solid(28, 32, 56) : Solid(255, 255, 255);
        res["BorderBrushX"] = dark ? Solid(45, 51, 84) : Solid(230, 234, 246);
        res["ControlBorderBrush"] = dark ? Solid(53, 60, 99) : Solid(220, 226, 245);
        res["RowAltBrush"] = dark ? Solid(24, 28, 51) : Solid(246, 248, 254);
        res["SelRowBrush"] = dark ? Solid(38, 44, 78) : Solid(238, 240, 255);
        res["TextBrush"] = dark ? Solid(244, 246, 255) : Solid(22, 26, 48);
        res["MutedBrush"] = dark ? Solid(149, 159, 194) : Solid(100, 109, 138);
        res["AccentBrush"] = dark ? Solid(123, 139, 255) : Solid(111, 109, 255);
        res["AccentSoftBrush"] = dark ? Solid(35, 40, 71) : Solid(238, 240, 255);
        res["AccentTextBrush"] = dark ? Solid(143, 168, 255) : Solid(93, 90, 246);
        res["ToggleOffBrush"] = dark ? Solid(45, 51, 84) : Solid(215, 222, 242);
        res["MintBrush"] = dark ? Solid(182, 236, 235) : Solid(140, 236, 228);
        res["SuccessBrush"] = Solid(16, 185, 129);
        res["DangerTextBrush"] = dark ? Solid(255, 180, 194) : Solid(219, 39, 119);
        res["AuraLavenderBrush"] = dark ? Solid(111, 109, 255, 26) : Solid(204, 197, 249, 110);
        res["AuraPeriwinkleBrush"] = dark ? Solid(115, 150, 254, 22) : Solid(182, 193, 250, 95);
        res["AuraMintBrush"] = dark ? Solid(182, 236, 235, 18) : Solid(182, 236, 235, 105);
        res["KeycapShadowBrush"] = dark ? Solid(14, 16, 30) : Solid(212, 220, 243);
        res["PopupShadowColor"] = dark ? Color.FromArgb(140, 0, 0, 0) : Color.FromArgb(40, 72, 76, 148);

        var grad = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(111, 109, 255), 0.0),
                new GradientStop(Color.FromRgb(135, 149, 246), 0.5),
                new GradientStop(Color.FromRgb(115, 150, 254), 1.0),
            }
        };
        grad.Freeze();
        res["BrandGradientBrush"] = grad;

        if (Application.Current?.Windows is { } windows)
        {
            foreach (Window w in windows)
                ApplyWindowChrome(w, dark);
        }
    }

    public static void ApplyWindowChrome(Window window, bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int darkMode = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, 20, ref darkMode, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            int cornerPref = 2; // DWMWCP_ROUND
            _ = DwmSetWindowAttribute(hwnd, 33, ref cornerPref, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
        }
        catch { /* Windows versions older than Win11 ignore attributes */ }
    }

    public static void HookWindowChrome(Window window, Func<bool> isDark)
    {
        void Sync() => ApplyWindowChrome(window, isDark());
        window.SourceInitialized += (_, _) => Sync();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}

using System.Diagnostics;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Icon = System.Drawing.Icon;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using MouseButtons = System.Windows.Forms.MouseButtons;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;

namespace VuSnap.Services;

/// <summary>Logo / biểu tượng app dùng chung, lấy từ thư mục Assets (app.ico, app-logo.png) nhúng trong exe.</summary>
public static class AppIcon
{
    private static readonly Uri IconUri = new("pack://application:,,,/Assets/app-fit.ico");
    private static readonly Uri LogoUri = new("pack://application:,,,/Assets/app-logo.png");

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Biểu tượng cho thanh tiêu đề / taskbar của cửa sổ WPF.</summary>
    public static System.Windows.Media.ImageSource LoadImage() => System.Windows.Media.Imaging.BitmapFrame.Create(IconUri);

    /// <summary>Logo lớn (PNG trong suốt) dùng trong giao diện.</summary>
    public static System.Windows.Media.ImageSource LoadLogo()
    {
        var img = new System.Windows.Media.Imaging.BitmapImage(LogoUri);
        img.Freeze();
        return img;
    }

    /// <summary>Icon khay hệ thống: cắt viền trong suốt quanh logo rồi thu nhỏ chất lượng cao đúng cỡ icon nhỏ của Windows (nét hơn là để Windows tự co từ 256px).</summary>
    public static Icon LoadTrayIcon()
    {
        try
        {
            var info = Application.GetResourceStream(LogoUri);
            using var src = new System.Drawing.Bitmap(info.Stream);
            int minX = src.Width, minY = src.Height, maxX = -1, maxY = -1;
            for (var y = 0; y < src.Height; y += 2)
                for (var x = 0; x < src.Width; x += 2)
                    if (src.GetPixel(x, y).A > 20)
                    {
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
            if (maxX < 0) { minX = 0; minY = 0; maxX = src.Width - 1; maxY = src.Height - 1; }
            var side = Math.Max(maxX - minX + 1, maxY - minY + 1);
            var sx = Math.Clamp((minX + maxX) / 2 - side / 2, 0, Math.Max(0, src.Width - side));
            var sy = Math.Clamp((minY + maxY) / 2 - side / 2, 0, Math.Max(0, src.Height - side));

            var size = Math.Max(16, System.Windows.Forms.SystemInformation.SmallIconSize.Width);
            using var dst = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(dst))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, new System.Drawing.Rectangle(0, 0, size, size), new System.Drawing.Rectangle(sx, sy, side, side), System.Drawing.GraphicsUnit.Pixel);
            }
            var handle = dst.GetHicon();
            var icon = (Icon)Icon.FromHandle(handle).Clone();
            DestroyIcon(handle);
            return icon;
        }
        catch { return System.Drawing.SystemIcons.Application; }
    }
}

/// <summary>Biểu tượng ở khay hệ thống ("Other system tray icons") + menu chuột phải: Mở / Chụp ngay / Thoát.</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly System.Windows.Forms.ToolStripMenuItem _open = new();
    private readonly System.Windows.Forms.ToolStripMenuItem _capture = new();
    private readonly System.Windows.Forms.ToolStripMenuItem _donate = new();
    private readonly System.Windows.Forms.ToolStripMenuItem _exit = new();

    public event Action? OpenRequested;
    public event Action? CaptureRequested;
    public event Action<VuSnap.Models.CaptureMode>? ModeRequested;
    private readonly System.Windows.Forms.ToolStripMenuItem _modes = new();
    public event Action? DonateRequested;
    public event Action? ExitRequested;

    public TrayService()
    {
        var menu = new ContextMenuStrip();
        _open.Font = new System.Drawing.Font(_open.Font, System.Drawing.FontStyle.Bold);
        _open.Click += (_, _) => OpenRequested?.Invoke();
        _capture.Click += (_, _) => CaptureRequested?.Invoke();
        _donate.Click += (_, _) => DonateRequested?.Invoke();
        _exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(_open);
        menu.Items.Add(_capture);
        foreach (var mode in Enum.GetValues<VuSnap.Models.CaptureMode>())
        {
            var item = new System.Windows.Forms.ToolStripMenuItem { Tag = mode };
            item.Click += (_, _) => ModeRequested?.Invoke(mode);
            _modes.DropDownItems.Add(item);
        }
        menu.Items.Add(_modes);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(_exit);

        _icon = new NotifyIcon { Icon = AppIcon.LoadTrayIcon(), Text = AppInfo.AppName, ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenRequested?.Invoke(); };
        SetTexts();
    }

    /// <summary>Cập nhật chuỗi menu theo ngôn ngữ hiện tại.</summary>
    public void SetTexts()
    {
        _open.Text = Loc.T("tray.open");
        _capture.Text = Loc.T("tray.capture");
        _modes.Text = Loc.T("capture.mode");
        foreach (System.Windows.Forms.ToolStripMenuItem item in _modes.DropDownItems) item.Text = Loc.T("capture." + item.Tag);
        _donate.Text = "♥  " + Loc.T("about.donate");
        _exit.Text = Loc.T("tray.exit");
        _icon.Text = AppInfo.TitleWithVersion;
    }

    public void ShowBalloon(string text) => _icon.ShowBalloonTip(3000, AppInfo.AppName, text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

/// <summary>Khởi động cùng Windows: ghi/xóa giá trị trong HKCU\...\Run (chạy với --tray để ẩn ngay xuống khay).</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VuSnap";

    public static void Apply(bool enable)
    {
        // Chạy thử với thư mục dữ liệu riêng (test) thì không đụng vào registry thật.
        if (Environment.GetEnvironmentVariable("VUSNAP_DATA_DIR") is { Length: > 0 }) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe) || Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return;
                key.SetValue(ValueName, $"\"{exe}\" --tray");
            }
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* không ghi được registry thì bỏ qua */ }
    }
}

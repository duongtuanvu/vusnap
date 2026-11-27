using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media.Imaging;
using VuSnap.Core;
using VuSnap.Models;
using Forms = System.Windows.Forms;

namespace VuSnap.Services;

public static class ScreenCaptureService
{
    public static Rectangle[] Monitors() => Forms.Screen.AllScreens.Select(s => s.Bounds).ToArray();

    public static CaptureResult Capture(CaptureRequest request, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(request.Mode) || request.DelaySeconds is not (0 or 3 or 5 or 10))
            throw new CaptureException("ERR_CAPTURE_002");
        var monitors = Monitors();
        var desktop = CaptureGeometry.DesktopBounds(monitors);
        var monitor = Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds;
        var bounds = monitor;
        Rectangle? initial = null;
        IReadOnlyList<Rectangle> targets = Array.Empty<Rectangle>();
        switch (request.Mode)
        {
            case CaptureMode.ColorPicker:
            case CaptureMode.Monitor: initial = monitor; break;
            case CaptureMode.Desktop: bounds = desktop; initial = desktop; break;
            case CaptureMode.ActiveWindow:
            case CaptureMode.WindowPicker:
                // These modes must never fall back to desktop pixels.
                throw new CaptureException("ERR_WGC_006");
            case CaptureMode.WindowHover:
                // Full desktop picker; one frozen snapshot, topmost-first rectangles.
                bounds = desktop;
                targets = WindowTargets.Enumerate().Select(w => Rectangle.Intersect(w.Bounds, desktop))
                    .Where(r => r.Width >= 8 && r.Height >= 8 && monitors.Any(m => m.IntersectsWith(r))).ToArray();
                if (targets.Count == 0) throw new CaptureException("ERR_CAPTURE_004");
                break;
            case CaptureMode.Repeat:
                if (request.Previous is not { } previous || !CaptureGeometry.SameLayout(previous.MonitorLayout, monitors) ||
                    !desktop.Contains(previous.Region) || previous.Region.Width <= 0 || previous.Region.Height <= 0 ||
                    !monitors.Any(m => m.IntersectsWith(previous.Region)))
                    throw new CaptureException("ERR_CAPTURE_005");
                initial = previous.Region;
                bounds = CaptureGeometry.EditorBounds(previous.Region, monitors);
                break;
        }
        CaptureGeometry.Validate(bounds);
        var image = CapturePixels(bounds, monitors, cancellation);
        cancellation.ThrowIfCancellationRequested();
        if (!CaptureGeometry.SameLayout(monitors, Monitors())) throw new CaptureException("ERR_CAPTURE_005");
        return new CaptureResult(image, bounds, request.Mode, targets, initial, monitors);
    }

    public static BitmapSource CapturePixels(Rectangle bounds, IReadOnlyList<Rectangle> monitors, CancellationToken cancellation = default)
    {
        CaptureGeometry.Validate(bounds);
        cancellation.ThrowIfCancellationRequested();
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Black);
            // Copy only actual displays; gaps in staggered arrangements remain black.
            foreach (var monitor in monitors)
            {
                cancellation.ThrowIfCancellationRequested();
                var part = Rectangle.Intersect(bounds, monitor);
                if (part.Width <= 0 || part.Height <= 0) continue;
                graphics.CopyFromScreen(part.Location, new Point(part.Left - bounds.Left, part.Top - bounds.Top),
                    part.Size, CopyPixelOperation.SourceCopy);
            }
        }
        var data = bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            if (data.Stride <= 0) throw new CaptureException("ERR_CAPTURE_006");
            var image = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgr32, null, data.Scan0, checked(data.Stride * bitmap.Height), data.Stride);
            image.Freeze();
            return image;
        }
        finally { bitmap.UnlockBits(data); }
    }
}

using System.Drawing;
using VuSnap.Models;

namespace VuSnap.Core;

public static class CaptureGeometry
{
    public const long MaximumPixels = 40_000_000;
    public static void Validate(Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || (long)rect.X + rect.Width > int.MaxValue || (long)rect.Y + rect.Height > int.MaxValue)
            throw new CaptureException("ERR_CAPTURE_002");
        if ((long)rect.Width * rect.Height > MaximumPixels || rect.Width > 16384 || rect.Height > 16384)
            throw new CaptureException("ERR_CAPTURE_003");
    }

    // Repeat requires the complete region to be covered by the current physical displays.
    public static bool IsCovered(Rectangle region, IEnumerable<Rectangle> monitors)
    {
        if (region.Width <= 0 || region.Height <= 0) return false;
        var remaining = new List<Rectangle> { region };
        foreach (var monitor in monitors)
        {
            var next = new List<Rectangle>();
            foreach (var r in remaining)
            {
                var i = Rectangle.Intersect(r, monitor);
                if (i.Width <= 0 || i.Height <= 0) { next.Add(r); continue; }
                void Add(int l, int t, int rr, int b) { if (rr > l && b > t) next.Add(Rectangle.FromLTRB(l,t,rr,b)); }
                Add(r.Left,r.Top,r.Right,i.Top); Add(r.Left,i.Bottom,r.Right,r.Bottom);
                Add(r.Left,i.Top,i.Left,i.Bottom); Add(i.Right,i.Top,r.Right,i.Bottom);
            }
            remaining = next;
            if (remaining.Count == 0) return true;
        }
        return false;
    }
    public static bool SameLayout(IEnumerable<Rectangle> a, IEnumerable<Rectangle> b) =>
        a.OrderBy(r => r.X).ThenBy(r => r.Y).ThenBy(r => r.Width).ThenBy(r => r.Height)
            .SequenceEqual(b.OrderBy(r => r.X).ThenBy(r => r.Y).ThenBy(r => r.Width).ThenBy(r => r.Height));

    public static Rectangle DesktopBounds(IReadOnlyList<Rectangle> monitors)
    {
        if (monitors.Count == 0) throw new CaptureException("ERR_CAPTURE_002");
        var bounds = monitors[0];
        foreach (var m in monitors.Skip(1)) bounds = Rectangle.Union(bounds, m);
        return bounds;
    }

    public static Rectangle EditorBounds(Rectangle selected, IReadOnlyList<Rectangle> monitors)
    {
        foreach (var monitor in monitors)
            if (monitor.Contains(selected)) return monitor;
        return DesktopBounds(monitors);
    }

    public static Rectangle? Hit(IEnumerable<Rectangle> topFirst, Point p)
    {
        foreach (var rect in topFirst) if (rect.Contains(p)) return rect;
        return null;
    }

    // Preview is a physical window rectangle, independent of the source pixel dimensions.
    public static Rectangle FitPreview(Rectangle workArea, int width, int height)
    {
        Validate(new Rectangle(0, 0, width, height));
        Validate(workArea);
        var availableWidth = Math.Max(1, workArea.Width - 32);
        var availableHeight = Math.Max(1, workArea.Height - 32);
        var scale = Math.Min((double)availableWidth / width, (double)availableHeight / height);
        var w = Math.Clamp((int)Math.Round(width * scale), 1, availableWidth);
        var h = Math.Clamp((int)Math.Round(height * scale), 1, availableHeight);
        return new Rectangle(workArea.Left + (workArea.Width - w) / 2, workArea.Top + (workArea.Height - h) / 2, w, h);
    }

    public static Rectangle SelectionPixels(Rectangle bounds, double x, double y, double width, double height, double scaleX, double scaleY)
    {
        Validate(bounds);
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height) ||
            !double.IsFinite(scaleX) || !double.IsFinite(scaleY) || width <= 0 || height <= 0 || scaleX <= 0 || scaleY <= 0)
            throw new CaptureException("ERR_CAPTURE_002");
        var left = (int)Math.Clamp(Math.Round(x * scaleX), 0, bounds.Width - 1);
        var top = (int)Math.Clamp(Math.Round(y * scaleY), 0, bounds.Height - 1);
        var right = (int)Math.Clamp(Math.Round((x + width) * scaleX), left + 1, bounds.Width);
        var bottom = (int)Math.Clamp(Math.Round((y + height) * scaleY), top + 1, bounds.Height);
        return new Rectangle(bounds.Left + left, bounds.Top + top, right - left, bottom - top);
    }
}

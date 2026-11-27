using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VuSnap.Services;

public readonly record struct SampleColor(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public string Rgb => $"rgb({R}, {G}, {B})";
    public (double Hue, double Saturation, double Value) Hsv
    {
        get
        {
            double r = R / 255.0, g = G / 255.0, b = B / 255.0;
            double max = Math.Max(r, Math.Max(g,b)), min = Math.Min(r, Math.Min(g,b)), delta = max-min;
            double hue = delta == 0 ? 0 : max == r ? 60*((g-b)/delta) : max == g ? 60*((b-r)/delta+2) : 60*((r-g)/delta+4);
            if (hue < 0) hue += 360;
            return (hue, max == 0 ? 0 : 100*delta/max, 100*max);
        }
    }
    public string HsvText
    {
        get { var (h,s,v) = Hsv; return FormattableString.Invariant($"hsv({h:0.#}°, {s:0.#}%, {v:0.#}%)"); }
    }
}

public static class ColorProbe
{
    public static BitmapSource Prepare(BitmapSource source)
    {
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0 || source.PixelWidth > 16384 || source.PixelHeight > 16384 ||
            (long)source.PixelWidth * source.PixelHeight > 40_000_000)
            throw new UtilityException("ERR_COLOR_001");
        if (source.Format == PixelFormats.Bgr32 || source.Format == PixelFormats.Bgra32) return source;
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    // Uniform image fitting: points on letterbox padding must not sample a pixel.
    public static bool TryMapPoint(int width, int height, double viewWidth, double viewHeight, double x, double y, out int px, out int py)
    {
        px = py = 0;
        if (width <= 0 || height <= 0 || viewWidth <= 0 || viewHeight <= 0 ||
            !double.IsFinite(viewWidth) || !double.IsFinite(viewHeight) || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        double scale = Math.Min(viewWidth / width, viewHeight / height);
        if (!double.IsFinite(scale) || scale <= 0) return false;
        double ix = (x - (viewWidth-width*scale)/2)/scale, iy = (y - (viewHeight-height*scale)/2)/scale;
        if (ix < 0 || iy < 0 || ix >= width || iy >= height) return false;
        px = Math.Clamp((int)ix,0,width-1); py = Math.Clamp((int)iy,0,height-1);
        return true;
    }

    public static SampleColor Read(BitmapSource prepared, int x, int y, byte[] scratch)
    {
        if (x < 0 || y < 0 || x >= prepared.PixelWidth || y >= prepared.PixelHeight || scratch.Length < 4 ||
            (prepared.Format != PixelFormats.Bgr32 && prepared.Format != PixelFormats.Bgra32))
            throw new UtilityException("ERR_COLOR_001");
        prepared.CopyPixels(new Int32Rect(x,y,1,1),scratch,4,0);
        return new SampleColor(scratch[2],scratch[1],scratch[0]);
    }
}

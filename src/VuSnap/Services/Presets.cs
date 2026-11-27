using System.Globalization;
using System.Windows.Media;

namespace VuSnap.Services;

/// <summary>Các giá trị có sẵn: kiểu nét, tỉ lệ khung hình, màu; dùng chung cho cửa sổ cài đặt và overlay.</summary>
public static class Presets
{
    /// <summary>Kiểu nét cho Bút / Đường thẳng / Mũi tên / Hình chữ nhật / Elip.</summary>
    public static readonly string[] StrokeIds = { "Solid", "Dash", "Dot", "DashDot", "Double" };

    /// <summary>Các loại mũi tên: đầu đặc, đầu mở, hai đầu, mũi tên cong.</summary>
    public static readonly string[] WeightIds = { "Light", "Normal", "SemiBold", "Bold" };
    public static readonly string[] LineTypeIds = { "Plain", "Ticks", "Dots", "Arrows" };
    public static readonly string[] ArrowIds = { "Filled", "Open", "Both", "Curved" };

    /// <summary>Tỉ lệ khung hình vùng chọn ("Free" = tự do).</summary>
    public static readonly string[] RatioIds = { "Free", "1:1", "4:3", "3:4", "3:2", "2:3", "16:9", "9:16", "21:9" };

    /// <summary>Chiều rộng / chiều cao, hoặc null nếu tự do.</summary>
    public static double? RatioValue(string? id)
    {
        if (string.IsNullOrEmpty(id) || id == "Free") return null;
        var p = id.Split(':');
        if (p.Length == 2 &&
            double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) &&
            double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && w > 0 && h > 0)
            return w / h;
        return null;
    }

    public static Color ParseColor(string? text, Color fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            var t = text.Trim();
            if (!t.StartsWith('#')) t = "#" + t;
            var c = (Color)ColorConverter.ConvertFromString(t);
            return Color.FromRgb(c.R, c.G, c.B);
        }
        catch { return fallback; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}

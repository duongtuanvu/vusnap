using System.Windows.Media.Imaging;
using VuSnap.Core;

namespace VuSnap.Services;

/// <summary>Encode a frozen bitmap off the UI thread and publish only a complete image.</summary>
public static class ImageExportService
{
    public static void Save(BitmapSource bitmap, string path, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (!bitmap.IsFrozen) throw new InvalidOperationException("ERR_EXPORT_BITMAP_NOT_FROZEN");
        var ext = Path.GetExtension(path);
        BitmapEncoder encoder = ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? new JpegBitmapEncoder { QualityLevel = 95 }
            : ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                ? new PngBitmapEncoder()
                : throw new NotSupportedException("ERR_EXPORT_FORMAT");
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        AtomicFile.Write(path, stream => encoder.Save(stream), overwrite);
    }
}

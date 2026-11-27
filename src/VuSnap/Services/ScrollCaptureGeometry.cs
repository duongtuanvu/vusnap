using SD = System.Drawing;
using VuSnap.Core;
using VuSnap.Models;

namespace VuSnap.Services;

public static class ScrollCaptureGeometry
{
    public const long MaxSourcePixels = 16_000_000;
    public const int MaxSourceDimension = 8192;

    public static void ValidateSource(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxSourceDimension || height > MaxSourceDimension ||
            (long)width * height > MaxSourcePixels)
            throw new CaptureException("ERR_SCROLL_005");
    }

    public static void ValidateCrop(int sourceWidth, int sourceHeight, SD.Rectangle crop)
    {
        ValidateSource(sourceWidth, sourceHeight);
        Stitcher.ValidateFrame(crop.Width, crop.Height);
        if (crop.X < 0 || crop.Y < 0 || (long)crop.X + crop.Width > sourceWidth ||
            (long)crop.Y + crop.Height > sourceHeight)
            throw new CaptureException("ERR_SCROLL_001");
    }

    public static bool IsPointerInCrop(SD.Rectangle bounds, SD.Rectangle crop, int sourceWidth, int sourceHeight, SD.Point pointer)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || bounds.Width <= 0 || bounds.Height <= 0 ||
            !bounds.Contains(pointer)) return false;
        long x = ((long)pointer.X - bounds.Left) * sourceWidth / bounds.Width;
        long y = ((long)pointer.Y - bounds.Top) * sourceHeight / bounds.Height;
        return x >= crop.Left && y >= crop.Top && x < (long)crop.X + crop.Width && y < (long)crop.Y + crop.Height;
    }
}

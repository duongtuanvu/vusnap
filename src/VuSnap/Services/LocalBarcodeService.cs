using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace VuSnap.Services;

public sealed record BarcodeHit(string Format, string Text);
public static class LocalBarcodeService
{
    public const long MaxPixels = 12_000_000;
    private const int MaxText = 65_536;
    private static readonly SemaphoreSlim Gate = new(1,1);
    public static void ValidateSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width*height > MaxPixels)
            throw new UtilityException("ERR_QR_001");
    }

    public static async Task<BarcodeHit?> DecodeAsync(BitmapSource source, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ValidateSize(source.PixelWidth,source.PixelHeight);
        if (!source.IsFrozen) throw new UtilityException("ERR_QR_002");
        if (!await Gate.WaitAsync(0,token).ConfigureAwait(false)) throw new UtilityException("ERR_QR_003");
        try
        {
            // ZXing is synchronous; keep it off the UI thread and do not release the gate
            // until decoding actually ends, even if the dialog is closed.
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var gray = source.Format == PixelFormats.Gray8 ? source : new FormatConvertedBitmap(source,PixelFormats.Gray8,null,0);
                int w = gray.PixelWidth, h = gray.PixelHeight;
                var pixels = new byte[checked(w*h)];
                gray.CopyPixels(pixels,w,0);
                token.ThrowIfCancellationRequested();
                var reader = new BarcodeReaderGeneric
                {
                    AutoRotate = true,
                    Options = new DecodingOptions
                    {
                        TryHarder = true, TryInverted = true,
                        PossibleFormats = new List<BarcodeFormat>
                        {
                            BarcodeFormat.QR_CODE, BarcodeFormat.DATA_MATRIX, BarcodeFormat.AZTEC,
                            BarcodeFormat.PDF_417, BarcodeFormat.CODE_128, BarcodeFormat.CODE_39,
                            BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.UPC_A,
                            BarcodeFormat.UPC_E, BarcodeFormat.ITF, BarcodeFormat.CODABAR
                        }
                    }
                };
                var result = reader.Decode(new RGBLuminanceSource(pixels,w,h,RGBLuminanceSource.BitmapFormat.Gray8));
                token.ThrowIfCancellationRequested();
                if (result is null) return null;
                var text = result.Text ?? "";
                if (text.Length > MaxText) throw new UtilityException("ERR_QR_004");
                return new BarcodeHit(result.BarcodeFormat.ToString(),text);
            },token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (UtilityException) { throw; }
        catch (Exception ex) { throw new UtilityException("ERR_QR_002",ex); }
        finally { Gate.Release(); }
    }
}

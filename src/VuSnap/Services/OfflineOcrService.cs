using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Globalization;

namespace VuSnap.Services;

public sealed record OcrOption(string Engine, string Language, string Display);
public sealed class UtilityException(string code, Exception? inner = null) : Exception(code, inner)
{
    public string Code { get; } = code;
}

public static class OfflineOcrService
{
    public const long MaximumPixels = 16_000_000;
    private const int MaxText = 1_000_000;
    // Never launch a name resolved through PATH or the current working directory.
    private static string? TesseractPath()
    {
        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(special);
            if (string.IsNullOrWhiteSpace(root)) continue;
            var exe = Path.Combine(root, "Tesseract-OCR", "tesseract.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    public static IReadOnlyList<OcrOption> Options()
    {
        var result = new List<OcrOption>();
        try
        {
            foreach (var language in OcrEngine.AvailableRecognizerLanguages.OrderBy(x => x.DisplayName))
                result.Add(new OcrOption("windows", language.LanguageTag, "Windows · " + language.DisplayName));
        }
        catch (Exception) { /* Tesseract remains available if OS OCR cannot initialize. */ }
        var exe = TesseractPath();
        if (exe is not null)
        {
            var dir = Path.Combine(Path.GetDirectoryName(exe)!, "tessdata");
            foreach (var (lang, label) in new[] { ("eng","English"), ("vie","Tiếng Việt"), ("vie+eng","Tiếng Việt + English") })
                if (lang.Split('+').All(x => File.Exists(Path.Combine(dir, x + ".traineddata"))))
                    result.Add(new OcrOption("tesseract",lang,"Tesseract · " + label));
        }
        return result;
    }

    public static void ValidateSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height > MaximumPixels)
            throw new UtilityException("ERR_OCR_002");
    }

    public static async Task<string> RecognizeAsync(BitmapSource image, OcrOption option, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!image.IsFrozen) throw new UtilityException("ERR_OCR_004");
        ValidateSize(image.PixelWidth, image.PixelHeight);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var text = await Task.Run(async () =>
            {
                if (!Options().Any(x => x.Engine == option.Engine && x.Language == option.Language))
                    throw new UtilityException("ERR_OCR_001");
                return option.Engine switch
                {
                    "windows" => await WindowsAsync(image, option.Language, timeout.Token).ConfigureAwait(false),
                    "tesseract" => await TesseractAsync(image, option.Language, timeout.Token).ConfigureAwait(false),
                    _ => throw new UtilityException("ERR_OCR_001")
                };
            }, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            return text.Length <= MaxText ? text.Trim() : throw new UtilityException("ERR_OCR_004");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new UtilityException("ERR_OCR_003"); }
        catch (UtilityException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new UtilityException("ERR_OCR_004", ex); }
    }

    private static async Task<string> WindowsAsync(BitmapSource source, string language, CancellationToken token)
    {
        var width = source.PixelWidth; var height = source.PixelHeight;
        if ((uint)width > OcrEngine.MaxImageDimension || (uint)height > OcrEngine.MaxImageDimension)
            throw new UtilityException("ERR_OCR_002");
        var engine = OcrEngine.TryCreateFromLanguage(new Language(language)) ?? throw new UtilityException("ERR_OCR_001");
        var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        bgra.CopyPixels(pixels, stride, 0);
        token.ThrowIfCancellationRequested();
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
        var result = await engine.RecognizeAsync(bitmap).AsTask(token).ConfigureAwait(false);
        return string.Join(Environment.NewLine, result.Lines.Select(x => x.Text));
    }

    private static async Task<string> TesseractAsync(BitmapSource image, string language, CancellationToken token)
    {
        if (language is not ("eng" or "vie" or "vie+eng")) throw new UtilityException("ERR_OCR_001");
        var exe = TesseractPath() ?? throw new UtilityException("ERR_OCR_001");
        using var input = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(input);
        input.Position = 0;
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        };
        start.Environment.Remove("TESSDATA_PREFIX");
        foreach (var arg in new[] {"stdin","stdout","--tessdata-dir",Path.Combine(start.WorkingDirectory,"tessdata"),"-l",language,"--oem","1","--psm","6"})
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new UtilityException("ERR_OCR_004");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        try
        {
            var output = ReadBoundedAsync(process.StandardOutput, MaxText, token);
            var error = ReadBoundedAsync(process.StandardError, 16384, token);
            try { await input.CopyToAsync(process.StandardInput.BaseStream, token).ConfigureAwait(false); }
            finally { process.StandardInput.Close(); }
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || output.Result is null) throw new UtilityException("ERR_OCR_004");
            return output.Result;
        }
        finally { try { if (!process.HasExited) process.Kill(true); } catch { } }
    }

    private static async Task<string?> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken token)
    {
        var buffer = new char[4096];
        var text = new StringBuilder();
        bool overflow = false;
        int n;
        while ((n = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (text.Length + n <= limit && !overflow) text.Append(buffer, 0, n);
            else overflow = true; // drain the pipe even if too large
        }
        return overflow ? null : text.ToString();
    }
}

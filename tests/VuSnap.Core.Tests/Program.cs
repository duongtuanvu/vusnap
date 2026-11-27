using System.IO;
using VuSnap.Core;
using VuSnap.Models;
using VuSnap.Services;

var folder = Path.Combine(Path.GetTempPath(), "VuSnap-test-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("VUSNAP_DATA_DIR", folder);
var failures = 0;
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
}
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected failure"); }

try
{
    Directory.CreateDirectory(folder);
    Test("history undo redo and branch", () =>
    {
        var h = new CommandHistory<int>(0, 2);
        Check(!h.CanUndo && !h.CanRedo && !h.Record(0));
        Check(h.Record(1) && h.Record(2) && h.Record(3));
        Check(h.TryUndo(out var x) && x == 2);
        Check(h.TryUndo(out x) && x == 1 && !h.CanUndo);
        Check(h.TryRedo(out x) && x == 2);
        Check(h.Record(4) && !h.CanRedo);
    });
    Test("atomic write preserves prior content on failure", () =>
    {
        var p = Path.Combine(folder, "atomic.txt");
        File.WriteAllText(p, "old");
        Throws(() => AtomicFile.Write(p, s => { s.WriteByte(42); throw new IOException(); }, true));
        Check(File.ReadAllText(p) == "old");
        Check(!Directory.EnumerateFiles(folder, "*.tmp").Any());
        Throws(() => AtomicFile.Write(p, s => s.WriteByte(42), false));
        Check(File.ReadAllText(p) == "old");
        AtomicFile.Write(p, s => s.WriteByte(42), true);
        Check(File.ReadAllBytes(p).SequenceEqual(new byte[] { 42 }));
    });
    Test("unique export paths", () =>
    {
        var now = DateTime.Now;
        Check(Enumerable.Range(0, 200).Select(_ => AtomicFile.NewCapturePath(folder, ".png", now)).Distinct().Count() == 200);
        Throws(() => AtomicFile.NewCapturePath(folder, ".exe", now));
    });
    Test("settings defaults and roundtrip", () =>
    {
        var s = new AppSettings { SaveDirectory = folder, Language = "vi" };
        Check(SettingsService.Validate(s));
        SettingsService.Save(s);
        Check(SettingsService.Load().Language == "vi");
    });
    Test("invalid settings do not overwrite previous", () =>
    {
        var s = new AppSettings { SaveDirectory = folder, DefaultZoom = double.NaN };
        Check(!SettingsService.Validate(s));
        Throws(() => SettingsService.Save(s));
        Check(SettingsService.Load().Language == "vi");
        s.DefaultZoom = 2; s.Shortcuts = null!;
        Check(!SettingsService.Validate(s));
    });
    Test("invalid imports", () =>
    {
        var p = Path.Combine(folder, "bad.json");
        foreach (var value in new[] { "{", "null", "{\"HiddenTools\":null}", "{\"GridSize\":0}", "{\"Hotkey\":\"Alt+99999\"}" })
        {
            File.WriteAllText(p, value); Check(SettingsService.Import(p) is null);
        }
        File.WriteAllText(p, new string(' ', 300000)); Check(SettingsService.Import(p) is null);
    });
    Test("hotkey conflicts", () =>
    {
        var s = new AppSettings { SaveDirectory = folder, Hotkey = "Ctrl+Z" };
        Check(!SettingsService.Validate(s));
        Check(!ShortcutHelper.TryParse("Alt+99999", out _, out _));
        Check(!ShortcutHelper.TryParse("Alt+LeftShift", out _, out _));
    });
    Test("shortcut canonicalization and reserved keys", () =>
    {
        Check(ShortcutHelper.Equivalent("Alt+Ctrl+A", "control+alt+a"));
        Check(!ShortcutHelper.TryParse("Ctrl++A", out _, out _));
        Check(!ShortcutHelper.TryParse("Ctrl+Control+A", out _, out _));
        Check(!ShortcutHelper.TryParse("Ctrl+A,B", out _, out _));
        Check(!ShortcutHelper.TryParseGlobal("Ctrl+F12", out _, out _));
        Check(!ShortcutHelper.TryParseGlobal("A", out _, out _));
        Check(ShortcutHelper.TryParseGlobal("Alt+Shift+S", out _, out _));
    });
    Test("serialized settings update and corruption preservation", () =>
    {
        SettingsService.Save(new AppSettings { SaveDirectory = folder, DefaultZoom = 2 });
        Parallel.For(0, 10, _ => SettingsService.Update(s => s.DefaultZoom += 0.5));
        Check(SettingsService.Load().DefaultZoom == 7);
        var path = Path.Combine(SettingsService.DataFolder, "settings.json");
        File.WriteAllText(path, "{broken");
        Throws(() => SettingsService.Update(s => s.Language = "vi"));
        Check(File.ReadAllText(path) == "{broken");
        SettingsService.Save(new AppSettings { SaveDirectory = folder });
    });
    Test("frozen bitmap PNG and JPEG export roundtrip", () =>
    {
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null,
            new byte[] { 0,0,255,255, 0,255,0,255, 255,0,0,255, 255,255,255,255 }, 8);
        bitmap.Freeze();
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            var target = Path.Combine(folder, "export" + ext);
            Task.Run(() => ImageExportService.Save(bitmap, target, false)).GetAwaiter().GetResult();
            using (var input = File.OpenRead(target))
            {
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(input,
                    System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                Check(decoder.Frames[0].PixelWidth == 2 && decoder.Frames[0].PixelHeight == 2);
            }
            var bytes = File.ReadAllBytes(target);
            Throws(() => ImageExportService.Save(bitmap, target, false));
            Check(File.ReadAllBytes(target).SequenceEqual(bytes));
        }
        Throws(() => ImageExportService.Save(bitmap, Path.Combine(folder, "bad.exe"), false));
    });
    Test("parallel no-overwrite export publication", () =>
    {
        var target = Path.Combine(folder, "race.bin");
        var wins = 0;
        Parallel.For(0, 8, i =>
        {
            try { AtomicFile.Write(target, s => s.WriteByte((byte)i), false); Interlocked.Increment(ref wins); }
            catch (IOException) { }
        });
        Check(wins == 1 && File.ReadAllBytes(target).Length == 1);
        Check(!Directory.EnumerateFiles(folder, ".vusnap-*.tmp").Any());
    });
    Test("capture geometry negative coordinates and mixed scale", () =>
    {
        var bounds = new System.Drawing.Rectangle(-1920, -120, 1920, 1080);
        var selected = CaptureGeometry.SelectionPixels(bounds, 10, 20, 100, 80, 1.5, 1.5);
        Check(selected == new System.Drawing.Rectangle(-1905, -90, 150, 120));
        Check(CaptureGeometry.SelectionPixels(bounds, 0, 0, 1280, 720, 1.5, 1.5) == bounds);
        Throws(() => CaptureGeometry.SelectionPixels(bounds, double.NaN, 0, 1, 1, 1, 1));
    });
    Test("monitor layout and desktop bounds", () =>
    {
        var left = new System.Drawing.Rectangle(-1920, 0, 1920, 1080);
        var right = new System.Drawing.Rectangle(0, -200, 2560, 1440);
        var layout = new[] { left, right };
        Check(CaptureGeometry.DesktopBounds(layout) == new System.Drawing.Rectangle(-1920, -200, 4480, 1440));
        Check(CaptureGeometry.SameLayout(layout, new[] { right, left }));
        Check(!CaptureGeometry.SameLayout(layout, new[] { right }));
        Check(CaptureGeometry.EditorBounds(new System.Drawing.Rectangle(-100, 10, 80, 80), layout) == left);
        Check(CaptureGeometry.IsCovered(new System.Drawing.Rectangle(-100, 10, 200, 80), layout));
        Check(!CaptureGeometry.IsCovered(new System.Drawing.Rectangle(-100, -100, 200, 80), layout));
    });
    Test("window hit order and boundaries", () =>
    {
        var top = new System.Drawing.Rectangle(-10, -10, 20, 20);
        var bottom = new System.Drawing.Rectangle(-20, -20, 50, 50);
        Check(CaptureGeometry.Hit(new[] { top, bottom }, new System.Drawing.Point(0, 0)) == top);
        Check(CaptureGeometry.Hit(new[] { top, bottom }, new System.Drawing.Point(10, 10)) == bottom);
        Check(CaptureGeometry.Hit(new[] { top, bottom }, new System.Drawing.Point(50, 50)) is null);
    });
    Test("capture validation and cancellation", () =>
    {
        CaptureGeometry.Validate(new System.Drawing.Rectangle(-3840, 0, 7680, 2160));
        Throws(() => CaptureGeometry.Validate(new System.Drawing.Rectangle(0, 0, 0, 1)));
        Throws(() => CaptureGeometry.Validate(new System.Drawing.Rectangle(0, 0, 10000, 10000)));
        Throws(() => CaptureGeometry.Validate(new System.Drawing.Rectangle(int.MaxValue, 0, 2, 1)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try { ScreenCaptureService.Capture(new CaptureRequest(CaptureMode.Region), cts.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        var s = new AppSettings { SaveDirectory = folder, DefaultCaptureMode = "Desktop", CaptureDelaySeconds = 5 };
        Check(SettingsService.Validate(s));
        SettingsService.Save(s);
        Check(SettingsService.Load().DefaultCaptureMode == "Desktop" && SettingsService.Load().CaptureDelaySeconds == 5);
        s.CaptureDelaySeconds = 4; Check(!SettingsService.Validate(s));
        s.CaptureDelaySeconds = 0; s.DefaultCaptureMode = "invalid"; Check(!SettingsService.Validate(s));
    });
    Test("WGC bounded frames and scaled preview", () =>
    {
        GraphicsWindowCapture.ValidateFrameSize(3840,2160);
        Throws(() => GraphicsWindowCapture.ValidateFrameSize(0,100));
        Throws(() => GraphicsWindowCapture.ValidateFrameSize(10000,10000));
        var area = new System.Drawing.Rectangle(-1920,0,1920,1040);
        var preview = CaptureGeometry.FitPreview(area,3840,2160);
        Check(area.Contains(preview) && preview.Width > 0 && preview.Height > 0);
        Check(Math.Abs((double)preview.Width / preview.Height - 3840.0 / 2160) < 0.01);
    });
    Test("isolated capture cannot silently use GDI", () =>
    {
        foreach (var mode in new[] { CaptureMode.ActiveWindow, CaptureMode.WindowPicker })
        {
            try { ScreenCaptureService.Capture(new CaptureRequest(mode)); throw new Exception("Expected backend rejection"); }
            catch (CaptureException ex) { Check(ex.Code == "ERR_WGC_006"); }
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            GraphicsWindowCapture.TakeAsync(new WindowTargets.Target(IntPtr.Zero, System.Drawing.Rectangle.Empty,0,"test"), cancellation.Token).GetAwaiter().GetResult();
            throw new Exception("Expected cancellation");
        }
        catch (OperationCanceledException) { }
    });
    Test("OCR image bounds and pre-cancellation", () =>
    {
        OfflineOcrService.ValidateSize(3840,2160);
        Throws(() => OfflineOcrService.ValidateSize(0,10));
        Throws(() => OfflineOcrService.ValidateSize(8192,8192));
        var image = System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,System.Windows.Media.PixelFormats.Bgr32,null,new byte[4],4);
        image.Freeze();
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { OfflineOcrService.RecognizeAsync(image,new OcrOption("windows","en-US","test"),cancel.Token).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
    });
    Test("Color picker pixel sampling and HEX/RGB/HSV", () =>
    {
        var image = System.Windows.Media.Imaging.BitmapSource.Create(2,1,96,96,System.Windows.Media.PixelFormats.Bgr32,null,
            new byte[] {0,0,255,0, 255,0,0,0},8);
        image.Freeze();
        var ready=ColorProbe.Prepare(image);
        var red=ColorProbe.Read(ready,0,0,new byte[4]);
        var blue=ColorProbe.Read(ready,1,0,new byte[4]);
        Check(red.Hex=="#FF0000" && red.Rgb=="rgb(255, 0, 0)" && red.Hsv==(0d,100d,100d));
        Check(blue.Hex=="#0000FF" && blue.Hsv==(240d,100d,100d));
        Check(new SampleColor(0,0,0).Hsv==(0d,0d,0d));
        Check(new SampleColor(128,128,128).Hsv.Saturation==0);
        Throws(()=>ColorProbe.Read(ready,2,0,new byte[4]));
        Check(ColorProbe.TryMapPoint(200,100,200,200,199,149,out int x,out int y) && x==199 && y==99);
        Check(!ColorProbe.TryMapPoint(200,100,200,200,100,49,out _,out _));
        Check(!ColorProbe.TryMapPoint(200,100,200,200,double.NaN,100,out _,out _));
    });
    Test("QR and Code128 synthetic decoding", () =>
    {
        foreach(var format in new[]{ZXing.BarcodeFormat.QR_CODE,ZXing.BarcodeFormat.CODE_128})
        {
            const string payload="https://example.invalid/vusnap";
            var matrix=new ZXing.MultiFormatWriter().encode(payload,format,400,240);
            var pixels=new byte[checked(matrix.Width*matrix.Height)];
            for(int y=0;y<matrix.Height;y++) for(int x=0;x<matrix.Width;x++) pixels[y*matrix.Width+x]=matrix[x,y]?(byte)0:(byte)255;
            var image=System.Windows.Media.Imaging.BitmapSource.Create(matrix.Width,matrix.Height,96,96,System.Windows.Media.PixelFormats.Gray8,null,pixels,matrix.Width);
            image.Freeze();
            var hit=LocalBarcodeService.DecodeAsync(image).GetAwaiter().GetResult();
            Check(hit is not null && hit.Text==payload && hit.Format==format.ToString());
        }
    });
    Test("QR blank input, limits and cancellation", () =>
    {
        Throws(()=>LocalBarcodeService.ValidateSize(10000,10000));
        Throws(()=>LocalBarcodeService.ValidateSize(0,100));
        var pixels=Enumerable.Repeat((byte)255,64*64).ToArray();
        var image=System.Windows.Media.Imaging.BitmapSource.Create(64,64,96,96,System.Windows.Media.PixelFormats.Gray8,null,pixels,64);
        image.Freeze();
        Check(LocalBarcodeService.DecodeAsync(image).GetAwaiter().GetResult() is null);
        using var cancel=new CancellationTokenSource(); cancel.Cancel();
        try { LocalBarcodeService.DecodeAsync(image,cancel.Token).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); }
        catch(OperationCanceledException) { }
    });
    Test("OCR layout restores rows and columns from unordered boxes", () =>
    {
        var words = new[] { new OcrWord("PASS",160,42,32,16), new OcrWord("Build",0,42,40,16),
            new OcrWord("Status",160,0,48,16),new OcrWord("Task",0,0,32,16) };
        var lines=OcrLayout.Format(words).Split(Environment.NewLine,StringSplitOptions.RemoveEmptyEntries);
        Check(lines.Length==2 && lines[0].StartsWith("Task") && lines[0].IndexOf("Status",StringComparison.Ordinal)==20);
        Check(lines[1].StartsWith("Build") && lines[1].IndexOf("PASS",StringComparison.Ordinal)==20);
        Check(OcrLayout.Format(Array.Empty<OcrWord>())=="");
        Check(OcrLayout.ParseTsv("5\\t1\\t1\\t1\\t1\\t1\\t0\\t0\\t32\\t16\\t90\\tViệt".Replace("\\t","\t"))=="Việt");
        Check(new OcrOption("windows","en-US","Windows English").ToString()=="Windows English");
    });
}
finally { try { Directory.Delete(folder, true); } catch { } }
return failures == 0 ? 0 : 1;

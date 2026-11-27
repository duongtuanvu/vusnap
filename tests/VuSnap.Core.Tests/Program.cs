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
}
finally { try { Directory.Delete(folder, true); } catch { } }
return failures == 0 ? 0 : 1;

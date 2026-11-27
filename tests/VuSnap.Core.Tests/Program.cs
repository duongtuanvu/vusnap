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
}
finally { try { Directory.Delete(folder, true); } catch { } }
return failures == 0 ? 0 : 1;

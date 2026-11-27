using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using VuSnap.Core;
using VuSnap.Models;

namespace VuSnap.Services;

public static class SettingsService
{
    private const int MaxBytes = 256 * 1024;
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 16 };
    public static string DataFolder { get; } =
        Environment.GetEnvironmentVariable("VUSNAP_DATA_DIR") is { Length: > 0 } custom ? custom :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VuSnap");
    private static string FilePath => Path.Combine(DataFolder, "settings.json");

    private static bool In(string? value, params string[] values) => value is not null && values.Contains(value);
    private static bool Between(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
    public static bool Validate(AppSettings? s)
    {
        if (s is null || !In(s.Language, "en", "vi") || !In(s.Theme, "Light", "Dark", "System") ||
            !In(s.DefaultCaptureMode, "Region", "Monitor", "Desktop", "ActiveWindow", "WindowPicker", "Repeat", "WindowHover") || s.CaptureDelaySeconds is not (0 or 3 or 5 or 10) ||
            !In(s.DefaultFormat, "PNG", "JPG") || !In(s.ToolbarPosition, "Top", "Bottom", "Left", "Right") ||
            !In(s.GridType, "Lines", "Dots") || s.GridSize is < 5 or > 100 ||
            !Between(s.OutlineOpacity, 0.1, 1) || !Between(s.DefaultThickness, 1, 40) ||
            !Between(s.DefaultOpacity, 0.05, 1) || !Between(s.DefaultZoom, 1.5, 10) ||
            !In(s.DefaultStroke, Presets.StrokeIds) || !In(s.DefaultAspect, Presets.RatioIds) ||
            !In(s.DefaultArrow, Presets.ArrowIds) || !In(s.DefaultFontWeight, Presets.WeightIds) ||
            !In(s.DefaultLineType, Presets.LineTypeIds) || !In(s.DefaultLens, "Circle", "Square") ||
            string.IsNullOrWhiteSpace(s.DefaultFont) || s.DefaultFont.Length > 256 ||
            s.DefaultColor is null || !Regex.IsMatch(s.DefaultColor, @"\A#[0-9A-Fa-f]{6}\z") ||
            string.IsNullOrWhiteSpace(s.SaveDirectory) || s.SaveDirectory.Length > 4096 ||
            s.ToolOrder is null || s.HiddenTools is null || s.Shortcuts is null ||
            !ShortcutHelper.TryParseGlobal(s.Hotkey, out var mods, out var key))
            return false;

        try { if (!Path.IsPathFullyQualified(s.SaveDirectory)) return false; _ = Path.GetFullPath(s.SaveDirectory); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }

        var allowedHidden = ShortcutHelper.ToolIds.Concat(ShortcutHelper.BarExtraIds).Concat(ShortcutHelper.ActionBarIds).Append("ObjBar").ToHashSet();
        if (s.ToolOrder.Count > ShortcutHelper.ToolIds.Length || s.ToolOrder.Any(x => !ShortcutHelper.ToolIds.Contains(x)) ||
            s.ToolOrder.Distinct().Count() != s.ToolOrder.Count ||
            s.HiddenTools.Count > allowedHidden.Count || s.HiddenTools.Any(x => x is null || !allowedHidden.Contains(x)) ||
            s.HiddenTools.Distinct().Count() != s.HiddenTools.Count ||
            s.Shortcuts.Count > ShortcutHelper.ToolIds.Length + ShortcutHelper.ActionIds.Length ||
            s.Shortcuts.Any(p => !ShortcutHelper.IsKnownAction(p.Key) || p.Value is null || p.Value.Length > 80))
            return false;
        var used = new HashSet<(ModifierKeys, Key)> { (mods, key) };
        foreach (var id in ShortcutHelper.ToolIds.Concat(ShortcutHelper.ActionIds))
        {
            var combo = ShortcutHelper.Get(s, id);
            if (combo.Length == 0) continue;
            if (!ShortcutHelper.TryParse(combo, out mods, out key) || !used.Add((mods, key))) return false;
        }
        return true;
    }

    private static AppSettings Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxBytes) throw new InvalidDataException(ErrorCodes.SettingsInvalid);
        var s = JsonSerializer.Deserialize<AppSettings>(stream, Options);
        if (!Validate(s)) throw new InvalidDataException(ErrorCodes.SettingsInvalid);
        return s!;
    }

    private static void Write(AppSettings settings, string path)
    {
        if (!Validate(settings)) throw new InvalidDataException(ErrorCodes.SettingsInvalid);
        AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, settings, Options), overwrite: true);
    }

    public static AppSettings Load()
    {
        lock (Gate)
        {
            try { if (File.Exists(FilePath)) return Read(FilePath); }
            catch (Exception ex) { ErrorLog.Write(ErrorCodes.SettingsInvalid, ex); }
            return new AppSettings();
        }
    }
    public static void Export(AppSettings settings, string path)
    {
        lock (Gate) Write(settings, path);
    }
    public static AppSettings? Import(string path)
    {
        lock (Gate)
        {
            try { return Read(path); }
            catch (Exception ex) { ErrorLog.Write(ErrorCodes.SettingsInvalid, ex); return null; }
        }
    }
    public static void Save(AppSettings settings)
    {
        lock (Gate) Write(settings, FilePath);
    }

    /// <summary>Serialized read-modify-write. Do not overwrite unreadable settings with defaults.</summary>
    public static AppSettings Update(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (Gate)
        {
            var settings = File.Exists(FilePath) ? Read(FilePath) : new AppSettings();
            change(settings);
            Write(settings, FilePath);
            return settings;
        }
    }
}

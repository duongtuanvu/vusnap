using System.Windows.Input;
using VuSnap.Models;

namespace VuSnap.Services;

/// <summary>Danh sách tool/thao tác có phím tắt, giá trị mặc định, parse/format chuỗi phím.</summary>
public static class ShortcutHelper
{
    public static readonly string[] ToolIds =
        { "Select", "Text", "Pen", "Line", "Arrow", "Rectangle", "Ellipse", "Highlight", "Blur", "Invert", "Number", "Ruler", "Magnifier" };

    public static readonly string[] ActionIds =
        { "Copy", "Confirm", "QuickSave", "SaveAs", "Undo", "Redo", "Delete", "SelectAll", "TogglePanel", "Duplicate", "Ratio" };

    /// <summary>Các nút khác trên thanh tool (có thể bật/tắt hiển thị).</summary>
    public static readonly string[] BarExtraIds = { "Color", "Ratio", "Panel", "Redo", "Undo" };

    /// <summary>Các nút trên thanh lưu/đóng (theo thứ tự hiển thị).</summary>
    public static readonly string[] ActionBarIds = { "Cancel", "SaveAs", "QuickSave", "Copy", "Confirm" };

    private static readonly HashSet<string> NamedKeys = new(Enum.GetNames<Key>(), StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Default = new()
    {
        ["Select"] = "V", ["Text"] = "T", ["Pen"] = "P", ["Line"] = "L", ["Arrow"] = "A", ["Rectangle"] = "R",
        ["Ellipse"] = "E", ["Highlight"] = "H", ["Blur"] = "B", ["Invert"] = "I", ["Number"] = "N", ["Ruler"] = "M", ["Magnifier"] = "Z",
        ["Copy"] = "Ctrl+C", ["Confirm"] = "Enter", ["QuickSave"] = "Ctrl+S", ["SaveAs"] = "",
        ["Undo"] = "Ctrl+Z", ["Redo"] = "Ctrl+Y", ["Delete"] = "Delete", ["SelectAll"] = "Ctrl+A", ["TogglePanel"] = "", ["Duplicate"] = "Ctrl+D", ["Ratio"] = "",
    };

    /// <summary>Các tool theo thứ tự người dùng đã sắp xếp (tool chưa có trong danh sách thì xếp tiếp theo thứ tự mặc định).</summary>
    public static List<string> OrderedTools(AppSettings s)
    {
        var result = new List<string>();
        foreach (var id in s.ToolOrder) if (ToolIds.Contains(id) && !result.Contains(id)) result.Add(id);
        foreach (var id in ToolIds) if (!result.Contains(id)) result.Add(id);
        return result;
    }

    public static Dictionary<string, string> Defaults() => new(Default);

    public static string Get(AppSettings s, string action) =>
        s.Shortcuts.TryGetValue(action, out var v) ? v : Default.GetValueOrDefault(action, "");

    public static bool IsKnownAction(string id) => Default.ContainsKey(id);

    public static string Format(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out ModifierKeys mods, out Key key)
    {
        mods = ModifierKeys.None; key = Key.None;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 80) return false;
        var tokens = text.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Any(string.IsNullOrEmpty)) return false;
        foreach (var t in tokens[..^1])
        {
            var bit = t.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "win" or "windows" => ModifierKeys.Windows,
                _ => ModifierKeys.None
            };
            if (bit == ModifierKeys.None || (mods & bit) != 0) return false;
            mods |= bit;
        }
        return NamedKeys.Contains(tokens[^1]) && Enum.TryParse(tokens[^1], true, out key) &&
            Enum.IsDefined(key) && key is not (Key.None or Key.Escape or Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or
                Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.ImeProcessed or Key.DeadCharProcessed or Key.F10);
    }

    public static bool TryParseGlobal(string? text, out ModifierKeys mods, out Key key) =>
        TryParse(text, out mods, out key) && mods != ModifierKeys.None && key != Key.F12 && KeyInterop.VirtualKeyFromKey(key) > 0;

    public static bool Equivalent(string? a, string? b) =>
        TryParse(a, out var am, out var ak) && TryParse(b, out var bm, out var bk) && am == bm && ak == bk;

    /// <summary>Tên phím dễ đọc: D1 -> 1, NumPad1 -> Num1, OemPlus -> +...</summary>
    public static string Pretty(string combo)
    {
        if (string.IsNullOrEmpty(combo)) return combo;
        if (!TryParse(combo, out var mods, out var key)) return combo;
        combo = Format(mods, key);
        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries).ToList();
        // "Ctrl++" (phím +) được Format thành "Ctrl+OemPlus" nên không có dấu + thừa
        var k = parts[^1];
        parts[^1] = k switch
        {
            "OemPlus" => "+", "OemMinus" => "-", "OemComma" => ",", "OemPeriod" => ".", "OemQuestion" => "/",
            "Oem1" => ";", "Oem3" => "`", "Oem4" => "[", "Oem5" => "\\", "Oem6" => "]", "Oem7" => "'",
            "Return" => "Enter", "Next" => "PgDn", "Prior" => "PgUp",
            _ when k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1]) => k[1].ToString(),
            _ when k.StartsWith("NumPad") => "Num" + k[6..],
            _ => k,
        };
        return string.Join("+", parts);
    }

    /// <summary>Chuỗi ngắn để hiện trên nút (Delete -> Del...).</summary>
    public static string Short(string s) => Pretty(s).Replace("Delete", "Del").Replace("Escape", "Esc").Replace("Ctrl+", "^");
}

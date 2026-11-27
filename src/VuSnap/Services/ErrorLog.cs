using System.Globalization;

namespace VuSnap.Services;

public static class ErrorCodes
{
    public const string Capture = "ERR_CAPTURE_001";
    public const string Render = "ERR_RENDER_001";
    public const string Clipboard = "ERR_CLIPBOARD_001";
    public const string Export = "ERR_EXPORT_001";
    public const string SettingsInvalid = "ERR_SETTINGS_001";
    public const string SettingsWrite = "ERR_SETTINGS_002";
    public const string Hotkey = "ERR_HOTKEY_001";
}

/// <summary>Local bounded diagnostic log. Never persist screenshots, text, paths or exception messages.</summary>
public static class ErrorLog
{
    private static readonly object Gate = new();
    private const long Limit = 256 * 1024;
    public static void Write(string code, Exception? error = null)
    {
        try
        {
            lock (Gate)
            {
                var folder = SettingsService.DataFolder;
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "diagnostics.log");
                if (File.Exists(path) && new FileInfo(path).Length >= Limit)
                    File.Move(path, path + ".1", overwrite: true);
                var type = error?.GetType().Name ?? "-";
                var hresult = error?.HResult.ToString("X8", CultureInfo.InvariantCulture) ?? "-";
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {code} {type} {hresult}{Environment.NewLine}");
            }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        catch (Exception) { /* Diagnostics must not crash the application. */ }
    }
}

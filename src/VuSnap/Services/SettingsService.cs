using System.Text.Json;
using VuSnap.Models;
namespace VuSnap.Services;
public static class SettingsService
{
    // VUSNAP_DATA_DIR: thư mục dữ liệu riêng (dùng khi chạy thử/test để không đụng vào cài đặt thật).
    public static string DataFolder => Folder;

    private static readonly string Folder = ResolveFolder();
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private static string ResolveFolder()
    {
        if (Environment.GetEnvironmentVariable("VUSNAP_DATA_DIR") is { Length: > 0 } custom) return custom;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VuSnap");
    }

    public static AppSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { }
        return new();
    }
    /// <summary>Xuất cài đặt ra file JSON do người dùng chọn.</summary>
    public static void Export(AppSettings settings, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Đọc cài đặt từ file JSON; trả null nếu file không hợp lệ.</summary>
    public static AppSettings? Import(string path)
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)); }
        catch { return null; }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}

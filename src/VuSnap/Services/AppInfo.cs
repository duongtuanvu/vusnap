using System.Diagnostics;
using System.Reflection;

namespace VuSnap.Services;

/// <summary>Thông tin app / người làm. Sửa các hằng số bên dưới cho đúng thông tin thật của bạn.</summary>
public static class AppInfo
{
    public const string AppName = "VuSnap";

    public const string Author = "vudt (based on NinoMark by Tokisaki Nino)";
    public const string WebsiteUrl = "https://github.com/duongtuanvu/vusnap";
    public const string RepoUrl = "https://github.com/duongtuanvu/vusnap";
    /// <summary>Trang bản phát hành mới nhất. App chỉ mở trang này bằng trình duyệt, không tự kết nối mạng.</summary>
    public const string ReleasesUrl = RepoUrl + "/releases/latest";
    public const string DonateUrl = "";

    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.1";

    /// <summary>Tiêu đề cửa sổ: tên app + phiên bản.</summary>
    public static string TitleWithVersion => $"{AppName} v{Version}";

    /// <summary>Mở URL http/https bằng trình duyệt mặc định. Trả về false nếu không mở được.</summary>
    public static bool OpenUrl(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                return false;
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}

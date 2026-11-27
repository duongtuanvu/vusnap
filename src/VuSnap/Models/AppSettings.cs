using VuSnap.Services;

namespace VuSnap.Models;

public sealed class AppSettings
{
    // ── General ──
    public string Language { get; set; } = "en";          // en, vi
    public string Theme { get; set; } = "Light";          // Light, Dark, System
    public string Hotkey { get; set; } = "Alt+Shift+S";  // phím tắt chụp (toàn cục)
    public string SaveDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "VuSnap");
    public bool CopyAfterCapture { get; set; } = true;    // copy thêm vào clipboard khi lưu file
    public string DefaultFormat { get; set; } = "PNG";

    // ── Hệ thống ──
    public bool StartWithWindows { get; set; } = false;       // khởi động cùng Windows
    public bool RunInTray { get; set; } = true;               // đóng cửa sổ vẫn chạy nền ở khay hệ thống

    // ── Display ──
    public string ToolbarPosition { get; set; } = "Bottom";   // Top, Bottom, Left, Right
    public bool MergeBars { get; set; } = true;               // gộp thanh tool với thanh lưu/đóng
    public bool ShowShortcutHints { get; set; } = true;       // hiện phím tắt trên nút tool
    public double OutlineOpacity { get; set; } = 0.2;         // độ mờ viền nét đứt khi chọn object (0.1–1)
    public bool ShowLoupe { get; set; } = true;               // kính lúp khi chọn vùng
    public bool ShowGrid { get; set; } = false;               // lưới căn chỉnh trên vùng chọn (không xuất ra ảnh)
    public int GridSize { get; set; } = 20;                   // khoảng cách lưới (px màn hình)
    public string GridType { get; set; } = "Lines";           // Lines (đường kẻ), Dots (chấm)
    public bool AutoSplitBar { get; set; } = true;            // thanh quá dài thì tách ra các cạnh vùng chọn
    // ── Giá trị mặc định khi mở overlay ──
    public string DefaultColor { get; set; } = "#FF0000";
    public double DefaultThickness { get; set; } = 4;         // 1–40
    public double DefaultOpacity { get; set; } = 1.0;         // 0.05–1
    public string DefaultStroke { get; set; } = "Solid";      // Solid, Dash, Dot, DashDot, Double
    public string DefaultAspect { get; set; } = "Free";       // Free, 1:1, 4:3, ...
    public double DefaultZoom { get; set; } = 2;              // độ phóng đại kính lúp (1.5–10), lưu lại lần dùng gần nhất
    public string DefaultLens { get; set; } = "Circle";       // Circle, Square
    public bool DefaultCorners { get; set; } = false;         // hình chữ nhật chỉ vẽ 4 góc
    public string DefaultArrow { get; set; } = "Filled";      // Filled, Open, Both, Curved
    public string DefaultFont { get; set; } = "Segoe UI";     // font mặc định cho tool Chữ
    public string DefaultFontWeight { get; set; } = "SemiBold";
    public string DefaultLineType { get; set; } = "Plain";
    public bool DefaultFontItalic { get; set; } = false;

    public List<string> ToolOrder { get; set; } = new();      // thứ tự các tool trên thanh công cụ (rỗng = mặc định)
    public List<string> HiddenTools { get; set; } = new() { "Ratio", "Redo", "Undo", "Invert", "Magnifier", "ObjBar", "Cancel", "SaveAs", "QuickSave", "Copy" };    // tool bị tắt

    // ── Shortcuts (action -> "Ctrl+Shift+X"; rỗng = chưa gán) ──
    public Dictionary<string, string> Shortcuts { get; set; } = ShortcutHelper.Defaults();
}

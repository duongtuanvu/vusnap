using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Material.Icons;
using Material.Icons.WPF;
using Microsoft.Win32;
using VuSnap.Models;
using VuSnap.Core;
using VuSnap.Services;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using DrawingRectangle = System.Drawing.Rectangle;
using Ellipse = System.Windows.Shapes.Ellipse;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using TextBox = System.Windows.Controls.TextBox;

namespace VuSnap.Windows;

/// <summary>
/// Overlay toàn màn hình: chọn vùng + chú thích ngay trên vùng chọn (kiểu Lightshot/Flameshot).
/// Mọi tọa độ UI tính bằng DIP của cửa sổ; khi xuất ảnh sẽ quy đổi sang pixel của ảnh gốc.
/// Mỗi chú thích là một "layer" (FrameworkElement trong <see cref="Ink"/>, Tag = <see cref="Ann"/>).
/// </summary>
public partial class CaptureWindow : Window
{
    private enum Drag { None, Creating, Moving, Resizing, Drawing, MovingAnn }

    /// <summary>Thuộc tính của một layer; element tương ứng được dựng lại từ đây trong <see cref="ApplyStyle"/>.</summary>
    private sealed class Ann
    {
        public Guid Id = Guid.NewGuid();
        public string Kind = "";
        public Color Color;
        public double Thick;
        public double Opacity = 1;
        public Point A, B;      // Line/Arrow/Number: điểm đầu/cuối
        public Vector Tail;     // Number: đuôi
        public int Label;       // Number
        public string? Name;    // tên do người dùng đặt
        public string Stroke = "Solid";   // Solid, Dash, Dot, DashDot, Double
        public string Head = "Filled";    // Arrow: Filled, Open, Both, Curved
        public bool CornersOnly;              // Rectangle: chỉ vẽ 4 góc
        public double Zoom = 2;           // Magnifier: độ phóng đại
        public string Font = "Segoe UI";  // Text
        public string Weight = "SemiBold"; public bool Italic;  // Text
        public string LineType = "Plain";  // Line
        public bool Round = true;         // Magnifier: kính tròn / chữ nhật
        public List<Point>? Pts;          // Pen: các điểm của nét vẽ
        public Ann Copy(bool shareCompletedPoints = false)
        {
            var c = (Ann)MemberwiseClone();
            // Completed pen strokes are never mutated; share geometry in history rather than cloning every point.
            c.Pts = shareCompletedPoints || Pts is null ? Pts : new List<Point>(Pts);
            return c;
        }
    }

    private static readonly Color[] QuickPalette =
    {
        Color.FromRgb(255, 0, 0), Color.FromRgb(126, 211, 33), Color.FromRgb(0, 0, 255),
        Color.FromRgb(0, 0, 0), Color.FromRgb(255, 214, 0), Color.FromRgb(255, 255, 255),
    };

    private static readonly Color[] PanelPalette =
    {
        Color.FromRgb(255, 0, 0), Color.FromRgb(255, 140, 0), Color.FromRgb(255, 214, 0), Color.FromRgb(126, 211, 33),
        Color.FromRgb(0, 160, 80), Color.FromRgb(0, 200, 220), Color.FromRgb(0, 0, 255), Color.FromRgb(130, 60, 220),
        Color.FromRgb(255, 60, 170), Color.FromRgb(0, 0, 0), Color.FromRgb(128, 128, 128), Color.FromRgb(255, 255, 255),
    };

    private static readonly Cursor[] HandleCursors =
    {
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
        Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW, Cursors.SizeWE,
    };

    private const double HandleSize = 9;
    private const double SvW = 226, SvH = 130;

    private readonly BitmapSource _source;
    private readonly DrawingRectangle _screenBounds;
    private readonly Ellipse[] _handles = new Ellipse[8];
    private readonly CommandHistory<LayerState[]> _history = new(Array.Empty<LayerState>(), 50, new LayerStateComparer());
    private bool _restoringHistory;
    private bool _outputBusy;
    private readonly bool _ready;
    private readonly AppSettings _cfg = SettingsService.Load();
    private readonly Dictionary<(ModifierKeys, Key), string> _keyMap = new();
    private enum Side { Top, Bottom, Left, Right }
    private readonly List<UIElement> _mainItems = new();     // nút tool + màu/panel/redo/undo
    private readonly List<UIElement> _actionItems = new();   // nút hủy/lưu/copy/xong
    private readonly List<Border> _bars = new();             // [0] = ToolBar, còn lại dùng khi tách thanh
    private Side _colorSide = Side.Bottom;                   // cạnh đang chứa nút màu (để đặt popup màu)

    private Rect _sel;
    private bool _hasSel;      // đã có vùng chọn hợp lệ (toolbar hiển thị)
    private Drag _drag = Drag.None;
    private Point _dragStart;
    private Point _lastEnd;
    private Rect _dragOrigRect;
    private bool _dragHadSelection;
    private int _dragHandle;

    private string? _tool;
    private Color _color = QuickPalette[0];
    private double _thick = 3;
    private double _opacity = 1;
    private bool _gridOn;                        // lưới căn chỉnh (chỉ hiển thị, không xuất ra ảnh)
    private int _gridSize = 20;                  // khoảng cách lưới (px màn hình)
    private string _gridType = "Lines";          // Lines / Dots
    private bool _cornersOnly;                      // hình chữ nhật chỉ vẽ 4 góc (mặc định cho hình mới)
    private string _arrow = "Filled";            // loại mũi tên mặc định
    private string _style = "Solid";             // kiểu nét mặc định cho nét mới
    private double? _ratio;                      // tỉ lệ khung hình vùng chọn (rộng/cao); null = tự do
    private string _ratioId = "Free";
    private readonly Dictionary<Border, Side> _barSide = new();
    private string? _flyKind;
    private string _font = "Segoe UI";         // font mặc định cho chữ mới
    private string _weight = "SemiBold", _lineType = "Plain";
    private bool _italic;
    private double _zoom = 2;                   // độ phóng đại kính lúp (tool)
    private bool _lensRound = true;
    private bool _zoomDirty;                         // có thay đổi zoom/hình dạng chưa lưu vào cài đặt
    private readonly DispatcherTimer _zoomSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _badgeTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private ImageBrush? _loupeBrush;             // brush của kính lúp khi chọn vùng
    private double _h, _s = 1, _v = 1;
    private int _suppress;      // >0: đang cập nhật UI từ code, bỏ qua event

    private FrameworkElement? _current;   // shape đang vẽ
    private readonly List<FrameworkElement> _selection = new();   // các layer đang chọn
    private FrameworkElement? _primary;                            // layer chọn gần nhất (nguồn thuộc tính)
    private FrameworkElement? _anchor;                             // mốc cho Shift-range trong danh sách layer
    private readonly Dictionary<FrameworkElement, Vector> _moveOrigs = new();
    private TextBox? _editor;             // ô nhập chữ đang mở
    private bool _svDrag, _hueDrag;

    public CaptureWindow(BitmapSource source, DrawingRectangle screenBounds)
    {
        Loc.Lang = _cfg.Language;
        InitializeComponent();
        _source = source;
        _screenBounds = screenBounds;
        Screenshot.Source = source;
        _loupeBrush = new ImageBrush(source) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(_loupeBrush, BitmapScalingMode.NearestNeighbor);
        LoupeView.Fill = _loupeBrush;

        for (var i = 0; i < _handles.Length; i++)
        {
            _handles[i] = new Ellipse
            {
                Width = HandleSize, Height = HandleSize,
                Fill = new SolidColorBrush(Color.FromRgb(10, 100, 255)),
                Stroke = Brushes.White, StrokeThickness = 1.5,
                Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            };
            Overlay.Children.Insert(1 + i, _handles[i]);
        }

        _color = Presets.ParseColor(_cfg.DefaultColor, _color);
        _thick = Math.Clamp(_cfg.DefaultThickness, 1, 40);
        _opacity = Math.Clamp(_cfg.DefaultOpacity, 0.05, 1);
        _style = Presets.StrokeIds.Contains(_cfg.DefaultStroke) ? _cfg.DefaultStroke : "Solid";
        _ratioId = Presets.RatioIds.Contains(_cfg.DefaultAspect) ? _cfg.DefaultAspect : "Free";
        _ratio = Presets.RatioValue(_ratioId);
        _arrow = Presets.ArrowIds.Contains(_cfg.DefaultArrow) ? _cfg.DefaultArrow : "Filled";
        _cornersOnly = _cfg.DefaultCorners;
        _gridOn = _cfg.ShowGrid;
        _gridSize = Math.Clamp(_cfg.GridSize, 5, 100);
        _gridType = _cfg.GridType == "Dots" ? "Dots" : "Lines";
        _zoom = Math.Clamp(_cfg.DefaultZoom, 1.5, 10);
        _zoomSaveTimer.Tick += (_, _) => { _zoomSaveTimer.Stop(); SaveLensDefaults(); };
        _badgeTimer.Tick += (_, _) => { _badgeTimer.Stop(); if (_drag == Drag.None) SizeBadge.Visibility = Visibility.Collapsed; };
        Closing += (_, e) => { if (_outputBusy && !IsEnabled) e.Cancel = true; };
        Closed += (_, _) => { _zoomSaveTimer.Stop(); _badgeTimer.Stop(); if (_zoomDirty) SaveLensDefaults(); };
        _lensRound = _cfg.DefaultLens != "Square";
        _font = string.IsNullOrWhiteSpace(_cfg.DefaultFont) ? "Segoe UI" : _cfg.DefaultFont;
        _weight = Presets.WeightIds.Contains(_cfg.DefaultFontWeight) ? _cfg.DefaultFontWeight : "SemiBold"; _italic = _cfg.DefaultFontItalic;
        _lineType = Presets.LineTypeIds.Contains(_cfg.DefaultLineType) ? _cfg.DefaultLineType : "Plain";

        BuildKeyMap();
        ApplyUiSettings();
        BuildSwatches(Swatches, QuickPalette, 22);
        BuildSwatches(PSwatches, PanelPalette, 20);
        InitializeHistoryGestures();
        _ready = true;
        SetColor(_color);
        SetThick(_thick);
        SetOpacity(_opacity);

        // Window.Left/Top/Width/Height tính bằng DIP, còn Screen.Bounds là pixel -> đặt vị trí bằng Win32.
        DpiChanged += (_, _) => PlaceOnMonitor();
        SizeChanged += (_, _) => { Ink.Width = Root.ActualWidth; Ink.Height = Root.ActualHeight; UpdateDim(); };
        Loaded += (_, _) => { PlaceOnMonitor(); Activate(); Focus(); UpdateDim(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PlaceOnMonitor();
    }

    // ───────────────────────── Win32 / DPI ─────────────────────────

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private void PlaceOnMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        const uint SWP_NOACTIVATE = 0x0010;
        SetWindowPos(hwnd, new IntPtr(-1), _screenBounds.Left, _screenBounds.Top, _screenBounds.Width, _screenBounds.Height, SWP_NOACTIVATE);
    }

    /// <summary>Số pixel ảnh gốc trên mỗi DIP.</summary>
    private double ScaleX => _source.PixelWidth / Math.Max(1, Root.ActualWidth);
    private double ScaleY => _source.PixelHeight / Math.Max(1, Root.ActualHeight);

    /// <summary>Bề rộng còn dùng được (trừ panel bên phải nếu đang mở).</summary>
    private double AvailW => Root.ActualWidth - (SidePanel.Visibility == Visibility.Visible ? SidePanel.Width : 0);

    // ───────────────────────── Chuột: chọn vùng / di chuyển / resize / vẽ ─────────────────────────

    private static bool IsInside(object? source, DependencyObject target)
    {
        for (var d = source as DependencyObject; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, target)) return true;
        return false;
    }

    private void Swallow(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private bool IsAnnTool => _tool is null or "Select";

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_outputBusy || _drag != Drag.None) return;
        var p = e.GetPosition(Root);
        if (_editor is not null)
        {
            if (IsInside(e.OriginalSource, _editor)) return;
            CommitText();
        }
        Focus(); // rời khỏi các ô nhập (hex...) để phím tắt hoạt động lại
        HideColorPanel();

        if (_hasSel)
        {
            var h = HitHandle(p);
            if (h >= 0) { BeginDrag(Drag.Resizing, p); _dragHandle = h; return; }
            if (_sel.Contains(p))
            {
                var mods = Keyboard.Modifiers;
                var ctrl = (mods & ModifierKeys.Control) != 0;
                var hitNow = HitAnnotation(p);
                // Sửa nội dung: nhấp đúp ở mọi tool, hoặc nhấp một lần khi đang dùng tool Text / Number (không giữ Ctrl)
                if (hitNow is TextBlock { Tag: Ann { Kind: "Text" } } textHit && (e.ClickCount == 2 || (_tool == "Text" && !ctrl)))
                {
                    Select(textHit);
                    StartText(p, textHit);
                    return;
                }
                if (hitNow is Canvas { Tag: Ann { Kind: "Number" } } numHit && (e.ClickCount == 2 || (_tool == "Number" && !ctrl)))
                {
                    Select(numHit);
                    StartNumber(numHit);
                    return;
                }
                // Đang ở tool vẽ: giữ Ctrl để chọn / di chuyển trực tiếp object dưới con trỏ
                if (IsAnnTool || (ctrl && hitNow is not null))
                {
                    var hit = hitNow;
                    var additive = IsAnnTool ? (mods & (ModifierKeys.Control | ModifierKeys.Shift)) != 0 : (mods & ModifierKeys.Shift) != 0;
                    if (hit is null)
                    {
                        if (!additive) Select(null);
                        if (Ink.Children.Count == 0) BeginDrag(Drag.Moving, p);
                        return;
                    }
                    if (additive) { ToggleSelect(hit); return; }
                    if (!_selection.Contains(hit)) Select(hit);
                    _moveOrigs.Clear();
                    foreach (var el in _selection) { var t = TranslateOf(el); _moveOrigs[el] = new Vector(t.X, t.Y); }
                    _drag = Drag.MovingAnn; _dragStart = p; if (!Root.CaptureMouse()) CancelPointerDrag();
                    return;
                }
                BeginDraw(p);
                return;
            }
            if (Ink.Children.Count > 0) return; // đã chú thích thì không cho chọn lại vùng khác
        }

        Hint.Visibility = Visibility.Collapsed;
        BeginDrag(Drag.Creating, p);
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Root);
        var w = Root.ActualWidth; var h = Root.ActualHeight;
        var c = new Point(Math.Clamp(p.X, 0, w), Math.Clamp(p.Y, 0, h));

        switch (_drag)
        {
            case Drag.Creating:
                ApplySelection(MakeRect(_dragStart, RatioPoint(_dragStart, c)));
                break;
            case Drag.Moving:
            {
                var x = Math.Clamp(_dragOrigRect.X + (p.X - _dragStart.X), 0, w - _dragOrigRect.Width);
                var y = Math.Clamp(_dragOrigRect.Y + (p.Y - _dragStart.Y), 0, h - _dragOrigRect.Height);
                ApplySelection(new Rect(x, y, _dragOrigRect.Width, _dragOrigRect.Height));
                break;
            }
            case Drag.Resizing:
                ApplySelection(ConstrainResize(_dragOrigRect, _dragHandle, c));
                break;
            case Drag.Drawing:
                UpdateDraw(p);
                break;
            case Drag.MovingAnn:
                foreach (var (el, orig) in _moveOrigs)
                {
                    var t = TranslateOf(el);
                    t.X = orig.X + (p.X - _dragStart.X);
                    t.Y = orig.Y + (p.Y - _dragStart.Y);
                }
                UpdateOutline();
                break;
            default:
                UpdateHoverCursor(p);
                break;
        }
        UpdateLoupe(p);
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == Drag.None) return;
        var was = _drag;
        _drag = Drag.None;
        Root.ReleaseMouseCapture();
        HideLoupe();

        if (was == Drag.Drawing) { EndDraw(); return; }
        if (was == Drag.MovingAnn)
        {
            // Blur/Invert phải lấy lại pixel ở vị trí mới
            foreach (var el in _selection)
                if (el is Rectangle r && r.Tag is Ann { Kind: "Blur" or "Invert" }) ApplyEffect(r);
            foreach (var el in _selection)
                if (el is Path mp && mp.Tag is Ann { Kind: "Magnifier" } ma) ApplyMagnifier(mp, ma);
            _moveOrigs.Clear();
            UpdateOutline(); RecordHistory();
            return;
        }

        if (was == Drag.Creating && (_sel.Width < 8 || _sel.Height < 8))
        {
            ClearSelection();
            return;
        }
        _hasSel = true;
        SizeBadge.Visibility = Visibility.Collapsed;
        ShowBars();
    }

    // ───────────────────────── Kính lúp khi chọn vùng ─────────────────────────

    private void Root_MouseLeave(object sender, MouseEventArgs e) => HideLoupe();

    private void HideLoupe() => Loupe.Visibility = Visibility.Collapsed;

    /// <summary>Ô phóng to 17×17 pixel quanh con trỏ (8×), kèm tọa độ và màu pixel — chỉ hiện khi chưa có vùng chọn hoặc đang kéo/resize vùng chọn.</summary>
    private void UpdateLoupe(Point p)
    {
        if (!_cfg.ShowLoupe || _loupeBrush is null || (_hasSel && _drag is not (Drag.Creating or Drag.Resizing)))
        {
            HideLoupe();
            return;
        }
        int pw = _source.PixelWidth, ph = _source.PixelHeight;
        var px = Math.Clamp((int)(p.X * ScaleX), 0, pw - 1);
        var py = Math.Clamp((int)(p.Y * ScaleY), 0, ph - 1);
        const int n = 17, half = n / 2;
        _loupeBrush.Viewbox = new Rect((px - half) / (double)pw, (py - half) / (double)ph, n / (double)pw, n / (double)ph);

        var buf = new byte[4];
        _source.CopyPixels(new Int32Rect(px, py, 1, 1), buf, 4, 0); // Bgra
        LoupeSwatch.Background = new SolidColorBrush(Color.FromRgb(buf[2], buf[1], buf[0]));
        LoupeText.Text = $"{px}, {py}  #{buf[2]:X2}{buf[1]:X2}{buf[0]:X2}";

        Loupe.Visibility = Visibility.Visible;
        Loupe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = Loupe.DesiredSize;
        var x = p.X + 24; var y = p.Y + 24;
        if (x + sz.Width > Root.ActualWidth) x = p.X - 24 - sz.Width;
        if (y + sz.Height > Root.ActualHeight) y = p.Y - 24 - sz.Height;
        Canvas.SetLeft(Loupe, Math.Max(0, x)); Canvas.SetTop(Loupe, Math.Max(0, y));
    }

    private void BeginDrag(Drag kind, Point p)
    {
        _drag = kind;
        _dragStart = p;
        _dragOrigRect = _sel;
        _dragHadSelection = _hasSel;
        if (kind == Drag.Creating) { _hasSel = false; _sel = new Rect(p, p); }
        HideBars();
        if (!Root.CaptureMouse()) CancelPointerDrag();
    }

    private void ClearSelection()
    {
        _hasSel = false;
        Selection.Visibility = Visibility.Collapsed;
        foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
        SizeBadge.Visibility = Visibility.Collapsed;
        Hint.Visibility = Visibility.Visible;
        HideBars();
        AnnotationHost.Clip = null;
        UpdateGrid();
        UpdateDim();
    }

    private static Rect MakeRect(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static Point[] HandlePoints(Rect r)
    {
        var mx = r.X + r.Width / 2; var my = r.Y + r.Height / 2;
        return new[]
        {
            new Point(r.Left, r.Top), new Point(mx, r.Top), new Point(r.Right, r.Top), new Point(r.Right, my),
            new Point(r.Right, r.Bottom), new Point(mx, r.Bottom), new Point(r.Left, r.Bottom), new Point(r.Left, my),
        };
    }

    private int HitHandle(Point p)
    {
        var pts = HandlePoints(_sel);
        for (var i = 0; i < pts.Length; i++)
            if (Math.Abs(pts[i].X - p.X) <= 7 && Math.Abs(pts[i].Y - p.Y) <= 7) return i;
        return -1;
    }

    private static Rect ResizeRect(Rect o, int handle, Point p)
    {
        double l = o.Left, t = o.Top, r = o.Right, b = o.Bottom;
        // 0 TL, 1 T, 2 TR, 3 R, 4 BR, 5 B, 6 BL, 7 L
        if (handle is 0 or 6 or 7) l = p.X;
        if (handle is 2 or 3 or 4) r = p.X;
        if (handle is 0 or 1 or 2) t = p.Y;
        if (handle is 4 or 5 or 6) b = p.Y;
        return MakeRect(new Point(l, t), new Point(r, b));
    }

    // ── Tỉ lệ khung hình ──

    /// <summary>Điểm cuối đã chỉnh để hình chữ nhật (anchor, c) đúng tỉ lệ và nằm trong màn hình.</summary>
    private Point RatioPoint(Point a, Point c) => _ratio is { } r ? RatioPointFor(a, c, r) : c;

    private Point RatioPointFor(Point a, Point c, double r)
    {
        double W = Root.ActualWidth, H = Root.ActualHeight;
        double dx = c.X - a.X, dy = c.Y - a.Y, w = Math.Abs(dx), h = Math.Abs(dy);
        if (w >= h * r) h = w / r; else w = h * r;
        var maxW = dx < 0 ? a.X : W - a.X; var maxH = dy < 0 ? a.Y : H - a.Y;
        if (w > maxW) { w = maxW; h = w / r; }
        if (h > maxH) { h = maxH; w = h * r; }
        return new Point(a.X + (dx < 0 ? -w : w), a.Y + (dy < 0 ? -h : h));
    }

    /// <summary>Resize vùng chọn. Giữ Shift (tỉ lệ tự do) = khóa tỉ lệ hiện tại của khung;
    /// giữ Alt = kéo một cạnh/góc thì phía đối diện giãn ra tương ứng (đối xứng qua tâm).</summary>
    private Rect ConstrainResize(Rect o, int handle, Point c)
    {
        var mods = Keyboard.Modifiers;
        var alt = (mods & ModifierKeys.Alt) != 0;
        var shift = (mods & ModifierKeys.Shift) != 0;
        double? ratioOpt = _ratio ?? (shift ? o.Width / Math.Max(1, o.Height) : null);
        double W = Root.ActualWidth, H = Root.ActualHeight;

        if (alt)
        {
            double cx = o.X + o.Width / 2, cy = o.Y + o.Height / 2, hw = o.Width / 2, hh = o.Height / 2;
            if (handle is 0 or 2 or 3 or 4 or 6 or 7) hw = Math.Abs(c.X - cx);   // handle có thành phần ngang
            if (handle is 0 or 1 or 2 or 4 or 5 or 6) hh = Math.Abs(c.Y - cy);   // handle có thành phần dọc
            hw = Math.Max(0.5, Math.Min(hw, Math.Min(cx, W - cx)));
            hh = Math.Max(0.5, Math.Min(hh, Math.Min(cy, H - cy)));
            if (ratioOpt is { } ar)
            {
                if (handle is 1 or 5) hw = hh * ar;
                else if (handle is 3 or 7) hh = hw / ar;
                else if (hw >= hh * ar) hh = hw / ar; else hw = hh * ar;
                return ShrinkInto(new Rect(cx - hw, cy - hh, 2 * hw, 2 * hh), new Point(cx, cy));
            }
            return new Rect(cx - hw, cy - hh, 2 * hw, 2 * hh);
        }

        var r = ResizeRect(o, handle, c);
        if (ratioOpt is not { } ratio) return r;
        if (handle is 0 or 2 or 4 or 6)
        {
            // góc: giữ cố định góc đối diện
            var anchor = handle switch { 0 => new Point(o.Right, o.Bottom), 2 => new Point(o.Left, o.Bottom), 4 => new Point(o.Left, o.Top), _ => new Point(o.Right, o.Top) };
            return MakeRect(anchor, RatioPointFor(anchor, c, ratio));
        }
        // cạnh: kéo một cạnh, chiều còn lại co giãn quanh tâm
        double cx2 = o.X + o.Width / 2, cy2 = o.Y + o.Height / 2, w, h;
        Rect nr; Point ap;
        if (handle is 1 or 5)
        {
            h = Math.Max(1, handle == 1 ? o.Bottom - c.Y : c.Y - o.Top); w = h * ratio;
            nr = new Rect(cx2 - w / 2, handle == 1 ? o.Bottom - h : o.Top, w, h);
            ap = new Point(cx2, handle == 1 ? o.Bottom : o.Top);
        }
        else
        {
            w = Math.Max(1, handle == 3 ? c.X - o.Left : o.Right - c.X); h = w / ratio;
            nr = new Rect(handle == 3 ? o.Left : o.Right - w, cy2 - h / 2, w, h);
            ap = new Point(handle == 3 ? o.Left : o.Right, cy2);
        }
        return ShrinkInto(nr, ap);
    }

    /// <summary>Thu nhỏ hình chữ nhật quanh điểm neo cho tới khi nằm gọn trong màn hình.</summary>
    private Rect ShrinkInto(Rect r, Point ap)
    {
        double W = Root.ActualWidth, H = Root.ActualHeight, s = 1;
        if (r.Left < 0 && ap.X > r.Left) s = Math.Min(s, ap.X / (ap.X - r.Left));
        if (r.Right > W && r.Right > ap.X) s = Math.Min(s, (W - ap.X) / (r.Right - ap.X));
        if (r.Top < 0 && ap.Y > r.Top) s = Math.Min(s, ap.Y / (ap.Y - r.Top));
        if (r.Bottom > H && r.Bottom > ap.Y) s = Math.Min(s, (H - ap.Y) / (r.Bottom - ap.Y));
        s = Math.Max(s, 0.01);
        return new Rect(ap.X + (r.Left - ap.X) * s, ap.Y + (r.Top - ap.Y) * s, r.Width * s, r.Height * s);
    }

    /// <summary>Đổi tỉ lệ: nếu đã có vùng chọn thì co lại (không phóng to) về đúng tỉ lệ, giữ nguyên tâm.</summary>
    private void SetRatio(string id)
    {
        _ratioId = id; _ratio = Presets.RatioValue(id);
        RatioLabel.Text = id == "Free" ? "∞" : id;
        if (_hasSel && _ratio is { } r && _drag == Drag.None)
        {
            var w = Math.Min(_sel.Width, _sel.Height * r); var h = w / r;
            var cx = _sel.X + _sel.Width / 2; var cy = _sel.Y + _sel.Height / 2;
            ApplySelection(new Rect(cx - w / 2, cy - h / 2, w, h));
            SizeBadge.Visibility = Visibility.Collapsed;
            ShowBars();
        }
    }

    private void UpdateHoverCursor(Point p)
    {
        if (!_hasSel) { Root.Cursor = null; return; }
        var h = HitHandle(p);
        if (h >= 0) { Root.Cursor = HandleCursors[h]; return; }
        if (_sel.Contains(p))
        {
            if (!IsAnnTool) Root.Cursor = Cursors.Cross;
            else if (HitAnnotation(p) is not null || Ink.Children.Count == 0) Root.Cursor = Cursors.SizeAll;
            else Root.Cursor = Cursors.Arrow;
        }
        else Root.Cursor = Ink.Children.Count == 0 ? Cursors.Cross : Cursors.Arrow;
    }

    // ───────────────────────── Hiển thị vùng chọn / thanh công cụ ─────────────────────────

    private void ApplySelection(Rect r)
    {
        _sel = r;
        Selection.Visibility = Visibility.Visible;
        Canvas.SetLeft(Selection, r.X); Canvas.SetTop(Selection, r.Y);
        Selection.Width = r.Width; Selection.Height = r.Height;

        var pts = HandlePoints(r);
        for (var i = 0; i < _handles.Length; i++)
        {
            _handles[i].Visibility = Visibility.Visible;
            Canvas.SetLeft(_handles[i], pts[i].X - HandleSize / 2);
            Canvas.SetTop(_handles[i], pts[i].Y - HandleSize / 2);
        }

        AnnotationHost.Clip = new RectangleGeometry(r);
        UpdateGrid();
        UpdateDim();

        SizeBadgeText.Text = $"{(int)Math.Round(r.Width * ScaleX)} × {(int)Math.Round(r.Height * ScaleY)}";
        SizeBadge.Visibility = Visibility.Visible;
        SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(SizeBadge, Math.Max(0, r.X));
        Canvas.SetTop(SizeBadge, r.Y - SizeBadge.DesiredSize.Height - 4 < 0 ? r.Y + 4 : r.Y - SizeBadge.DesiredSize.Height - 4);
    }

    private void UpdateDim()
    {
        var w = Root.ActualWidth; var h = Root.ActualHeight;
        if (w <= 0 || h <= 0) return;
        Geometry full = new RectangleGeometry(new Rect(0, 0, w, h));
        Dim.Data = Selection.Visibility == Visibility.Visible
            ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(_sel))
            : full;
    }

    private void HideBars()
    {
        foreach (var b in _bars) b.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Collapsed;
        ObjBar.Visibility = Visibility.Collapsed;
        HideColorPanel();
    }

    private Side ToolSide => _cfg.ToolbarPosition switch { "Top" => Side.Top, "Left" => Side.Left, "Right" => Side.Right, _ => Side.Bottom };

    private double Extent(Side s) => s is Side.Top or Side.Bottom ? _sel.Width : _sel.Height;

    /// <summary>Thứ tự các cạnh dùng khi tách thanh: cạnh đã chọn trước, tràn sang các cạnh còn lại.</summary>
    private static Side[] SideOrder(Side first) => first switch
    {
        Side.Bottom => new[] { Side.Bottom, Side.Right, Side.Top, Side.Left },
        Side.Top => new[] { Side.Top, Side.Right, Side.Bottom, Side.Left },
        Side.Right => new[] { Side.Right, Side.Bottom, Side.Top, Side.Left },
        _ => new[] { Side.Left, Side.Bottom, Side.Top, Side.Right },
    };

    /// <summary>Đặt một thanh cạnh vùng chọn, căn giữa theo cạnh; hết chỗ thì lật sang phía đối diện, rồi vào trong.</summary>
    private Point PlaceBar(Size sz, Side side, double extra)
    {
        const double gap = 6;
        var w = AvailW; var h = Root.ActualHeight;
        double x, y;
        if (side is Side.Top or Side.Bottom)
        {
            x = Math.Clamp(_sel.Left + (_sel.Width - sz.Width) / 2, 0, Math.Max(0, w - sz.Width));
            if (side == Side.Bottom)
            {
                y = _sel.Bottom + gap + extra;
                if (y + sz.Height > h) { y = _sel.Top - gap - extra - sz.Height; if (y < 0) y = Math.Max(0, _sel.Bottom - sz.Height - gap); }
            }
            else
            {
                y = _sel.Top - gap - extra - sz.Height;
                if (y < 0) { y = _sel.Bottom + gap + extra; if (y + sz.Height > h) y = Math.Max(0, _sel.Top + gap); }
            }
        }
        else
        {
            y = Math.Clamp(_sel.Top + (_sel.Height - sz.Height) / 2, 0, Math.Max(0, h - sz.Height));
            if (side == Side.Right)
            {
                x = _sel.Right + gap + extra;
                if (x + sz.Width > w) { x = _sel.Left - gap - extra - sz.Width; if (x < 0) x = Math.Max(0, _sel.Right - sz.Width - gap); }
            }
            else
            {
                x = _sel.Left - gap - extra - sz.Width;
                if (x < 0) { x = _sel.Right + gap + extra; if (x + sz.Width > w) x = Math.Max(0, _sel.Left + gap); }
            }
        }
        return new Point(Math.Round(x), Math.Round(y));
    }

    private static bool IsShown(UIElement e) => e.Visibility == Visibility.Visible;

    /// <summary>Dựng lại các thanh: gộp/tách, hướng ngang/dọc, tách quanh viền nếu quá dài, rồi đặt vị trí.</summary>
    private void ShowBars()
    {
        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (var b in _bars) { ((Panel)b.Child).Children.Clear(); b.Visibility = Visibility.Collapsed; }
        ActionPanel.Children.Clear();
        ActionBar.Visibility = Visibility.Collapsed;

        var main = _mainItems.Where(IsShown).ToList();
        var acts = _actionItems.Where(IsShown).ToList();
        if (main.Count + acts.Count == 0) return;

        // flow: null = vạch phân cách giữa nhóm tool và nhóm lưu/đóng (chỉ khi gộp và không tách)
        var flow = new List<UIElement?>(main);
        if (_cfg.MergeBars)
        {
            if (main.Count > 0 && acts.Count > 0) flow.Add(null);
            flow.AddRange(acts);
        }

        var side0 = ToolSide;
        var segs = new List<(Side Side, List<UIElement?> Items)>();
        var need = flow.Sum(i => i is null ? 13.0 : 40.0);
        if (_cfg.AutoSplitBar && need > Extent(side0) + 1)
        {
            var items = flow.Where(i => i is not null).ToList();
            var order = SideOrder(side0);

            // Mỗi cạnh chỉ chứa vừa số nút nằm gọn trong độ dài cạnh đó (để thanh luôn căn giữa, không tràn/đè lên cạnh khác).
            var caps = order.Select(s => Math.Max(1, (int)(Extent(s) / 40))).ToArray();
            // Vùng chọn quá bé, tổng sức chứa không đủ: chỉ nới các cạnh trên/dưới (kéo dài ngang thoải mái),
            // cạnh trái/phải giữ đúng độ dài để không đè lên thanh nằm ngang.
            for (var k = 0; caps.Sum() < items.Count; k = (k + 1) % caps.Length)
                if (order[k] is Side.Top or Side.Bottom) caps[k]++;

            for (var k = 0; k < order.Length && items.Count > 0; k++)
            {
                segs.Add((order[k], items.Take(caps[k]).ToList()));
                items = items.Skip(caps[k]).ToList();
            }
        }
        else segs.Add((side0, flow));

        var bottomBarHeight = 0.0;
        for (var idx = 0; idx < segs.Count; idx++)
        {
            var (side, items) = segs[idx];
            var bar = _bars[idx];
            var panel = (StackPanel)bar.Child;
            var vertical = side is Side.Left or Side.Right;
            panel.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            foreach (var item in items)
            {
                if (item is null)
                    panel.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                        Width = vertical ? 24 : 1, Height = vertical ? 1 : 24,
                        Margin = vertical ? new Thickness(8, 6, 8, 6) : new Thickness(6, 8, 6, 8),
                    });
                else
                {
                    panel.Children.Add(item);
                    if (ReferenceEquals(item, ColorButton.Parent)) _colorSide = side;
                }
            }
            _barSide[bar] = side;
            bar.Visibility = Visibility.Visible;
            bar.InvalidateMeasure(); // ép đo lại, tránh dùng kích thước cũ từ lần dựng trước
            bar.Measure(inf);
            var pos = PlaceBar(bar.DesiredSize, side, 0);
            Canvas.SetLeft(bar, pos.X); Canvas.SetTop(bar, pos.Y);
            if (side == Side.Bottom) bottomBarHeight = bar.DesiredSize.Height + 6;
        }

        if (!_cfg.MergeBars && acts.Count > 0)
        {
            foreach (var a in acts) ActionPanel.Children.Add(a);
            ActionBar.Visibility = Visibility.Visible;
            ActionBar.InvalidateMeasure();
            ActionBar.Measure(inf);
            var ap = PlaceBar(ActionBar.DesiredSize, Side.Bottom, bottomBarHeight);
            Canvas.SetLeft(ActionBar, ap.X); Canvas.SetTop(ActionBar, ap.Y);
        }
        UpdateGrid();
        UpdateOutline(); // hiện lại thanh thao tác nhanh nếu đang chọn layer
    }

    // ───────────────────────── Áp cài đặt giao diện (ẩn tool, hint phím, tooltip, ngôn ngữ) ─────────────────────────

    private void BuildKeyMap()
    {
        _keyMap.Clear();
        foreach (var id in ShortcutHelper.ToolIds.Concat(ShortcutHelper.ActionIds))
            if (ShortcutHelper.TryParse(ShortcutHelper.Get(_cfg, id), out var mods, out var key)) _keyMap[(mods, key)] = id;
    }

    private static string WithShortcut(string text, string combo) => combo == "" ? text : $"{text} ({ShortcutHelper.Pretty(combo)})";

    private void ApplyUiSettings()
    {
        var hidden = _cfg.HiddenTools;
        Visibility Vis(string id) => hidden.Contains(id) ? Visibility.Collapsed : Visibility.Visible;

        // Thứ tự các tool theo cài đặt (kéo thả trong cửa sổ chính); các nút khác (tỉ lệ, màu, panel, undo...) giữ vị trí cuối
        var toolButtons = ToolPanel.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>()
            .Where(b => b.Tag is string t && ShortcutHelper.ToolIds.Contains(t)).ToList();
        var otherItems = ToolPanel.Children.Cast<UIElement>().Where(c => !toolButtons.Contains(c)).ToList();
        ToolPanel.Children.Clear();
        foreach (var id in ShortcutHelper.OrderedTools(_cfg))
            if (toolButtons.FirstOrDefault(b => (string)b.Tag == id) is { } tb) ToolPanel.Children.Add(tb);
        foreach (var item in otherItems) ToolPanel.Children.Add(item);

        // Ngôn ngữ
        HintText.Text = Loc.T("ov.hint");
        PropsTitle.Text = Loc.T("pn.props"); PSizeLabel.Text = Loc.T("pn.size"); POpacityLabel.Text = Loc.T("pn.opacity");
        PColorLabel.Text = Loc.T("pn.color"); LayersTitle.Text = Loc.T("pn.layers"); PanelCloseButton.Content = Loc.T("pn.close");
        LayerDeleteButton.ToolTip = WithShortcut(Loc.T("pn.del"), ShortcutHelper.Get(_cfg, "Delete"));
        LayerUpButton.ToolTip = Loc.T("pn.up"); LayerDownButton.ToolTip = Loc.T("pn.down");
        AnnOutline.Opacity = Math.Clamp(_cfg.OutlineOpacity, 0.1, 1);

        // Nút tool: ẩn/hiện, tooltip, nhãn phím tắt, mũi tên ▾ mở menu kiểu nét
        foreach (var child in ToolPanel.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().ToList())
        {
            if (child.Tag is not string id) continue;
            var combo = ShortcutHelper.Get(_cfg, id);
            child.Visibility = Vis(id);
            var hasMenu = id is "Pen" or "Line" or "Arrow" or "Rectangle" or "Ellipse" or "Magnifier";
            child.ToolTip = WithShortcut(Loc.T("tool." + id), combo) + (hasMenu ? "\n" + Loc.T(id == "Magnifier" ? "fly.mag" : id == "Arrow" ? "fly.arrow" : "fly.styles") : "");
            var showHint = _cfg.ShowShortcutHints && combo != "";
            if ((showHint || hasMenu) && child.Content is UIElement icon)
            {
                child.Content = null;
                if (icon is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Center; fe.VerticalAlignment = VerticalAlignment.Center; }
                var grid = new Grid { Width = 40, Height = 40 };
                grid.Children.Add(icon);
                if (showHint)
                    grid.Children.Add(new TextBlock
                    {
                        Text = ShortcutHelper.Short(combo), FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 3, 1),
                        IsHitTestVisible = false,
                    });
                if (hasMenu)
                {
                    var kind = id;
                    var chev = new Border
                    {
                        Width = 15, Height = 15, Background = Brushes.Transparent, Cursor = Cursors.Hand,
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                        Child = new Path { Data = Geometry.Parse("M3,4 L10,4 L6.5,9 Z"), Fill = new SolidColorBrush(Color.FromRgb(170, 170, 170)) },
                    };
                    // Handled = true để nút tool không bị bấm theo
                    chev.MouseLeftButtonDown += (_, e) => { ShowFlyout(child, kind); e.Handled = true; };
                    grid.Children.Add(chev);
                }
                child.Content = grid;
            }
            if (hasMenu)
            {
                var kind = id;
                child.MouseRightButtonUp += (_, e) => { ShowFlyout(child, kind); e.Handled = true; };
            }
        }

        // Nút tỉ lệ khung hình + combo kiểu nét trong panel thuộc tính
        RatioButton.Visibility = Vis("Ratio");
        RatioButton.ToolTip = WithShortcut(Loc.T("act.Ratio"), ShortcutHelper.Get(_cfg, "Ratio"));
        RatioLabel.Text = _ratioId == "Free" ? "∞" : _ratioId;
        PStyleLabel.Text = Loc.T("pn.style");
        PFontLabel.Text = Loc.T("pn.font");
        PArrowLabel.Text = Loc.T("pn.arrow");
        PLineTypeLabel.Text = Loc.T("pn.linetype");
        PCornersBox.Content = Loc.T("pn.corners");
        PGridBox.Content = Loc.T("pn.grid"); PGridBox.ToolTip = Loc.T("pn.gridtip");
        PGridSizeLabel.Text = Loc.T("pn.gridsize"); PGridTypeLabel.Text = Loc.T("pn.gridtype");
        PGridTypeBox.Items.Clear();
        foreach (var gid in new[] { "Lines", "Dots" }) PGridTypeBox.Items.Add(new ComboBoxItem { Tag = gid, Content = Loc.T("grid." + gid) });
        foreach (ComboBoxItem it in PGridTypeBox.Items) if ((string?)it.Tag == _gridType) PGridTypeBox.SelectedItem = it;
        PGridBox.IsChecked = _gridOn;
        PGridSizeSlider.Value = _gridSize; PGridSizeText.Text = _gridSize + " px";
        PGridOptions.Visibility = _gridOn ? Visibility.Visible : Visibility.Collapsed;
        PArrowBox.Items.Clear();
        foreach (var aid in Presets.ArrowIds) PArrowBox.Items.Add(new ComboBoxItem { Tag = aid, Content = Loc.T("arrow." + aid) });
        PWeightBox.Items.Clear();
        foreach (var wid in Presets.WeightIds) PWeightBox.Items.Add(new ComboBoxItem { Tag = wid, Content = Loc.T("weight." + wid) });
        PLineTypeBox.Items.Clear();
        foreach (var lid in Presets.LineTypeIds) PLineTypeBox.Items.Add(new ComboBoxItem { Tag = lid, Content = Loc.T("line." + lid) });
        PFontBox.Items.Add(_font);
        PFontBox.SelectedIndex = 0;
        PStyleBox.Items.Clear();
        foreach (var sid in Presets.StrokeIds) PStyleBox.Items.Add(new ComboBoxItem { Tag = sid, Content = Loc.T("stroke." + sid) });
        _barSide[ActionBar] = Side.Bottom;

        // Các nút khác trên thanh tool
        ((FrameworkElement)ColorButton.Parent).Visibility = Vis("Color");
        PanelButton.Visibility = Vis("Panel"); RedoButton.Visibility = Vis("Redo"); UndoButton.Visibility = Vis("Undo");
        ColorButton.ToolTip = Loc.T("ov.color");
        DupButton.ToolTip = WithShortcut(Loc.T("obj.dup"), ShortcutHelper.Get(_cfg, "Duplicate"));
        RotateButton.ToolTip = Loc.T("obj.rot"); FlipHButton.ToolTip = Loc.T("obj.flipH"); FlipVButton.ToolTip = Loc.T("obj.flipV");
        ObjDeleteButton.ToolTip = WithShortcut(Loc.T("obj.del"), ShortcutHelper.Get(_cfg, "Delete"));
        PanelButton.ToolTip = WithShortcut(Loc.T("act.TogglePanel"), ShortcutHelper.Get(_cfg, "TogglePanel"));
        RedoButton.ToolTip = WithShortcut(Loc.T("act.Redo"), ShortcutHelper.Get(_cfg, "Redo"));
        UndoButton.ToolTip = WithShortcut(Loc.T("act.Undo"), ShortcutHelper.Get(_cfg, "Undo"));

        // Thanh lưu/đóng
        var actions = ActionPanel.Children.OfType<Button>().ToList();
        var ids = ShortcutHelper.ActionBarIds;
        for (var i = 0; i < actions.Count && i < ids.Length; i++)
        {
            actions[i].Visibility = Vis(ids[i]);
            actions[i].ToolTip = ids[i] == "Cancel" ? Loc.T("act.Cancel") : WithShortcut(Loc.T("act." + ids[i]), ShortcutHelper.Get(_cfg, ids[i]));
        }

        // Ghi nhớ danh sách nút (thứ tự gốc) và tạo thêm thanh phụ cho chế độ tách quanh viền
        _mainItems.AddRange(ToolPanel.Children.Cast<UIElement>());
        _actionItems.AddRange(actions);
        _bars.Add(ToolBar);
        for (var i = 0; i < 3; i++)
        {
            var bar = new Border { Background = (Brush)FindResource("BarBrush"), Cursor = Cursors.Arrow, Visibility = Visibility.Collapsed, Child = new StackPanel() };
            bar.MouseLeftButtonDown += Swallow;
            Overlay.Children.Add(bar);
            _bars.Add(bar);
        }
    }

    private void Panel_Click(object sender, RoutedEventArgs e)
    {
        CommitText();
        var open = SidePanel.Visibility != Visibility.Visible;
        SidePanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        PanelButton.IsChecked = open;
        HideColorPanel();
        if (_hasSel) ShowBars();
        RefreshLayers();
    }

    // ───────────────────────── Công cụ ─────────────────────────

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        CommitText();
        HideColorPanel();
        if (sender is System.Windows.Controls.Primitives.ToggleButton { Tag: string tag })
            SetTool(_tool == tag ? null : tag);
    }

    // ───────────────────────── Menu mở rộng (kiểu nét / tỉ lệ khung hình) ─────────────────────────

    private void Ratio_Click(object sender, RoutedEventArgs e) => ShowFlyout(RatioButton, "Ratio");

    private Side SideOf(DependencyObject? d)
    {
        for (; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is Border b && _barSide.TryGetValue(b, out var side)) return side;
        return Side.Bottom;
    }

    private void PlacePopup(FrameworkElement panel, FrameworkElement anchor, Side side)
    {
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var ps = panel.DesiredSize;
        var btn = anchor.TranslatePoint(new Point(0, 0), Overlay);
        var h = Root.ActualHeight;
        double x = btn.X, y = btn.Y;
        switch (side)
        {
            case Side.Bottom: y = btn.Y - ps.Height - 4; if (y < 0) y = btn.Y + anchor.ActualHeight + 4; break;
            case Side.Top: y = btn.Y + anchor.ActualHeight + 4; if (y + ps.Height > h) y = btn.Y - ps.Height - 4; break;
            default: x = btn.X + anchor.ActualWidth + 4; if (x + ps.Width > AvailW) x = btn.X - ps.Width - 4; break;
        }
        Canvas.SetLeft(panel, Math.Clamp(x, 0, Math.Max(0, AvailW - ps.Width)));
        Canvas.SetTop(panel, Math.Clamp(y, 0, Math.Max(0, h - ps.Height)));
    }

    /// <summary>Mở menu cạnh nút <paramref name="anchor"/>: kind = tên tool (chọn kiểu nét) hoặc "Ratio".</summary>
    private void ShowFlyout(FrameworkElement anchor, string kind)
    {
        CommitText();
        var same = FlyPanel.Visibility == Visibility.Visible && _flyKind == kind;
        HideColorPanel(); // cũng đóng FlyPanel
        if (same) return;
        _flyKind = kind;
        FlyList.Children.Clear();
        if (kind == "Ratio")
            foreach (var id in Presets.RatioIds)
                AddFlyRow(id == "Free" ? Loc.T("ratio.Free") : id, null, id == _ratioId, () => SetRatio(id));
        else if (kind == "Arrow")
            foreach (var id in Presets.ArrowIds)
                AddFlyRow(Loc.T("arrow." + id), MakeArrowPreview(id), id == _arrow, () => PickArrow(id));
        else if (kind == "Magnifier")
        {
            foreach (var z in new[] { 2, 3, 4, 6 })
            {
                var zz = z;
                AddFlyRow($"{z}×", null, Math.Abs(z - _zoom) < 0.01, () => PickLens(zz, null));
            }
            AddFlyRow("○  " + Loc.T("mag.round"), null, _lensRound, () => PickLens(null, true));
            AddFlyRow("□  " + Loc.T("mag.square"), null, !_lensRound, () => PickLens(null, false));
        }
        else
        {
            foreach (var id in Presets.StrokeIds)
                AddFlyRow(Loc.T("stroke." + id), MakeStylePreview(id), id == _style, () => PickStyle(id, kind));
            if (kind == "Line")
            {
                FlyList.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(225, 228, 233)), Margin = new Thickness(4, 4, 4, 4) });
                foreach (var lid in Presets.LineTypeIds)
                {
                    var id2 = lid;
                    AddFlyRow(Loc.T("line." + lid), MakeLineTypePreview(lid), lid == _lineType, () => PickLineType(id2));
                }
            }
            if (kind == "Rectangle")
            {
                FlyList.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(225, 228, 233)), Margin = new Thickness(4, 4, 4, 4) });
                AddFlyRow(Loc.T("corner.square"), MakeCornerPreview(false), !_cornersOnly, () => PickCorner(false));
                AddFlyRow(Loc.T("corner.only"), MakeCornerPreview(true), _cornersOnly, () => PickCorner(true));
            }
        }
        FlyPanel.Visibility = Visibility.Visible;
        PlacePopup(FlyPanel, anchor, SideOf(anchor));
    }

    private void AddFlyRow(string label, UIElement? preview, bool selected, Action pick)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = selected ? "✓" : "", Width = 16, Foreground = new SolidColorBrush(Color.FromRgb(10, 100, 255)), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        if (preview is not null) line.Children.Add(preview);
        line.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30)), VerticalAlignment = VerticalAlignment.Center });
        var row = new Border
        {
            Padding = new Thickness(6, 5, 12, 5), Cursor = Cursors.Hand, CornerRadius = new CornerRadius(3), Child = line,
            Background = selected ? new SolidColorBrush(Color.FromRgb(232, 240, 254)) : Brushes.Transparent,
        };
        row.MouseEnter += (_, _) => { if (!selected) row.Background = new SolidColorBrush(Color.FromRgb(243, 244, 246)); };
        row.MouseLeave += (_, _) => { if (!selected) row.Background = Brushes.Transparent; };
        row.MouseLeftButtonDown += (_, e) => { e.Handled = true; HideColorPanel(); pick(); };
        FlyList.Children.Add(row);
    }

    private static UIElement MakeStylePreview(string id)
    {
        var c = new Canvas { Width = 64, Height = 16, Margin = new Thickness(0, 0, 8, 0) };
        Line L(double y) => new()
        {
            X1 = 2, X2 = 62, Y1 = y, Y2 = y, Stroke = Brushes.Black, StrokeThickness = 2.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        if (id == "Double") { c.Children.Add(L(4.5)); c.Children.Add(L(11.5)); }
        else { var l = L(8); SetDash(l, id); c.Children.Add(l); }
        return c;
    }

    private static UIElement MakeCornerPreview(bool cornersOnly)
    {
        var box = new Canvas { Width = 30, Height = 18, Margin = new Thickness(17, 0, 25, 0) };
        box.Children.Add(cornersOnly
            ? new Path { Data = Geometry.Parse("M1,7 V1 H8 M22,1 H29 V7 M29,11 V17 H22 M8,17 H1 V11"), Stroke = Brushes.Black, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round }
            : new Rectangle { Width = 30, Height = 18, Stroke = Brushes.Black, StrokeThickness = 2 });
        return box;
    }

    private static UIElement MakeLineTypePreview(string id)
    {
        var c = new Canvas { Width = 64, Height = 22, Margin = new Thickness(0, 0, 8, 0) };
        var ann = new Ann { Kind = "Line", A = new Point(8, 11), B = new Point(56, 11), Thick = 2, LineType = id };
        c.Children.Add(new Path { Data = LineGeo(ann), Stroke = Brushes.Black, StrokeThickness = 2, Fill = id == "Dots" ? Brushes.Black : null, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        return c;
    }

    private void PickLineType(string id)
    {
        _lineType = id;
        var editSelected = IsAnnTool && _selection.Any(e => e.Tag is Ann { Kind: "Line" });
        if (!editSelected) { Select(null); SetTool("Line"); }
        RefreshPropUi();
        if (editSelected) ApplyToSelected();
    }

    private void PickStyle(string id, string kind)
    {
        _style = id;
        // Đang ở chế độ chọn và có layer nét được chọn -> sửa kiểu nét của layer đó.
        // Ngược lại (đang dùng tool vẽ) -> chỉ đặt kiểu nét cho các nét vẽ sau, không đụng tới hình vừa vẽ.
        var editSelected = IsAnnTool && _selection.Any(e => e.Tag is Ann a && IsStrokeKind(a.Kind));
        if (!editSelected) { Select(null); SetTool(kind); }
        RefreshPropUi();
        if (editSelected) ApplyToSelected();
    }

    /// <summary>Chọn độ phóng đại / hình dạng kính lúp: sửa layer kính đang chọn, hoặc đặt mặc định cho kính vẽ sau.</summary>
    private void PickLens(int? zoom, bool? round)
    {
        if (zoom is { } z) _zoom = z;
        if (round is { } r) _lensRound = r;
        var editSelected = IsAnnTool && _selection.Any(e => e.Tag is Ann { Kind: "Magnifier" });
        if (!editSelected) { Select(null); SetTool("Magnifier"); }
        else ApplyToSelected();
        SaveLensDefaults(); // nhớ lựa chọn cho lần dùng sau
    }

    /// <summary>Lưu độ phóng đại + hình dạng kính lúp vào cài đặt (đọc-sửa-ghi để không đè các cài đặt khác).</summary>
    private void SaveLensDefaults()
    {
        _zoomDirty = true;
        try
        {
            var s = SettingsService.Update(s =>
            {
                s.DefaultZoom = _zoom;
                s.DefaultLens = _lensRound ? "Circle" : "Square";
                s.ShowGrid = _gridOn; s.GridSize = _gridSize; s.GridType = _gridType;
            });
            _cfg.DefaultZoom = s.DefaultZoom; _cfg.DefaultLens = s.DefaultLens;
            _zoomDirty = false;
        }
        catch (Exception ex) { ErrorLog.Write(ErrorCodes.SettingsWrite, ex); }
    }

    private void ApplyZoomToSelection()
    {
        foreach (var el in _selection)
            if (el is Path mp && mp.Tag is Ann { Kind: "Magnifier" } a && Ink.Children.Contains(el))
            {
                a.Zoom = _zoom;
                ApplyMagnifier(mp, a);
            }
    }

    /// <summary>Lăn chuột = tăng/giảm độ phóng đại (0.5 mỗi nấc, giữ Shift = 0.1): khi đang dùng tool Kính lúp,
    /// đang kéo một kính, hoặc đang chọn layer kính lúp.</summary>
    private void Root_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_hasSel || _editor is not null) return;
        var src = e.OriginalSource;
        if (IsInside(src, SidePanel) || IsInside(src, FlyPanel) || IsInside(src, ColorPanel) || IsInside(src, ActionBar) || IsInside(src, ObjBar) || _bars.Any(b => IsInside(src, b)))
            return;

        var p = e.GetPosition(Root);
        var drawing = _drag == Drag.Drawing && _current?.Tag is Ann { Kind: "Magnifier" };
        var lensSelected = IsAnnTool && _selection.Any(x => x.Tag is Ann { Kind: "Magnifier" });
        if (!drawing && !((_tool == "Magnifier" || lensSelected) && _sel.Contains(p))) return;

        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 0.1 : 0.5;
        _zoom = Math.Round(Math.Clamp(_zoom + (e.Delta > 0 ? step : -step), 1.5, 10) * 10) / 10;
        if (drawing && _current is Path cp && cp.Tag is Ann ca) { ca.Zoom = _zoom; ApplyMagnifier(cp, ca); }
        ApplyZoomToSelection();
        if (!drawing) RecordHistory();

        // nhãn nhỏ cạnh con trỏ báo giá trị zoom
        SizeBadgeText.Text = "×" + _zoom.ToString("0.0", CultureInfo.InvariantCulture);
        SizeBadge.Visibility = Visibility.Visible;
        SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(SizeBadge, Math.Clamp(p.X + 18, 0, Math.Max(0, Root.ActualWidth - SizeBadge.DesiredSize.Width)));
        Canvas.SetTop(SizeBadge, Math.Clamp(p.Y + 18, 0, Math.Max(0, Root.ActualHeight - SizeBadge.DesiredSize.Height)));
        _badgeTimer.Stop(); _badgeTimer.Start();

        _zoomDirty = true;
        _zoomSaveTimer.Stop(); _zoomSaveTimer.Start();
        e.Handled = true;
    }

    private bool _fontsLoaded;

    /// <summary>Chỉ liệt kê font hệ thống khi người dùng mở danh sách (tránh làm chậm lúc mở overlay).</summary>
    private void PFont_DropDownOpened(object? sender, EventArgs e)
    {
        if (_fontsLoaded) return;
        _fontsLoaded = true;
        _suppress++;
        try
        {
            PFontBox.Items.Clear();
            foreach (var n in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
                PFontBox.Items.Add(n);
            if (!PFontBox.Items.Contains(_font)) PFontBox.Items.Insert(0, _font);
            PFontBox.SelectedItem = _font;
        }
        finally { _suppress--; }
    }

    private void PFont_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _suppress > 0 || PFontBox.SelectedItem is not string name) return;
        _font = name;
        ApplyToSelected();
    }

    private void PWeight_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _suppress > 0 || PWeightBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _weight = id; ApplyToSelected();
    }

    private void PLineType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _suppress > 0 || PLineTypeBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _lineType = id; ApplyToSelected();
    }

    private static FontWeight WeightOf(string w) => w switch
    {
        "Light" => FontWeights.Light, "Normal" => FontWeights.Normal, "Bold" => FontWeights.Bold, _ => FontWeights.SemiBold,
    };
    private void PItalic_Click(object sender, RoutedEventArgs e) { _italic = PItalicButton.IsChecked == true; ApplyToSelected(); }

    // ───────────────────────── Lưới căn chỉnh ─────────────────────────

    private void PGrid_Click(object sender, RoutedEventArgs e)
    {
        _gridOn = PGridBox.IsChecked == true;
        PGridOptions.Visibility = _gridOn ? Visibility.Visible : Visibility.Collapsed;
        UpdateGrid();
        ScheduleSettingsSave();
    }

    private void PGridSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        _gridSize = (int)e.NewValue;
        PGridSizeText.Text = _gridSize + " px";
        UpdateGrid();
        ScheduleSettingsSave();
    }

    private void PGridType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || PGridTypeBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _gridType = id;
        UpdateGrid();
        ScheduleSettingsSave();
    }

    /// <summary>Lưu cài đặt sau một lúc (gộp nhiều lần chỉnh thanh trượt thành một lần ghi).</summary>
    private void ScheduleSettingsSave()
    {
        _zoomDirty = true;
        _zoomSaveTimer.Stop(); _zoomSaveTimer.Start();
    }

    /// <summary>Hiện lưới phủ lên vùng chọn (gốc lưới = góc trái-trên vùng chọn). Lưới nằm ngoài lớp Ink nên không lọt vào ảnh xuất.</summary>
    private void UpdateGrid()
    {
        if (!_gridOn || !_hasSel || Selection.Visibility != Visibility.Visible || _sel.Width < 1 || _sel.Height < 1)
        {
            GridView.Visibility = Visibility.Collapsed;
            return;
        }
        var step = Math.Max(3, _gridSize / ScaleX); // px màn hình -> DIP
        Canvas.SetLeft(GridView, _sel.X); Canvas.SetTop(GridView, _sel.Y);
        GridView.Width = _sel.Width; GridView.Height = _sel.Height;
        GridView.Fill = BuildGridBrush(step, _gridType);
        GridView.Visibility = Visibility.Visible;
    }

    /// <summary>Ô lưới lặp lại: đường kẻ hoặc chấm, vẽ 2 lớp (viền tối mờ + nét sáng) để thấy rõ trên mọi nền.</summary>
    private static Brush BuildGridBrush(double step, string type)
    {
        var halo = Frozen(Colors.Black, 0x40);
        var light = Frozen(Colors.White, 0xC8);
        var group = new DrawingGroup();
        Rect tile;
        if (type == "Dots")
        {
            tile = new Rect(-step / 2, -step / 2, step, step); // chấm nằm đúng bội số của khoảng cách lưới
            group.Children.Add(new GeometryDrawing(halo, null, new EllipseGeometry(new Point(0, 0), 2.4, 2.4)));
            group.Children.Add(new GeometryDrawing(light, null, new EllipseGeometry(new Point(0, 0), 1.4, 1.4)));
        }
        else
        {
            tile = new Rect(0, 0, step, step);
            var lines = new GeometryGroup();
            lines.Children.Add(new LineGeometry(new Point(0, 0.5), new Point(step, 0.5)));
            lines.Children.Add(new LineGeometry(new Point(0.5, 0), new Point(0.5, step)));
            group.Children.Add(new GeometryDrawing(null, new Pen(halo, 3), lines));
            group.Children.Add(new GeometryDrawing(null, new Pen(light, 1), lines));
        }
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(tile))); // giữ kích thước ô
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None,
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = tile,
            ViewportUnits = BrushMappingMode.Absolute, Viewport = tile,
        };
        brush.Freeze();
        return brush;
    }

    private void PCorners_Click(object sender, RoutedEventArgs e) { _cornersOnly = PCornersBox.IsChecked == true; ApplyToSelected(); }

    /// <summary>Chọn viền đầy đủ / chỉ 4 góc cho hình chữ nhật: sửa hình đang chọn (chế độ chọn) hoặc đặt mặc định cho hình vẽ sau.</summary>
    private void PickCorner(bool cornersOnly)
    {
        _cornersOnly = cornersOnly;
        var editSelected = IsAnnTool && _selection.Any(e => e.Tag is Ann { Kind: "Rectangle" });
        if (!editSelected) { Select(null); SetTool("Rectangle"); }
        RefreshPropUi();
        if (editSelected) ApplyToSelected();
    }

    private void PArrow_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _suppress > 0 || PArrowBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _arrow = id;
        ApplyToSelected();
    }

    /// <summary>Chọn loại mũi tên: sửa mũi tên đang chọn (khi ở chế độ chọn), hoặc đặt mặc định cho mũi tên vẽ sau.</summary>
    private void PickArrow(string id)
    {
        _arrow = id;
        var editSelected = IsAnnTool && _selection.Any(e => e.Tag is Ann { Kind: "Arrow" });
        if (!editSelected) { Select(null); SetTool("Arrow"); }
        RefreshPropUi();
        if (editSelected) ApplyToSelected();
    }

    private static UIElement MakeArrowPreview(string id)
    {
        var c = new Canvas { Width = 64, Height = 22, Margin = new Thickness(0, 0, 8, 0) };
        var ann = new Ann { Kind = "Arrow", A = new Point(4, 6), B = new Point(60, 6), Thick = 2, Head = id };
        var path = new Path { Data = ArrowGeometry(ann), Stroke = Brushes.Black, StrokeThickness = id == "Open" ? 1.8 : 1.4, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        if (id != "Open") path.Fill = Brushes.Black;
        c.Children.Add(path);
        return c;
    }

    private void PStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _suppress > 0 || PStyleBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _style = id;
        ApplyToSelected();
    }

    private void SetTool(string? tool)
    {
        _tool = tool;
        foreach (var child in ToolPanel.Children)
            if (child is System.Windows.Controls.Primitives.ToggleButton tb && tb.Tag is string tag)
                tb.IsChecked = tag == _tool;
    }

    // ───────────────────────── Màu / độ dày / độ đậm ─────────────────────────

    private void BuildSwatches(Panel host, Color[] palette, double size)
    {
        foreach (var c in palette)
        {
            var color = c;
            var dot = new Ellipse
            {
                Width = size, Height = size, Margin = new Thickness(2), Cursor = Cursors.Hand, Tag = color,
                Fill = Frozen(color), Stroke = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), StrokeThickness = 1,
            };
            dot.MouseLeftButtonDown += (_, e) => { SetColor(color); e.Handled = true; };
            host.Children.Add(dot);
        }
    }

    private void SetColor(Color c, bool keepHsv = false)
    {
        _color = c;
        if (!keepHsv) { var (h, s, v) = ToHsv(c); if (s > 0 && v > 0) _h = h; _s = s; _v = v; }
        RefreshPropUi();
        ApplyToSelected();
    }

    private void SetThick(double v) { _thick = v; RefreshPropUi(); ApplyToSelected(); }
    private void SetOpacity(double v) { _opacity = v; RefreshPropUi(); ApplyToSelected(); }

    private void SizeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_ready && _suppress == 0) SetThick(e.NewValue); }
    private void PSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_ready && _suppress == 0) SetThick(e.NewValue); }
    private void POpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_ready && _suppress == 0) SetOpacity(e.NewValue / 100); }

    /// <summary>Đồng bộ toàn bộ control thuộc tính với _color/_thick/_opacity.</summary>
    private void RefreshPropUi()
    {
        if (!_ready) return;
        _suppress++;
        try
        {
            ColorDot.Fill = Frozen(_color);
            ColorDot.Width = ColorDot.Height = Math.Min(20, 4 + _thick * 1.2);
            PColorSwatch.Background = Frozen(_color);
            if (!PFontBox.Items.Contains(_font)) PFontBox.Items.Insert(0, _font);
            PFontBox.SelectedItem = _font;
            PItalicButton.IsChecked = _italic;
            foreach (ComboBoxItem it in PWeightBox.Items)
                if ((string?)it.Tag == _weight) { PWeightBox.SelectedItem = it; break; }
            foreach (ComboBoxItem it in PLineTypeBox.Items)
                if ((string?)it.Tag == _lineType) { PLineTypeBox.SelectedItem = it; break; }
            PCornersBox.IsChecked = _cornersOnly;
            foreach (ComboBoxItem it in PArrowBox.Items)
                if ((string?)it.Tag == _arrow) { PArrowBox.SelectedItem = it; break; }
            foreach (ComboBoxItem it in PStyleBox.Items)
                if ((string?)it.Tag == _style) { PStyleBox.SelectedItem = it; break; }
            SizeSlider.Value = PSizeSlider.Value = _thick;
            SizeText.Text = PSizeText.Text = ((int)_thick).ToString();
            POpacitySlider.Value = _opacity * 100;
            POpacityText.Text = $"{(int)Math.Round(_opacity * 100)}%";
            if (!PHex.IsKeyboardFocused) PHex.Text = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";

            SvBox.Background = Frozen(FromHsv(_h, 1, 1));
            Canvas.SetLeft(SvMarker, _s * SvW - 6); Canvas.SetTop(SvMarker, (1 - _v) * SvH - 6);
            Canvas.SetLeft(HueMarker, _h / 360 * SvW - 2);

            foreach (var host in new Panel[] { Swatches, PSwatches })
                foreach (var child in host.Children)
                    if (child is Ellipse el)
                    {
                        var on = (Color)el.Tag == _color;
                        el.Stroke = on ? new SolidColorBrush(Color.FromRgb(10, 100, 255)) : new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
                        el.StrokeThickness = on ? 2.5 : 1;
                    }
        }
        finally { _suppress--; }
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        CommitText();
        if (ColorPanel.Visibility == Visibility.Visible) { HideColorPanel(); return; }
        var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
        ColorPanel.Visibility = Visibility.Visible;
        ColorPanel.Measure(inf);
        var ps = ColorPanel.DesiredSize;
        var btn = ColorButton.TranslatePoint(new Point(0, 0), Overlay);
        var h = Root.ActualHeight;
        double x = btn.X, y = btn.Y;
        switch (_colorSide)
        {
            case Side.Bottom: y = btn.Y - ps.Height - 4; if (y < 0) y = btn.Y + 44; break;
            case Side.Top: y = btn.Y + 44; if (y + ps.Height > h) y = btn.Y - ps.Height - 4; break;
            default: x = btn.X + 44; if (x + ps.Width > AvailW) x = btn.X - ps.Width - 4; break;
        }
        Canvas.SetLeft(ColorPanel, Math.Clamp(x, 0, Math.Max(0, AvailW - ps.Width)));
        Canvas.SetTop(ColorPanel, Math.Clamp(y, 0, Math.Max(0, h - ps.Height)));
        ColorButton.IsChecked = true;
    }

    private void HideColorPanel()
    {
        ColorPanel.Visibility = Visibility.Collapsed;
        FlyPanel.Visibility = Visibility.Collapsed;
        ColorButton.IsChecked = false;
    }

    private static SolidColorBrush Frozen(Color c, byte? alpha = null)
    {
        var b = new SolidColorBrush(alpha is null ? c : Color.FromArgb(alpha.Value, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    // ── Bộ chọn màu HSV ──

    private static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s; var x = c * (1 - Math.Abs(h / 60 % 2 - 1)); var m = v - c;
        double r, g, b;
        switch ((int)(h / 60))
        {
            case 0: (r, g, b) = (c, x, 0); break;
            case 1: (r, g, b) = (x, c, 0); break;
            case 2: (r, g, b) = (0, c, x); break;
            case 3: (r, g, b) = (0, x, c); break;
            case 4: (r, g, b) = (x, 0, c); break;
            default: (r, g, b) = (c, 0, x); break;
        }
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static (double h, double s, double v) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d + 6) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        return (h, max == 0 ? 0 : d / max, max);
    }

    private void SvPick(Point p) { _s = Math.Clamp(p.X / SvW, 0, 1); _v = 1 - Math.Clamp(p.Y / SvH, 0, 1); SetColor(FromHsv(_h, _s, _v), keepHsv: true); }
    private void HuePick(Point p) { _h = Math.Clamp(p.X / SvW, 0, 1) * 359.999; SetColor(FromHsv(_h, _s, _v), keepHsv: true); }

    private void Sv_Down(object sender, MouseButtonEventArgs e) { _svDrag = SvBox.CaptureMouse(); SvPick(e.GetPosition(SvBox)); e.Handled = true; }
    private void Sv_Move(object sender, MouseEventArgs e) { if (_svDrag) SvPick(e.GetPosition(SvBox)); }
    private void Sv_Up(object sender, MouseButtonEventArgs e) { _svDrag = false; SvBox.ReleaseMouseCapture(); RecordHistory(); }
    private void Hue_Down(object sender, MouseButtonEventArgs e) { _hueDrag = HueBar.CaptureMouse(); HuePick(e.GetPosition(HueBar)); e.Handled = true; }
    private void Hue_Move(object sender, MouseEventArgs e) { if (_hueDrag) HuePick(e.GetPosition(HueBar)); }
    private void Hue_Up(object sender, MouseButtonEventArgs e) { _hueDrag = false; HueBar.ReleaseMouseCapture(); RecordHistory(); }

    private void PHex_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyHex(); Focus(); e.Handled = true;
    }
    private void PHex_LostFocus(object sender, RoutedEventArgs e) => ApplyHex();

    private void ApplyHex()
    {
        var t = PHex.Text.Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(t);
            SetColor(Color.FromRgb(c.R, c.G, c.B));
        }
        catch { RefreshPropUi(); }
    }

    // ───────────────────────── Layer: tạo / style / chọn / sắp xếp ─────────────────────────

    private IEnumerable<FrameworkElement> Layers => Ink.Children.OfType<FrameworkElement>().Where(e => e.Tag is Ann);

    private Ann NewAnn(string kind) => new() { Kind = kind, Color = _color, Thick = _thick, Opacity = _opacity, Stroke = _style, Head = _arrow, CornersOnly = _cornersOnly, Zoom = _zoom, Round = _lensRound, Font = _font, Weight = _weight, Italic = _italic, LineType = _lineType };

    /// <summary>RenderTransform của layer: [0] xoay, [1] lật (scale -1), [2] dịch chuyển. Xoay/lật quanh tâm layer.</summary>
    private static TransformGroup GroupOf(FrameworkElement el)
    {
        if (el.RenderTransform is TransformGroup { Children.Count: 3 } g) return g;
        g = new TransformGroup();
        g.Children.Add(new RotateTransform(0));
        g.Children.Add(new ScaleTransform(1, 1));
        g.Children.Add(new TranslateTransform());
        el.RenderTransform = g;
        return g;
    }

    private static TranslateTransform TranslateOf(FrameworkElement el) => (TranslateTransform)GroupOf(el).Children[2];

    /// <summary>Áp thuộc tính trong Ann lên element (dùng cho cả lúc tạo và lúc sửa layer).</summary>
    private void ApplyStyle(FrameworkElement el)
    {
        var a = (Ann)el.Tag;
        var br = Frozen(a.Color);
        switch (el)
        {
            case Path mp when a.Kind == "Magnifier":
                ApplyMagnifier(mp, a);
                break;
            case Path sp when IsStrokeKind(a.Kind):
                ApplyStrokePath(sp, a);
                break;
            case Rectangle r when a.Kind == "Highlight":
                r.Fill = Frozen(a.Color, 90);
                break;
            case TextBlock tb:
                tb.Foreground = br; tb.FontSize = 12 + a.Thick * 4;
                tb.FontFamily = new FontFamily(a.Font);
                tb.FontWeight = WeightOf(a.Weight);
                tb.FontStyle = a.Italic ? FontStyles.Italic : FontStyles.Normal;
                break;
            case Canvas c:
                if (a.Kind == "Ruler") BuildRuler(c, a); else BuildNumber(c, a);
                break;
        }
        el.Opacity = a.Opacity;
    }

    /// <summary>Kính lúp: hình tròn/chữ nhật hiển thị vùng ảnh gốc quanh tâm kính, phóng to <see cref="Ann.Zoom"/> lần.
    /// Gọi lại sau khi di chuyển để kính lấy nội dung ở vị trí mới.</summary>
    private void ApplyMagnifier(Path p, Ann a)
    {
        var r = MakeRect(a.A, a.B);
        var t = TranslateOf(p);
        var local = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
        var center = new Point(local.X + t.X, local.Y + t.Y); // tâm kính trong tọa độ cửa sổ (đã tính phần di chuyển)

        p.Data = a.Round ? new EllipseGeometry(local, r.Width / 2, r.Height / 2) : new RectangleGeometry(r);
        p.Stroke = Frozen(a.Color);
        p.StrokeThickness = Math.Max(1, a.Thick);
        p.StrokeLineJoin = PenLineJoin.Round;

        int pw = _source.PixelWidth, ph = _source.PixelHeight;
        double vw = r.Width * ScaleX / a.Zoom, vh = r.Height * ScaleY / a.Zoom; // vùng nguồn (pixel)
        if (vw >= 1 && vh >= 1)
        {
            var brush = new ImageBrush(_source)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect((center.X * ScaleX - vw / 2) / pw, (center.Y * ScaleY - vh / 2) / ph, vw / pw, vh / ph),
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            p.Fill = brush;
        }
        else p.Fill = Brushes.Transparent;
        p.Effect ??= new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.4 };
    }

    private static bool IsStrokeKind(string kind) => kind is "Pen" or "Line" or "Arrow" or "Rectangle" or "Ellipse";

    /// <summary>Đặt kiểu nét đứt/chấm cho một Shape (Solid/Double không dùng dash).</summary>
    private static void SetDash(Shape s, string style)
    {
        switch (style)
        {
            case "Dash": s.StrokeDashArray = new DoubleCollection { 4, 2.5 }; s.StrokeDashCap = PenLineCap.Flat; break;
            case "Dot": s.StrokeDashArray = new DoubleCollection { 0.001, 2.2 }; s.StrokeDashCap = PenLineCap.Round; break;
            case "DashDot": s.StrokeDashArray = new DoubleCollection { 4, 2.2, 0.001, 2.2 }; s.StrokeDashCap = PenLineCap.Round; break;
            default: s.StrokeDashArray = null; s.StrokeDashCap = PenLineCap.Flat; break;
        }
    }

    /// <summary>Đường tâm của layer nét (Pen/Line/Arrow/Rectangle/Ellipse) theo dữ liệu trong Ann.</summary>
    private static Geometry BuildGeometry(Ann a)
    {
        switch (a.Kind)
        {
            case "Pen":
            {
                var pts = a.Pts;
                if (pts is null || pts.Count < 2) return new LineGeometry(a.A, a.A);
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(pts[0], false, false);
                    ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
                }
                g.Freeze();
                return g;
            }
            case "Line": return LineGeo(a);
            case "Arrow": return ArrowGeometry(a);
            case "Rectangle":
            {
                var rc = MakeRect(a.A, a.B);
                if (!a.CornersOnly) return new RectangleGeometry(rc);
                // Chỉ 4 góc: mỗi góc là một chữ L, cạnh dài ~25% cạnh ngắn của hình (tối đa nửa cạnh)
                var len = Math.Min(Math.Clamp(Math.Min(rc.Width, rc.Height) * 0.25, 6, 80), Math.Min(rc.Width, rc.Height) / 2);
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(rc.Left, rc.Top + len), false, false);
                    ctx.LineTo(new Point(rc.Left, rc.Top), true, true); ctx.LineTo(new Point(rc.Left + len, rc.Top), true, true);
                    ctx.BeginFigure(new Point(rc.Right - len, rc.Top), false, false);
                    ctx.LineTo(new Point(rc.Right, rc.Top), true, true); ctx.LineTo(new Point(rc.Right, rc.Top + len), true, true);
                    ctx.BeginFigure(new Point(rc.Right, rc.Bottom - len), false, false);
                    ctx.LineTo(new Point(rc.Right, rc.Bottom), true, true); ctx.LineTo(new Point(rc.Right - len, rc.Bottom), true, true);
                    ctx.BeginFigure(new Point(rc.Left + len, rc.Bottom), false, false);
                    ctx.LineTo(new Point(rc.Left, rc.Bottom), true, true); ctx.LineTo(new Point(rc.Left, rc.Bottom - len), true, true);
                }
                g.Freeze();
                return g;
            }
            default:
            {
                var r = MakeRect(a.A, a.B);
                return new EllipseGeometry(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
            }
        }
    }

    /// <summary>Đường thẳng: thường, hoặc hai đầu có vạch ngang / chấm tròn / mũi tên mở.</summary>
    private static Geometry LineGeo(Ann a)
    {
        var main = new LineGeometry(a.A, a.B);
        if (a.LineType is not ("Ticks" or "Dots" or "Arrows")) return main;
        var v = a.B - a.A;
        if (v.Length < 1) return main;
        v.Normalize();
        var n = new Vector(-v.Y, v.X);
        var th = Math.Max(1, a.Thick);
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        g.Children.Add(main);
        foreach (var (pt, inward) in new[] { (a.A, v), (a.B, -v) })
        {
            switch (a.LineType)
            {
                case "Ticks":
                {
                    var h = 5 + th * 1.6;
                    g.Children.Add(new LineGeometry(pt + n * h, pt - n * h));
                    break;
                }
                case "Dots":
                {
                    var r = 2.2 + th * 1.1;
                    g.Children.Add(new EllipseGeometry(pt, r, r));
                    break;
                }
                default:
                {
                    var len = 8 + th * 2.2;
                    g.Children.Add(new LineGeometry(pt, pt + inward * len + n * len * 0.6));
                    g.Children.Add(new LineGeometry(pt, pt + inward * len - n * len * 0.6));
                    break;
                }
            }
        }
        return g;
    }

    /// <summary>Dựng lại Path của layer nét: hình học + màu + độ dày + kiểu nét (liền/đứt/chấm/đôi).</summary>
    private static void ApplyStrokePath(Path p, Ann a)
    {
        var br = Frozen(a.Color);
        var th = a.Kind == "Arrow" ? Math.Max(1, a.Thick * 0.6) : a.Thick;
        var geo = BuildGeometry(a);
        p.Stroke = br;
        p.StrokeLineJoin = PenLineJoin.Round;
        p.StrokeStartLineCap = p.StrokeEndLineCap = PenLineCap.Round;

        if (a.Kind == "Arrow")
        {
            // Mũi tên: kiểu được quyết định bởi loại đầu mũi tên (Head), không dùng nét đứt/đôi.
            p.Data = geo;
            p.StrokeDashArray = null;
            p.StrokeThickness = a.Head == "Open" ? Math.Max(1.5, a.Thick * 0.9) : Math.Max(1, a.Thick * 0.6);
            p.Fill = a.Head == "Open" ? null : br;
            return;
        }

        if (a.Stroke == "Double")
        {
            // Nét đôi: lấy biên của vùng nét rộng ~2.6·th rồi vẽ nét mảnh dọc theo biên → 2 đường song song.
            var pen = new Pen(Brushes.Black, th * 2.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
            p.Data = geo.GetWidenedPathGeometry(pen);
            p.StrokeThickness = Math.Max(1, th * 0.8);
            p.StrokeDashArray = null;
            p.Fill = Brushes.Transparent; // mũi tên nét đôi: đầu rỗng
        }
        else
        {
            p.Data = geo;
            p.StrokeThickness = th;
            SetDash(p, a.Stroke);
            p.Fill = a.Kind switch { "Arrow" => br, "Rectangle" => a.CornersOnly ? null : Brushes.Transparent, "Ellipse" => Brushes.Transparent, "Line" when a.LineType == "Dots" => br, _ => null };
        }
    }

    /// <summary>Layer số: vòng tròn + đuôi (nếu kéo chuột) — gốc tọa độ là tâm vòng tròn.</summary>
    private static void BuildNumber(Canvas host, Ann a)
    {
        host.Children.Clear();
        var d = 20 + a.Thick * 2.5; var r = d / 2;
        var brush = Frozen(a.Color);
        var white = Frozen(Colors.White);

        if (a.Tail.Length > r * 1.3)
        {
            var u = a.Tail; u.Normalize();
            var perp = new Vector(-u.Y, u.X) * (r * 0.9);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(perp.X, perp.Y), true, true);
                ctx.LineTo(new Point(a.Tail.X, a.Tail.Y), true, true);
                ctx.LineTo(new Point(-perp.X, -perp.Y), true, true);
            }
            g.Freeze();
            host.Children.Add(new Path { Data = g, Fill = brush, Stroke = white, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round });
        }

        var light = a.Color.R + a.Color.G + a.Color.B > 600;
        var bubble = new Border
        {
            Width = d, Height = d, CornerRadius = new CornerRadius(r), Background = brush,
            BorderBrush = white, BorderThickness = new Thickness(1.5),
            Child = new TextBlock
            {
                Text = a.Label.ToString(), FontWeight = FontWeights.Bold, FontSize = d * 0.5,
                Foreground = light ? Brushes.Black : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Canvas.SetLeft(bubble, -r); Canvas.SetTop(bubble, -r);
        host.Children.Add(bubble);
    }

    private void ApplyToSelected()
    {
        var any = false;
        foreach (var el in _selection)
            if (el.Tag is Ann a && Ink.Children.Contains(el))
            {
                a.Color = _color; a.Thick = _thick; a.Opacity = _opacity;
                if (IsStrokeKind(a.Kind)) a.Stroke = _style;
                if (a.Kind == "Arrow") a.Head = _arrow;
                if (a.Kind == "Rectangle") a.CornersOnly = _cornersOnly;
                if (a.Kind == "Magnifier") { a.Zoom = _zoom; a.Round = _lensRound; }
                if (a.Kind == "Line") a.LineType = _lineType;
                if (a.Kind == "Text") { a.Font = _font; a.Weight = _weight; a.Italic = _italic; }
                ApplyStyle(el); any = true;
            }
        if (any) { UpdateOutline(); RecordHistory(); }
    }

    // ── Chọn layer: một / nhiều (Ctrl) / dải (Shift) ──

    private void Select(FrameworkElement? el)
    {
        _selection.Clear();
        if (el is not null) _selection.Add(el);
        _anchor = el;
        SetPrimary(el);
    }

    private void ToggleSelect(FrameworkElement el)
    {
        if (!_selection.Remove(el)) _selection.Add(el);
        _anchor = el;
        SetPrimary(_selection.Contains(el) ? el : _selection.LastOrDefault());
    }

    private void SelectRange(FrameworkElement el)
    {
        var order = Layers.Reverse().ToList(); // đúng thứ tự hiển thị trong danh sách
        int i = order.IndexOf(_anchor ?? el), j = order.IndexOf(el);
        if (i < 0 || j < 0) { Select(el); return; }
        _selection.Clear();
        for (var k = Math.Min(i, j); k <= Math.Max(i, j); k++) _selection.Add(order[k]);
        SetPrimary(el);
    }

    private void SetPrimary(FrameworkElement? el)
    {
        _primary = el;
        if (el?.Tag is Ann a)
        {
            _color = a.Color; _thick = a.Thick; _opacity = a.Opacity;
            if (IsStrokeKind(a.Kind)) _style = a.Stroke;
            if (a.Kind == "Arrow") _arrow = a.Head;
            if (a.Kind == "Rectangle") _cornersOnly = a.CornersOnly;
            if (a.Kind == "Magnifier") { _zoom = a.Zoom; _lensRound = a.Round; }
            if (a.Kind == "Line") _lineType = a.LineType;
            if (a.Kind == "Text") { _font = a.Font; _weight = a.Weight; _italic = a.Italic; }
            var (h, s, v) = ToHsv(a.Color); if (s > 0 && v > 0) _h = h; _s = s; _v = v;
            RefreshPropUi();
        }
        UpdateOutline();
        RefreshLayers();
    }

    private void UpdateOutline()
    {
        Ink.UpdateLayout();
        Rect? union = null;
        foreach (var el in _selection)
        {
            if (!Ink.Children.Contains(el) || el.Visibility != Visibility.Visible) continue;
            var local = el is TextBlock or Rectangle or Ellipse
                ? new Rect(0, 0, el.ActualWidth, el.ActualHeight)
                : VisualTreeHelper.GetDescendantBounds(el);
            if (local.IsEmpty) continue;
            var r = el.TransformToVisual(Root).TransformBounds(local);
            union = union is null ? r : Rect.Union(union.Value, r);
        }
        if (union is null) { AnnOutline.Visibility = Visibility.Collapsed; ObjBar.Visibility = Visibility.Collapsed; return; }
        var u = union.Value;
        u.Inflate(5, 5);
        Canvas.SetLeft(AnnOutline, u.X); Canvas.SetTop(AnnOutline, u.Y);
        AnnOutline.Width = Math.Max(1, u.Width); AnnOutline.Height = Math.Max(1, u.Height);
        AnnOutline.Visibility = Visibility.Visible;
        PositionObjBar(u);
    }

    /// <summary>Đặt thanh thao tác nhanh phía trên object (hết chỗ thì xuống dưới).</summary>
    private void PositionObjBar(Rect u)
    {
        if (!_hasSel || _editor is not null || _cfg.HiddenTools.Contains("ObjBar")) { ObjBar.Visibility = Visibility.Collapsed; return; }
        ObjBar.Visibility = Visibility.Visible;
        ObjBar.InvalidateMeasure();
        ObjBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = ObjBar.DesiredSize;
        var h = Root.ActualHeight;
        var x = Math.Clamp(u.X + (u.Width - sz.Width) / 2, 0, Math.Max(0, AvailW - sz.Width));
        var y = u.Y - sz.Height - 6;
        if (y < 0) { y = u.Bottom + 6; if (y + sz.Height > h) y = Math.Max(0, u.Y + 6); }
        Canvas.SetLeft(ObjBar, Math.Round(x)); Canvas.SetTop(ObjBar, Math.Round(y));
    }

    /// <summary>Layer trên cùng tại điểm p (có dung sai vài px để dễ trỏ vào nét mảnh).</summary>
    private FrameworkElement? HitAnnotation(Point p)
    {
        Ink.UpdateLayout();
        var offsets = new[] { (0.0, 0.0), (4.0, 0.0), (-4.0, 0.0), (0.0, 4.0), (0.0, -4.0) };
        foreach (var el in Layers.Reverse())
        {
            if (el.Visibility != Visibility.Visible) continue;
            foreach (var (dx, dy) in offsets)
            {
                var local = Root.TranslatePoint(new Point(p.X + dx, p.Y + dy), el);
                if (VisualTreeHelper.HitTest(el, local) is not null) return el;
            }
        }
        return null;
    }

    private static string LayerName(FrameworkElement el)
    {
        var a = (Ann)el.Tag;
        if (!string.IsNullOrWhiteSpace(a.Name)) return a.Name!;
        return a.Kind switch
        {
            "Number" => Loc.F("lay.Number", a.Label),
            "Text" => Loc.T("lay.Text") + ((el as TextBlock)?.Text.Replace('\n', ' ') ?? ""),
            _ => Loc.T("lay." + a.Kind),
        };
    }

    private void RefreshLayers()
    {
        LayerList.Children.Clear();
        foreach (var el in Layers.Reverse())
        {
            var cap = el;
            var a = (Ann)el.Tag;
            var visible = el.Visibility == Visibility.Visible;
            var row = new Border
            {
                Padding = new Thickness(4, 4, 6, 4), Cursor = Cursors.Hand,
                Background = _selection.Contains(el) ? new SolidColorBrush(Color.FromRgb(10, 80, 200)) : Brushes.Transparent,
            };
            var line = new StackPanel { Orientation = Orientation.Horizontal };

            var eye = new MaterialIcon
            {
                Kind = visible ? MaterialIconKind.Eye : MaterialIconKind.EyeOff, Width = 18, Height = 18, Margin = new Thickness(3, 0, 5, 0),
                Foreground = Brushes.White, Opacity = visible ? 1 : 0.45, Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center, ToolTip = Loc.T("pn.eye"),
            };
            eye.MouseLeftButtonDown += (_, e) =>
            {
                cap.Visibility = cap.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                RecordHistory(); UpdateOutline(); RefreshLayers(); e.Handled = true;
            };
            line.Children.Add(eye);
            line.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = Frozen(a.Color), Stroke = Brushes.Gray, StrokeThickness = 0.5, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(new TextBlock
            {
                Text = LayerName(el), Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 140,
                Opacity = visible ? 1 : 0.5, ToolTip = Loc.T("pn.rename"),
            });
            row.Child = line;
            row.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (e.ClickCount == 2) { BeginRename(line, cap); return; }
                var mod = Keyboard.Modifiers;
                if ((mod & ModifierKeys.Control) != 0) ToggleSelect(cap);
                else if ((mod & ModifierKeys.Shift) != 0) SelectRange(cap);
                else Select(cap);
            };
            LayerList.Children.Add(row);
        }
    }

    /// <summary>Đổi tên layer: thay TextBlock tên bằng TextBox; Enter/mất focus để lưu, Esc để hủy.</summary>
    private void BeginRename(StackPanel line, FrameworkElement el)
    {
        var a = (Ann)el.Tag;
        var box = new TextBox
        {
            Text = LayerName(el), Width = 150, Padding = new Thickness(2, 0, 2, 0),
            Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)), Foreground = Brushes.White, CaretBrush = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(10, 100, 255)),
        };
        var done = false;
        void Finish(bool save)
        {
            if (done) return;
            done = true;
            if (save && Ink.Children.Contains(el) && ReferenceEquals(el.Tag, a)) { a.Name = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim(); RecordHistory(); }
            RefreshLayers();
            Focus();
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Finish(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
        };
        box.LostKeyboardFocus += (_, _) => Finish(true);
        line.Children.RemoveAt(2);
        line.Children.Add(box);
        Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
    }

    private void MoveLayer(int delta)
    {
        var items = _selection.Where(Ink.Children.Contains).OrderBy(e => Ink.Children.IndexOf(e)).ToList();
        if (items.Count == 0) return;
        if (delta > 0) items.Reverse();
        foreach (var el in items)
        {
            var idx = Ink.Children.IndexOf(el);
            var target = idx + (delta > 0 ? 1 : -1);
            if (target < 0 || target >= Ink.Children.Count) continue;
            if (Ink.Children[target] is FrameworkElement n && (_selection.Contains(n) || n.Tag is not Ann)) continue;
            Ink.Children.Remove(el);
            Ink.Children.Insert(target, el);
        }
        RecordHistory(); RefreshLayers();
    }

    private void DeleteSelected()
    {
        if (_selection.Count == 0) return;
        foreach (var el in _selection.ToList()) Ink.Children.Remove(el);
        _selection.Clear(); _primary = null; _anchor = null;
        RecordHistory(); UpdateOutline();
        RefreshLayers();
    }

    private void SelectAll()
    {
        _selection.Clear();
        _selection.AddRange(Layers);
        _anchor = _selection.FirstOrDefault();
        SetPrimary(_selection.LastOrDefault());
    }

    private void LayerUp_Click(object sender, RoutedEventArgs e) => MoveLayer(+1);
    private void LayerDown_Click(object sender, RoutedEventArgs e) => MoveLayer(-1);
    private void LayerDelete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    /// <summary>Đăng ký layer vừa tạo xong vào lịch sử undo và chọn nó.</summary>
    private void Commit(FrameworkElement el)
    {
        Select(el);
        RecordHistory();
    }

    // ───────────────────────── Vẽ ─────────────────────────

    private void BeginDraw(Point p)
    {
        if (_tool == "Text") { Select(null); StartText(p); return; }
        var kind = _tool!;
        Select(null);

        var ann = NewAnn(kind);
        ann.A = ann.B = p;
        FrameworkElement el;
        switch (kind)
        {
            case "Pen": ann.Pts = new List<Point> { p }; el = new Path(); break;
            case "Line" or "Arrow" or "Rectangle" or "Ellipse" or "Magnifier": el = new Path(); break;
            case "Highlight" or "Blur" or "Invert": el = new Rectangle(); break;
            case "Ruler": el = new Canvas(); break;
            case "Number":
                ann.Label = NextNumber();
                el = new Canvas();
                Canvas.SetLeft(el, p.X); Canvas.SetTop(el, p.Y);
                break;
            default: return;
        }
        el.Tag = ann;
        ApplyStyle(el);
        if (kind is "Blur" or "Invert" && el is Rectangle blur)
        {
            // khung xem trước trong lúc kéo; hiệu ứng được áp khi thả chuột
            blur.Stroke = Frozen(Color.FromRgb(10, 100, 255)); blur.StrokeThickness = 1;
            blur.StrokeDashArray = new DoubleCollection { 3, 2 };
            blur.Fill = Frozen(Color.FromRgb(10, 100, 255), 40);
        }
        if (el is Rectangle) { Canvas.SetLeft(el, p.X); Canvas.SetTop(el, p.Y); }

        Ink.Children.Add(el);
        _current = el;
        _drag = Drag.Drawing; _dragStart = p; _lastEnd = p;
        if (!Root.CaptureMouse()) CancelPointerDrag();
    }

    /// <summary>Giữ Shift: hình vuông/tròn cho Rect/Ellipse..., khóa góc 45° cho Line/Arrow/đuôi số.</summary>
    private static Point Constrain(string kind, Point a, Point p)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0) return p;
        var dx = p.X - a.X; var dy = p.Y - a.Y;
        switch (kind)
        {
            case "Line" or "Arrow" or "Number" or "Ruler":
            {
                var len = Math.Sqrt(dx * dx + dy * dy);
                var ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
                return new Point(a.X + len * Math.Cos(ang), a.Y + len * Math.Sin(ang));
            }
            case "Rectangle" or "Ellipse" or "Highlight" or "Blur" or "Invert" or "Magnifier":
            {
                var m = Math.Max(Math.Abs(dx), Math.Abs(dy));
                return new Point(a.X + (dx < 0 ? -m : m), a.Y + (dy < 0 ? -m : m));
            }
        }
        return p;
    }

    private void UpdateDraw(Point p)
    {
        if (_current is not { Tag: Ann a } el) return;
        var q = Constrain(a.Kind, _dragStart, p);
        _lastEnd = q; a.B = q;
        switch (el)
        {
            case Path mp when a.Kind == "Magnifier":
                ApplyMagnifier(mp, a);
                break;
            case Path sp when IsStrokeKind(a.Kind):
                if (a.Kind == "Pen") a.Pts!.Add(p);
                ApplyStrokePath(sp, a);
                break;
            case Canvas ruler when a.Kind == "Ruler": BuildRuler(ruler, a); break;
            case Canvas num: a.Tail = q - a.A; BuildNumber(num, a); break;
            case Shape box:
                var r = MakeRect(a.A, q);
                Canvas.SetLeft(box, r.X); Canvas.SetTop(box, r.Y); box.Width = r.Width; box.Height = r.Height;
                break;
        }
    }

    private void EndDraw()
    {
        var el = _current; _current = null;
        if (el?.Tag is not Ann a) return;

        var tiny = a.Kind switch
        {
            "Number" => false,
            "Pen" => a.Pts is null || a.Pts.Count < 2,
            _ => Math.Abs(_lastEnd.X - a.A.X) < 3 && Math.Abs(_lastEnd.Y - a.A.Y) < 3,
        };
        if (tiny) { Ink.Children.Remove(el); return; }

        if (a.Kind is "Blur" or "Invert" && el is Rectangle fx) ApplyEffect(fx);
        Commit(el);
    }

    /// <summary>Hình học mũi tên theo loại: Filled (đầu đặc), Open (đầu mở), Both (hai đầu), Curved (cong).</summary>
    private static Geometry ArrowGeometry(Ann a)
    {
        var head = 10 + a.Thick * 3;
        return a.Head switch
        {
            "Open" => OpenArrow(a.A, a.B, head),
            "Both" => BothArrow(a.A, a.B, head),
            "Curved" => CurvedArrow(a.A, a.B, head),
            _ => FilledArrow(a.A, a.B, head),
        };
    }

    private const double HeadHalfAngle = 0.45; // ~26°

    private static void Triangle(StreamGeometryContext ctx, Point tip, double ang, double head)
    {
        // ang: hướng từ đuôi tới đỉnh mũi tên
        var p1 = new Point(tip.X - head * Math.Cos(ang - HeadHalfAngle), tip.Y - head * Math.Sin(ang - HeadHalfAngle));
        var p2 = new Point(tip.X - head * Math.Cos(ang + HeadHalfAngle), tip.Y - head * Math.Sin(ang + HeadHalfAngle));
        ctx.BeginFigure(tip, true, true); ctx.LineTo(p1, true, true); ctx.LineTo(p2, true, true);
    }

    private static Geometry FilledArrow(Point a, Point b, double head)
    {
        var v = b - a; var len = v.Length;
        head = Math.Min(head, len);
        var ang = Math.Atan2(v.Y, v.X);
        var shaftEnd = new Point(b.X - head * Math.Cos(HeadHalfAngle) * Math.Cos(ang), b.Y - head * Math.Cos(HeadHalfAngle) * Math.Sin(ang));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(a, false, false); ctx.LineTo(shaftEnd, true, true);
            Triangle(ctx, b, ang, head);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Đầu mũi tên hở dạng "&gt;" (nét mảnh).</summary>
    private static Geometry OpenArrow(Point a, Point b, double head)
    {
        var v = b - a; var len = v.Length;
        head = Math.Min(head, len);
        var ang = Math.Atan2(v.Y, v.X);
        const double half = 0.55;
        var p1 = new Point(b.X - head * Math.Cos(ang - half), b.Y - head * Math.Sin(ang - half));
        var p2 = new Point(b.X - head * Math.Cos(ang + half), b.Y - head * Math.Sin(ang + half));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(a, false, false); ctx.LineTo(b, true, true);
            ctx.BeginFigure(p1, false, false); ctx.LineTo(b, true, true); ctx.LineTo(p2, true, true);
        }
        g.Freeze();
        return g;
    }

    private static Geometry BothArrow(Point a, Point b, double head)
    {
        var v = b - a; var len = v.Length;
        head = Math.Min(head, len / 2.2);
        var ang = Math.Atan2(v.Y, v.X);
        var k = head * Math.Cos(HeadHalfAngle);
        var s0 = new Point(a.X + k * Math.Cos(ang), a.Y + k * Math.Sin(ang));
        var s1 = new Point(b.X - k * Math.Cos(ang), b.Y - k * Math.Sin(ang));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(s0, false, false); ctx.LineTo(s1, true, true);
            Triangle(ctx, b, ang, head);
            Triangle(ctx, a, ang + Math.PI, head);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Mũi tên cong: đường Bézier bậc 2 cong về một phía, đầu mũi tên theo tiếp tuyến tại điểm cuối.</summary>
    private static Geometry CurvedArrow(Point a, Point b, double head)
    {
        var v = b - a; var len = v.Length;
        if (len < 1) return FilledArrow(a, b, head);
        head = Math.Min(head, len * 0.6);
        var n = new Vector(-v.Y, v.X) / len;                       // pháp tuyến (quay 90°)
        var c = new Point((a.X + b.X) / 2 + n.X * len * 0.28, (a.Y + b.Y) / 2 + n.Y * len * 0.28);
        var t = b - c; var ang = Math.Atan2(t.Y, t.X);              // hướng tại đầu mút
        var end = new Point(b.X - head * Math.Cos(HeadHalfAngle) * Math.Cos(ang), b.Y - head * Math.Cos(HeadHalfAngle) * Math.Sin(ang));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(a, false, false); ctx.QuadraticBezierTo(c, end, true, true);
            Triangle(ctx, b, ang, head);
        }
        g.Freeze();
        return g;
    }

    /// <summary>Vùng pixel của ảnh gốc nằm dưới rect (tính cả vị trí đã di chuyển).</summary>
    private Int32Rect EffectRegion(Rectangle rect)
    {
        var t = TranslateOf(rect);
        var left = Canvas.GetLeft(rect) + t.X; var top = Canvas.GetTop(rect) + t.Y;
        var x = Math.Clamp((int)Math.Round(left * ScaleX), 0, _source.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Round(top * ScaleY), 0, _source.PixelHeight - 1);
        var w = Math.Clamp((int)Math.Round(rect.Width * ScaleX), 1, _source.PixelWidth - x);
        var h = Math.Clamp((int)Math.Round(rect.Height * ScaleY), 1, _source.PixelHeight - y);
        return new Int32Rect(x, y, w, h);
    }

    /// <summary>Blur = pixelate thật; Invert = đảo màu (tương phản) — đều lấy pixel từ ảnh gốc bên dưới.</summary>
    private void ApplyEffect(Rectangle rect)
    {
        var r = EffectRegion(rect);
        var crop = new CroppedBitmap(_source, r);
        BitmapSource result;
        if (rect.Tag is Ann { Kind: "Invert" })
        {
            var conv = new FormatConvertedBitmap(crop, PixelFormats.Bgra32, null, 0);
            var stride = r.Width * 4;
            var px = new byte[stride * r.Height];
            conv.CopyPixels(px, stride, 0);
            for (var i = 0; i < px.Length; i += 4) { px[i] = (byte)(255 - px[i]); px[i + 1] = (byte)(255 - px[i + 1]); px[i + 2] = (byte)(255 - px[i + 2]); }
            result = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgra32, null, px, stride);
        }
        else
        {
            const int block = 10;
            result = new TransformedBitmap(crop, new ScaleTransform(Math.Max(1, r.Width / block) / (double)r.Width, Math.Max(1, r.Height / block) / (double)r.Height));
        }
        result.Freeze();
        var brush = new ImageBrush(result) { Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        rect.Fill = brush; rect.Stroke = null; rect.StrokeDashArray = null;
    }

    /// <summary>Thước đo: đoạn thẳng + 2 vạch đầu mút + nhãn độ dài theo pixel của ảnh gốc.</summary>
    private void BuildRuler(Canvas host, Ann a)
    {
        host.Children.Clear();
        var brush = Frozen(a.Color);
        var th = Math.Max(1, a.Thick * 0.5);
        var v = a.B - a.A; var len = v.Length;
        var u = len > 0 ? v / len : new Vector(1, 0);
        var n = new Vector(-u.Y, u.X);

        Line Seg(Point p, Point q) => new() { X1 = p.X, Y1 = p.Y, X2 = q.X, Y2 = q.Y, Stroke = brush, StrokeThickness = th };
        host.Children.Add(Seg(a.A, a.B));
        host.Children.Add(Seg(a.A + n * 6, a.A - n * 6));
        host.Children.Add(Seg(a.B + n * 6, a.B - n * 6));

        double dxp = Math.Abs(v.X) * ScaleX, dyp = Math.Abs(v.Y) * ScaleY;
        var total = Math.Sqrt(dxp * dxp + dyp * dyp);
        var text = dxp >= 1 && dyp >= 1 ? $"{total:0} px  ({dxp:0} × {dyp:0})" : $"{total:0} px";
        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 20, 20, 20)), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1),
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold },
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // nhãn đặt lệch khỏi đoạn thẳng về phía "trên" để không che nét
        var off = n.Y > 0 ? -n : n;
        var mid = new Point((a.A.X + a.B.X) / 2, (a.A.Y + a.B.Y) / 2) + off * (10 + label.DesiredSize.Height / 2);
        Canvas.SetLeft(label, mid.X - label.DesiredSize.Width / 2); Canvas.SetTop(label, mid.Y - label.DesiredSize.Height / 2);
        host.Children.Add(label);
    }

    // ───────────────────────── Text inline ─────────────────────────

    private int NextNumber() => Layers.Select(l => (Ann)l.Tag).Where(x => x.Kind == "Number").Select(x => x.Label).DefaultIfEmpty(0).Max() + 1;

    /// <summary>Mở ô nhập số ngay trên vòng tròn đánh số để sửa.</summary>
    private void StartNumber(FrameworkElement host)
    {
        var a = (Ann)host.Tag;
        var t = TranslateOf(host);
        var cx = Canvas.GetLeft(host) + t.X; var cy = Canvas.GetTop(host) + t.Y;
        var d = 20 + a.Thick * 2.5;
        var brush = Frozen(a.Color);
        var ed = new TextBox
        {
            Text = a.Label.ToString(), MaxLength = 3, Width = Math.Max(52, d + 20), FontSize = d * 0.5 + 2, FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center, Padding = new Thickness(2),
            Foreground = brush, CaretBrush = brush, BorderBrush = brush, BorderThickness = new Thickness(1.5),
            Background = Frozen(Color.FromRgb(255, 255, 224), 235),
        };
        ed.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        ed.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space) e.Handled = true;
            else if (e.Key == Key.Escape) { CancelText(); e.Handled = true; }
            else if (e.Key == Key.Enter) { CommitText(); e.Handled = true; }
        };
        ed.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_editor, ed)) CommitText(); }, DispatcherPriority.Background);
        host.Visibility = Visibility.Collapsed;
        _editTarget = host;
        Canvas.SetLeft(ed, cx - ed.Width / 2); Canvas.SetTop(ed, cy - ed.FontSize * 0.95);
        Ink.Children.Add(ed);
        _editor = ed;
        Dispatcher.BeginInvoke(() => { if (!ReferenceEquals(_editor, ed)) return; ed.Focus(); Keyboard.Focus(ed); ed.SelectAll(); }, DispatcherPriority.Input);
        UpdateOutline();
    }

    private FrameworkElement? _editTarget;   // layer chữ đang được sửa (ẩn tạm trong lúc mở ô nhập)

    private void StartText(Point p, TextBlock? existing = null)
    {
        var fa = existing?.Tag as Ann;
        var color = fa?.Color ?? _color;
        var brush = Frozen(color);
        var ed = new TextBox
        {
            FontSize = existing?.FontSize ?? 12 + _thick * 4,
            FontFamily = new FontFamily(fa?.Font ?? _font),
            FontWeight = WeightOf(fa?.Weight ?? _weight),
            FontStyle = (fa?.Italic ?? _italic) ? FontStyles.Italic : FontStyles.Normal,
            Foreground = brush, CaretBrush = brush, BorderBrush = brush, BorderThickness = new Thickness(1),
            Background = Frozen(Color.FromRgb(255, 255, 224), 215), Padding = new Thickness(2),
            AcceptsReturn = true, MinWidth = 60,
        };
        if (existing is not null)
        {
            ed.Text = existing.Text;
            var t = TranslateOf(existing);
            p = new Point(Canvas.GetLeft(existing) + t.X, Canvas.GetTop(existing) + t.Y);
            existing.Visibility = Visibility.Collapsed;
            _editTarget = existing;
        }
        ed.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CancelText(); e.Handled = true; }
            else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { CommitText(); e.Handled = true; }
        };
        ed.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() => { if (ReferenceEquals(_editor, ed)) CommitText(); }, DispatcherPriority.Background);
        Canvas.SetLeft(ed, p.X); Canvas.SetTop(ed, p.Y);
        Ink.Children.Add(ed);
        _editor = ed;
        Dispatcher.BeginInvoke(() => { if (!ReferenceEquals(_editor, ed)) return; ed.Focus(); Keyboard.Focus(ed); if (existing is not null) ed.SelectAll(); }, DispatcherPriority.Input);
        UpdateOutline();
    }

    private void CommitText()
    {
        var ed = _editor;
        if (ed is null) return;
        _editor = null;
        Ink.Children.Remove(ed);

        if (_editTarget is Canvas { Tag: Ann { Kind: "Number" } } numHost)
        {
            _editTarget = null;
            numHost.Visibility = Visibility.Visible;
            if (int.TryParse(ed.Text, out var num) && num >= 0) { ((Ann)numHost.Tag).Label = num; ApplyStyle(numHost); }
            RecordHistory(); UpdateOutline(); RefreshLayers(); Focus();
            return;
        }

        if (_editTarget is TextBlock target)
        {
            _editTarget = null;
            target.Visibility = Visibility.Visible;
            if (string.IsNullOrWhiteSpace(ed.Text))
            {
                _selection.Remove(target);
                if (ReferenceEquals(_primary, target)) _primary = _selection.LastOrDefault();
                Ink.Children.Remove(target);
            }
            else target.Text = ed.Text;
            RecordHistory(); UpdateOutline(); RefreshLayers(); Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(ed.Text)) { Focus(); return; }
        var tb = new TextBlock
        {
            Text = ed.Text, FontWeight = ed.FontWeight,
            Background = ed.Background, Padding = new Thickness(3),
            Tag = NewAnn("Text"),
        };
        ApplyStyle(tb);
        Canvas.SetLeft(tb, Canvas.GetLeft(ed)); Canvas.SetTop(tb, Canvas.GetTop(ed));
        Ink.Children.Add(tb);
        Commit(tb);
        Focus();
    }

    private void CancelText()
    {
        var ed = _editor;
        if (ed is null) return;
        _editor = null;
        Ink.Children.Remove(ed);
        if (_editTarget is not null) { _editTarget.Visibility = Visibility.Visible; _editTarget = null; }
        UpdateOutline();
        Focus();
    }

    // ───────────────────────── Thao tác nhanh trên object: nhân bản / xoay / lật ─────────────────────────

    private static bool IsEffectLayer(FrameworkElement el) => el.Tag is Ann { Kind: "Blur" or "Invert" or "Magnifier" };

    /// <summary>Tâm của layer trong hệ tọa độ cục bộ (trước RenderTransform) — làm tâm xoay/lật.</summary>
    private static Point LocalCenter(FrameworkElement el)
    {
        var local = el is TextBlock or Rectangle or Ellipse
            ? new Rect(0, 0, el.ActualWidth, el.ActualHeight)
            : VisualTreeHelper.GetDescendantBounds(el);
        return local.IsEmpty ? new Point() : new Point(local.X + local.Width / 2, local.Y + local.Height / 2);
    }

    private IEnumerable<FrameworkElement> TransformTargets() =>
        _selection.Where(e => Ink.Children.Contains(e) && e.Tag is Ann && !IsEffectLayer(e)).ToList();

    private void Transform(Action<RotateTransform, ScaleTransform> op)
    {
        CommitText();
        Ink.UpdateLayout();
        foreach (var el in TransformTargets())
        {
            var g = GroupOf(el);
            var rot = (RotateTransform)g.Children[0]; var sc = (ScaleTransform)g.Children[1];
            var c = LocalCenter(el);
            rot.CenterX = sc.CenterX = c.X; rot.CenterY = sc.CenterY = c.Y;
            op(rot, sc);
        }
        RecordHistory(); UpdateOutline();
    }

    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 15.0 : 90.0;
        // Khi layer đang bị lật, quay theo chiều ngược lại để trên màn hình vẫn là quay thuận chiều kim đồng hồ.
        Transform((rot, sc) => rot.Angle += sc.ScaleX * sc.ScaleY < 0 ? -step : step);
    }

    private void FlipH_Click(object sender, RoutedEventArgs e) => Transform((_, sc) => sc.ScaleX = -sc.ScaleX);
    private void FlipV_Click(object sender, RoutedEventArgs e) => Transform((_, sc) => sc.ScaleY = -sc.ScaleY);

    private void Dup_Click(object sender, RoutedEventArgs e) => DuplicateSelected();

    private void DuplicateSelected()
    {
        CommitText();
        var sources = _selection.Where(Ink.Children.Contains).OrderBy(e => Ink.Children.IndexOf(e)).ToList();
        var clones = new List<FrameworkElement>();
        foreach (var src in sources)
        {
            var c = CloneLayer(src);
            if (c is null) continue;
            Ink.Children.Insert(Ink.Children.IndexOf(src) + 1, c);
            clones.Add(c);
        }
        if (clones.Count == 0) return;
        _selection.Clear(); _selection.AddRange(clones);
        _anchor = clones[0];
        SetPrimary(clones[^1]); RecordHistory();
    }

    /// <summary>Tạo bản sao của một layer (cùng thuộc tính, cùng phép xoay/lật), lệch nhẹ để nhìn thấy.</summary>
    private FrameworkElement? CloneLayer(FrameworkElement el)
    {
        if (el.Tag is not Ann src) return null;
        var a = src.Copy(shareCompletedPoints: true); a.Id = Guid.NewGuid(); a.Name = null;
        FrameworkElement c;
        switch (el)
        {
            case Path: c = new Path(); break;
            case Rectangle r: c = new Rectangle { Width = r.Width, Height = r.Height }; break;
            case TextBlock t: c = new TextBlock { Text = t.Text, FontWeight = t.FontWeight, Background = t.Background, Padding = t.Padding }; break;
            case Canvas:
                c = new Canvas();
                if (a.Kind == "Number") a.Label = NextNumber();
                break;
            default: return null;
        }
        c.Tag = a;
        c.Visibility = el.Visibility;
        var left = Canvas.GetLeft(el); var top = Canvas.GetTop(el);
        if (!double.IsNaN(left)) Canvas.SetLeft(c, left);
        if (!double.IsNaN(top)) Canvas.SetTop(c, top);
        ApplyStyle(c);

        var g0 = GroupOf(el); var g1 = GroupOf(c);
        var r0 = (RotateTransform)g0.Children[0]; var s0 = (ScaleTransform)g0.Children[1]; var t0 = (TranslateTransform)g0.Children[2];
        var r1 = (RotateTransform)g1.Children[0]; var s1 = (ScaleTransform)g1.Children[1]; var t1 = (TranslateTransform)g1.Children[2];
        r1.Angle = r0.Angle; r1.CenterX = r0.CenterX; r1.CenterY = r0.CenterY;
        s1.ScaleX = s0.ScaleX; s1.ScaleY = s0.ScaleY; s1.CenterX = s0.CenterX; s1.CenterY = s0.CenterY;
        t1.X = t0.X + 16; t1.Y = t0.Y + 16;

        if (c is Rectangle fx && a.Kind is "Blur" or "Invert") ApplyEffect(fx);
        if (c is Path mp && a.Kind == "Magnifier") ApplyMagnifier(mp, a);
        return c;
    }

    // ───────────────────────── Hoàn tác / làm lại / xuất ảnh ─────────────────────────

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        if (_drag != Drag.None || _outputBusy) return;
        CommitText();
        if (_history.TryUndo(out var state)) RestoreHistory(state);
    }

    private void Redo()
    {
        if (_drag != Drag.None || _outputBusy) return;
        CommitText();
        if (_history.TryRedo(out var state)) RestoreHistory(state);
    }

    private BitmapSource RenderFinal()
    {
        CommitText();
        var sx = ScaleX; var sy = ScaleY;
        var px = Math.Clamp((int)Math.Round(_sel.X * sx), 0, _source.PixelWidth - 1);
        var py = Math.Clamp((int)Math.Round(_sel.Y * sy), 0, _source.PixelHeight - 1);
        var pw = Math.Clamp((int)Math.Round(_sel.Width * sx), 1, _source.PixelWidth - px);
        var ph = Math.Clamp((int)Math.Round(_sel.Height * sy), 1, _source.PixelHeight - py);

        var crop = new CroppedBitmap(_source, new Int32Rect(px, py, pw, ph));
        var w = Root.ActualWidth; var h = Root.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(crop, new Rect(0, 0, pw, ph));
            // DIP cửa sổ -> pixel ảnh, dịch gốc về góc trái-trên vùng chọn
            dc.PushTransform(new ScaleTransform(pw / _sel.Width, ph / _sel.Height));
            dc.PushTransform(new TranslateTransform(-_sel.X, -_sel.Y));
            dc.PushClip(new RectangleGeometry(_sel));
            var brush = new VisualBrush(Ink)
            {
                Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h),
                ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, w, h),
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        var bmp = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static async Task CopyToClipboardAsync(BitmapSource bmp)
    {
        for (var i = 0; ; i++)
        {
            try { System.Windows.Clipboard.SetImage(bmp); return; }
            catch (COMException) when (i < 5) { await Task.Delay(60); }
        }
    }

    private void ReportOutputError(string code, string message, Exception ex)
    {
        ErrorLog.Write(code, ex);
        System.Windows.MessageBox.Show(this, $"[{code}] {message}", AppInfo.AppName);
    }

    private async Task OutputAsync(string? path, bool overwrite, bool copy)
    {
        if (!_hasSel || _outputBusy || _drag != Drag.None) return;
        CommitText();
        RecordHistory();
        _outputBusy = true;
        IsEnabled = false;
        string code = ErrorCodes.Render;
        try
        {
            var bmp = RenderFinal();
            if (path is not null)
            {
                code = ErrorCodes.Export;
                await Task.Run(() => ImageExportService.Save(bmp, path, overwrite));
            }
            if (copy)
            {
                code = ErrorCodes.Clipboard;
                try { await CopyToClipboardAsync(bmp); }
                catch (Exception ex) when (path is not null)
                {
                    ReportOutputError(code, Loc.Lang == "vi" ? "Ảnh đã lưu; không thể sao chép vào clipboard." : "Image saved; clipboard copy failed.", ex);
                }
            }
            IsEnabled = true;
            Close();
        }
        catch (Exception ex) { ReportOutputError(code, Loc.T(code == ErrorCodes.Clipboard ? "err.copy" : "err.save"), ex); }
        finally { _outputBusy = false; IsEnabled = true; }
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => DoCopy();
    private async void DoCopy() => await OutputAsync(null, false, true);

    private void QuickSave_Click(object sender, RoutedEventArgs e) => DoQuickSave();

    private async void DoQuickSave()
    {
        if (!_hasSel || _outputBusy || _drag != Drag.None) return;
        try
        {
            var settings = SettingsService.Load();
            var ext = settings.DefaultFormat == "JPG" ? ".jpg" : ".png";
            var path = AtomicFile.NewCapturePath(settings.SaveDirectory, ext, DateTime.Now);
            await OutputAsync(path, false, settings.CopyAfterCapture);
        }
        catch (Exception ex) { ReportOutputError(ErrorCodes.Export, Loc.T("err.save"), ex); }
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasSel || _outputBusy || _drag != Drag.None) return;
        try
        {
            var settings = SettingsService.Load();
            var jpg = settings.DefaultFormat == "JPG";
            var dialog = new SaveFileDialog
            {
                Title = Loc.T("dlg.saveTitle"), Filter = "PNG image|*.png|JPEG image|*.jpg",
                FilterIndex = jpg ? 2 : 1, DefaultExt = jpg ? ".jpg" : ".png",
                FileName = "VuSnap_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"),
                OverwritePrompt = true, AddExtension = true,
                InitialDirectory = Directory.Exists(settings.SaveDirectory) ? settings.SaveDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            };
            if (dialog.ShowDialog(this) != true) return;
            await OutputAsync(dialog.FileName, true, settings.CopyAfterCapture);
        }
        catch (Exception ex) { ReportOutputError(ErrorCodes.Export, Loc.T("err.save"), ex); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ───────────────────────── Phím tắt ─────────────────────────

    private bool RunShortcut(string id)
    {
        if (ShortcutHelper.ToolIds.Contains(id))
        {
            if (_cfg.HiddenTools.Contains(id)) return false;
            HideColorPanel();
            SetTool(_tool == id ? null : id);
            return true;
        }
        switch (id)
        {
            case "Copy" or "Confirm": DoCopy(); break;
            case "QuickSave": DoQuickSave(); break;
            case "SaveAs": SaveAs_Click(this, new RoutedEventArgs()); break;
            case "Undo": Undo(); break;
            case "Redo": Redo(); break;
            case "Delete": DeleteSelected(); break;
            case "SelectAll": SelectAll(); break;
            case "Duplicate": DuplicateSelected(); break;
            case "Ratio": ShowFlyout(RatioButton, "Ratio"); break;
            case "TogglePanel": Panel_Click(this, new RoutedEventArgs()); break;
            default: return false;
        }
        return true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (_outputBusy) { e.Handled = true; return; }
        if (e.OriginalSource is TextBox) return; // ô nhập chữ / hex tự xử lý phím

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.F10) { e.Handled = true; return; } // Alt dùng để resize từ tâm
        var mods = Keyboard.Modifiers;
        if (key == Key.Escape && mods == ModifierKeys.None)
        {
            if (ColorPanel.Visibility == Visibility.Visible) HideColorPanel(); else Close();
            e.Handled = true;
            return;
        }
        if (!_hasSel || _outputBusy || _drag != Drag.None) return;
        if (_keyMap.TryGetValue((mods, key), out var id) && RunShortcut(id)) e.Handled = true;
    }
}

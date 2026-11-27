using System.Diagnostics;
using System.Globalization;
using System.Text;
using Material.Icons;
using Material.Icons.WPF;
using System.Windows;
using Application = System.Windows.Application;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using VuSnap.Models;
using VuSnap.Services;
using VuSnap.Windows;

namespace VuSnap;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x5153;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private HwndSource? _source;
    private AppSettings _settings = SettingsService.Load();
    private bool _loading;
    private bool _capturing;
    private readonly Dictionary<string, TextBox> _scBoxes = new();
    private readonly TrayService _tray;
    private bool _exiting, _balloonShown;

    /// <summary>Hiện lại cửa sổ từ khay hệ thống (hoặc khi mở app lần 2).</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
        Application.Current.Shutdown();
    }

    /// <summary>Một dòng của bảng: nhãn, id bật/tắt hiển thị (null = không có), id phím tắt (null = không có).</summary>
    private sealed record Row(string Label, string? VisId, string? ScId);

    public MainWindow()
    {
        Loc.Lang = _settings.Language;
        InitializeComponent();
        Icon = AppIcon.LoadImage();
        BuildDefSwatches();
        RefreshAll();
        // Overlay có thể lưu độ phóng đại/hình dạng kính lúp lần dùng gần nhất -> đồng bộ lại khi quay về cửa sổ này
        Activated += (_, _) => SyncFromDisk();
        SourceInitialized += (_, _) => InitHotkey();
        if (_settings.StartWithWindows) AutoStart.Apply(true); // cập nhật đường dẫn exe trong registry nếu app đã được chuyển chỗ

        _tray = new TrayService();
        _tray.OpenRequested += ShowFromTray;
        _tray.CaptureRequested += StartCapture;
        _tray.DonateRequested += () => Donate_Click(this, new RoutedEventArgs());
        _tray.ExitRequested += ExitApp;
        Loc.Lang = _settings.Language; _tray.SetTexts();
        Loaded += (_, _) => MemoryTrim.Schedule(4000); // sau khi khởi động xong, trả bớt RAM tạm

        Closing += (_, e) =>
        {
            // Đóng cửa sổ: nếu bật "chạy nền" thì chỉ ẩn xuống khay hệ thống.
            if (_exiting || !_settings.RunInTray) return;
            e.Cancel = true;
            Hide();
            MemoryTrim.Schedule();
            if (!_balloonShown) { _balloonShown = true; _tray.ShowBalloon(Loc.T("tray.balloon")); }
        };
        Closed += (_, _) => { UnregisterHotkey(); _tray.Dispose(); };
    }

    // ───────────────────────── Hotkey toàn cục ─────────────────────────

    private IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    private void InitHotkey()
    {
        _source = HwndSource.FromHwnd(Hwnd);
        _source.AddHook(WndProc);
        if (!RegisterHotkey(_settings.Hotkey))
        { ErrorLog.Write(ErrorCodes.Hotkey); SetStatus($"[{ErrorCodes.Hotkey}] " + Loc.T("status.hotkeyFail")); }
    }

    private static uint ToWinMods(ModifierKeys m) =>
        ((m & ModifierKeys.Alt) != 0 ? 1u : 0) | ((m & ModifierKeys.Control) != 0 ? 2u : 0) |
        ((m & ModifierKeys.Shift) != 0 ? 4u : 0) | ((m & ModifierKeys.Windows) != 0 ? 8u : 0);

    private bool RegisterHotkey(string text)
    {
        if (!ShortcutHelper.TryParse(text, out var mods, out var key) || mods == ModifierKeys.None) return false;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return Native.RegisterHotKey(Hwnd, HotkeyId, ToWinMods(mods) | ModNoRepeat, vk);
    }

    private void UnregisterHotkey() => Native.UnregisterHotKey(Hwnd, HotkeyId);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId) { handled = true; StartCapture(); }
        return IntPtr.Zero;
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool IsModifierKey(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
          or Key.LWin or Key.RWin or Key.System or Key.ImeProcessed or Key.DeadCharProcessed or Key.None;

    private static string ActionName(string id) => Loc.T((ShortcutHelper.ToolIds.Contains(id) ? "tool." : "act.") + id);

    /// <summary>Tìm action đang dùng tổ hợp này (trừ <paramref name="except"/>); null nếu rảnh.</summary>
    private string? FindConflict(string combo, string? except)
    {
        if (except != "__global" && string.Equals(_settings.Hotkey, combo, StringComparison.OrdinalIgnoreCase)) return Loc.T("gen.hotkey");
        var other = ShortcutHelper.ToolIds.Concat(ShortcutHelper.ActionIds)
            .FirstOrDefault(a => a != except && string.Equals(ShortcutHelper.Get(_settings, a), combo, StringComparison.OrdinalIgnoreCase));
        return other is null ? null : ActionName(other);
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key)) return;
        if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None) { SetStatus(Loc.T("sc.needMod")); return; }

        var combo = ShortcutHelper.Format(mods, key);
        if (combo == _settings.Hotkey) return;
        var conflict = FindConflict(combo, "__global");
        if (conflict is not null) { SetStatus(Loc.T("sc.conflict") + conflict); return; }

        var old = _settings.Hotkey;
        UnregisterHotkey();
        if (!RegisterHotkey(combo))
        {
            RegisterHotkey(old);
            SetStatus(Loc.T("sc.regFail"));
            return;
        }
        _settings.Hotkey = combo;
        HotkeyBox.Text = ShortcutHelper.Pretty(combo);
        SetStatus("");
        SaveSettings();
    }

    // ───────────────────────── Chụp ─────────────────────────

    private async void StartCapture()
    {
        if (_capturing) return; // đang có overlay, bỏ qua hotkey bấm lần nữa
        _capturing = true;
        try
        {
            // Ẩn cửa sổ chính trước khi chụp để nó không lọt vào ảnh.
            if (WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
                await Task.Delay(250);
            }
            var (image, screen) = ScreenCaptureService.CaptureMonitorAtCursor();
            var overlay = new CaptureWindow(image, screen.Bounds);
            // Chụp bằng hotkey nên để cửa sổ chính ở trạng thái thu nhỏ, không giành focus của app đang cần dán ảnh vào.
            overlay.Closed += (_, _) => { _capturing = false; MemoryTrim.Schedule(); };
            overlay.Show();
            overlay.Activate();
        }
        catch (Exception ex)
        {
            _capturing = false;
            WindowState = WindowState.Normal;
            ErrorLog.Write(ErrorCodes.Capture, ex);
            SetStatus($"[{ErrorCodes.Capture}] " + Loc.T("status.captureError"));
        }
    }

    // ───────────────────────── Đồng bộ UI <-> settings ─────────────────────────

    private bool SaveSettings()
    {
        try { SettingsService.Save(_settings); return true; }
        catch (Exception ex) { ErrorLog.Write(ErrorCodes.SettingsWrite, ex); SetStatus($"[{ErrorCodes.SettingsWrite}]"); return false; }
    }

    private void RefreshAll()
    {
        _loading = true;
        Loc.Lang = _settings.Language;
        ApplyTheme(_settings.Theme);
        ApplyLanguage();

        SelectByTag(LangBox, _settings.Language);
        SelectByTag(ThemeBox, _settings.Theme);
        HotkeyBox.Text = ShortcutHelper.Pretty(_settings.Hotkey);
        CopyAfterBox.IsChecked = _settings.CopyAfterCapture;
        SaveDirBox.Text = _settings.SaveDirectory;
        SelectByTag(SaveFormatBox, _settings.DefaultFormat);

        foreach (var rb in new[] { PosTop, PosLeft, PosRight, PosBottom })
            rb.IsChecked = (string)rb.Tag == _settings.ToolbarPosition;
        MergeBox.IsChecked = _settings.MergeBars;
        SplitBox.IsChecked = _settings.AutoSplitBar;
        HintsBox.IsChecked = _settings.ShowShortcutHints;
        LoupeBox.IsChecked = _settings.ShowLoupe;
        StartBox.IsChecked = _settings.StartWithWindows;
        DefCornersBox.IsChecked = _settings.DefaultCorners;
        TrayBox.IsChecked = _settings.RunInTray;
        OutlineSlider.Value = Math.Clamp(_settings.OutlineOpacity, 0.1, 1) * 100;
        UpdateOutlineLabel();

        SetDefaultColor(Presets.ParseColor(_settings.DefaultColor, Colors.Red), save: false);
        DefThickSlider.Value = Math.Clamp(_settings.DefaultThickness, 1, 40);
        DefOpacitySlider.Value = Math.Clamp(_settings.DefaultOpacity, 0.05, 1) * 100;
        DefItalicBox.IsChecked = _settings.DefaultFontItalic;
        DefThickValue.Text = $"{(int)DefThickSlider.Value}";
        DefOpacityValue.Text = $"{(int)Math.Round(DefOpacitySlider.Value)}%";
        _loading = false;
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        foreach (ComboBoxItem item in box.Items)
            if ((string)item.Tag == tag) { box.SelectedItem = item; return; }
        box.SelectedIndex = 0;
    }

    /// <summary>Nội dung nút gồm icon Material + chữ.</summary>
    private static StackPanel IconText(MaterialIconKind kind, string text)
    {
        // Chữ lấy màu theo nút (kiểu TextBlock mặc định của cửa sổ sẽ ép màu tối nên phải bind lại, vd. nút Donate chữ trắng)
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1),
        });
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new MaterialIcon { Kind = kind, Width = 17, Height = 17, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center },
                label,
            },
        };
    }

    private static TextBlock Wrap(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 250 };

    /// <summary>Đặt lại toàn bộ chuỗi theo ngôn ngữ hiện tại (kể cả bảng dựng bằng code).</summary>
    private void ApplyLanguage()
    {
        Title = AppInfo.TitleWithVersion;
        SetStatus("");

        LangLabel.Text = Loc.T("gen.language");
        ThemeLabel.Text = Loc.T("gen.theme");
        foreach (ComboBoxItem item in ThemeBox.Items) item.Content = Loc.T("theme." + item.Tag);
        HotkeyLabel.Text = Loc.T("gen.hotkey");
        HotkeyBox.ToolTip = Loc.T("gen.hotkeyHint");
        ResetButton.Content = Loc.T("gen.reset");

        ImportButton.Content = IconText(MaterialIconKind.Import, Loc.T("set.import"));
        ExportButton.Content = IconText(MaterialIconKind.Export, Loc.T("set.export"));
        InfoButton.Content = IconText(MaterialIconKind.InformationOutline, Loc.T("about.info"));
        FeaturesButton.Content = IconText(MaterialIconKind.Creation, Loc.T("about.features"));
        HowToButton.Content = IconText(MaterialIconKind.HelpCircleOutline, Loc.T("about.howto"));
        DonateButton.Content = IconText(MaterialIconKind.Heart, Loc.T("about.donate"));

        PosTitle.Text = Loc.T("disp.position");
        PosTop.Content = "▲  " + Loc.T("pos.Top"); PosBottom.Content = "▼  " + Loc.T("pos.Bottom");
        PosLeft.Content = "◀"; PosRight.Content = "▶";
        PosLeft.ToolTip = Loc.T("pos.Left"); PosRight.ToolTip = Loc.T("pos.Right");
        PosCenter.Text = Loc.T("disp.selection");
        MergeBox.Content = Wrap(Loc.T("disp.merge"));
        SplitBox.Content = Wrap(Loc.T("disp.split"));
        HintsBox.Content = Wrap(Loc.T("disp.showHints"));
        LoupeBox.Content = Wrap(Loc.T("disp.loupe"));
        OutlineTitle.Text = Loc.T("disp.outline");
        SaveTitle.Text = Loc.T("disp.saving");
        SaveDirLabel.Text = Loc.T("save.dir");
        SaveFormatLabel.Text = Loc.T("save.format");
        BrowseButton.Content = IconText(MaterialIconKind.FolderOpenOutline, Loc.T("save.browse"));
        OpenDirButton.Content = Loc.T("save.open");
        CopyAfterBox.Content = Wrap(Loc.T("gen.copyAfter"));

        DefTitle.Text = Loc.T("def.title");
        DefColorLabel.Text = Loc.T("def.color");
        DefThickLabel.Text = Loc.T("def.thick");
        DefOpacityLabel.Text = Loc.T("def.opacity");
        DefStrokeLabel.Text = Loc.T("def.stroke");
        DefAspectLabel.Text = Loc.T("def.aspect");
        DefStrokeBox.Items.Clear();
        foreach (var id in Presets.StrokeIds) DefStrokeBox.Items.Add(new ComboBoxItem { Tag = id, Content = Loc.T("stroke." + id) });
        DefAspectBox.Items.Clear();
        foreach (var id in Presets.RatioIds) DefAspectBox.Items.Add(new ComboBoxItem { Tag = id, Content = id == "Free" ? Loc.T("ratio.Free") : id });
        DefZoomLabel.Text = Loc.T("def.zoom");
        DefLensLabel.Text = Loc.T("def.lens");
        BuildZoomItems();
        SysTitle.Text = Loc.T("sys.title");
        StartBox.Content = Wrap(Loc.T("sys.autostart"));
        TrayBox.Content = Wrap(Loc.T("sys.tray"));
        _tray?.SetTexts();
        DefArrowLabel.Text = Loc.T("def.arrow");
        DefCornersBox.Content = Wrap(Loc.T("def.corners"));
        DefArrowBox.Items.Clear();
        foreach (var id in Presets.ArrowIds) DefArrowBox.Items.Add(new ComboBoxItem { Tag = id, Content = Loc.T("arrow." + id) });
        SelectByTag(DefArrowBox, _settings.DefaultArrow);
        DefFontLabel.Text = Loc.T("def.font");
        DefWeightLabel.Text = Loc.T("def.weight");
        DefWeightBox.Items.Clear();
        foreach (var id in Presets.WeightIds) DefWeightBox.Items.Add(new ComboBoxItem { Tag = id, Content = Loc.T("weight." + id) });
        SelectByTag(DefWeightBox, _settings.DefaultFontWeight);
        DefLineTypeLabel.Text = Loc.T("def.linetype");
        DefLineTypeBox.Items.Clear();
        foreach (var id in Presets.LineTypeIds) DefLineTypeBox.Items.Add(new ComboBoxItem { Tag = id, Content = Loc.T("line." + id) });
        SelectByTag(DefLineTypeBox, _settings.DefaultLineType);
        DefItalicBox.Content = Loc.T("def.italic");
        if (_fontsLoaded) SelectByTag(DefFontBox, _settings.DefaultFont); else SetFontItem(_settings.DefaultFont);
        DefLensBox.Items.Clear();
        DefLensBox.Items.Add(new ComboBoxItem { Tag = "Circle", Content = Loc.T("mag.round") });
        DefLensBox.Items.Add(new ComboBoxItem { Tag = "Square", Content = Loc.T("mag.square") });
        SelectByTag(DefLensBox, _settings.DefaultLens);
        SelectByTag(DefStrokeBox, _settings.DefaultStroke);
        SelectByTag(DefAspectBox, _settings.DefaultAspect);

        TableTitle.Text = Loc.T("tbl.title");
        TableHint.Text = Loc.T("sc.hint");
        ColName.Text = Loc.T("tbl.name"); ColShow.Text = Loc.T("tbl.show"); ColKey.Text = Loc.T("tbl.key");
        ResetShortcutsButton.Content = Loc.T("sc.reset");
        ResetOrderButton.Content = Loc.T("tbl.resetorder");
        SearchPlaceholder.Text = Loc.T("tbl.search");
        SearchClear.Content = new MaterialIcon { Kind = MaterialIconKind.Close, Width = 15, Height = 15 };
        BuildTable();
    }

    // ───────────────────────── Bảng: Tên | Hiển thị | Phím tắt ─────────────────────────

    private IEnumerable<(string Title, Row[] Rows)> TableGroups()
    {
        yield return (Loc.T("tbl.tools"),
            ShortcutHelper.OrderedTools(_settings).Select(id => new Row(Loc.T("tool." + id), id, id)).ToArray());
        yield return (Loc.T("tbl.toolbar"), new[]
        {
            new Row(Loc.T("ov.color"), "Color", null),
            new Row(Loc.T("obj.bar"), "ObjBar", null),
            new Row(Loc.T("act.Ratio"), "Ratio", "Ratio"),
            new Row(Loc.T("act.TogglePanel"), "Panel", "TogglePanel"),
            new Row(Loc.T("act.Redo"), "Redo", "Redo"),
            new Row(Loc.T("act.Undo"), "Undo", "Undo"),
        });
        yield return (Loc.T("tbl.actionbar"), new[]
        {
            new Row(Loc.T("act.Cancel"), "Cancel", null),
            new Row(Loc.T("act.SaveAs"), "SaveAs", "SaveAs"),
            new Row(Loc.T("act.QuickSave"), "QuickSave", "QuickSave"),
            new Row(Loc.T("act.Copy"), "Copy", "Copy"),
            new Row(Loc.T("act.Confirm"), "Confirm", "Confirm"),
        });
        yield return (Loc.T("tbl.other"), new[]
        {
            new Row(Loc.T("act.Delete"), null, "Delete"),
            new Row(Loc.T("act.SelectAll"), null, "SelectAll"),
            new Row(Loc.T("act.Duplicate"), null, "Duplicate"),
        });
    }

    /// <summary>Bỏ dấu + hạ chữ thường để tìm kiếm không phân biệt dấu (đ -> d).</summary>
    private static string Fold(string text)
    {
        var d = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant();
    }

    private bool RowMatches(Row r, string q) =>
        Fold(r.Label).Contains(q) ||
        (r.VisId is { } v && Fold(v).Contains(q)) ||
        (r.ScId is { } sc && Fold(ShortcutHelper.Pretty(ShortcutHelper.Get(_settings, sc))).Contains(q));

    private void BuildTable()
    {
        _scBoxes.Clear();
        _toolRows.Clear();
        TableRows.Children.Clear();
        var q = Fold(SearchBox?.Text.Trim() ?? "");
        var filtering = q.Length > 0;
        foreach (var (title, rows) in TableGroups())
        {
            var shown = filtering ? rows.Where(r => RowMatches(r, q)).ToArray() : rows;
            if (shown.Length == 0) continue;

            var head = new Border
            {
                Padding = new Thickness(10, 7, 10, 7),
                Child = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12, Opacity = 0.8 },
            };
            head.SetResourceReference(Border.BackgroundProperty, "HeaderBrush");
            TableRows.Children.Add(head);

            var alt = false;
            foreach (var r in shown)
            {
                // Kéo thả đổi thứ tự chỉ dành cho nhóm Công cụ và khi không đang lọc (tránh nhầm vị trí)
                var draggable = !filtering && r.VisId is { } v && ShortcutHelper.ToolIds.Contains(v);
                TableRows.Children.Add(MakeTableRow(r, alt, draggable));
                alt = !alt;
            }
        }
        if (TableRows.Children.Count == 0)
            TableRows.Children.Add(new TextBlock { Text = Loc.T("tbl.noresult"), Opacity = 0.6, Margin = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Center });
    }

    // ── Ô tìm kiếm ──
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TableRows is null) return;
        var empty = SearchBox.Text.Length == 0;
        SearchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        SearchClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        BuildTable();
    }

    private void SearchClear_Click(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }

    // ── Kéo thả đổi thứ tự tool ──
    // ── Chọn một tool để dùng nút lên/xuống ──
    private string? _selectedTool;
    private readonly Dictionary<string, (Border Row, StackPanel Arrows, bool Alt)> _toolRows = new();

    private void SelectTool(string id)
    {
        if (_selectedTool == id) return;
        _selectedTool = id;
        foreach (var (key, (row, arrows, alt)) in _toolRows)
        {
            var sel = key == id;
            if (sel) row.SetResourceReference(Border.BackgroundProperty, "SelRowBrush");
            else if (alt) row.SetResourceReference(Border.BackgroundProperty, "RowAltBrush");
            else row.Background = Brushes.Transparent;
            arrows.Visibility = sel ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Di chuyển tool lên/xuống một vị trí trong thanh công cụ (delta = -1 lên, +1 xuống).</summary>
    private void MoveTool(string id, int delta)
    {
        var order = ShortcutHelper.OrderedTools(_settings);
        var idx = order.IndexOf(id);
        var target = idx + delta;
        if (idx < 0 || target < 0 || target >= order.Count) return;
        order.RemoveAt(idx);
        order.Insert(target, id);
        _settings.ToolOrder = order;
        SaveSettings();
        BuildTable(); // giữ nguyên tool đang chọn (_selectedTool) để bấm tiếp được
    }

    private Point _dragStart;
    private string? _dragCandidate;

    private void ToolRow_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border b || !e.Data.GetDataPresent("vusnap.tool")) return;
        e.Effects = DragDropEffects.Move; e.Handled = true;
        var after = e.GetPosition(b).Y > b.ActualHeight / 2;
        b.BorderBrush = (Brush)FindResource("AccentBrush");
        b.BorderThickness = after ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
    }

    private void ToolRow_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b) b.BorderThickness = new Thickness(0);
    }

    private void ToolRow_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border { Tag: string target } b || e.Data.GetData("vusnap.tool") is not string dragged) return;
        var after = e.GetPosition(b).Y > b.ActualHeight / 2;
        b.BorderThickness = new Thickness(0);
        e.Handled = true;
        if (dragged == target) return;

        var order = ShortcutHelper.OrderedTools(_settings);
        order.Remove(dragged);
        var idx = order.IndexOf(target) + (after ? 1 : 0);
        order.Insert(Math.Clamp(idx, 0, order.Count), dragged);
        _settings.ToolOrder = order;
        SaveSettings();
        BuildTable();
    }

    private void ResetOrder_Click(object sender, RoutedEventArgs e)
    {
        _settings.ToolOrder = new List<string>();
        SaveSettings();
        BuildTable();
    }

    private static TextBlock Dash() => new()
    {
        Text = "—", Opacity = 0.35, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    private UIElement MakeTableRow(Row r, bool alt, bool draggable)
    {
        var border = new Border { Padding = new Thickness(10, 4, 10, 4), Tag = r.VisId, Background = Brushes.Transparent };
        if (alt) border.SetResourceReference(Border.BackgroundProperty, "RowAltBrush");
        if (draggable)
        {
            border.AllowDrop = true;
            border.DragOver += ToolRow_DragOver; border.DragLeave += ToolRow_DragLeave; border.Drop += ToolRow_Drop;
        }

        var grid = new Grid { MinHeight = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

        // Cột Tên: tay nắm kéo (chỉ nhóm Công cụ) + nhãn
        var nameCell = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var grip = new MaterialIcon { Kind = MaterialIconKind.DragVertical, Width = 20, Height = 20, Opacity = draggable ? 0.45 : 0, VerticalAlignment = VerticalAlignment.Center };
        if (draggable && r.VisId is { } toolId)
        {
            grip.Cursor = Cursors.SizeAll; grip.ToolTip = Loc.T("tbl.grip"); grip.Background = Brushes.Transparent;
            grip.MouseLeftButtonDown += (_, e) => { _dragStart = e.GetPosition(null); _dragCandidate = toolId; };
            grip.MouseLeftButtonUp += (_, _) => _dragCandidate = null;
            grip.MouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate != toolId) return;
                var p = e.GetPosition(null);
                if (Math.Abs(p.X - _dragStart.X) < 4 && Math.Abs(p.Y - _dragStart.Y) < 4) return;
                _dragCandidate = null;
                DragDrop.DoDragDrop(grip, new DataObject("vusnap.tool", toolId), DragDropEffects.Move);
            };
        }
        nameCell.Children.Add(grip);
        if ((r.VisId ?? r.ScId) is { } iconId && ToolIcons.Create(iconId) is { } icon) nameCell.Children.Add(icon);
        nameCell.Children.Add(new TextBlock { Text = r.Label, ToolTip = r.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        grid.Children.Add(nameCell);
        // Cột Hiển thị
        UIElement show;
        if (r.VisId is { } vis)
        {
            var cb = new CheckBox
            {
                Tag = vis, IsChecked = !_settings.HiddenTools.Contains(vis), Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            cb.Checked += ToolCheck_Changed; cb.Unchecked += ToolCheck_Changed;
            show = cb;
        }
        else show = Dash();
        Grid.SetColumn(show, 1);
        grid.Children.Add(show);

        // Cột Phím tắt (+ nút xóa)
        if (r.ScId is { } sc)
        {
            var box = new TextBox { Tag = sc, IsReadOnly = true, IsReadOnlyCaretVisible = false, Cursor = Cursors.Arrow, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            box.PreviewKeyDown += Shortcut_PreviewKeyDown;
            Grid.SetColumn(box, 2);
            grid.Children.Add(box);
            _scBoxes[sc] = box;
            ShowShortcut(box, ShortcutHelper.Get(_settings, sc));

            var clear = new Button { Content = new MaterialIcon { Kind = MaterialIconKind.Close, Width = 15, Height = 15 }, Style = (Style)FindResource("IconButton"), ToolTip = "Clear" };
            clear.Click += (_, _) => SetShortcut(sc, "");
            Grid.SetColumn(clear, 3);
            grid.Children.Add(clear);
        }
        else
        {
            var d = Dash();
            Grid.SetColumn(d, 2);
            grid.Children.Add(d);
        }

        if (draggable && r.VisId is { } rowTool)
        {
            var order = ShortcutHelper.OrderedTools(_settings);
            var idx = order.IndexOf(rowTool);
            var iconStyle = (Style)FindResource("IconButton");
            var up = new Button { Content = new MaterialIcon { Kind = MaterialIconKind.ArrowUp, Width = 17, Height = 17 }, Style = iconStyle, Width = 26, Height = 26, ToolTip = Loc.T("tbl.up"), IsEnabled = idx > 0 };
            var down = new Button { Content = new MaterialIcon { Kind = MaterialIconKind.ArrowDown, Width = 17, Height = 17 }, Style = iconStyle, Width = 26, Height = 26, ToolTip = Loc.T("tbl.down"), IsEnabled = idx >= 0 && idx < order.Count - 1 };
            up.Click += (_, _) => MoveTool(rowTool, -1);
            down.Click += (_, _) => MoveTool(rowTool, +1);
            var arrows = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { up, down } };
            var selected = _selectedTool == rowTool;
            arrows.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            if (selected) border.SetResourceReference(Border.BackgroundProperty, "SelRowBrush");
            Grid.SetColumn(arrows, 4);
            grid.Children.Add(arrows);
            _toolRows[rowTool] = (border, arrows, alt);
            border.MouseLeftButtonDown += (_, _) => SelectTool(rowTool);
        }

        border.Child = grid;
        return border;
    }

    private void ShowShortcut(TextBox box, string combo)
    {
        box.Text = combo == "" ? Loc.T("sc.none") : ShortcutHelper.Pretty(combo);
        box.Opacity = combo == "" ? 0.55 : 1;
    }

    private void SetShortcut(string id, string combo)
    {
        _settings.Shortcuts[id] = combo;
        if (_scBoxes.TryGetValue(id, out var box)) ShowShortcut(box, combo);
        SetStatus("");
        SaveSettings();
    }

    private void Shortcut_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender; var id = (string)box.Tag;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key)) return;
        if (key == Key.Escape) { Keyboard.ClearFocus(); return; }

        var combo = ShortcutHelper.Format(Keyboard.Modifiers, key);
        var conflict = FindConflict(combo, id);
        if (conflict is not null) { SetStatus(Loc.T("sc.conflict") + conflict); return; }
        SetShortcut(id, combo);
    }

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
    {
        _settings.Shortcuts = ShortcutHelper.Defaults();
        foreach (var id in ShortcutHelper.ToolIds.Concat(ShortcutHelper.ActionIds))
            if (ShortcutHelper.TryParse(_settings.Hotkey, out var gm, out var gk) && ShortcutHelper.TryParse(_settings.Shortcuts[id], out var m, out var k) && gm == m && gk == k) _settings.Shortcuts[id] = "";
        SaveSettings();
        BuildTable();
    }

    private void ToolCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string id } cb) return;
        _settings.HiddenTools.Remove(id);
        if (cb.IsChecked != true) _settings.HiddenTools.Add(id);
        SaveSettings();
    }

    // ───────────────────────── Handlers cài đặt ─────────────────────────

    private void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LangBox.SelectedItem is not ComboBoxItem item) return;
        _settings.Language = (string)item.Tag; Loc.Lang = _settings.Language;
        SaveSettings();
        _loading = true; ApplyLanguage(); _loading = false;
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem item) return;
        _settings.Theme = (string)item.Tag; ApplyTheme(_settings.Theme); SaveSettings();
    }

    // ───────────────────────── Thư mục lưu ảnh ─────────────────────────

    private void SaveDir_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplySaveDir(); Keyboard.ClearFocus(); e.Handled = true;
    }

    private void SaveDir_LostFocus(object sender, RoutedEventArgs e) => ApplySaveDir();

    /// <summary>Lưu đường dẫn đang nhập; để trống thì quay về thư mục mặc định, đường dẫn sai thì giữ giá trị cũ.</summary>
    private void ApplySaveDir()
    {
        if (_loading) return;
        var text = Environment.ExpandEnvironmentVariables(SaveDirBox.Text.Trim().Trim('"'));
        if (text.Length == 0) text = new AppSettings().SaveDirectory;
        try { text = Path.GetFullPath(text); }
        catch
        {
            SaveDirBox.Text = _settings.SaveDirectory;
            SetStatus(Loc.T("save.invalid"));
            return;
        }
        _settings.SaveDirectory = text;
        SaveDirBox.Text = text;
        SaveSettings();
        SetStatus("");
    }

    private void BrowseSaveDir_Click(object sender, RoutedEventArgs e)
    {
        var current = _settings.SaveDirectory;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Loc.T("save.dir"),
            InitialDirectory = Directory.Exists(current) ? current : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dlg.ShowDialog(this) != true) return;
        SaveDirBox.Text = dlg.FolderName;
        ApplySaveDir();
    }

    private void OpenSaveDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = _settings.SaveDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void SaveFormat_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SaveFormatBox.SelectedItem is not ComboBoxItem { Tag: string f }) return;
        _settings.DefaultFormat = f; SaveSettings();
    }

    private void CopyAfter_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.CopyAfterCapture = CopyAfterBox.IsChecked == true; SaveSettings();
    }

    private void Pos_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string tag }) return;
        _settings.ToolbarPosition = tag; SaveSettings();
    }

    private void Display_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.MergeBars = MergeBox.IsChecked == true;
        _settings.AutoSplitBar = SplitBox.IsChecked == true;
        _settings.ShowShortcutHints = HintsBox.IsChecked == true;
        _settings.ShowLoupe = LoupeBox.IsChecked == true;
        SaveSettings();
    }

    private void OutlineSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OutlineValue is null) return;
        UpdateOutlineLabel();
        if (_loading) return;
        _settings.OutlineOpacity = Math.Round(OutlineSlider.Value) / 100; SaveSettings();
    }

    private void UpdateOutlineLabel()
    {
        OutlineValue.Text = $"{(int)Math.Round(OutlineSlider.Value)}%";
        OutlinePreview.Opacity = OutlineSlider.Value / 100;
    }

    // ───────────────────────── Giá trị mặc định (màu / độ dày / độ đậm / kiểu nét / tỉ lệ) ─────────────────────────

    private static readonly Color[] DefPalette =
    {
        Color.FromRgb(255, 0, 0), Color.FromRgb(255, 140, 0), Color.FromRgb(255, 214, 0), Color.FromRgb(126, 211, 33),
        Color.FromRgb(0, 160, 80), Color.FromRgb(0, 200, 220), Color.FromRgb(0, 0, 255), Color.FromRgb(130, 60, 220),
        Color.FromRgb(255, 60, 170), Color.FromRgb(0, 0, 0), Color.FromRgb(128, 128, 128), Color.FromRgb(255, 255, 255),
    };

    private void BuildDefSwatches()
    {
        foreach (var c in DefPalette)
        {
            var color = c;
            var dot = new Ellipse
            {
                Width = 22, Height = 22, Margin = new Thickness(2), Cursor = Cursors.Hand, Tag = color,
                Fill = new SolidColorBrush(color), Stroke = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)), StrokeThickness = 1,
            };
            dot.MouseLeftButtonDown += (_, _) => SetDefaultColor(color);
            DefSwatches.Children.Add(dot);
        }
    }

    private void SetDefaultColor(Color c, bool save = true)
    {
        DefColorPreview.Background = new SolidColorBrush(c);
        if (!DefHex.IsKeyboardFocused) DefHex.Text = Presets.ToHex(c);
        foreach (Ellipse e in DefSwatches.Children)
        {
            var on = (Color)e.Tag == c;
            e.Stroke = on ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromArgb(70, 128, 128, 128));
            e.StrokeThickness = on ? 2.5 : 1;
        }
        if (_loading || !save) return;
        _settings.DefaultColor = Presets.ToHex(c);
        SaveSettings();
    }

    private void ApplyDefHex()
    {
        var c = Presets.ParseColor(DefHex.Text, Presets.ParseColor(_settings.DefaultColor, Colors.Red));
        SetDefaultColor(c);
        DefHex.Text = Presets.ToHex(c);
    }

    private void DefHex_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ApplyDefHex(); Keyboard.ClearFocus(); e.Handled = true;
    }

    private void DefHex_LostFocus(object sender, RoutedEventArgs e) { if (!_loading) ApplyDefHex(); }

    private void DefThick_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DefThickValue is null) return;
        DefThickValue.Text = $"{(int)DefThickSlider.Value}";
        if (_loading) return;
        _settings.DefaultThickness = DefThickSlider.Value; SaveSettings();
    }

    private void DefOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DefOpacityValue is null) return;
        DefOpacityValue.Text = $"{(int)Math.Round(DefOpacitySlider.Value)}%";
        if (_loading) return;
        _settings.DefaultOpacity = Math.Round(DefOpacitySlider.Value) / 100; SaveSettings();
    }

    private void DefStroke_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefStrokeBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _settings.DefaultStroke = id; SaveSettings();
    }

    private static string ZoomTag(double z) => z.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Các mức zoom có sẵn + giá trị đang lưu (có thể lẻ do lăn chuột ở overlay).</summary>
    private void BuildZoomItems()
    {
        var values = new List<double> { 2, 3, 4, 6 };
        if (!values.Any(v => Math.Abs(v - _settings.DefaultZoom) < 0.001)) values.Add(_settings.DefaultZoom);
        values.Sort();
        DefZoomBox.Items.Clear();
        foreach (var z in values) DefZoomBox.Items.Add(new ComboBoxItem { Tag = ZoomTag(z), Content = $"{ZoomTag(z)}×" });
        SelectByTag(DefZoomBox, ZoomTag(_settings.DefaultZoom));
    }

    private void SyncFromDisk()
    {
        var d = SettingsService.Load();
        // Overlay lưu độ phóng đại / hình dạng kính lúp / lưới lần dùng gần nhất: lấy lại để lần ghi cài đặt sau không đè mất.
        _settings.ShowGrid = d.ShowGrid; _settings.GridSize = d.GridSize; _settings.GridType = d.GridType;
        if (Math.Abs(d.DefaultZoom - _settings.DefaultZoom) < 0.001 && d.DefaultLens == _settings.DefaultLens) return;
        _settings.DefaultZoom = d.DefaultZoom; _settings.DefaultLens = d.DefaultLens;
        var was = _loading;
        _loading = true;
        BuildZoomItems();
        SelectByTag(DefLensBox, _settings.DefaultLens);
        _loading = was;
    }

    private bool _fontsLoaded;

    private void SetFontItem(string name)
    {
        DefFontBox.Items.Clear();
        DefFontBox.Items.Add(new ComboBoxItem { Content = name, Tag = name, FontFamily = new FontFamily(name) });
        DefFontBox.SelectedIndex = 0;
    }

    /// <summary>Chỉ liệt kê font hệ thống khi mở danh sách lần đầu (tránh làm chậm lúc khởi động).</summary>
    private void DefFontBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (_fontsLoaded) return;
        _fontsLoaded = true;
        var was = _loading; _loading = true;
        DefFontBox.Items.Clear();
        foreach (var name in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
            DefFontBox.Items.Add(new ComboBoxItem { Content = name, Tag = name, FontFamily = new FontFamily(name) });
        SelectByTag(DefFontBox, _settings.DefaultFont);
        _loading = was;
    }

    private void DefFont_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefFontBox.SelectedItem is not ComboBoxItem { Tag: string name }) return;
        _settings.DefaultFont = name; SaveSettings();
    }

    private void DefFontStyle_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.DefaultFontItalic = DefItalicBox.IsChecked == true;
        SaveSettings();
    }

    private void DefWeight_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefWeightBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _settings.DefaultFontWeight = id; SaveSettings();
    }

    private void DefLineType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefLineTypeBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _settings.DefaultLineType = id; SaveSettings();
    }

    private void DefZoom_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefZoomBox.SelectedItem is not ComboBoxItem { Tag: string id } ||
            !double.TryParse(id, NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return;
        _settings.DefaultZoom = z; SaveSettings();
    }

    private void DefLens_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefLensBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _settings.DefaultLens = id; SaveSettings();
    }

    private void DefCorners_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.DefaultCorners = DefCornersBox.IsChecked == true; SaveSettings();
    }

    private void DefArrow_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefArrowBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _settings.DefaultArrow = id; SaveSettings();
    }

    private void System_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.StartWithWindows = StartBox.IsChecked == true;
        _settings.RunInTray = TrayBox.IsChecked == true;
        SaveSettings();
        AutoStart.Apply(_settings.StartWithWindows);
    }

    private void DefAspect_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefAspectBox.SelectedItem is not ComboBoxItem { Tag: string id }) return;
        _settings.DefaultAspect = id; SaveSettings();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "JSON|*.json", FileName = "vusnap-settings.json", DefaultExt = ".json" };
        if (dlg.ShowDialog(this) != true) return;
        try { SettingsService.Export(_settings, dlg.FileName); SetStatus(Loc.T("set.exportOk")); }
        catch (Exception ex) { SetStatus(ex.Message); }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        if (SettingsService.Import(dlg.FileName) is not { } imported) { SetStatus($"[{ErrorCodes.SettingsInvalid}] " + Loc.T("set.importFail")); return; }
        var previous = _settings;
        UnregisterHotkey();
        if (!RegisterHotkey(imported.Hotkey))
        {
            RegisterHotkey(previous.Hotkey);
            ErrorLog.Write(ErrorCodes.Hotkey);
            SetStatus($"[{ErrorCodes.Hotkey}] " + Loc.T("status.hotkeyFail")); return;
        }
        _settings = imported;
        if (!SaveSettings())
        {
            _settings = previous; UnregisterHotkey(); RegisterHotkey(previous.Hotkey); return;
        }
        AutoStart.Apply(_settings.StartWithWindows);
        RefreshAll(); SetStatus(Loc.T("set.importOk"));
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Loc.T("gen.resetConfirm"), AppInfo.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        UnregisterHotkey();
        _settings = new AppSettings();
        SaveSettings();
        AutoStart.Apply(_settings.StartWithWindows);
        if (!RegisterHotkey(_settings.Hotkey)) SetStatus(Loc.T("status.hotkeyFail"));
        RefreshAll();
    }

    // ───────────────────────── Thông tin / Tính năng / Cách dùng / Donate ─────────────────────────

    private void Info_Click(object sender, RoutedEventArgs e) => new AboutWindow(this).ShowDialog();

    private void Features_Click(object sender, RoutedEventArgs e)
    {
        var lines = Enumerable.Range(1, 6).Select(i => Loc.T("feat." + i)).ToList();
        new InfoListWindow(this, Loc.T("about.features"), lines, numbered: false).ShowDialog();
    }

    private void HowTo_Click(object sender, RoutedEventArgs e)
    {
        var lines = Enumerable.Range(1, 6)
            .Select(i => i == 1 ? Loc.F("how.1", ShortcutHelper.Pretty(_settings.Hotkey)) : Loc.T("how." + i))
            .ToList();
        new InfoListWindow(this, Loc.T("about.howto"), lines, numbered: true).ShowDialog();
    }

    private void Donate_Click(object sender, RoutedEventArgs e)
    {
        if (!AppInfo.OpenUrl(AppInfo.DonateUrl)) MessageBox.Show(this, Loc.T("about.openFailed"), AppInfo.AppName);
    }

    // ───────────────────────── Theme ─────────────────────────

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
        }
        catch { return true; }
    }

    private void ApplyTheme(string theme)
    {
        var dark = theme == "Dark" || (theme == "System" && !SystemUsesLightTheme());
        Resources["WindowBrush"] = new SolidColorBrush(dark ? Color.FromRgb(24, 26, 31) : Color.FromRgb(245, 246, 248));
        Resources["PanelBrush"] = new SolidColorBrush(dark ? Color.FromRgb(36, 39, 46) : Colors.White);
        Resources["TextBrush"] = new SolidColorBrush(dark ? Color.FromRgb(238, 240, 245) : Color.FromRgb(23, 32, 51));
        Resources["BorderBrushX"] = new SolidColorBrush(dark ? Color.FromRgb(62, 67, 78) : Color.FromRgb(218, 222, 229));
        Resources["RowAltBrush"] = new SolidColorBrush(dark ? Color.FromRgb(42, 45, 53) : Color.FromRgb(247, 248, 250));
        Resources["SelRowBrush"] = new SolidColorBrush(dark ? Color.FromRgb(44, 62, 102) : Color.FromRgb(227, 236, 255));
        Resources["HeaderBrush"] = new SolidColorBrush(dark ? Color.FromRgb(50, 54, 64) : Color.FromRgb(237, 240, 244));
        Resources["ButtonBrush"] = new SolidColorBrush(dark ? Color.FromRgb(58, 62, 72) : Color.FromRgb(241, 243, 246));
        Resources["InputBrush"] = new SolidColorBrush(dark ? Color.FromRgb(28, 30, 36) : Colors.White);
    }

    public static class Native
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}

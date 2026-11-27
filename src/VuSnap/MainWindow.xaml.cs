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
using CaptureMode = VuSnap.Models.CaptureMode;
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
    private CancellationTokenSource? _captureCancellation;
    private LastCaptureRegion? _lastRegion;
    private readonly Dictionary<string, TextBox> _scBoxes = new();
    private readonly TrayService _tray;
    private bool _exiting, _balloonShown;
    private string _toolFilter = "All";

    /// <summary>Hiện lại cửa sổ từ khay hệ thống (hoặc khi mở app lần 2).</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        _captureCancellation?.Cancel();
        _exiting = true;
        Close();
        Application.Current.Shutdown();
    }

    /// <summary>Một dòng của bảng: nhóm, nhãn, id bật/tắt hiển thị (null = không có), id phím tắt (null = không có).</summary>
    private sealed record Row(string Group, string Label, string? VisId, string? ScId);

    public MainWindow()
    {
        Loc.Lang = _settings.Language;
        InitializeComponent();
        Icon = AppIcon.LoadImage();
        ThemeManager.HookWindowChrome(this, () => ThemeManager.IsDark(_settings.Theme));
        BuildDefSwatches();
        RefreshAll();
        // Overlay có thể lưu độ phóng đại/hình dạng kính lúp lần dùng gần nhất -> đồng bộ lại khi quay về cửa sổ này
        Activated += (_, _) => SyncFromDisk();
        SourceInitialized += (_, _) => InitHotkey();
        if (_settings.StartWithWindows) AutoStart.Apply(true); // cập nhật đường dẫn exe trong registry nếu app đã được chuyển chỗ

        _tray = new TrayService();
        _tray.OpenRequested += ShowFromTray;
        _tray.CaptureRequested += StartCapture;
        _tray.ModeRequested += mode => StartCaptureMode(mode, _settings.CaptureDelaySeconds);
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
        Closed += (_, _) => { _exiting = true; _captureCancellation?.Cancel(); UnregisterHotkey(); _tray.Dispose(); };
    }

    // ───────────────────────── Window chrome & Navigation ─────────────────────────

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        if (HelpButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = HelpButton;
            menu.IsOpen = true;
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseWin_Click(object sender, RoutedEventArgs e) => Close();

    private void NavTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || PageCapture is null || PageTools is null || PageToolbar is null || PageGeneral is null || sender is not RadioButton { Tag: string tag }) return;
        PageCapture.Visibility = tag == "Capture" ? Visibility.Visible : Visibility.Collapsed;
        PageTools.Visibility = tag == "Tools" ? Visibility.Visible : Visibility.Collapsed;
        PageToolbar.Visibility = tag == "Toolbar" ? Visibility.Visible : Visibility.Collapsed;
        PageGeneral.Visibility = tag == "General" ? Visibility.Visible : Visibility.Collapsed;
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
        if (!ShortcutHelper.TryParseGlobal(text, out var mods, out var key)) return false;
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
        if (StatusBanner is null) return;
        StatusText.Text = text;
        StatusBanner.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        if (string.IsNullOrEmpty(text)) CancelCaptureButton.Visibility = Visibility.Collapsed;
    }

    private static bool IsModifierKey(Key k) =>
        k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
          or Key.LWin or Key.RWin or Key.System or Key.ImeProcessed or Key.DeadCharProcessed or Key.None;

    private static string ActionName(string id) => Loc.T((ShortcutHelper.ToolIds.Contains(id) ? "tool." : "act.") + id);

    /// <summary>Tìm action đang dùng tổ hợp này (trừ <paramref name="except"/>); null nếu rảnh.</summary>
    private string? FindConflict(string combo, string? except)
    {
        if (except != "__global" && ShortcutHelper.Equivalent(_settings.Hotkey, combo)) return Loc.T("gen.hotkey");
        var other = ShortcutHelper.ToolIds.Concat(ShortcutHelper.ActionIds)
            .FirstOrDefault(a => a != except && ShortcutHelper.Equivalent(ShortcutHelper.Get(_settings, a), combo));
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
        if (!ShortcutHelper.TryParseGlobal(combo, out _, out _)) { SetStatus($"[{ErrorCodes.Hotkey}] " + Loc.T("sc.regFail")); return; }
        if (ShortcutHelper.Equivalent(combo, _settings.Hotkey)) return;
        var conflict = FindConflict(combo, "__global");
        if (conflict is not null) { SetStatus(Loc.T("sc.conflict") + conflict); return; }

        var old = _settings.Hotkey;
        UnregisterHotkey();
        if (!RegisterHotkey(combo))
        {
            if (!RegisterHotkey(old)) ErrorLog.Write(ErrorCodes.Hotkey);
            SetStatus($"[{ErrorCodes.Hotkey}] " + Loc.T("sc.regFail"));
            return;
        }
        _settings.Hotkey = combo;
        if (!SaveSettings())
        {
            _settings.Hotkey = old; UnregisterHotkey();
            if (!RegisterHotkey(old)) ErrorLog.Write(ErrorCodes.Hotkey);
            UpdateHotkeyDisplay(old); return;
        }
        UpdateHotkeyDisplay(combo);
        SetStatus("");
    }

    private void UpdateHotkeyDisplay(string combo)
    {
        var pretty = ShortcutHelper.Pretty(combo);
        HotkeyBox.Text = pretty;
        HotkeyHintChip.Text = pretty;
        TrayStatusText.Text = $"{Loc.T("tray.listening")} · {pretty}";
    }

    // ───────────────────────── Chụp ─────────────────────────

    private void StartCapture() => StartCaptureMode(Enum.Parse<CaptureMode>(_settings.DefaultCaptureMode), _settings.CaptureDelaySeconds);

    private void CaptureNow_Click(object sender, RoutedEventArgs e) => StartCapture();
    private void CancelCapture_Click(object sender, RoutedEventArgs e) => _captureCancellation?.Cancel();

    private void ModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string mode }) return;
        _settings.DefaultCaptureMode = mode;
        SaveSettings();
    }

    private void DelayRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string delay }) return;
        _settings.CaptureDelaySeconds = int.Parse(delay, CultureInfo.InvariantCulture);
        SaveSettings();
    }

    private async void StartCaptureMode(CaptureMode mode, int delay)
    {
        if (_capturing) { _captureCancellation?.Cancel(); return; }
        _capturing = true;
        using var cancellation = new CancellationTokenSource();
        _captureCancellation = cancellation;
        var token = cancellation.Token;
        var priorState = WindowState;
        bool shown = false;
        try
        {
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
                await Task.Delay(250, token);
            }
            CancelCaptureButton.IsEnabled = true;
            CancelCaptureButton.Visibility = Visibility.Visible;
            for (int remaining = delay; remaining > 0; remaining--)
            {
                SetStatus(Loc.F("capture.countdown", remaining));
                await Task.Delay(1000, token);
            }
            token.ThrowIfCancellationRequested();
            // The main window may have been restored from the tray during the delay.
            if (IsVisible && WindowState != WindowState.Minimized)
            {
                WindowState = WindowState.Minimized;
                await Task.Delay(250, token);
            }
            if (mode == CaptureMode.Scrolling)
            {
                await RunScrollingCaptureAsync(token);
                return;
            }
            var request = new CaptureRequest(mode, delay, _lastRegion);
            CaptureResult result;
            bool isolated = mode is CaptureMode.ActiveWindow or CaptureMode.WindowPicker;
            if (isolated)
            {
                WindowTargets.Target? target;
                if (mode == CaptureMode.WindowPicker)
                {
                    var picker = new WindowSourcePicker(token);
                    if (picker.ShowDialog() != true) throw new OperationCanceledException();
                    target = picker.SelectedTarget;
                }
                else target = WindowTargets.Identify(WindowTargets.Foreground());
                if (target is null) throw new CaptureException("ERR_WGC_002");
                var image = await GraphicsWindowCapture.TakeAsync(target, token);
                var work = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
                var bounds = VuSnap.Core.CaptureGeometry.FitPreview(work, image.PixelWidth, image.PixelHeight);
                result = new CaptureResult(image, bounds, mode, Array.Empty<System.Drawing.Rectangle>(), null, ScreenCaptureService.Monitors());
            }
            else result = await Task.Run(() => ScreenCaptureService.Capture(request, token), token);
            token.ThrowIfCancellationRequested();
            if (mode == CaptureMode.ColorPicker)
            {
                var inspector = new ColorInspectorWindow(result.Image);
                inspector.Closed += (_, _) =>
                {
                    _capturing = false;
                    if (!_exiting && !Dispatcher.HasShutdownStarted) SyncFromDisk();
                };
                inspector.Show();
                shown = true;
                inspector.Activate();
                SetStatus("");
                return;
            }
            var overlay = new CaptureWindow(result.Image, result.Bounds, result.Mode, result.Windows, result.InitialRegion, isolated);
            if (!isolated) overlay.OutputCompleted += region => _lastRegion = new LastCaptureRegion(region, result.MonitorLayout);
            overlay.Closed += (_, _) =>
            {
                _capturing = false;
                if (!_exiting && !Dispatcher.HasShutdownStarted) SyncFromDisk();
            };
            overlay.Show();
            shown = true;
            overlay.Activate();
            SetStatus("");
        }
        catch (OperationCanceledException) { if (!_exiting) SetStatus(""); }
        catch (Exception ex)
        {
            var code = ex is CaptureException ce ? ce.Code : ErrorCodes.Capture;
            ErrorLog.Write(code, ex);
            if (!_exiting)
            {
                var key = code switch
                {
                    "ERR_CAPTURE_004" => "capture.noWindow",
                    "ERR_CAPTURE_005" => "capture.noRepeat",
                    "ERR_CAPTURE_003" => "capture.tooLarge",
                    "ERR_WGC_001" => "wgc.unsupported",
                    "ERR_WGC_002" => "wgc.unavailable",
                    "ERR_WGC_003" => "wgc.timeout",
                    "ERR_WGC_004" => "wgc.failed",
                    "ERR_WGC_005" => "wgc.resized",
                    "ERR_WGC_006" => "wgc.failed",
                    "ERR_SCROLL_001" => "scroll.invalidRegion",
                    "ERR_SCROLL_002" => "scroll.targetChanged",
                    "ERR_SCROLL_003" => "scroll.noFrame",
                    "ERR_SCROLL_004" => "scroll.failed",
                    "ERR_SCROLL_005" => "scroll.sourceLimit",
                    _ => "status.captureError"
                };
                ShowFromTray();
                SetStatus($"[{code}] " + Loc.T(key));
            }
        }
        finally
        {
            _captureCancellation = null;
            if (!_exiting)
            {
                CancelCaptureButton.IsEnabled = false;
                CancelCaptureButton.Visibility = Visibility.Collapsed;
                if (!shown && IsVisible && WindowState == WindowState.Minimized) WindowState = priorState;
            }
            if (!shown) _capturing = false;
        }
    }

    private async Task RunScrollingCaptureAsync(CancellationToken token)
    {
        var picker = new WindowSourcePicker(token) { Title = Loc.T("capture.Scrolling") };
        if (picker.ShowDialog() != true || picker.SelectedTarget is not { } target)
            throw new OperationCanceledException();
        token.ThrowIfCancellationRequested();
        var snapshot = await GraphicsWindowCapture.TakeAsync(target, token);
        if (WindowTargets.Identify(target.Handle) is not { } current || current.ProcessId != target.ProcessId)
            throw new CaptureException("ERR_SCROLL_002");
        target = current;
        ScrollCaptureGeometry.ValidateSource(snapshot.PixelWidth, snapshot.PixelHeight);
        var work = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var bounds = VuSnap.Core.CaptureGeometry.FitPreview(work, snapshot.PixelWidth, snapshot.PixelHeight);
        var selector = new CaptureWindow(snapshot, bounds, CaptureMode.Scrolling, isolated: true);
        using (token.Register(() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (selector is { IsVisible: true }) selector.Close();
        }))))
        {
            token.ThrowIfCancellationRequested();
            if (selector.ShowDialog() != true || selector.ScrollSelection is null)
                throw new OperationCanceledException();
        }
        token.ThrowIfCancellationRequested();
        var crop = selector.ScrollSelection.Value;
        ScrollCaptureGeometry.ValidateCrop(snapshot.PixelWidth, snapshot.PixelHeight, crop);
        // Release the full selection snapshot before starting the continuous capture.
        var sourceWidth = snapshot.PixelWidth; var sourceHeight = snapshot.PixelHeight;
        snapshot = null!; selector = null!;
        var session = new ScrollingCaptureWindow(target, crop, sourceWidth, sourceHeight);
        var result = await session.RunAsync(token);
        token.ThrowIfCancellationRequested();
        if (_exiting || Dispatcher.HasShutdownStarted) return;
        var preview = new ScrollPreviewWindow(result);
        using var closePreview = token.Register(() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (preview.IsVisible) preview.Close();
        })));
        preview.ShowDialog();
        SetStatus("");
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

        foreach (var rb in new[] { ModeRegion, ModeActiveWindow, ModeMonitor, ModeDesktop, ModeWindowPicker, ModeWindowHover, ModeRepeat, ModeColorPicker, ModeScrolling })
            rb.IsChecked = (string)rb.Tag == _settings.DefaultCaptureMode;
        foreach (var rb in new[] { Delay0, Delay3, Delay5, Delay10 })
            rb.IsChecked = (string)rb.Tag == _settings.CaptureDelaySeconds.ToString(CultureInfo.InvariantCulture);

        SelectByTag(LangBox, _settings.Language);
        foreach (var rb in new[] { ThemeLightRadio, ThemeDarkRadio, ThemeSystemRadio })
            rb.IsChecked = (string)rb.Tag == _settings.Theme;
        foreach (var rb in new[] { FmtPngRadio, FmtJpgRadio })
            rb.IsChecked = (string)rb.Tag == _settings.DefaultFormat;

        UpdateHotkeyDisplay(_settings.Hotkey);
        CopyAfterBox.IsChecked = _settings.CopyAfterCapture;
        SaveDirBox.Text = _settings.SaveDirectory;

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
        DefThickValue.Text = $"{(int)DefThickSlider.Value} px";
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
    private static StackPanel IconText(MaterialIconKind kind, string text, double iconSize = 16)
    {
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1),
        });
        var icon = new MaterialIcon { Kind = kind, Width = iconSize, Height = iconSize, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetBinding(MaterialIcon.ForegroundProperty, new System.Windows.Data.Binding("Foreground")
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1),
        });
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } };
    }

    /// <summary>Đặt lại toàn bộ chuỗi theo ngôn ngữ hiện tại.</summary>
    private void ApplyLanguage()
    {
        Title = AppInfo.TitleWithVersion;
        VersionBadge.Text = $"v{AppInfo.Version}";
        SetStatus("");

        HelpButton.ToolTip = Loc.T("win.help");
        MinButton.ToolTip = Loc.T("win.minimize");
        CloseButton.ToolTip = Loc.T("win.close");
        MenuAbout.Header = Loc.T("about.info");
        MenuFeatures.Header = Loc.T("about.features");
        MenuHowTo.Header = Loc.T("about.howto");
        MenuDonate.Header = Loc.T("about.donate");
        MenuSep.Visibility = MenuDonate.Visibility = string.IsNullOrEmpty(AppInfo.DonateUrl) ? Visibility.Collapsed : Visibility.Visible;

        NavCaptureLabel.Text = Loc.T("tab.capture");
        NavToolsLabel.Text = Loc.T("tab.tools");
        NavToolbarLabel.Text = Loc.T("tab.toolbar");
        NavGeneralLabel.Text = Loc.T("tab.general");

        ModeRegionText.Text = Loc.T("capture.Region");
        ModeActiveWindowText.Text = Loc.T("capture.ActiveWindowShort");
        ModeActiveWindow.ToolTip = Loc.T("capture.ActiveWindow");
        ModeMonitorText.Text = Loc.T("capture.Monitor");
        ModeDesktop.Content = Loc.T("capture.Desktop");
        ModeWindowPicker.Content = Loc.T("capture.WindowPicker");
        ModeWindowHover.Content = Loc.T("capture.WindowHover");
        ModeRepeat.Content = Loc.T("capture.Repeat");
        ModeColorPicker.Content = Loc.T("capture.ColorPicker");
        ModeScrolling.Content = Loc.T("capture.Scrolling");

        CaptureDelayLabel.Text = Loc.T("capture.delay");
        Delay0.Content = "Off";
        Delay3.Content = "3s";
        Delay5.Content = "5s";
        Delay10.Content = "10s";

        CaptureNowButton.Content = IconText(MaterialIconKind.CropFree, Loc.T("btn.capture"), 18);
        CancelCaptureButton.Content = Loc.T("capture.cancel");
        HotkeyLabel.Text = Loc.T("gen.hotkey");
        HotkeyBox.ToolTip = Loc.T("gen.hotkeyHint");
        OrPressText.Text = Loc.T("capture.orPress");
        AnywhereText.Text = Loc.T("capture.anywhere");
        Step1Text.Text = Loc.T("capture.step1");
        Step2Text.Text = Loc.T("capture.step2");
        Step3Text.Text = Loc.T("capture.step3");

        LangLabel.Text = Loc.T("gen.language");
        ThemeLabel.Text = Loc.T("gen.theme");
        ThemeLightRadio.Content = Loc.T("theme.Light");
        ThemeDarkRadio.Content = Loc.T("theme.Dark");
        ThemeSystemRadio.Content = Loc.T("theme.System");
        ResetButton.Content = Loc.T("gen.reset");

        ImportButton.Content = IconText(MaterialIconKind.Import, Loc.T("set.importShort"), 15);
        ImportButton.ToolTip = Loc.T("set.import");
        ExportButton.Content = IconText(MaterialIconKind.Export, Loc.T("set.exportShort"), 15);
        ExportButton.ToolTip = Loc.T("set.export");
        InfoButton.Content = IconText(MaterialIconKind.InformationOutline, Loc.T("about.info"), 15);
        FeaturesButton.Content = IconText(MaterialIconKind.Creation, Loc.T("about.features"), 15);
        HowToButton.Content = IconText(MaterialIconKind.HelpCircleOutline, Loc.T("about.howto"), 15);
        DonateButton.Content = IconText(MaterialIconKind.Heart, Loc.T("about.donate"), 15);

        PosTitle.Text = Loc.T("disp.position").ToUpperInvariant();
        PosTop.Content = "▲  " + Loc.T("pos.Top"); PosBottom.Content = "▼  " + Loc.T("pos.Bottom");
        PosLeft.Content = "◀"; PosRight.Content = "▶";
        PosLeft.ToolTip = Loc.T("pos.Left"); PosRight.ToolTip = Loc.T("pos.Right");
        PosCenter.Text = Loc.T("disp.selection");
        MergeLabel.Text = Loc.T("disp.merge");
        SplitLabel.Text = Loc.T("disp.split");
        HintsLabel.Text = Loc.T("disp.showHints");
        LoupeLabel.Text = Loc.T("disp.loupe");
        OutlineTitle.Text = Loc.T("disp.outline");

        SaveDirLabel.Text = Loc.T("save.dir");
        BrowseButton.Content = new MaterialIcon { Kind = MaterialIconKind.FolderOpenOutline, Width = 16, Height = 16 };
        BrowseButton.ToolTip = Loc.T("save.browse");
        OpenDirButton.Content = new MaterialIcon { Kind = MaterialIconKind.OpenInNew, Width = 16, Height = 16 };
        OpenDirButton.ToolTip = Loc.T("save.open");
        CopyAfterLabel.Text = Loc.T("gen.copyAfter");
        StartLabel.Text = Loc.T("sys.autostart");
        TrayLabel.Text = Loc.T("sys.tray");

        DefTitle.Text = Loc.T("def.title").ToUpperInvariant();
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
        _tray?.SetTexts();
        DefArrowLabel.Text = Loc.T("def.arrow");
        DefCornersBox.Content = Loc.T("def.corners");
        DefArrowBox.Items.Clear();
        foreach (var id in Presets.ArrowIds) DefArrowBox.Items.Add(new ComboBoxItem { Tag = id, Content = Loc.T("arrow." + id) });
        SelectByTag(DefArrowBox, _settings.DefaultArrow);
        DefFontLabel.Text = Loc.T("def.font");
        DefLineTypeLabel.Text = Loc.T("def.linetype");
        DefWeightBox.Items.Clear();
        foreach (var id in Presets.WeightIds) DefWeightBox.Items.Add(new ComboBoxItem { Tag = id, Content = Loc.T("weight." + id) });
        SelectByTag(DefWeightBox, _settings.DefaultFontWeight);
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

        FilterAll.Content = Loc.T("filter.all");
        FilterTools.Content = Loc.T("filter.tools");
        FilterToolbar.Content = Loc.T("filter.toolbar");
        FilterActions.Content = Loc.T("filter.actions");
        TableHint.Text = Loc.T("sc.hint");
        ResetShortcutsButton.Content = Loc.T("sc.reset");
        ResetOrderButton.Content = Loc.T("tbl.resetorder");
        SearchPlaceholder.Text = Loc.T("tbl.search");
        SearchClear.Content = new MaterialIcon { Kind = MaterialIconKind.Close, Width = 14, Height = 14 };
        UpdateHotkeyDisplay(_settings.Hotkey);
        BuildTable();
    }

    // ───────────────────────── Bảng công cụ & phím tắt (Card list) ─────────────────────────

    private IEnumerable<(string Group, string Title, Row[] Rows)> TableGroups()
    {
        yield return ("Tools", Loc.T("tbl.tools"),
            ShortcutHelper.OrderedTools(_settings).Select(id => new Row("Tools", Loc.T("tool." + id), id, id)).ToArray());
        yield return ("Toolbar", Loc.T("tbl.toolbar"), new[]
        {
            new Row("Toolbar", Loc.T("ov.color"), "Color", null),
            new Row("Toolbar", Loc.T("obj.bar"), "ObjBar", null),
            new Row("Toolbar", Loc.T("act.Ratio"), "Ratio", "Ratio"),
            new Row("Toolbar", Loc.T("act.TogglePanel"), "Panel", "TogglePanel"),
            new Row("Toolbar", Loc.T("act.Redo"), "Redo", "Redo"),
            new Row("Toolbar", Loc.T("act.Undo"), "Undo", "Undo"),
        });
        yield return ("Actions", Loc.T("tbl.actionbar"), new[]
        {
            new Row("Actions", Loc.T("act.Cancel"), "Cancel", null),
            new Row("Actions", Loc.T("act.SaveAs"), "SaveAs", "SaveAs"),
            new Row("Actions", Loc.T("act.QuickSave"), "QuickSave", "QuickSave"),
            new Row("Actions", Loc.T("act.Copy"), "Copy", "Copy"),
            new Row("Actions", Loc.T("act.Confirm"), "Confirm", "Confirm"),
            new Row("Actions", Loc.T("act.Ocr"), "Ocr", "Ocr"),
            new Row("Actions", Loc.T("act.ColorInspect"), "ColorInspect", "ColorInspect"),
            new Row("Actions", Loc.T("act.Barcode"), "Barcode", "Barcode"),
        });
        yield return ("Actions", Loc.T("tbl.other"), new[]
        {
            new Row("Actions", Loc.T("act.Delete"), null, "Delete"),
            new Row("Actions", Loc.T("act.SelectAll"), null, "SelectAll"),
            new Row("Actions", Loc.T("act.Duplicate"), null, "Duplicate"),
        });
    }

    private void ToolFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || TableRows is null || sender is not RadioButton { Tag: string filter }) return;
        _toolFilter = filter;
        BuildTable();
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
        foreach (var (group, title, rows) in TableGroups())
        {
            if (_toolFilter != "All" && group != _toolFilter) continue;
            var shown = filtering ? rows.Where(r => RowMatches(r, q)).ToArray() : rows;
            if (shown.Length == 0) continue;

            var head = new TextBlock
            {
                Text = title.ToUpperInvariant(),
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Margin = new Thickness(2, TableRows.Children.Count == 0 ? 0 : 10, 0, 6),
            };
            head.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            TableRows.Children.Add(head);

            foreach (var r in shown)
            {
                var draggable = !filtering && r.VisId is { } v && ShortcutHelper.ToolIds.Contains(v);
                TableRows.Children.Add(MakeTableRow(r, draggable));
            }
        }
        if (TableRows.Children.Count == 0)
        {
            var empty = new TextBlock { Text = Loc.T("tbl.noresult"), Margin = new Thickness(18), HorizontalAlignment = HorizontalAlignment.Center };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            TableRows.Children.Add(empty);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TableRows is null) return;
        var empty = SearchBox.Text.Length == 0;
        SearchPlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        SearchClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        BuildTable();
    }

    private void SearchClear_Click(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }

    // ── Kéo thả & di chuyển lên/xuống thứ tự tool ──
    private string? _selectedTool;
    private readonly Dictionary<string, (Border Row, StackPanel Arrows)> _toolRows = new();

    private void SelectTool(string id)
    {
        if (_selectedTool == id) return;
        _selectedTool = id;
        foreach (var (key, (row, arrows)) in _toolRows)
        {
            var sel = key == id;
            row.SetResourceReference(Border.BackgroundProperty, sel ? "SelRowBrush" : "PanelBrush");
            row.SetResourceReference(Border.BorderBrushProperty, sel ? "AccentBrush" : "BorderBrushX");
            arrows.Visibility = sel ? Visibility.Visible : Visibility.Collapsed;
        }
    }

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
        BuildTable();
    }

    private Point _dragStart;
    private string? _dragCandidate;

    private void ToolRow_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border b || !e.Data.GetDataPresent("vusnap.tool")) return;
        e.Effects = DragDropEffects.Move; e.Handled = true;
        var after = e.GetPosition(b).Y > b.ActualHeight / 2;
        b.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        b.BorderThickness = after ? new Thickness(1, 1, 1, 2.5) : new Thickness(1, 2.5, 1, 1);
    }

    private void ToolRow_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b)
        {
            b.BorderThickness = new Thickness(1);
            b.SetResourceReference(Border.BorderBrushProperty, _selectedTool == b.Tag as string ? "AccentBrush" : "BorderBrushX");
        }
    }

    private void ToolRow_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border { Tag: string target } b || e.Data.GetData("vusnap.tool") is not string dragged) return;
        var after = e.GetPosition(b).Y > b.ActualHeight / 2;
        b.BorderThickness = new Thickness(1);
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

    private UIElement MakeTableRow(Row r, bool draggable)
    {
        var border = new Border
        {
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Tag = r.VisId,
        };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "BorderBrushX");

        if (draggable)
        {
            border.AllowDrop = true;
            border.DragOver += ToolRow_DragOver; border.DragLeave += ToolRow_DragLeave; border.Drop += ToolRow_Drop;
        }

        var grid = new Grid { MinHeight = 30 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameCell = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (draggable && r.VisId is { } toolId)
        {
            var grip = new MaterialIcon { Kind = MaterialIconKind.DragVertical, Width = 18, Height = 18, Opacity = 0.45, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
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
            nameCell.Children.Add(grip);
        }
        if ((r.VisId ?? r.ScId) is { } iconId && ToolIcons.Create(iconId) is { } icon) nameCell.Children.Add(icon);
        nameCell.Children.Add(new TextBlock
        {
            Text = r.Label, ToolTip = r.Label, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold, FontSize = 12.5,
        });
        grid.Children.Add(nameCell);

        // Toggle switch hiển thị trên thanh công cụ
        if (r.VisId is { } vis)
        {
            var toggle = new CheckBox
            {
                Tag = vis,
                IsChecked = !_settings.HiddenTools.Contains(vis),
                Style = (Style)FindResource("ProToggle"),
                Margin = new Thickness(8, 0, 8, 0),
                ToolTip = Loc.T("tbl.show"),
            };
            toggle.Checked += ToolCheck_Changed; toggle.Unchecked += ToolCheck_Changed;
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);
        }

        // Keycap phím tắt + nút xóa
        if (r.ScId is { } sc)
        {
            var keyWrap = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var box = new TextBox
            {
                Tag = sc,
                Style = (Style)FindResource("KeycapBox"),
                Width = 68,
            };
            box.PreviewKeyDown += Shortcut_PreviewKeyDown;
            keyWrap.Children.Add(box);
            _scBoxes[sc] = box;
            ShowShortcut(box, ShortcutHelper.Get(_settings, sc));

            var clear = new Button
            {
                Content = new MaterialIcon { Kind = MaterialIconKind.Close, Width = 13, Height = 13 },
                Style = (Style)FindResource("IconButton"),
                Width = 24, Height = 24, Margin = new Thickness(2, 0, 0, 0), ToolTip = "Clear",
            };
            clear.Click += (_, _) => SetShortcut(sc, "");
            keyWrap.Children.Add(clear);
            Grid.SetColumn(keyWrap, 2);
            grid.Children.Add(keyWrap);
        }

        if (draggable && r.VisId is { } rowTool)
        {
            var order = ShortcutHelper.OrderedTools(_settings);
            var idx = order.IndexOf(rowTool);
            var iconStyle = (Style)FindResource("IconButton");
            var up = new Button { Content = new MaterialIcon { Kind = MaterialIconKind.ArrowUp, Width = 15, Height = 15 }, Style = iconStyle, Width = 24, Height = 24, ToolTip = Loc.T("tbl.up"), IsEnabled = idx > 0 };
            var down = new Button { Content = new MaterialIcon { Kind = MaterialIconKind.ArrowDown, Width = 15, Height = 15 }, Style = iconStyle, Width = 24, Height = 24, ToolTip = Loc.T("tbl.down"), IsEnabled = idx >= 0 && idx < order.Count - 1 };
            up.Click += (_, _) => MoveTool(rowTool, -1);
            down.Click += (_, _) => MoveTool(rowTool, +1);
            var arrows = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), Children = { up, down } };
            var selected = _selectedTool == rowTool;
            arrows.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            if (selected)
            {
                border.SetResourceReference(Border.BackgroundProperty, "SelRowBrush");
                border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            }
            Grid.SetColumn(arrows, 3);
            grid.Children.Add(arrows);
            _toolRows[rowTool] = (border, arrows);
            border.MouseLeftButtonDown += (_, _) => SelectTool(rowTool);
        }

        border.Child = grid;
        return border;
    }

    private void ShowShortcut(TextBox box, string combo)
    {
        box.Text = combo == "" ? "—" : ShortcutHelper.Pretty(combo);
        box.Opacity = combo == "" ? 0.5 : 1;
        box.ToolTip = combo == "" ? Loc.T("sc.none") : ShortcutHelper.Pretty(combo);
    }

    private void SetShortcut(string id, string combo)
    {
        var previous = _settings.Shortcuts.TryGetValue(id, out var old);
        _settings.Shortcuts[id] = combo;
        if (!SaveSettings())
        {
            if (previous) _settings.Shortcuts[id] = old!; else _settings.Shortcuts.Remove(id);
            return;
        }
        if (_scBoxes.TryGetValue(id, out var box)) ShowShortcut(box, combo);
        SetStatus("");
    }

    private void Shortcut_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender; var id = (string)box.Tag;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (IsModifierKey(key)) return;
        if (key == Key.Escape) { Keyboard.ClearFocus(); return; }

        var combo = ShortcutHelper.Format(Keyboard.Modifiers, key);
        if (!ShortcutHelper.TryParse(combo, out _, out _)) { SetStatus($"[{ErrorCodes.Hotkey}] " + Loc.T("sc.regFail")); return; }
        var conflict = FindConflict(combo, id);
        if (conflict is not null) { SetStatus(Loc.T("sc.conflict") + conflict); return; }
        SetShortcut(id, combo);
    }

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
    {
        var previous = _settings.Shortcuts;
        _settings.Shortcuts = ShortcutHelper.Defaults();
        foreach (var id in ShortcutHelper.ToolIds.Concat(ShortcutHelper.ActionIds))
            if (ShortcutHelper.TryParse(_settings.Hotkey, out var gm, out var gk) && ShortcutHelper.TryParse(_settings.Shortcuts[id], out var m, out var k) && gm == m && gk == k) _settings.Shortcuts[id] = "";
        if (!SaveSettings()) { _settings.Shortcuts = previous; return; }
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

    private void ThemeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string theme }) return;
        _settings.Theme = theme; ApplyTheme(theme); SaveSettings();
    }

    private void FormatRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string fmt }) return;
        _settings.DefaultFormat = fmt; SaveSettings();
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
        if (OutlineValue is null || OutlinePreview is null) return;
        UpdateOutlineLabel();
        if (_loading || !IsInitialized) return;
        _settings.OutlineOpacity = Math.Round(OutlineSlider.Value) / 100; SaveSettings();
    }

    private void UpdateOutlineLabel()
    {
        OutlineValue.Text = $"{(int)Math.Round(OutlineSlider.Value)}%";
        OutlinePreview.Opacity = OutlineSlider.Value / 100;
    }

    // ───────────────────────── Giá trị mặc định ─────────────────────────

    private static readonly Color[] DefPalette =
    {
        Color.FromRgb(255, 59, 92), Color.FromRgb(255, 140, 0), Color.FromRgb(255, 214, 0), Color.FromRgb(16, 185, 129),
        Color.FromRgb(111, 109, 255), Color.FromRgb(115, 150, 254), Color.FromRgb(130, 60, 220), Color.FromRgb(0, 0, 0),
        Color.FromRgb(255, 255, 255),
    };

    private void BuildDefSwatches()
    {
        foreach (var c in DefPalette)
        {
            var color = c;
            var dot = new Ellipse
            {
                Width = 20, Height = 20, Margin = new Thickness(2.5), Cursor = Cursors.Hand, Tag = color,
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
            e.Stroke = on ? new SolidColorBrush(Color.FromRgb(111, 109, 255)) : new SolidColorBrush(Color.FromArgb(70, 128, 128, 128));
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
        DefThickValue.Text = $"{(int)DefThickSlider.Value} px";
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
        if (TryReplaceSettings(imported)) SetStatus(Loc.T("set.importOk"));
    }

    private bool TryReplaceSettings(AppSettings imported)
    {
        var previous = _settings;
        UnregisterHotkey();
        if (!RegisterHotkey(imported.Hotkey))
        {
            if (!RegisterHotkey(previous.Hotkey)) ErrorLog.Write(ErrorCodes.Hotkey);
            ErrorLog.Write(ErrorCodes.Hotkey);
            SetStatus($"[{ErrorCodes.Hotkey}] " + Loc.T("status.hotkeyFail")); return false;
        }
        _settings = imported;
        if (!SaveSettings())
        {
            _settings = previous; UnregisterHotkey(); if (!RegisterHotkey(previous.Hotkey)) ErrorLog.Write(ErrorCodes.Hotkey); return false;
        }
        AutoStart.Apply(_settings.StartWithWindows);
        RefreshAll(); return true;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Loc.T("gen.resetConfirm"), AppInfo.AppName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (TryReplaceSettings(new AppSettings())) SetStatus("");
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

    private void ApplyTheme(string theme) => ThemeManager.Apply(theme);

    public static class Native
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}

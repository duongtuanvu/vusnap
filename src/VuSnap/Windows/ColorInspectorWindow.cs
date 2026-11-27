using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VuSnap.Services;
using Image = System.Windows.Controls.Image;

namespace VuSnap.Windows;

internal sealed class ColorInspectorWindow : Window
{
    private readonly BitmapSource _source;
    private readonly Image _image;
    private readonly Image _loupe = new() { Width = 126, Height = 126, Stretch = Stretch.Fill };
    private readonly Border _swatch = new() { Height = 40, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), FontSize = 12 };
    private readonly TextBlock _position = new() { Margin = new Thickness(0, 8, 0, 8), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12 };
    private readonly TextBox _hex = ValueBox(), _rgb = ValueBox(), _hsv = ValueBox();
    private readonly byte[] _pixel = new byte[4];
    private readonly CancellationTokenSource _life = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _x = -1, _y = -1;
    private long _lastUpdate = -100;
    private bool _locked, _closed;

    private static TextBox ValueBox() => new()
    {
        IsReadOnly = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = 12.5, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 0),
    };

    public ColorInspectorWindow(BitmapSource image)
    {
        _source = ColorProbe.Prepare(image);
        _image = new Image { Source = _source, Stretch = Stretch.Uniform, Focusable = true, Cursor = Cursors.Cross };
        Title = Loc.T("color.title"); Width = 1000; Height = 620; MinWidth = 540; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        ThemeManager.HookWindowChrome(this, () => ThemeManager.IsDark(SettingsService.Load().Theme));
        _swatch.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        _position.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var grid = new Grid { Margin = new Thickness(18) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(256) });

        var photo = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(20, 23, 41)),
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
            ClipToBounds = true, Child = _image,
        };
        photo.SetResourceReference(Border.BorderBrushProperty, "BorderBrushX");
        grid.Children.Add(photo);

        var panel = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        var hint = new TextBlock { Text = Loc.T("color.hint"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 12) };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        panel.Children.Add(hint);

        RenderOptions.SetBitmapScalingMode(_loupe, BitmapScalingMode.NearestNeighbor);
        var zoomBorder = new Border { Width = 128, Height = 128, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.5), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
        zoomBorder.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        var zoom = new Grid { Width = 126, Height = 126 };
        zoom.Children.Add(_loupe);
        zoom.Children.Add(new Border { Width = 14, Height = 14, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Child = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(1) } });
        zoomBorder.Child = zoom;
        panel.Children.Add(zoomBorder); panel.Children.Add(_position); panel.Children.Add(_swatch);
        AddField(panel, "HEX", _hex); AddField(panel, "RGB", _rgb); AddField(panel, "HSV", _hsv);

        var resume = new Button { Content = Loc.T("color.resume"), Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(10, 7, 10, 7), Height = 36, FontSize = 12.5 };
        resume.Style = (Style)Application.Current.FindResource("GradientButton");
        resume.Click += (_, _) => { _locked = false; _status.Text = Loc.T("color.live"); _image.Focus(); };
        panel.Children.Add(resume);
        var close = new Button { Content = Loc.T("utility.close"), IsCancel = true, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 6, 10, 6) };
        close.Click += (_, _) => Close();
        panel.Children.Add(close); panel.Children.Add(_status);
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1); grid.Children.Add(scroll); Content = grid;

        _image.MouseMove += (_, e) => { if (!_locked && _clock.ElapsedMilliseconds - _lastUpdate >= 16) SamplePoint(e.GetPosition(_image), false); };
        _image.MouseLeftButtonDown += (_, e) => { SamplePoint(e.GetPosition(_image), true); _image.Focus(); e.Handled = true; };
        _image.KeyDown += (_, e) =>
        {
            var (dx, dy) = e.Key switch { Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), Key.Down => (0, 1), _ => (0, 0) };
            if (dx == 0 && dy == 0) return;
            _locked = true;
            UpdateSample(Math.Clamp(_x + dx, 0, _source.PixelWidth - 1), Math.Clamp(_y + dy, 0, _source.PixelHeight - 1), true);
            e.Handled = true;
        };
        Loaded += (_, _) => { UpdateSample(_source.PixelWidth / 2, _source.PixelHeight / 2, true); _image.Focus(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Closed += (_, _) => { _closed = true; _life.Cancel(); _life.Dispose(); _image.Source = null; _loupe.Source = null; };
    }

    private void AddField(Panel panel, string label, TextBox box)
    {
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, FontSize = 11.5, Margin = new Thickness(0, 10, 0, 4) });
        var line = new DockPanel();
        var copy = new Button { Content = Loc.T("color.copy"), Padding = new Thickness(10, 4, 10, 4), MinHeight = 30, FontSize = 12 };
        copy.Click += async (_, _) =>
        {
            _locked = true;
            try { await UtilityClipboard.CopyAsync(box.Text, _life.Token); if (!_closed) _status.Text = Loc.T("utility.copied"); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ErrorLog.Write(ErrorCodes.Clipboard, ex); if (!_closed) _status.Text = "[" + ErrorCodes.Clipboard + "] " + Loc.T("err.copy"); }
        };
        DockPanel.SetDock(copy, Dock.Right); line.Children.Add(copy); line.Children.Add(box); panel.Children.Add(line);
    }

    private void SamplePoint(Point point, bool lockSample)
    {
        if (!ColorProbe.TryMapPoint(_source.PixelWidth, _source.PixelHeight, _image.ActualWidth, _image.ActualHeight, point.X, point.Y, out int x, out int y)) return;
        _locked = lockSample;
        UpdateSample(x, y, lockSample);
    }

    private void UpdateSample(int x, int y, bool force)
    {
        if (!force && x == _x && y == _y) return;
        try
        {
            var sample = ColorProbe.Read(_source, x, y, _pixel);
            _x = x; _y = y; _lastUpdate = _clock.ElapsedMilliseconds;
            _hex.Text = sample.Hex; _rgb.Text = sample.Rgb; _hsv.Text = sample.HsvText;
            _swatch.Background = new SolidColorBrush(Color.FromRgb(sample.R, sample.G, sample.B));
            _position.Text = $"{x}, {y}";
            const int n = 9;
            int left = Math.Max(0, x - 4), top = Math.Max(0, y - 4);
            int right = Math.Min(_source.PixelWidth - 1, x + 4), bottom = Math.Min(_source.PixelHeight - 1, y + 4);
            int width = right - left + 1, height = bottom - top + 1;
            byte[] region = new byte[width * height * 4], zoom = new byte[n * n * 4];
            _source.CopyPixels(new Int32Rect(left, top, width, height), region, width * 4, 0);
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
                {
                    int sx = Math.Clamp(x + i - 4, left, right) - left, sy = Math.Clamp(y + j - 4, top, bottom) - top;
                    Buffer.BlockCopy(region, (sy * width + sx) * 4, zoom, (j * n + i) * 4, 4);
                }
            var preview = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgr32, null, zoom, n * 4);
            preview.Freeze(); _loupe.Source = preview;
            _status.Text = Loc.T(_locked ? "color.locked" : "color.live");
        }
        catch (Exception ex) { ErrorLog.Write("ERR_COLOR_001", ex); _status.Text = "[ERR_COLOR_001] " + Loc.T("color.failed"); }
    }
}

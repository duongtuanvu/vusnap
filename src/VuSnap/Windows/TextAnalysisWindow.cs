using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VuSnap.Services;

namespace VuSnap.Windows;

internal sealed class TextAnalysisWindow : Window
{
    private readonly BitmapSource _image;
    private readonly ComboBox _languages = new() { DisplayMemberPath = "Display", MinWidth = 230, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBox _text = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 15, Padding = new Thickness(12) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _run = new() { Padding = new Thickness(14, 7, 14, 7) };
    private readonly Button _cancel = new() { Padding = new Thickness(12, 7, 12, 7), IsEnabled = false };
    private readonly Button _copy = new() { Padding = new Thickness(12, 7, 12, 7), IsEnabled = false };
    private CancellationTokenSource? _job;
    private bool _closed;
    public TextAnalysisWindow(BitmapSource image)
    {
        _image = image;
        Title = Loc.T("ocr.title"); Width = 720; Height = 520; MinWidth = 440; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = Loc.T("ocr.help"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0,0,0,12) });
        top.Children.Add(new TextBlock { Text = Loc.T("ocr.language"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,5) });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        _run.Content = Loc.T("ocr.run"); _cancel.Content = Loc.T("utility.cancel");
        _cancel.Margin = new Thickness(8,0,0,0);
        toolbar.Children.Add(_languages); toolbar.Children.Add(_run); toolbar.Children.Add(_cancel);
        top.Children.Add(toolbar); top.Children.Add(_status);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,10,0,0) };
        _copy.Content = Loc.T("utility.copy");
        var close = new Button { Content = Loc.T("utility.close"), IsCancel = true, Padding = new Thickness(12,7,12,7), Margin = new Thickness(8,0,0,0) };
        buttons.Children.Add(_copy); buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); root.Children.Add(_text); Content = root;
        _run.Click += async (_,_) => await RunAsync();
        _cancel.Click += (_,_) => _job?.Cancel();
        _copy.Click += async (_,_) => await CopyAsync();
        _text.TextChanged += (_,_) => _copy.IsEnabled = _job is null && _text.Text.Length > 0;
        Closed += (_,_) => { _closed = true; _job?.Cancel(); _text.Clear(); };
        Loaded += (_,_) =>
        {
            var options = OfflineOcrService.Options();
            _languages.ItemsSource = options;
            _languages.SelectedItem = options.FirstOrDefault(x => Loc.Lang == "vi" && x.Language == "vie+eng")
                ?? options.FirstOrDefault(x => Loc.Lang == "vi" && (x.Language == "vie" || x.Language.StartsWith("vi-")))
                ?? options.FirstOrDefault(x => x.Language.StartsWith("en-") || x.Language == "eng") ?? options.FirstOrDefault();
            _run.IsEnabled = options.Count > 0;
            if (options.Count == 0) _status.Text = "[ERR_OCR_001] " + Loc.T("ocr.unavailable");
        };
    }
    private async Task RunAsync()
    {
        if (_job is not null || _languages.SelectedItem is not OcrOption option) return;
        using var job = new CancellationTokenSource(); _job = job;
        _run.IsEnabled = _languages.IsEnabled = _copy.IsEnabled = false; _cancel.IsEnabled = true;
        _text.IsReadOnly = true; _text.Clear(); _status.Text = Loc.T("ocr.running");
        try
        {
            var result = await OfflineOcrService.RecognizeAsync(_image, option, job.Token);
            if (_closed) return;
            _text.Text = result; _status.Text = Loc.T(result.Length == 0 ? "ocr.empty" : "ocr.done");
        }
        catch (OperationCanceledException) { if (!_closed) _status.Text = Loc.T("ocr.timeout"); }
        catch (Exception ex)
        {
            var code = ex is UtilityException ue ? ue.Code : "ERR_OCR_004";
            ErrorLog.Write(code, ex);
            if (!_closed) _status.Text = "[" + code + "] " + Loc.T(code switch { "ERR_OCR_001" => "ocr.unavailable", "ERR_OCR_002" => "ocr.large", "ERR_OCR_003" => "ocr.timeout", _ => "ocr.failed" });
        }
        finally
        {
            _job = null;
            if (!_closed) { _run.IsEnabled = _languages.IsEnabled = true; _cancel.IsEnabled = false; _text.IsReadOnly = false; _copy.IsEnabled = _text.Text.Length > 0; }
        }
    }
    private async Task CopyAsync()
    {
        var value = _text.SelectionLength > 0 ? _text.SelectedText : _text.Text;
        if (value.Length == 0) return;
        try
        {
            for (int i = 0; ; i++)
            {
                try { Clipboard.SetText(value); break; }
                catch (COMException) when (i < 5) { await Task.Delay(60); }
            }
            if (!_closed) _status.Text = Loc.T("utility.copied");
        }
        catch (Exception ex) { ErrorLog.Write(ErrorCodes.Clipboard,ex); if (!_closed) _status.Text = "[" + ErrorCodes.Clipboard + "] " + Loc.T("err.copy"); }
    }
}

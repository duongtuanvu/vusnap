using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using VuSnap.Services;

namespace VuSnap.Windows;

internal sealed class BarcodeResultWindow : Window
{
    private readonly BitmapSource _image;
    private readonly CancellationTokenSource _life = new();
    private readonly TextBox _text = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 15, Padding = new Thickness(10) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10) };
    private readonly Button _copy = new() { IsEnabled = false, Padding = new Thickness(12,6,12,6) };
    private bool _closed,_started;
    public BarcodeResultWindow(BitmapSource image)
    {
        _image=image; Title=Loc.T("qr.title"); Width=650; Height=390; MinWidth=380; MinHeight=250;
        WindowStartupLocation=WindowStartupLocation.CenterOwner; ShowInTaskbar=false;
        var root=new DockPanel { Margin=new Thickness(16) };
        _status.Text=Loc.T("qr.running"); DockPanel.SetDock(_status,Dock.Top); root.Children.Add(_status);
        var footer=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,10,0,0) };
        _copy.Content=Loc.T("utility.copy");
        var close=new Button { Content=Loc.T("utility.close"), IsCancel=true, Margin=new Thickness(8,0,0,0), Padding=new Thickness(12,6,12,6) };
        close.Click+=(_,_)=>Close(); footer.Children.Add(_copy); footer.Children.Add(close);
        DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer); root.Children.Add(_text); Content=root;
        Loaded+=async (_,_) => { if (!_started) { _started=true; await DecodeAsync(); } };
        _copy.Click+=async (_,_) =>
        {
            try { await UtilityClipboard.CopyAsync(_text.SelectionLength>0?_text.SelectedText:_text.Text,_life.Token); if (!_closed) _status.Text=Loc.T("utility.copied"); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ErrorLog.Write(ErrorCodes.Clipboard,ex); if (!_closed) _status.Text="["+ErrorCodes.Clipboard+"] "+Loc.T("err.copy"); }
        };
        Closed+=(_,_)=>{_closed=true;_life.Cancel();_life.Dispose();_text.Clear();};
    }
    private async Task DecodeAsync()
    {
        try
        {
            var result=await LocalBarcodeService.DecodeAsync(_image,_life.Token);
            if (_closed) return;
            if (result is null) { _status.Text=Loc.T("qr.empty"); return; }
            _text.Text=result.Text; _copy.IsEnabled=result.Text.Length>0;
            _status.Text=result.Format+" · "+Loc.T("qr.safe");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            string code=ex is UtilityException ue?ue.Code:"ERR_QR_002";
            ErrorLog.Write(code,ex);
            if (!_closed) _status.Text="["+code+"] "+Loc.T(code switch { "ERR_QR_001"=>"qr.large", "ERR_QR_003"=>"qr.busy", "ERR_QR_004"=>"qr.resultLarge", _=>"qr.failed" });
        }
    }
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using VuSnap.Services;

namespace VuSnap.Windows;

internal sealed class ScrollPreviewWindow : Window
{
    private readonly ScrollCaptureOutcome _result;
    private readonly Image _image = new() { Stretch=Stretch.Uniform, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Top };
    private readonly ScrollViewer _viewer = new() { HorizontalScrollBarVisibility=ScrollBarVisibility.Auto, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping=TextWrapping.Wrap, FontSize=12, Margin=new Thickness(0,6,0,8) };
    private readonly Button _save = new() { MinWidth=100, Height=36, Padding=new Thickness(12,6,12,6) };
    private readonly Button _copy = new() { MinWidth=100, Height=36, Padding=new Thickness(12,6,12,6), Margin=new Thickness(0,0,8,0) };
    private readonly Button _scale = new() { MinWidth=110, Height=36, Padding=new Thickness(12,6,12,6), Margin=new Thickness(0,0,8,0) };
    private bool _fit=true, _busy;

    public ScrollPreviewWindow(ScrollCaptureOutcome result)
    {
        _result=result; Title=Loc.T("scroll.preview"); Width=820; Height=740; MinWidth=420; MinHeight=360;
        WindowStartupLocation=WindowStartupLocation.CenterScreen; Icon=AppIcon.LoadImage();
        SetResourceReference(BackgroundProperty,"WindowBrush"); SetResourceReference(ForegroundProperty,"TextBrush");
        ThemeManager.HookWindowChrome(this,()=>ThemeManager.IsDark(SettingsService.Load().Theme));
        var root=new DockPanel { Margin=new Thickness(16) };
        var header=new StackPanel();
        header.Children.Add(new TextBlock { Text=Loc.T("scroll.preview"), FontSize=18, FontWeight=FontWeights.Bold });
        header.Children.Add(new TextBlock { Text=Loc.F("scroll.metrics",result.Image.PixelWidth,result.Image.PixelHeight,result.Frames), FontSize=12, Margin=new Thickness(0,6,0,0) });
        _status.Text=Loc.T(result.Message);
        _status.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush"); header.Children.Add(_status);
        DockPanel.SetDock(header,Dock.Top); root.Children.Add(header);
        var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,12,0,0) };
        _scale.Content=Loc.T("scroll.actual"); _copy.Content=Loc.T("scroll.copy"); _save.Content=Loc.T("act.SaveAs");
        _save.Style=(Style)Application.Current.FindResource("GradientButton");
        var close=new Button { Content=Loc.T("utility.close"), Height=36, Padding=new Thickness(12,6,12,6), Margin=new Thickness(8,0,0,0) };
        buttons.Children.Add(_scale); buttons.Children.Add(_copy); buttons.Children.Add(_save); buttons.Children.Add(close);
        DockPanel.SetDock(buttons,Dock.Bottom); root.Children.Add(buttons);
        _image.Source=result.Image; RenderOptions.SetBitmapScalingMode(_image,BitmapScalingMode.HighQuality);
        _viewer.Content=_image; _viewer.SetResourceReference(BackgroundProperty,"PanelBrush");
        root.Children.Add(_viewer); Content=root;
        _viewer.SizeChanged+=(_,_)=>UpdateScale(); Loaded+=(_,_)=>UpdateScale();
        _scale.Click+=(_,_)=>{ _fit=!_fit; UpdateScale(); };
        _save.Click+=async (_,_)=>await SaveAsync();
        _copy.Click+=async (_,_)=>await CopyAsync();
        close.Click+=(_,_)=>Close();
        Closing+=(_,e)=>{ if(_busy) e.Cancel=true; };
        Closed+=(_,_)=>_image.Source=null;
        PreviewKeyDown+=(_,e)=>{ if(e.Key==Key.Escape && !_busy){ Close(); e.Handled=true; } };
    }
    private void UpdateScale()
    {
        var available=Math.Max(1,_viewer.ActualWidth-20);
        _image.Width=_fit?Math.Min(_result.Image.PixelWidth,available):_result.Image.PixelWidth;
        _scale.Content=Loc.T(_fit?"scroll.actual":"scroll.fit");
    }
    private void Busy(bool value)
    {
        _busy=value; _copy.IsEnabled=_save.IsEnabled=!value;
    }
    private async Task SaveAsync()
    {
        if(_busy) return;
        var settings=SettingsService.Load();
        var dialog=new SaveFileDialog { Title=Loc.T("dlg.saveTitle"), Filter="PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg",
            FilterIndex=settings.DefaultFormat=="JPG"?2:1, FileName="VuSnap-scroll-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"),
            AddExtension=true, OverwritePrompt=true, InitialDirectory=Directory.Exists(settings.SaveDirectory)?settings.SaveDirectory:null };
        if(dialog.ShowDialog(this)!=true) return;
        Busy(true);
        try { await Task.Run(()=>ImageExportService.Save(_result.Image,dialog.FileName,true)); _status.Text=Loc.T("scroll.saved"); }
        catch(Exception ex){ ErrorLog.Write(ErrorCodes.Export,ex); _status.Text="["+ErrorCodes.Export+"] "+Loc.T("err.save"); }
        finally { Busy(false); }
    }
    private async Task CopyAsync()
    {
        if(_busy) return; Busy(true);
        try
        {
            for(int i=0;;i++)
            {
                try { Clipboard.SetImage(_result.Image); break; }
                catch(COMException) when(i<5){ await Task.Delay(60); }
            }
            _status.Text=Loc.T("utility.copied");
        }
        catch(Exception ex){ ErrorLog.Write(ErrorCodes.Clipboard,ex); _status.Text="["+ErrorCodes.Clipboard+"] "+Loc.T("err.copy"); }
        finally { Busy(false); }
    }
}

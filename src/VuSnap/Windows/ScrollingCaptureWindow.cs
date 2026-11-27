using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VuSnap.Models;
using VuSnap.Services;
using SD = System.Drawing;

namespace VuSnap.Windows;

internal sealed class ScrollingCaptureWindow : Window
{
    private readonly WindowTargets.Target _target;
    private readonly SD.Rectangle _crop;
    private readonly int _width, _height;
    private readonly ScrollCaptureControl _control = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, MinHeight = 36, FontSize = 12 };
    private readonly TextBlock _metrics = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6) };
    private readonly Button _auto = new() { MinWidth = 112, Padding = new Thickness(10,6,10,6) };
    private readonly Button _finish = new() { MinWidth = 90, Padding = new Thickness(10,6,10,6), IsEnabled = false };
    private CancellationTokenSource? _life;
    private bool _running, _closing;

    public ScrollingCaptureWindow(WindowTargets.Target target, SD.Rectangle crop, int width, int height)
    {
        _target=target; _crop=crop; _width=width; _height=height;
        Title=Loc.T("capture.Scrolling"); Width=450; SizeToContent=SizeToContent.Height;
        ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false; Topmost=true; ShowActivated=false;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Icon=AppIcon.LoadImage();
        SetResourceReference(BackgroundProperty,"WindowBrush"); SetResourceReference(ForegroundProperty,"TextBrush");
        ThemeManager.HookWindowChrome(this,()=>ThemeManager.IsDark(SettingsService.Load().Theme));
        var root=new StackPanel { Margin=new Thickness(16) };
        root.Children.Add(new TextBlock { Text=Loc.T("capture.Scrolling"), FontSize=18, FontWeight=FontWeights.Bold });
        var description=new TextBlock { Text=Loc.T("scroll.instructions"), FontSize=12, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,0) };
        description.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");
        root.Children.Add(description);
        _metrics.Text=Loc.F("scroll.metrics",crop.Width,0,0); root.Children.Add(_metrics);
        _status.Text=Loc.T("scroll.waiting"); root.Children.Add(_status);
        var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,8,0,0) };
        _auto.Margin=new Thickness(0,0,8,0); _finish.Margin=new Thickness(0,0,8,0);
        _finish.Content=Loc.T("scroll.finish"); _finish.Style=(Style)Application.Current.FindResource("GradientButton");
        _finish.Height=36;
        var cancel=new Button { Content=Loc.T("utility.cancel"), MinWidth=76, Padding=new Thickness(10,6,10,6) };
        buttons.Children.Add(_auto); buttons.Children.Add(_finish); buttons.Children.Add(cancel);
        root.Children.Add(buttons); Content=root; SyncAuto();
        _auto.Click+=(_,_)=>{ if (!_control.FinishRequested) { _control.Auto=!_control.Auto; SyncAuto(); } };
        _finish.Click+=(_,_)=>{ _control.Finish(); _auto.IsEnabled=_finish.IsEnabled=false; SyncAuto(); _status.Text=Loc.T("scroll.finishing"); };
        cancel.Click+=(_,_)=>_life?.Cancel();
        PreviewKeyDown+=(_,e)=>{ if(e.Key==Key.Escape){ _life?.Cancel(); e.Handled=true; } };
        Closing+=(_,e)=>{ if (_running && !_closing) { e.Cancel=true; _life?.Cancel(); } };
    }

    private void SyncAuto()
    {
        _auto.Content=Loc.T(_control.Auto?"scroll.manual":"scroll.auto");
        _auto.ToolTip=Loc.T(_control.Auto?"scroll.manual":"scroll.auto");
    }

    public async Task<ScrollCaptureOutcome> RunAsync(CancellationToken token)
    {
        if (_running || _closing) throw new CaptureException("ERR_SCROLL_004");
        using var life=CancellationTokenSource.CreateLinkedTokenSource(token);
        _life=life; _running=true;
        var progress=new Progress<ScrollCaptureProgress>(value=>
        {
            if (!_running || _closing || life.IsCancellationRequested) return;
            _metrics.Text=Loc.F("scroll.metrics",value.Width,value.Height,value.Frames);
            if (!_control.FinishRequested)
            {
                _status.Text=Loc.T(value.Message); _finish.IsEnabled=value.Frames>0; SyncAuto();
            }
        });
        try
        {
            life.Token.ThrowIfCancellationRequested();
            Show();
            return await GraphicsWindowCapture.ScrollAsync(_target,_crop,_width,_height,_control,progress,life.Token);
        }
        finally
        {
            _control.Auto=false; _running=false; _closing=true; _life=null;
            Close();
        }
    }
}

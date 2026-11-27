using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VuSnap.Services;

namespace VuSnap.Windows;

public sealed class WindowSourcePicker : Window
{
    private readonly ListBox _list = new() { DisplayMemberPath = "Display" };
    private readonly Border _preview = new() { Background = new SolidColorBrush(Color.FromRgb(22, 30, 45)), MinHeight = 200 };
    private readonly Button _capture = new() { IsEnabled = false, Padding = new Thickness(16, 8, 16, 8) };
    private IntPtr _thumbnail;
    private CancellationTokenRegistration _registration;
    public WindowTargets.Target? SelectedTarget { get; private set; }

    public WindowSourcePicker(CancellationToken cancellation)
    {
        Title = Loc.T("wgc.title"); Width = 800; Height = 480; MinWidth = 560; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _list.Margin = new Thickness(0,0,12,0);
        root.Children.Add(_list);
        Grid.SetColumn(_preview,1); root.Children.Add(_preview);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        var refresh = new Button { Content = Loc.T("wgc.refresh"), Padding = new Thickness(12,8,12,8), Margin = new Thickness(0,0,8,0) };
        var cancel = new Button { Content = Loc.T("wgc.cancel"), IsCancel = true, Padding = new Thickness(12,8,12,8), Margin = new Thickness(0,0,8,0) };
        _capture.Content = Loc.T("btn.capture"); _capture.IsDefault = true;
        buttons.Children.Add(refresh); buttons.Children.Add(cancel); buttons.Children.Add(_capture);
        Grid.SetRow(buttons,1); Grid.SetColumnSpan(buttons,2); root.Children.Add(buttons); Content = root;
        refresh.Click += (_,_) => Refresh();
        _list.SelectionChanged += (_,_) => { _capture.IsEnabled = _list.SelectedItem is WindowTargets.Target; UpdateThumbnail(true); };
        _capture.Click += (_,_) => { if (_list.SelectedItem is WindowTargets.Target t && WindowTargets.IsCurrent(t)) { SelectedTarget = t; DialogResult = true; } else Refresh(); };
        Loaded += (_,_) => { Refresh(); if (cancellation.IsCancellationRequested) { DialogResult = false; return; } _registration = cancellation.Register(() => Dispatcher.BeginInvoke(new Action(() => { if (IsVisible) DialogResult = false; }))); };
        SizeChanged += (_,_) => UpdateThumbnail(false);
        Closed += (_,_) => { _registration.Dispose(); ReleaseThumbnail(); };
    }
    private void Refresh()
    {
        ReleaseThumbnail();
        _list.ItemsSource = WindowTargets.Enumerate();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }
    private void ReleaseThumbnail()
    {
        if (_thumbnail != IntPtr.Zero) { DwmUnregisterThumbnail(_thumbnail); _thumbnail = IntPtr.Zero; }
    }
    private void UpdateThumbnail(bool recreate)
    {
        if (!IsLoaded || _preview.ActualWidth <= 0 || _preview.ActualHeight <= 0) return;
        if (recreate) ReleaseThumbnail();
        if (_list.SelectedItem is not WindowTargets.Target target) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (_thumbnail == IntPtr.Zero && DwmRegisterThumbnail(hwnd,target.Handle,out _thumbnail) != 0) { _thumbnail = IntPtr.Zero; return; }
        if (DwmQueryThumbnailSourceSize(_thumbnail,out var size) != 0 || size.Width <= 0 || size.Height <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var origin = _preview.TranslatePoint(new Point(0,0),this);
        var width = _preview.ActualWidth * dpi.DpiScaleX;
        var height = _preview.ActualHeight * dpi.DpiScaleY;
        var scale = Math.Min(width / size.Width,height / size.Height);
        int w = Math.Max(1,(int)(size.Width*scale)), h = Math.Max(1,(int)(size.Height*scale));
        int x = (int)(origin.X*dpi.DpiScaleX+(width-w)/2), y = (int)(origin.Y*dpi.DpiScaleY+(height-h)/2);
        var props = new ThumbnailProperties { Flags = 1|4|8, Destination = new NativeRect { Left=x,Top=y,Right=x+w,Bottom=y+h }, Opacity=255, Visible=true };
        DwmUpdateThumbnailProperties(_thumbnail, ref props);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width,Height; }
    [StructLayout(LayoutKind.Sequential)] private struct ThumbnailProperties
    {
        public uint Flags; public NativeRect Destination,Source; public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool SourceClientAreaOnly;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(IntPtr destination,IntPtr source,out IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(IntPtr thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(IntPtr thumbnail,out NativeSize size);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(IntPtr thumbnail,ref ThumbnailProperties properties);
}

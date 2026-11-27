using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VuSnap.Services;
using VuSnap.Windows;
using DrawingRectangle = System.Drawing.Rectangle;
using ShapePath = System.Windows.Shapes.Path;

namespace VuSnap.Tests;

internal static class EditorRegressionTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object target, string method, params object?[] arguments) =>
        target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, arguments);
    private static T Field<T>(object target, string field) =>
        (T)target.GetType().GetField(field, PrivateInstance)!.GetValue(target)!;
    private static void Set(object target, string field, object? value) =>
        target.GetType().GetField(field, PrivateInstance)!.SetValue(target, value);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Suite(Action<string, Action> run)
    {
        // WPF edits run on an STA dispatcher with the same resources as the application.
        Exception? setupFailure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/VuSnap;component/Styles/UiPro.xaml")
                });
                run("multi-layer edits preserve other properties through undo and redo", MultiSelection);
                run("pen appends points and finalizes all stroke styles", PenDrawing);
                run("OCR and QR limits reject before committing or rendering annotations", AnalysisLimits);
                run("settings sliders persist on completion and debounce keyboard edits", SettingsSliders);
                app.Shutdown();
            }
            catch (Exception ex) { setupFailure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (setupFailure is not null) run("editor test setup", () => throw setupFailure);
    }

    private static CaptureWindow Editor(int width = 600, int height = 400)
    {
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null,
            new byte[checked(width * height * 4)], checked(width * 4));
        image.Freeze();
        var window = new CaptureWindow(image, new DrawingRectangle(0, 0, width, height));
        var root = Field<Grid>(window, "Root");
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        Set(window, "_hasSel", true);
        Set(window, "_sel", new Rect(0, 0, width, height));
        return window;
    }

    private static ShapePath AddLine(CaptureWindow window, Color color, double thickness, string stroke)
    {
        Set(window, "_color", color);
        Set(window, "_thick", thickness);
        Set(window, "_style", stroke);
        var annotation = Call(window, "NewAnn", "Line")!;
        annotation.GetType().GetField("A")!.SetValue(annotation, new Point(20, 20));
        annotation.GetType().GetField("B")!.SetValue(annotation, new Point(200, 200));
        var line = new ShapePath { Tag = annotation };
        Field<Canvas>(window, "Ink").Children.Add(line);
        Call(window, "ApplyStyle", line);
        Call(window, "Commit", line);
        return line;
    }

    private static void MultiSelection()
    {
        var window = Editor();
        try
        {
            AddLine(window, Colors.Red, 2, "Dash");
            AddLine(window, Colors.Blue, 8, "Solid");
            Call(window, "SelectAll");
            Call(window, "SetOpacity", 0.5);
            void Verify(double opacity)
            {
                var layers = Field<Canvas>(window, "Ink").Children.OfType<ShapePath>().ToArray();
                Check(layers.Length == 2, "Layer count changed");
                Check(((SolidColorBrush)layers[0].Stroke).Color == Colors.Red &&
                    ((SolidColorBrush)layers[1].Stroke).Color == Colors.Blue, "Colors changed");
                Check(layers[0].StrokeThickness == 2 && layers[1].StrokeThickness == 8, "Thickness changed");
                Check(layers[0].StrokeDashArray?.Count > 0 && layers[1].StrokeDashArray is null, "Dash style changed");
                Check(layers.All(layer => layer.Opacity == opacity), "Wrong opacity");
            }
            Verify(0.5);
            Call(window, "Undo");
            Verify(1);
            Call(window, "Redo");
            Verify(0.5);
        }
        finally { window.Close(); }
    }

    private static void PenDrawing()
    {
        var window = Editor();
        try
        {
            foreach (var style in new[] { "Solid", "Dash", "Dot", "DashDot", "Double" })
            {
                Set(window, "_style", style);
                var annotation = Call(window, "NewAnn", "Pen")!;
                var points = new List<Point> { new(10, 20) };
                annotation.GetType().GetField("Pts")!.SetValue(annotation, points);
                var segment = new PolyLineSegment();
                var geometry = new PathGeometry
                {
                    Figures = { new PathFigure { StartPoint = points[0], IsFilled = false, Segments = { segment } } }
                };
                var path = new ShapePath { Tag = annotation };
                Field<Canvas>(window, "Ink").Children.Add(path);
                Set(window, "_current", path);
                Set(window, "_penPreview", geometry);
                Call(window, "ApplyStyle", path);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 1; i <= 2000; i++) Call(window, "UpdateDraw", new Point(10 + i * 0.2, 20 + i % 20));
                var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(ReferenceEquals(Field<PathGeometry>(window, "_penPreview"), geometry), "Preview geometry replaced");
                Check(segment.Points.Count == 2000 && points.Count == 2001, "Pen points lost");
                Check(allocated < 10_000_000, "Per-move geometry rebuilding returned");
                Call(window, "EndDraw");
                Check(path.Data.IsFrozen || style == "Double", "Completed path should be immutable");
                Check(Field<PathGeometry?>(window, "_penPreview") is null, "Live preview retained");
                if (style == "Double") Check(path.StrokeThickness == 3.2, "Double outline lost");
            }
        }
        finally { window.Close(); }
    }

    private static void AnalysisLimits()
    {
        var window = Editor(5000, 3300);
        try
        {
            var pendingText = new TextBox { Text = "Uncommitted annotation" };
            Field<Canvas>(window, "Ink").Children.Add(pendingText);
            Set(window, "_editor", pendingText);
            foreach (var (validator, code) in new (Action<int, int>, string)[]
            {
                (OfflineOcrService.ValidateSize, "ERR_OCR_002"),
                (LocalBarcodeService.ValidateSize, "ERR_QR_001")
            })
            {
                try
                {
                    Call(window, "RenderAnalysisSelection", validator);
                    throw new Exception("Oversized image accepted");
                }
                catch (TargetInvocationException ex) when (ex.InnerException is UtilityException utility)
                {
                    Check(utility.Code == code, "Wrong validation error");
                }
                Check(ReferenceEquals(Field<TextBox>(window, "_editor"), pendingText),
                    "RenderFinal committed annotations before size validation");
            }
        }
        finally { window.Close(); }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void SettingsSliders()
    {
        SettingsService.Save(new VuSnap.Models.AppSettings());
        var window = new MainWindow();
        try
        {
            var slider = Field<Slider>(window, "DefThickSlider");
            slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            slider.Value = 7;
            slider.Value = 9;
            Pump(500);
            Check(SettingsService.Load().DefaultThickness == 4, "Disk write during slider drag");
            slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Check(SettingsService.Load().DefaultThickness == 9, "Drag completion did not persist");

            slider.Value = 11;
            slider.Value = 12;
            Check(SettingsService.Load().DefaultThickness == 9, "Keyboard changes were not debounced");
            Pump(600);
            Check(SettingsService.Load().DefaultThickness == 12, "Debounced changes lost");

            slider.Value = 14;
            Call(window, "FlushSliderSettings");
            Check(SettingsService.Load().DefaultThickness == 14, "Pending value lost before capture/close");
        }
        finally { Set(window, "_exiting", true); window.Close(); }
    }
}

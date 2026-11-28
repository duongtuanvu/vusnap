using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Rectangle = System.Windows.Shapes.Rectangle;
using Path = System.Windows.Shapes.Path;

namespace VuSnap.Windows;

public partial class CaptureWindow
{
    // Snapshot only annotation metadata, not screenshots, rendered bitmaps or UI trees.
    // Stable annotation IDs are stored in Ann; history does not keep any WPF visuals alive.
    private sealed record LayerState(Ann Annotation, string? Text,
        double Left, double Top, double Width, double Height, Visibility Visibility,
        double Angle, double CenterX, double CenterY, double ScaleX, double ScaleY,
        double ScaleCenterX, double ScaleCenterY, double X, double Y);

    private sealed class LayerStateComparer : IEqualityComparer<LayerState[]>
    {
        public bool Equals(LayerState[]? x, LayerState[]? y)
        {
            if (ReferenceEquals(x,y)) return true;
            if (x is null || y is null || x.Length != y.Length) return false;
            for (var i = 0; i < x.Length; i++)
            {
                var a = x[i]; var b = y[i];
                if ((a with { Annotation = b.Annotation }) != b || !SameAnn(a.Annotation,b.Annotation)) return false;
            }
            return true;
        }
        public int GetHashCode(LayerState[] value) => value.Length;
        private static bool SameAnn(Ann a, Ann b) =>
            a.Id == b.Id && a.Kind == b.Kind && a.Color == b.Color && a.Thick == b.Thick && a.Opacity == b.Opacity &&
            a.A == b.A && a.B == b.B && a.Tail == b.Tail && a.Label == b.Label && a.Name == b.Name &&
            a.Stroke == b.Stroke && a.Head == b.Head && a.CornersOnly == b.CornersOnly && a.Zoom == b.Zoom &&
            a.Font == b.Font && a.Weight == b.Weight && a.Italic == b.Italic && a.LineType == b.LineType &&
            a.Round == b.Round && (ReferenceEquals(a.Pts,b.Pts) ||
                (a.Pts is not null && b.Pts is not null && a.Pts.SequenceEqual(b.Pts)));
    }

    private LayerState[] CaptureHistory() => Layers.Select(el =>
    {
        var g = GroupOf(el);
        var r = (RotateTransform)g.Children[0];
        var s = (ScaleTransform)g.Children[1];
        var t = (TranslateTransform)g.Children[2];
        return new LayerState(((Ann)el.Tag).Copy(shareCompletedPoints: true), (el as TextBlock)?.Text,
            Canvas.GetLeft(el), Canvas.GetTop(el), el.Width, el.Height, el.Visibility,
            r.Angle,r.CenterX,r.CenterY,s.ScaleX,s.ScaleY,s.CenterX,s.CenterY,t.X,t.Y);
    }).ToArray();

    private void RecordHistory()
    {
        if (!_restoringHistory && _drag == Drag.None && _editor is null && !_svDrag && !_hueDrag && !_sliderGesture)
            _history.Record(CaptureHistory());
    }

    private bool _sliderGesture;

    private void InitializeHistoryGestures()
    {
        foreach (var slider in new[] { SizeSlider, PSizeSlider, POpacitySlider })
        {
            slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _sliderGesture = true), true);
            slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
            {
                _sliderGesture = false; RecordHistory();
            }), true);
        }
        SvBox.LostMouseCapture += (_, _) => { _svDrag = false; RecordHistory(); };
        HueBar.LostMouseCapture += (_, _) => { _hueDrag = false; RecordHistory(); };
        Root.LostMouseCapture += (_, e) => { if (ReferenceEquals(e.OriginalSource, Root)) CancelPointerDrag(); };
    }

    private void CancelPointerDrag()
    {
        var was = _drag;
        if (was == Drag.None) return;
        _drag = Drag.None;
        if (was == Drag.Drawing)
        {
            ClearPenPreview();
            if (_current is not null) Ink.Children.Remove(_current);
            _current = null;
        }
        else if (was == Drag.MovingAnn)
        {
            foreach (var (el, orig) in _moveOrigs)
            {
                var t = TranslateOf(el); t.X = orig.X; t.Y = orig.Y;
            }
            _moveOrigs.Clear(); UpdateOutline();
        }
        else if (_dragHadSelection)
        {
            _hasSel = true; ApplySelection(_dragOrigRect); ShowBars();
        }
        else ClearSelection();
        HideLoupe();
    }

    private void RestoreHistory(LayerState[] state)
    {
        _restoringHistory = true;
        try
        {
            _selection.Clear(); _primary = null; _anchor = null; _moveOrigs.Clear();
            Ink.Children.Clear();
            foreach (var s in state)
            {
                FrameworkElement el = s.Annotation.Kind switch
                {
                    "Text" => new TextBlock { Text = s.Text ?? "", Background = Frozen(Color.FromRgb(255, 255, 224), 215), Padding = new Thickness(3) },
                    "Number" or "Ruler" => new Canvas(),
                    "Highlight" or "Blur" or "Invert" => new Rectangle(),
                    _ => new Path()
                };
                el.Tag = s.Annotation.Copy(shareCompletedPoints: true);
                Canvas.SetLeft(el,s.Left); Canvas.SetTop(el,s.Top);
                el.Width = s.Width; el.Height = s.Height; el.Visibility = s.Visibility;
                var g = GroupOf(el);
                var r = (RotateTransform)g.Children[0]; var sc = (ScaleTransform)g.Children[1];
                var t = (TranslateTransform)g.Children[2];
                r.Angle=s.Angle; r.CenterX=s.CenterX; r.CenterY=s.CenterY;
                sc.ScaleX=s.ScaleX; sc.ScaleY=s.ScaleY; sc.CenterX=s.ScaleCenterX; sc.CenterY=s.ScaleCenterY;
                t.X=s.X; t.Y=s.Y;
                Ink.Children.Add(el);
                ApplyStyle(el);
                if (el is Rectangle fx && s.Annotation.Kind is "Blur" or "Invert") ApplyEffect(fx);
            }
            UpdateOutline(); RefreshLayers();
        }
        finally { _restoringHistory = false; }
    }
}

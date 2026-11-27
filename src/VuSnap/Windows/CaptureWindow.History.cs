using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace VuSnap.Windows;

public partial class CaptureWindow
{
    // Snapshot only annotation metadata, not screenshots, rendered bitmaps or UI trees.
    // Keep stable IDs to distinguish identical layers and preserve z-order.
    private sealed record LayerState(FrameworkElement Element, Ann Annotation, string? Text,
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
            a.Kind == b.Kind && a.Color == b.Color && a.Thick == b.Thick && a.Opacity == b.Opacity &&
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
        return new LayerState(el, ((Ann)el.Tag).Copy(), (el as TextBlock)?.Text,
            Canvas.GetLeft(el), Canvas.GetTop(el), el.Width, el.Height, el.Visibility,
            r.Angle,r.CenterX,r.CenterY,s.ScaleX,s.ScaleY,s.CenterX,s.CenterY,t.X,t.Y);
    }).ToArray();

    private void RecordHistory()
    {
        if (!_restoringHistory && _drag == Drag.None && _editor is null)
            _history.Record(CaptureHistory());
    }

    private void RestoreHistory(LayerState[] state)
    {
        _restoringHistory = true;
        try
        {
            _selection.Clear(); _primary = null; _anchor = null;
            Ink.Children.Clear();
            foreach (var s in state)
            {
                var el = s.Element;
                el.Tag = s.Annotation.Copy();
                if (el is TextBlock tb) tb.Text = s.Text ?? "";
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

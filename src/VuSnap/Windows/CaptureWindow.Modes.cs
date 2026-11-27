using System.Windows;
using System.Windows.Input;
using VuSnap.Core;
using VuSnap.Models;
using VuSnap.Services;
using Point = System.Windows.Point;

namespace VuSnap.Windows;

public partial class CaptureWindow
{
    private void InitializeCaptureMode()
    {
        if (_captureMode == CaptureMode.Region) return;
        if (_pickingWindow) { HintText.Text = Loc.T("capture.pickHint"); return; }
        if (Root.ActualWidth <= 0 || Root.ActualHeight <= 0) return;
        SetInitialRegion(_initialRegion is { } region ? ToOverlayRect(region) : new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
    }

    private void SetInitialRegion(Rect rect)
    {
        _hasSel = true;
        Hint.Visibility = Visibility.Collapsed;
        ApplySelection(rect);
        SizeBadge.Visibility = Visibility.Collapsed;
        ShowBars();
    }

    private Rect ToOverlayRect(System.Drawing.Rectangle target) => new(
        (target.Left - _screenBounds.Left) / ScaleX, (target.Top - _screenBounds.Top) / ScaleY,
        target.Width / ScaleX, target.Height / ScaleY);

    private bool TryWindowSelection(Point p, bool commit)
    {
        var pixel = new System.Drawing.Point(_screenBounds.Left + (int)(p.X * ScaleX), _screenBounds.Top + (int)(p.Y * ScaleY));
        var match = CaptureGeometry.Hit(_windowTargets, pixel);
        if (match is not { } target)
        {
            ClearSelection();
            return false;
        }
        var rect = ToOverlayRect(target);
        if (commit)
        {
            _pickingWindow = false;
            SetInitialRegion(rect);
        }
        else if (rect != _sel || Selection.Visibility != Visibility.Visible) ApplySelection(rect);
        return true;
    }
}

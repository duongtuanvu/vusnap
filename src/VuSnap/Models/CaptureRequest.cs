using System.Drawing;
using System.Windows.Media.Imaging;

namespace VuSnap.Models;

public enum CaptureMode { Region, Monitor, Desktop, ActiveWindow, WindowPicker, Repeat }
public sealed record LastCaptureRegion(Rectangle Region, IReadOnlyList<Rectangle> MonitorLayout);
public sealed record CaptureRequest(CaptureMode Mode, int DelaySeconds = 0, LastCaptureRegion? Previous = null);
public sealed record CaptureResult(BitmapSource Image, Rectangle Bounds, CaptureMode Mode,
    IReadOnlyList<Rectangle> Windows, Rectangle? InitialRegion, IReadOnlyList<Rectangle> MonitorLayout);
public sealed class CaptureException(string code) : Exception(code) { public string Code { get; } = code; }

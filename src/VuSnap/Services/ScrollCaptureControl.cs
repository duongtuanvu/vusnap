using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using SD = System.Drawing;

namespace VuSnap.Services;

internal sealed class ScrollCaptureControl
{
    private int _auto, _finish;
    public bool Auto { get => Volatile.Read(ref _auto) != 0; set => Volatile.Write(ref _auto, value ? 1 : 0); }
    public bool FinishRequested => Volatile.Read(ref _finish) != 0;
    public void Finish() { Volatile.Write(ref _finish, 1); Auto = false; }
}

internal sealed record ScrollCaptureProgress(int Width, int Height, int Frames, string Message);
internal sealed record ScrollCaptureOutcome(BitmapSource Image, int Frames, string Message);

// WGC frame timestamps and Stopwatch.GetElapsedTime(0) both use the system QPC clock.
internal sealed class ScrollWheelFeedback
{
    private TimeSpan? _sentAt;
    public bool Pending => _sentAt.HasValue;
    public int UnchangedSteps { get; private set; }

    public void Sent(TimeSpan now) => _sentAt = now;
    public bool Accepts(TimeSpan frameTime) => _sentAt is not { } sent || frameTime > sent;
    public bool TimedOut(TimeSpan now) => _sentAt is { } sent && now - sent >= TimeSpan.FromSeconds(5);

    public void Observe(TimeSpan frameTime, int addedRows)
    {
        if (!Pending || !Accepts(frameTime)) return;
        UnchangedSteps = addedRows > 0 ? 0 : UnchangedSteps + 1;
        _sentAt = null;
    }

    public void Reset() { _sentAt = null; UnchangedSteps = 0; }
}

internal static class ScrollWheel
{
    internal enum Result { Waiting, Sent, Blocked }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] input, int size);

    public static Result TryDown(WindowTargets.Target target, SD.Rectangle crop, int sourceWidth, int sourceHeight,
        ScrollCaptureControl control, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Never move the cursor or activate a window. Explicit Auto opt-in and all guards
        // must still hold immediately before injecting a single wheel notch.
        if (!control.Auto || control.FinishRequested || !WindowTargets.IsCurrent(target) ||
            WindowTargets.Foreground() != target.Handle ||
            !WindowTargets.TryGetWindowBounds(target.Handle, out var bounds) || bounds != target.Bounds ||
            !GetCursorPos(out var point) ||
            !ScrollCaptureGeometry.IsPointerInCrop(bounds, crop, sourceWidth, sourceHeight, new SD.Point(point.X, point.Y)) ||
            GetAncestor(WindowFromPoint(point), 2) != target.Handle)
            return Result.Waiting;
        foreach (int key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x01, 0x02, 0x04 })
            if ((GetAsyncKeyState(key) & 0x8000) != 0) return Result.Waiting;
        token.ThrowIfCancellationRequested();
        if (!control.Auto || control.FinishRequested || WindowTargets.Foreground() != target.Handle ||
            !GetCursorPos(out var check) || check.X != point.X || check.Y != point.Y ||
            GetAncestor(WindowFromPoint(check), 2) != target.Handle) return Result.Waiting;

        int delta = crop.Height < 300 ? -60 : -120;
        var input = new[] { new Input { Type = 0, Mouse = new MouseInput { Data = unchecked((uint)delta), Flags = 0x0800 } } };
        return SendInput(1, input, Marshal.SizeOf<Input>()) == 1 ? Result.Sent : Result.Blocked;
    }
}

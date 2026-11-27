// Continuous WGC reuses the existing capture-item/device creation path.
// No screen fallback, no disk files, no network, one worker owns the stitcher.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using VuSnap.Core;
using VuSnap.Models;
using WinRT;
using SD = System.Drawing;

namespace VuSnap.Services;

public static partial class GraphicsWindowCapture
{
    internal static Task<ScrollCaptureOutcome> ScrollAsync(WindowTargets.Target target, SD.Rectangle crop,
        int sourceWidth, int sourceHeight, ScrollCaptureControl control, IProgress<ScrollCaptureProgress> progress,
        CancellationToken cancellation)
    {
        ScrollCaptureGeometry.ValidateCrop(sourceWidth, sourceHeight, crop);
        return Task.Run(async () =>
        {
            try { return await ScrollCoreAsync(target, crop, sourceWidth, sourceHeight, control, progress, cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (CaptureException) { throw; }
            catch (Exception ex) { throw new CaptureException("ERR_SCROLL_004", ex); }
        }, cancellation);
    }

    private static bool SameScrollTarget(WindowTargets.Target target) =>
        WindowTargets.IsCurrent(target) && WindowTargets.TryGetWindowBounds(target.Handle, out var rect) && rect == target.Bounds;

    private static async Task<ScrollCaptureOutcome> ScrollCoreAsync(WindowTargets.Target target, SD.Rectangle crop,
        int width, int height, ScrollCaptureControl control, IProgress<ScrollCaptureProgress> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsSupported()) throw new CaptureException("ERR_WGC_001");
        if (!SameScrollTarget(target)) throw new CaptureException("ERR_SCROLL_002");
        var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        var pointer = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>().CreateForWindow(target.Handle, ref iid);
        if (pointer == IntPtr.Zero) throw new CaptureException("ERR_SCROLL_002");
        GraphicsCaptureItem item;
        try { item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
        if (item.Size.Width != width || item.Size.Height != height) throw new CaptureException("ERR_SCROLL_002");
        var stitcher = new Stitcher(crop.Width, crop.Height, Math.Min(24, crop.Width / 8), Stitcher.MaximumDimension);
        var feedback = new ScrollWheelFeedback();
        int[] buffer = new int[checked(crop.Width * crop.Height)];
        var clock = Stopwatch.StartNew();
        Direct3D11CaptureFrame? latest = null;
        int frames = 0;
        long settleUntil = 250, lastReport = -1000;
        bool hadFocus = false, wasAuto = false, uncertain = false;
        string reason = "scroll.done", status = "scroll.waiting", lastMessage = "";
        int lastHeight = -1, lastFrames = -1;
        void Report(string message, bool force = false)
        {
            var now = clock.ElapsedMilliseconds;
            if (!force && now - lastReport < 160) return;
            if (!force && message == lastMessage && stitcher.Height == lastHeight && frames == lastFrames) return;
            progress.Report(new ScrollCaptureProgress(crop.Width, stitcher.Height, frames, message));
            lastMessage = message; lastHeight = stitcher.Height; lastFrames = frames; lastReport = now;
        }
        using (var device = CreateDevice())
        using (var nativeDevice = ScrollGpuCrop.OpenDevice(device))
        using (var context = nativeDevice.ImmediateContext)
        using (var readback = new ScrollGpuCrop(nativeDevice, context, crop))
        using (var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size))
        using (var session = pool.CreateCaptureSession(item))
        {
        session.IsCursorCaptureEnabled = false;
        try
        {
            session.StartCapture();
            while (!control.FinishRequested)
            {
                token.ThrowIfCancellationRequested();
                long now = clock.ElapsedMilliseconds;
                if (now >= 180_000) { reason = "scroll.timeout"; break; }
                if (!SameScrollTarget(target)) { reason = "scroll.partial"; break; }
                // Drain the free-threaded pool; retain just the most recent frame during settling.
                for (int i = 0; i < 4; i++)
                {
                    var frame = pool.TryGetNextFrame();
                    if (frame is null) break;
                    latest?.Dispose();
                    latest = frame;
                }
                bool focused = WindowTargets.Foreground() == target.Handle;
                if (!focused)
                {
                    hadFocus = false; feedback.Reset();
                    Report("scroll.waiting");
                    await Task.Delay(100, token).ConfigureAwait(false);
                    continue;
                }
                bool auto = control.Auto;
                if (!hadFocus || auto != wasAuto)
                {
                    settleUntil = now + 250;
                    feedback.Reset();
                    hadFocus = true; wasAuto = auto;
                }
                if (now < settleUntil)
                {
                    await Task.Delay(90, token).ConfigureAwait(false);
                    continue;
                }

                if (latest is not null)
                {
                    using var frame = latest;
                    latest = null;
                    if (frame.ContentSize.Width != width || frame.ContentSize.Height != height)
                    { reason = "scroll.partial"; break; }
                    if (feedback.Accepts(frame.SystemRelativeTime))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!readback.TryCopy(frame.Surface, width, height, buffer))
                        { reason = "scroll.partial"; break; }
                        if (!SameScrollTarget(target)) { reason = "scroll.partial"; break; }
                        if (WindowTargets.Foreground() != target.Handle) continue;
                        buffer = stitcher.Add(buffer, out var match, out var added, token) ?? new int[checked(crop.Width * crop.Height)];
                        if (added > 0) { frames++; uncertain = false; status = frames == 1 ? "scroll.ready" : "scroll.capturing"; }
                        if (match == Stitcher.Result.Full) { reason = "scroll.limit"; break; }
                        if (match == Stitcher.Result.Lost || match == Stitcher.Result.Up)
                        {
                            uncertain = true; control.Auto = false; feedback.Reset();
                            status = match == Stitcher.Result.Lost ? "scroll.gap" : "scroll.up";
                            Report(status, true);
                        }
                        else feedback.Observe(frame.SystemRelativeTime, added);
                    }
                }

                if (control.FinishRequested) break;
                if (feedback.TimedOut(Stopwatch.GetElapsedTime(0)))
                {
                    control.Auto = false;
                    feedback.Reset();
                    status = "scroll.frameDelayed";
                }
                if (feedback.UnchangedSteps >= 4 && control.Auto)
                { reason = frames <= 1 ? "scroll.noMovement" : "scroll.end"; break; }
                if (frames > 0 && control.Auto && !feedback.Pending)
                {
                    // Do not inject a new wheel action until the previous capture has settled.
                    switch (ScrollWheel.TryDown(target, crop, width, height, control, token))
                    {
                        case ScrollWheel.Result.Sent:
                            feedback.Sent(Stopwatch.GetElapsedTime(0));
                            settleUntil = clock.ElapsedMilliseconds + 700;
                            status = "scroll.capturing";
                            break;
                        case ScrollWheel.Result.Waiting:
                            status = "scroll.pointer";
                            break;
                        case ScrollWheel.Result.Blocked:
                            control.Auto = false; status = "scroll.autoFailed";
                            ErrorLog.Write("ERR_SCROLL_007");
                            break;
                    }
                }
                if (frames == 0) status = "scroll.waiting";
                Report(status);
                await Task.Delay(100, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            if (frames == 0)
                throw new CaptureException(reason == "scroll.partial" ? "ERR_SCROLL_002" : "ERR_SCROLL_003");
            if (reason == "scroll.done") reason = frames <= 1 ? "scroll.noMovement" : uncertain ? "scroll.gap" : reason;
        }
        finally
        {
            control.Auto = false;
            latest?.Dispose();
        }

        }
        // Capture pipeline has been disposed before allocating the final tall bitmap.
        token.ThrowIfCancellationRequested();
        Report("scroll.finishing", true);
        using var bitmap = stitcher.Compose();
        token.ThrowIfCancellationRequested();
        var data = bitmap.LockBits(new SD.Rectangle(0,0,bitmap.Width,bitmap.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride <= 0) throw new CaptureException("ERR_SCROLL_004");
            var image = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgr32, null,
                data.Scan0, checked(data.Stride * bitmap.Height), data.Stride);
            image.Freeze();
            token.ThrowIfCancellationRequested();
            return new ScrollCaptureOutcome(image, frames, reason);
        }
        finally { bitmap.UnlockBits(data); }
    }
}

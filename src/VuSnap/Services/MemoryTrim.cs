using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace VuSnap.Services;

/// <summary>Giảm RAM khi app chạy nền: dọn GC rồi trả bớt working set cho hệ điều hành.</summary>
public static class MemoryTrim
{
    [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr hProcess);

    private static DispatcherTimer? _timer;

    /// <summary>Hẹn dọn sau <paramref name="delayMs"/> ms (gọi lại nhiều lần thì chỉ dọn một lần, tính từ lần gọi cuối).</summary>
    public static void Schedule(int delayMs = 1500)
    {
        _timer ??= new DispatcherTimer();
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void OnTick(object? s, EventArgs e) { _timer?.Stop(); Now(); }

    public static void Now()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
        }
        catch { }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using VuSnap.Services;
using Application = System.Windows.Application;

namespace VuSnap;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Chỉ cho chạy một bản (theo thư mục dữ liệu): mở lần nữa thì chỉ báo bản đang chạy hiện cửa sổ lên.
        var id = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(SettingsService.DataFolder.ToLowerInvariant())))[..12];
        _mutex = new Mutex(true, "VuSnap.Instance." + id, out var first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "VuSnap.Show." + id);
        if (!first)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var win = new MainWindow();
        MainWindow = win;

        // --tray: do Windows khởi động -> không hiện cửa sổ, chỉ tạo handle (để đăng ký hotkey) và nằm ở khay hệ thống.
        if (e.Args.Contains("--tray")) new WindowInteropHelper(win).EnsureHandle();
        else win.Show();

        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne()) Dispatcher.BeginInvoke(win.ShowFromTray);
        }) { IsBackground = true };
        listener.Start();
    }
}

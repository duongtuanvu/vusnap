using System.Runtime.InteropServices;
using System.Windows;

namespace VuSnap.Services;

internal static class UtilityClipboard
{
    public static async Task CopyAsync(string value, CancellationToken cancellation = default)
    {
        if (string.IsNullOrEmpty(value)) return;
        for (int attempt = 0; ; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            try { Clipboard.SetText(value); return; }
            catch (COMException) when (attempt < 5) { await Task.Delay(60,cancellation); }
        }
    }
}

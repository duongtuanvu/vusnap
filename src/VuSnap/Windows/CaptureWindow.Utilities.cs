using System.Windows;
using VuSnap.Services;

namespace VuSnap.Windows;

public partial class CaptureWindow
{
    private void ColorInspect_Click(object sender, RoutedEventArgs e) =>
        AnalyzeSelection(image => new ColorInspectorWindow(image), null, "ERR_COLOR_001", "color.failed");

    private void Barcode_Click(object sender, RoutedEventArgs e) =>
        AnalyzeSelection(image => new BarcodeResultWindow(image), LocalBarcodeService.ValidateSize, "ERR_QR_002", "qr.failed");

    private void Ocr_Click(object sender, RoutedEventArgs e) =>
        AnalyzeSelection(image => new TextAnalysisWindow(image), OfflineOcrService.ValidateSize, "ERR_OCR_004", "ocr.failed");

    private void AnalyzeSelection(Func<System.Windows.Media.Imaging.BitmapSource, Window> create,
        Action<int, int>? validateSize, string errorCode, string message)
    {
        if (!_hasSel || _outputBusy || _drag != Drag.None) return;
        _outputBusy = true;
        try
        {
            var image = RenderAnalysisSelection(validateSize);
            var window = create(image);
            window.Owner = this;
            window.Topmost = true;
            window.ShowInTaskbar = false;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            var code = ex is UtilityException ue ? ue.Code : errorCode;
            ReportOutputError(code, Loc.T(code switch { "ERR_QR_001" => "qr.large", "ERR_OCR_002" => "ocr.large", _ => message }), ex);
        }
        finally { _outputBusy = false; }
    }
    private System.Windows.Media.Imaging.BitmapSource RenderAnalysisSelection(Action<int, int>? validateSize)
    {
        var pixels = SelectedPixels();
        validateSize?.Invoke(pixels.Width, pixels.Height);
        return RenderFinal();
    }

}

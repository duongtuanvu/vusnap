using System.Windows;
using VuSnap.Services;

namespace VuSnap.Windows;

public partial class CaptureWindow
{
    private void ColorInspect_Click(object sender, RoutedEventArgs e) =>
        AnalyzeSelection(image => new ColorInspectorWindow(image), "ERR_COLOR_001", "color.failed");

    private void Barcode_Click(object sender, RoutedEventArgs e) =>
        AnalyzeSelection(image =>
        {
            LocalBarcodeService.ValidateSize(image.PixelWidth, image.PixelHeight);
            return new BarcodeResultWindow(image);
        }, "ERR_QR_002", "qr.failed");

    private void AnalyzeSelection(Func<System.Windows.Media.Imaging.BitmapSource, Window> create, string errorCode, string message)
    {
        if (!_hasSel || _outputBusy || _drag != Drag.None) return;
        _outputBusy = true;
        try
        {
            var image = RenderFinal();
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
            ReportOutputError(code, Loc.T(code == "ERR_QR_001" ? "qr.large" : message), ex);
        }
        finally { _outputBusy = false; }
    }

    private void Ocr_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasSel || _outputBusy || _drag != Drag.None) return;
        _outputBusy = true;
        try
        {
            // Analyze the exported selection (including redactions/annotations), never pixels outside it.
            var image = RenderFinal();
            OfflineOcrService.ValidateSize(image.PixelWidth,image.PixelHeight);
            new TextAnalysisWindow(image) { Owner = this, Topmost = true }.ShowDialog();
        }
        catch (Exception ex)
        {
            var code = ex is UtilityException ue ? ue.Code : "ERR_OCR_004";
            ReportOutputError(code, Loc.T(code == "ERR_OCR_002" ? "ocr.large" : "ocr.failed"), ex);
        }
        finally { _outputBusy = false; }
    }
}

using System.Windows;
using VuSnap.Services;

namespace VuSnap.Windows;

public partial class CaptureWindow
{
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

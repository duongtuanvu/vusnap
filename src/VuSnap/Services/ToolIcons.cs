using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Material.Icons;
using Material.Icons.WPF;
using Color = System.Windows.Media.Color;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace VuSnap.Services;

/// <summary>Icon dùng chung (Material Design Icons): trên thanh công cụ ở overlay và ở bảng cài đặt.</summary>
public static class ToolIcons
{
    private static readonly Brush Bar = Frozen(Color.FromRgb(24, 28, 58));

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>Icon cho từng tool/nút (id trùng với id trong bảng cài đặt), kèm màu nhấn nếu có.</summary>
    private static (MaterialIconKind Kind, Color? Tint)? Lookup(string id) => id switch
    {
        "Select" => (MaterialIconKind.CursorMove, null),
        "Text" => (MaterialIconKind.FormatText, null),
        "Pen" => (MaterialIconKind.Pencil, null),
        "Line" => (MaterialIconKind.VectorLine, null),
        "Arrow" => (MaterialIconKind.ArrowTopRight, null),
        "Rectangle" => (MaterialIconKind.RectangleOutline, null),
        "Ellipse" => (MaterialIconKind.EllipseOutline, null),
        "Highlight" => (MaterialIconKind.FormatColorHighlight, Color.FromRgb(255, 230, 0)),
        "Blur" => (MaterialIconKind.Blur, Color.FromRgb(140, 236, 228)),
        "Invert" => (MaterialIconKind.InvertColors, null),
        "Ruler" => (MaterialIconKind.Ruler, null),
        "Number" => (MaterialIconKind.Numeric1CircleOutline, null),
        "Magnifier" => (MaterialIconKind.MagnifyPlusOutline, null),
        "Ratio" => (MaterialIconKind.AspectRatio, null),
        "ObjBar" => (MaterialIconKind.Selection, null),
        "Duplicate" => (MaterialIconKind.ContentDuplicate, null),
        "Panel" => (MaterialIconKind.TuneVariant, null),
        "Redo" => (MaterialIconKind.Redo, null),
        "Undo" => (MaterialIconKind.Undo, null),
        "Cancel" => (MaterialIconKind.Close, Color.FromRgb(255, 139, 157)),
        "SaveAs" => (MaterialIconKind.ContentSaveEdit, null),
        "QuickSave" => (MaterialIconKind.ContentSave, null),
        "Copy" => (MaterialIconKind.ContentCopy, null),
        "Confirm" => (MaterialIconKind.Check, Color.FromRgb(16, 185, 129)),
        "Ocr" => (MaterialIconKind.TextRecognition, Color.FromRgb(182, 236, 235)),
        "ColorInspect" => (MaterialIconKind.Eyedropper, Color.FromRgb(143, 168, 255)),
        "Barcode" => (MaterialIconKind.QrcodeScan, Color.FromRgb(182, 236, 235)),
        "Delete" => (MaterialIconKind.DeleteOutline, null),
        "SelectAll" => (MaterialIconKind.SelectAll, null),
        _ => null,
    };

    /// <summary>Tạo ô icon 28×28 (nền indigo đậm bo góc đồng bộ với thanh công cụ); null nếu không có icon cho id này.</summary>
    public static FrameworkElement? Create(string id)
    {
        UIElement content;
        if (id == "Color")
        {
            // nút màu trên thanh công cụ: chấm màu trong vòng tròn trắng
            content = new Grid
            {
                Width = 18, Height = 18,
                Children = { new Ellipse { Fill = Brushes.White }, new Ellipse { Fill = Frozen(Color.FromRgb(255, 59, 92)), Width = 8, Height = 8 } },
            };
        }
        else if (Lookup(id) is { } info)
        {
            content = new MaterialIcon
            {
                Kind = info.Kind, Width = 17, Height = 17,
                Foreground = info.Tint is { } c ? Frozen(c) : Brushes.White,
            };
        }
        else return null;

        return new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = Bar, Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center, Child = content,
        };
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VuSnap.Services;

namespace VuSnap.Windows;

/// <summary>Cửa sổ nhỏ hiển thị một danh sách (Tính năng / Cách dùng).</summary>
public partial class InfoListWindow : Window
{
    public InfoListWindow(Window owner, string title, IReadOnlyList<string> lines, bool numbered)
    {
        InitializeComponent();
        Owner = owner;
        Icon = AppIcon.LoadImage();
        // dùng cùng bảng màu (sáng/tối) với cửa sổ chính
        foreach (var key in new[] { "WindowBrush", "PanelBrush", "TextBrush", "BorderBrushX" })
            if (owner.Resources[key] is { } brush) Resources[key] = brush;

        Title = title;
        HeadingText.Text = title;
        CloseButton.Content = Loc.T("info.close");
        for (var i = 0; i < lines.Count; i++)
            Rows.Children.Add(MakeRow(numbered ? (i + 1).ToString() : "✓", lines[i], numbered));
    }

    private static UIElement MakeRow(string marker, string text, bool badge)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UIElement mark = badge
            ? new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = marker, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            }
            : new TextBlock { Text = marker, Foreground = new SolidColorBrush(Color.FromRgb(34, 160, 90)), FontWeight = FontWeights.Bold, FontSize = 16, VerticalAlignment = VerticalAlignment.Top };
        row.Children.Add(mark);

        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 21, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tb, 1);
        row.Children.Add(tb);
        return row;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

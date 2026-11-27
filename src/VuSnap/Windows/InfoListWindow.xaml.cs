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
        ThemeManager.HookWindowChrome(this, () => ThemeManager.IsDark(SettingsService.Load().Theme));

        Title = title;
        HeadingText.Text = title;
        CloseButton.Content = Loc.T("info.close");
        for (var i = 0; i < lines.Count; i++)
            Rows.Children.Add(MakeRow(numbered ? (i + 1).ToString() : "✓", lines[i], numbered));
    }

    private UIElement MakeRow(string marker, string text, bool badge)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            BorderThickness = new Thickness(1),
        };
        card.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrushX");

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UIElement mark;
        if (badge)
        {
            var circle = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = marker, Foreground = Brushes.White, FontSize = 11.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            circle.SetResourceReference(Border.BackgroundProperty, "BrandGradientBrush");
            mark = circle;
        }
        else
        {
            mark = new TextBlock
            {
                Text = marker,
                Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                FontWeight = FontWeights.Bold, FontSize = 15, VerticalAlignment = VerticalAlignment.Top,
            };
        }
        row.Children.Add(mark);

        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tb, 1);
        row.Children.Add(tb);
        card.Child = row;
        return card;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

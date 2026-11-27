using System.Windows;
using VuSnap.Services;

namespace VuSnap.Windows;

/// <summary>Cửa sổ nhỏ giới thiệu app và người làm.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        Icon = AppIcon.LoadImage();
        LogoImage.Source = AppIcon.LoadLogo();
        ThemeManager.HookWindowChrome(this, () => ThemeManager.IsDark(SettingsService.Load().Theme));

        Title = Loc.T("info.title");
        NameText.Text = AppInfo.AppName;
        TaglineText.Text = Loc.T("about.heading");
        DescText.Text = Loc.T("about.desc");
        AuthorLabel.Text = Loc.T("info.author"); AuthorValue.Text = AppInfo.Author;
        VersionLabel.Text = Loc.T("gen.version"); VersionValue.Text = AppInfo.Version;
        WebsiteLabel.Text = Loc.T("info.website"); WebsiteText.Text = AppInfo.WebsiteUrl;
        BuiltLabel.Text = Loc.T("info.built"); BuiltValue.Text = "C# · WPF · .NET 10 · Material Design Icons";
        ThanksText.Text = Loc.T("info.thanks");
        CopyrightText.Text = "© 2026 vudt · © 2026 Tokisaki Nino (upstream) · MIT";
        UpdateButton.Content = "⟳ " + Loc.T("about.update");
        DonateButton.Content = "♥ " + Loc.T("about.donate");
        CloseButton.Content = Loc.T("info.close");
    }

    private void Website_Click(object sender, RoutedEventArgs e)
    {
        if (!AppInfo.OpenUrl(AppInfo.WebsiteUrl)) MessageBox.Show(this, Loc.T("about.openFailed"), AppInfo.AppName);
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (!AppInfo.OpenUrl(AppInfo.ReleasesUrl)) MessageBox.Show(this, Loc.T("about.openFailed"), AppInfo.AppName);
    }

    private void Donate_Click(object sender, RoutedEventArgs e)
    {
        if (!AppInfo.OpenUrl(AppInfo.DonateUrl)) MessageBox.Show(this, Loc.T("about.openFailed"), AppInfo.AppName);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

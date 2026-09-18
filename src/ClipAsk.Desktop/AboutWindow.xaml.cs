using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace ClipAsk.Desktop;

internal partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = typeof(AboutWindow).Assembly.GetName().Version;
        VersionText.Text = version is null ? "Version 0.1.0" : $"Version {version.Major}.{version.Minor}.{version.Build}";
        SourceInitialized += (_, _) => NativeMethods.EnableRoundedCorners(this);
    }

    internal string LicenseSummary => LicenseSummaryText.Text;

    private void LicenseClick(object sender, RoutedEventArgs e) => OpenIncludedFile("LICENSE", "The GPL license file was not found beside ClipAsk.");

    private void NoticesClick(object sender, RoutedEventArgs e) => OpenIncludedFile("THIRD-PARTY-NOTICES.md", "The third-party notices file was not found beside ClipAsk.");

    private void OpenIncludedFile(string fileName, string missingMessage)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            MessageBox.Show(this, missingMessage, "ClipAsk", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Windows could not open {fileName}.", "ClipAsk", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}

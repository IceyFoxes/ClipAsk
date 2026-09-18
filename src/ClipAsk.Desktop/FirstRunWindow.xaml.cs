using System.Windows;
using System.Windows.Input;

namespace ClipAsk.Desktop;

internal partial class FirstRunWindow : Window
{
    public FirstRunWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeMethods.EnableRoundedCorners(this);
    }

    internal string AccountGuidance => AccountGuidanceText.Text;

    private void ContinueClick(object sender, RoutedEventArgs e) => Close();

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

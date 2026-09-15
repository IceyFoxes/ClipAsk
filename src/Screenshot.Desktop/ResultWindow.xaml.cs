using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Screenshot.Core.Diagnostics;

namespace Screenshot.Desktop;

internal partial class ResultWindow : Window
{
    public const double CardWidth = 480;
    public const double CardHeight = 220;

    private bool allowClose;
    private bool isConnected;
    private bool isBusy;
    private string accountText = "Disconnected";
    private string welcomeText = "Capture a question with Ctrl+Alt+S.";
    private string status = string.Empty;

    public ResultWindow()
    {
        InitializeComponent();
        Width = CardWidth;
        Height = CardHeight;
        ShowActivated = false;
        UpdateControls();
    }

    public event Action? CaptureRequested;
    public event Action? ConnectRequested;
    public event Action? AnswerRequested;
    public event Action? StopRequested;
    public event Action? DismissRequested;

    public BitmapSource? Preview => PreviewImage.Source as BitmapSource;
    public bool IsBusy => isBusy;
    public bool IsCopyEnabled => CopyButton.IsEnabled;
    public Visibility StopVisibility => StopButton.Visibility;
    public Visibility PrimaryActionVisibility => PrimaryActionButton.Visibility;
    public bool IsActionsMenuOpen => ActionsMenu.IsOpen;

    public void SetPreview(BitmapSource source)
    {
        PreviewImage.Source = source;
        AnswerText.Text = string.Empty;
        SetTiming(null);
        isBusy = false;
        UpdateControls();
    }

    public void ClearPreview()
    {
        PreviewImage.Source = null;
        AnswerText.Text = string.Empty;
        SetTiming(null);
        isBusy = false;
        UpdateControls();
    }

    public void SetAnswer(string answer)
    {
        AnswerText.Text = answer;
        UpdateControls();
    }

    public string Answer => AnswerText.Text;

    public void ClearAnswer()
    {
        AnswerText.Text = string.Empty;
        UpdateControls();
    }

    public void SetBusy(bool busy)
    {
        isBusy = busy;
        UpdateControls();
    }

    public void SetStatus(string value)
    {
        status = value ?? string.Empty;
        StatusText.Text = status;
        StatusText.ToolTip = status;
        UpdateControls();
    }

    public void SetWelcome(string text)
    {
        welcomeText = text;
        UpdateControls();
    }

    public void SetAccount(string text, bool connected)
    {
        accountText = text;
        isConnected = connected;
        UpdateControls();
    }

    public void SetTiming(CaptureTiming? timing)
    {
        FirstTextItem.Header = $"First text received: {(timing?.FirstTextDelay is { } first ? Format(first) : "—")}";
        CompletionItem.Header = $"Response complete: {(timing?.CompletionDelay is { } complete ? Format(complete) : "—")}";
        UpdateControls();
    }

    public void CopyImageToClipboard()
    {
        if (Preview is null)
        {
            SetStatus("Capture an image first");
            return;
        }
        try
        {
            Clipboard.SetImage(Preview);
            SetStatus("Image copied");
        }
        catch (ExternalException)
        {
            SetStatus("Clipboard is busy; try again");
        }
    }

    public void CopyAnswerToClipboard()
    {
        try
        {
            Clipboard.SetText(AnswerText.Text);
            SetStatus("Answer copied");
        }
        catch (ExternalException)
        {
            SetStatus("Clipboard is busy; try again");
        }
    }

    public void CloseForExit()
    {
        allowClose = true;
        Close();
    }

    public void DismissForSmoke()
    {
        Hide();
        DismissRequested?.Invoke();
    }

    private void UpdateControls()
    {
        var hasPreview = Preview is not null;
        var hasAnswer = !string.IsNullOrWhiteSpace(AnswerText.Text);
        HeaderTitle.Text = isBusy ? "Answering…" : hasPreview ? "Answer" : "Screenshot";
        PreviewColumn.Width = hasPreview ? new GridLength(104) : new GridLength(0);
        GapColumn.Width = hasPreview ? new GridLength(12) : new GridLength(0);
        PreviewContainer.Visibility = hasPreview ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = hasAnswer;
        CopyImageItem.IsEnabled = hasPreview;
        AnswerAgainItem.IsEnabled = hasPreview && isConnected && !isBusy;
        ConnectionItem.Header = isConnected ? "Disconnect ChatGPT" : "Connect ChatGPT";
        AccountDetailsItem.Header = accountText;
        PrimaryActionButton.Content = !isConnected ? "Connect ChatGPT" : !hasPreview ? "Capture" : "Answer";
        var primaryNeeded = !isBusy && (!isConnected || !hasPreview || !hasAnswer);
        PrimaryActionButton.Visibility = primaryNeeded ? Visibility.Visible : Visibility.Collapsed;
        if (!hasAnswer && !isBusy)
        {
            HintText.Text = !hasPreview ? welcomeText : !isConnected ? "Connect ChatGPT to answer." : "Ready to answer.";
            HintText.Visibility = Visibility.Visible;
        }
        else
        {
            HintText.Visibility = Visibility.Collapsed;
        }
        var routine = status is "" or "Ready" or "Response complete" or "Preparing answer…";
        Footer.Visibility = primaryNeeded || !routine ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = status;
        StatusText.ToolTip = status;
    }

    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void MoreClick(object sender, RoutedEventArgs e)
    {
        ActionsMenu.PlacementTarget = MoreButton;
        ActionsMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ActionsMenu.IsOpen = true;
    }

    private void PrimaryActionClick(object sender, RoutedEventArgs e)
    {
        if (!isConnected)
            ConnectRequested?.Invoke();
        else if (Preview is null)
            CaptureRequested?.Invoke();
        else
            AnswerRequested?.Invoke();
    }

    private void CaptureClick(object sender, RoutedEventArgs e) => CaptureRequested?.Invoke();
    private void ConnectClick(object sender, RoutedEventArgs e) => ConnectRequested?.Invoke();
    private void AnswerClick(object sender, RoutedEventArgs e) => AnswerRequested?.Invoke();
    private void StopClick(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    private void CopyImageClick(object sender, RoutedEventArgs e) => CopyImageToClipboard();
    private void CopyAnswerClick(object sender, RoutedEventArgs e) => CopyAnswerToClipboard();

    private void CloseClick(object sender, RoutedEventArgs e)
    {
        Hide();
        DismissRequested?.Invoke();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            Hide();
            DismissRequested?.Invoke();
            return;
        }
        base.OnClosing(e);
    }

    private static string Format(TimeSpan value) => $"{value.TotalMilliseconds:F0} ms";
}

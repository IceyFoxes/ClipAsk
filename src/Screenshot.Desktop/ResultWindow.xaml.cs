using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Screenshot.Core.Capture;
using Screenshot.Core.Diagnostics;

namespace Screenshot.Desktop;

internal partial class ResultWindow : Window
{
    public const double InitialWidth = 420;
    public const double InitialHeight = 228;

    private bool allowClose;
    private bool isConnected;
    private bool isBusy;
    private string accountText = "Disconnected";
    private string answer = string.Empty;
    private string modelText = "Automatic";
    private string welcomeText = "Capture a question with Ctrl+Alt+S.";
    private string status = string.Empty;

    public ResultWindow()
    {
        InitializeComponent();
        Width = InitialWidth;
        Height = InitialHeight;
        ShowActivated = false;
        UpdateControls();
    }

    public event Action? CaptureRequested;
    public event Action? ConnectRequested;
    public event Action? AnswerRequested;
    public event Action? AnswerOptionsRequested;
    public event Action? StopRequested;
    public event Action? DismissRequested;

    public BitmapSource? Preview => PreviewImage.Source as BitmapSource;
    public bool IsBusy => isBusy;
    public bool IsCopyEnabled => CopyButton.IsEnabled;
    public Visibility StopVisibility => StopButton.Visibility;
    public Visibility PrimaryActionVisibility => PrimaryActionButton.Visibility;
    public bool IsActionsMenuOpen => ActionsMenu.IsOpen;

    public void SetPreview(BitmapSource source, ResultLayout layout)
    {
        PreviewImage.Source = source;
        SetAnswerCore(string.Empty);
        SetTiming(null);
        isBusy = false;
        ApplyLayout(layout);
        UpdateControls();
    }

    public void ClearPreview()
    {
        PreviewImage.Source = null;
        SetAnswerCore(string.Empty);
        SetTiming(null);
        isBusy = false;
        ApplyInitialLayout();
        UpdateControls();
    }

    public void SetAnswer(string answer)
    {
        SetAnswerCore(answer);
        UpdateControls();
    }

    public string Answer => answer;

    public void ClearAnswer()
    {
        SetAnswerCore(string.Empty);
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

    public void SetModel(string text)
    {
        modelText = string.IsNullOrWhiteSpace(text) ? "Automatic" : text;
        ModelDetailsItem.Header = $"Model: {modelText}";
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
            Clipboard.SetText(answer);
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
        var hasAnswer = !string.IsNullOrWhiteSpace(answer);
        PreviewContainer.Visibility = hasPreview ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
        CopyButton.IsEnabled = hasAnswer;
        CopyImageItem.IsEnabled = hasPreview;
        AnswerAgainItem.IsEnabled = hasPreview && isConnected && !isBusy;
        ConnectionItem.Header = isConnected ? "Disconnect ChatGPT" : "Connect ChatGPT";
        AccountDetailsItem.Header = accountText;
        PrimaryActionButton.Content = !isConnected ? "Connect ChatGPT" : !hasPreview ? "Capture" : "Answer";
        var primaryNeeded = !isBusy && (!isConnected || !hasPreview || !hasAnswer);
        PrimaryActionButton.Visibility = primaryNeeded ? Visibility.Visible : Visibility.Collapsed;
        if (!hasAnswer)
        {
            HintText.Text = isBusy ? "Looking…" : !hasPreview ? welcomeText : !isConnected ? "Connect ChatGPT to answer." : "Ready to answer.";
            HintPanel.Visibility = Visibility.Visible;
        }
        else
        {
            HintPanel.Visibility = Visibility.Collapsed;
        }
        ActivityBar.Visibility = isBusy && !hasAnswer ? Visibility.Visible : Visibility.Collapsed;
        var routine = status is "" or "Ready" or "Response complete" or "Preparing answer…";
        Footer.Visibility = primaryNeeded || (!isBusy && !routine) ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = status;
        StatusText.ToolTip = status;
    }

    private void SetAnswerCore(string value)
    {
        answer = value ?? string.Empty;
        AnswerDocument.Document = AnswerDocumentRenderer.Create(answer);
    }

    private void ApplyLayout(ResultLayout layout)
    {
        Width = layout.WindowWidth;
        Height = layout.WindowHeight;
        PreviewContainer.Width = layout.PreviewWidth;
        PreviewContainer.Height = layout.PreviewHeight;
        PreviewImage.Width = layout.PreviewWidth;
        PreviewImage.Height = layout.PreviewHeight;
        AnswerPane.Width = layout.AnswerWidth;
        AnswerPane.HorizontalAlignment = HorizontalAlignment.Left;

        if (layout.Mode == ResultLayoutMode.Stacked)
        {
            AnswerTopRow.Height = new GridLength(0);
            FirstRow.Height = new GridLength(layout.PreviewHeight);
            LayoutGapRow.Height = new GridLength(12);
            SecondRow.Height = new GridLength(1, GridUnitType.Star);
            FirstColumn.Width = new GridLength(1, GridUnitType.Star);
            LayoutGapColumn.Width = new GridLength(0);
            SecondColumn.Width = new GridLength(0);
            Grid.SetRow(PreviewContainer, 0);
            Grid.SetColumn(PreviewContainer, 0);
            Grid.SetRow(AnswerPane, 2);
            Grid.SetColumn(AnswerPane, 0);
        }
        else
        {
            AnswerTopRow.Height = new GridLength(0);
            FirstRow.Height = new GridLength(1, GridUnitType.Star);
            LayoutGapRow.Height = new GridLength(0);
            SecondRow.Height = new GridLength(0);
            FirstColumn.Width = new GridLength(layout.PreviewWidth);
            LayoutGapColumn.Width = new GridLength(12);
            SecondColumn.Width = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(PreviewContainer, 0);
            Grid.SetColumn(PreviewContainer, 0);
            Grid.SetRow(AnswerPane, 0);
            Grid.SetColumn(AnswerPane, 2);
        }
    }

    private void ApplyInitialLayout()
    {
        Width = InitialWidth;
        Height = InitialHeight;
        FirstRow.Height = new GridLength(1, GridUnitType.Star);
        LayoutGapRow.Height = new GridLength(0);
        SecondRow.Height = new GridLength(0);
        FirstColumn.Width = new GridLength(1, GridUnitType.Star);
        LayoutGapColumn.Width = new GridLength(0);
        SecondColumn.Width = new GridLength(0);
        AnswerTopRow.Height = new GridLength(0);
        AnswerPane.Width = double.NaN;
        AnswerPane.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(AnswerPane, 0);
        Grid.SetColumn(AnswerPane, 0);
    }

    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        Hide();
        DismissRequested?.Invoke();
        e.Handled = true;
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
    private void AnswerOptionsClick(object sender, RoutedEventArgs e) => AnswerOptionsRequested?.Invoke();
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

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipAsk.Core.Capture;
using ClipAsk.Core.Diagnostics;
using ClipAsk.Core.Providers;

namespace ClipAsk.Desktop;

internal partial class ResultWindow : Window
{
    public const double InitialWidth = 420;
    public const double InitialHeight = 228;
    private const double TitleHeight = 40;
    private const double ContentInsets = 32;
    private const double LayoutGap = 12;
    private const double CompactPromptHeight = 48;
    private const double AnswerContentTopSpacing = 6;
    private const double MinimumResponseHeight = 88;
    private const double PreparingResponseHeight = 112;
    private const int StreamingResizeCharacterStep = 96;

    private bool allowClose;
    private bool isConnected;
    private bool isBusy;
    private bool instructionMode;
    private bool instructionReady;
    private bool startupEnabled;
    private bool answerComplete;
    private bool resizeQueued;
    private bool programmaticResizePending;
    private bool userSizedWindow;
    private bool windowMoveInProgress;
    private bool resizeAfterWindowMove;
    private bool restartManagerShutdownRequested;
    private int programmaticResizeGeneration;
    private int lastMeasuredAnswerLength;
    private double currentAnswerPaneHeight;
    private ResultLayout? currentLayout;
    private string accountText = "Disconnected";
    private string answer = string.Empty;
    private string modelText = "Automatic (resolved per capture)";
    private string welcomeText = "Select anything on screen with Ctrl+Alt+S.";
    private string status = string.Empty;

    public ResultWindow()
    {
        InitializeComponent();
        Width = InitialWidth;
        Height = InitialHeight;
        ShowActivated = false;
        SourceInitialized += (_, _) =>
        {
            NativeMethods.EnableRoundedCorners(this);
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(WindowProcedure);
        };
        UpdateControls();
    }

    public event Action? CaptureRequested;
    public event Action? ConnectRequested;
    public event Action? AnswerRequested;
    public event Action? AnswerOptionsRequested;
    public event Action? AboutRequested;
    public event Action? SaveImageRequested;
    public event Action<string>? InstructionSubmitted;
    public event Action<bool>? StartupChanged;
    public event Action? StopRequested;
    public event Action? DismissRequested;
    public event Action? MovedByUser;
    public event Action? RestartManagerShutdownRequested;

    public BitmapSource? Preview => PreviewImage.Source as BitmapSource;
    public bool IsBusy => isBusy;
    public bool IsCopyEnabled => CopyButton.IsEnabled;
    public Visibility StopVisibility => StopButton.Visibility;
    public Visibility PrimaryActionVisibility => PrimaryActionButton.Visibility;
    public bool IsActionsMenuOpen => ActionsMenu.IsOpen;
    public bool IsActionsMenuTargetingPreview => ReferenceEquals(ActionsMenu.PlacementTarget, PreviewContainer);
    public bool IsSaveImageEnabled => SaveImageItem.IsEnabled;
    public bool IsUserResizeEnabled => ResizeMode == ResizeMode.CanResize;
    public double PreviewDisplayWidth => PreviewContainer.Width;
    public double PreviewDisplayHeight => PreviewContainer.Height;
    public Visibility InstructionComposerVisibility => InstructionComposer.Visibility;
    public bool IsInstructionSendEnabled => InstructionSendButton.IsEnabled;
    internal void SetInstructionForSmoke(string value) => InstructionTextBox.Text = value;
    internal void PrepareImageActionsForSmoke() => ConfigureActionsMenu(PreviewContainer, System.Windows.Controls.Primitives.PlacementMode.MousePoint);
    internal void BeginWindowMoveForSmoke() => windowMoveInProgress = true;
    internal void EndWindowMoveForSmoke() => CompleteWindowMove(false);
    internal void ResizeForSmoke(double width, double height)
    {
        userSizedWindow = true;
        programmaticResizePending = false;
        Width = width;
        Height = height;
        ApplyUserSizedLayout(width, height);
    }
    internal IntPtr ProcessNativeMessageForSmoke(int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        WindowProcedure(IntPtr.Zero, message, wParam, lParam, ref handled);

    private IntPtr WindowProcedure(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!NativeMethods.IsRestartManagerMessage(message, lParam))
            return IntPtr.Zero;
        if (message == NativeMethods.WmQueryEndSession)
        {
            handled = true;
            return new IntPtr(1);
        }
        if (message == NativeMethods.WmEndSession && wParam != IntPtr.Zero)
        {
            handled = true;
            if (!restartManagerShutdownRequested)
            {
                restartManagerShutdownRequested = true;
                RestartManagerShutdownRequested?.Invoke();
            }
        }
        return IntPtr.Zero;
    }

    public void SetPreview(BitmapSource source, ResultLayout layout)
    {
        currentLayout = layout;
        userSizedWindow = false;
        ResetInstructionEntry();
        PreviewImage.Source = source;
        SetAnswerCore(string.Empty);
        ResetAnswerSizing();
        SetTiming(null);
        isBusy = false;
        ApplyLayout(layout);
        UpdateControls();
    }

    public void ClearPreview()
    {
        currentLayout = null;
        userSizedWindow = false;
        ResetInstructionEntry();
        PreviewImage.Source = null;
        SetAnswerCore(string.Empty);
        ResetAnswerSizing();
        SetTiming(null);
        isBusy = false;
        ApplyInitialLayout();
        UpdateControls();
    }

    public void SetAnswer(string answer, bool complete = false)
    {
        var wasEmpty = string.IsNullOrWhiteSpace(this.answer);
        SetAnswerCore(answer);
        answerComplete = complete;
        if (!string.IsNullOrWhiteSpace(this.answer))
        {
            if (wasEmpty)
            {
                ResizeAnswerPane(PreparingResponseHeight);
            }
            if (complete)
            {
                ScheduleAnswerResize();
            }
            else if (this.answer.Length - lastMeasuredAnswerLength >= StreamingResizeCharacterStep)
            {
                ScheduleAnswerResize();
            }
        }
        UpdateControls();
    }

    public string Answer => answer;

    public void ClearAnswer()
    {
        SetAnswerCore(string.Empty);
        answerComplete = false;
        ResetAnswerSizing();
        ResizeForCurrentState();
        UpdateControls();
    }

    public void SetBusy(bool busy)
    {
        var wasBusy = isBusy;
        isBusy = busy;
        UpdateControls();
        if (instructionMode)
            ResizeForCurrentState();
        else if (busy && string.IsNullOrWhiteSpace(answer))
            ResizeAnswerPane(PreparingResponseHeight);
        else if (wasBusy && !busy && !string.IsNullOrWhiteSpace(answer))
            ScheduleAnswerResize();
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
        ResizeForCurrentState();
    }

    public void BeginInstructionEntry(bool ready)
    {
        instructionMode = true;
        instructionReady = ready;
        InstructionTextBox.Text = string.Empty;
        InstructionComposer.Visibility = Visibility.Visible;
        AnswerTopRow.Height = new GridLength(48);
        SetStatus(!isConnected ? "Connect ChatGPT to continue" : ready ? "Add an instruction and press Enter" : "Preparing selection…");
        ResizeForCurrentState();
    }

    public void SetInstructionReady(bool ready)
    {
        instructionReady = ready;
        if (instructionMode)
            SetStatus(!isConnected ? "Connect ChatGPT to continue" : ready ? "Add an instruction and press Enter" : "Preparing selection…");
        else
            UpdateControls();
    }

    public void FocusInstruction()
    {
        if (!instructionMode)
            return;
        Activate();
        InstructionTextBox.Focus();
        Keyboard.Focus(InstructionTextBox);
    }

    public void SetModel(string text)
    {
        modelText = string.IsNullOrWhiteSpace(text) ? "Automatic (resolved per capture)" : text;
        ModelDetailsItem.Header = $"Model: {modelText}";
    }

    public void SetStartupEnabled(bool enabled)
    {
        startupEnabled = enabled;
        StartupItem.Header = enabled ? "✓  Start ClipAsk on startup" : "Start ClipAsk on startup";
    }

    public void SetTiming(CaptureTiming? timing)
    {
        UpdateControls();
    }

    public void SetRateLimits(AccountRateLimits? limits)
    {
        var window = limits?.Primary;
        if (window is null)
        {
            UsageDetailsItem.Header = isConnected ? "Usage: unavailable" : "Usage: sign in to view";
            ResetDetailsItem.Header = "Reset: —";
            return;
        }

        var remaining = Math.Clamp(100 - window.UsedPercent, 0, 100);
        UsageDetailsItem.Header = $"{FormatWindow(window.Duration)} usage: {remaining:0}% left";
        ResetDetailsItem.Header = $"Reset: {FormatReset(window.ResetsAt - DateTimeOffset.Now)}";
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
            SetStatus("Response copied");
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
        var hasInstruction = !string.IsNullOrWhiteSpace(InstructionTextBox.Text);
        PreviewContainer.Visibility = hasPreview ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.Visibility = isBusy ? Visibility.Collapsed : Visibility.Visible;
        CopyButton.IsEnabled = hasAnswer;
        CopyImageItem.IsEnabled = hasPreview;
        SaveImageItem.IsEnabled = hasPreview;
        AnswerAgainItem.IsEnabled = hasPreview && isConnected && !isBusy && !instructionMode;
        ConnectionItem.Header = isConnected ? "Disconnect ChatGPT" : "Connect ChatGPT";
        AccountDetailsItem.Header = accountText;
        InstructionPlaceholder.Visibility = hasInstruction ? Visibility.Collapsed : Visibility.Visible;
        InstructionSendButton.IsEnabled = instructionMode && instructionReady && isConnected && hasInstruction;
        PrimaryActionButton.Content = !isConnected ? "Connect ChatGPT" : !hasPreview ? "Capture" : "Analyze";
        var primaryNeeded = !isBusy && (!isConnected || !hasPreview || (!hasAnswer && !instructionMode));
        PrimaryActionButton.Visibility = primaryNeeded ? Visibility.Visible : Visibility.Collapsed;
        if (instructionMode)
        {
            HintPanel.Visibility = Visibility.Collapsed;
        }
        else if (!hasAnswer)
        {
            HintText.Text = isBusy ? "Asking ChatGPT…" : !hasPreview ? welcomeText : !isConnected ? "Connect ChatGPT to analyze." : "Ready to analyze.";
            HintPanel.Visibility = Visibility.Visible;
        }
        else
        {
            HintPanel.Visibility = Visibility.Collapsed;
        }
        ActivityBar.Visibility = isBusy && !hasAnswer && !instructionMode ? Visibility.Visible : Visibility.Collapsed;
        var routine = status is "" or "Ready" or "Response complete" or "Asking ChatGPT…" or "Preparing selection…" or "Add an instruction and press Enter";
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
        SetProgrammaticSize(layout.WindowWidth, layout.WindowHeight);
        PreviewContainer.Width = layout.PreviewWidth;
        PreviewContainer.Height = layout.PreviewHeight;
        PreviewImage.Width = layout.PreviewWidth;
        PreviewImage.Height = layout.PreviewHeight;
        if (layout.Mode == ResultLayoutMode.Stacked)
        {
            AnswerPane.Width = double.NaN;
            AnswerPane.HorizontalAlignment = HorizontalAlignment.Stretch;
            AnswerTopRow.Height = new GridLength(instructionMode ? 48 : 0);
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
            AnswerPane.Width = layout.AnswerWidth;
            AnswerPane.HorizontalAlignment = HorizontalAlignment.Left;
            AnswerTopRow.Height = new GridLength(instructionMode ? 48 : 0);
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
        ResizeForCurrentState();
    }

    private void ApplyInitialLayout()
    {
        SetProgrammaticSize(InitialWidth, InitialHeight);
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

    private void ResizeForCurrentState()
    {
        if (currentLayout is null)
            return;
        if (instructionMode)
        {
            ResizeAnswerPane(isConnected ? CompactPromptHeight : 96);
            return;
        }
        if (string.IsNullOrWhiteSpace(answer))
        {
            ResizeAnswerPane(isBusy ? PreparingResponseHeight : currentLayout.Value.AnswerHeight);
            return;
        }
        ResizeAnswerPane(Math.Max(PreparingResponseHeight, currentAnswerPaneHeight));
        ScheduleAnswerResize();
    }

    private void ResizeAnswerPane(double requestedHeight)
    {
        if (currentLayout is not { } layout)
            return;
        var fixedHeight = TitleHeight + ContentInsets + (layout.Mode == ResultLayoutMode.Stacked ? layout.PreviewHeight + LayoutGap : 0);
        var minimumAnswerHeight = instructionMode ? CompactPromptHeight : MinimumResponseHeight;
        var maximumAnswerHeight = Math.Max(minimumAnswerHeight, layout.MaximumWindowHeight - fixedHeight);
        var answerHeight = Math.Clamp(requestedHeight, minimumAnswerHeight, maximumAnswerHeight);
        currentAnswerPaneHeight = answerHeight;
        if (userSizedWindow)
        {
            ApplyUserSizedLayout(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
            return;
        }
        if (layout.Mode == ResultLayoutMode.Stacked)
        {
            SecondRow.Height = new GridLength(answerHeight);
            SetProgrammaticHeight(Math.Min(layout.MaximumWindowHeight, fixedHeight + answerHeight));
        }
        else
        {
            FirstRow.Height = new GridLength(1, GridUnitType.Star);
            var contentHeight = Math.Max(layout.PreviewHeight, answerHeight);
            SetProgrammaticHeight(Math.Min(layout.MaximumWindowHeight, TitleHeight + ContentInsets + contentHeight));
        }
    }

    private void WindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (currentLayout is null || programmaticResizePending || e.NewSize.Width < 1 || e.NewSize.Height < 1)
            return;
        userSizedWindow = true;
        ApplyUserSizedLayout(e.NewSize.Width, e.NewSize.Height);
    }

    private void ApplyUserSizedLayout(double windowWidth, double windowHeight)
    {
        if (currentLayout is not { } layout || layout.PreviewWidth <= 0 || layout.PreviewHeight <= 0)
            return;

        var contentWidth = Math.Max(1, windowWidth - ContentInsets);
        var contentHeight = Math.Max(1, windowHeight - TitleHeight - ContentInsets);
        var aspectRatio = layout.PreviewWidth / layout.PreviewHeight;

        AnswerPane.Width = double.NaN;
        AnswerPane.HorizontalAlignment = HorizontalAlignment.Stretch;
        AnswerTopRow.Height = new GridLength(instructionMode ? 48 : 0);
        PreviewContainer.HorizontalAlignment = HorizontalAlignment.Center;
        PreviewContainer.VerticalAlignment = VerticalAlignment.Top;

        double previewWidth;
        double previewHeight;
        if (layout.Mode == ResultLayoutMode.Stacked)
        {
            var minimumAnswerHeight = instructionMode
                ? isConnected ? CompactPromptHeight : 96
                : MinimumResponseHeight;
            var previewHeightLimit = Math.Max(1, contentHeight - LayoutGap - minimumAnswerHeight);
            previewWidth = Math.Min(contentWidth, previewHeightLimit * aspectRatio);
            previewHeight = previewWidth / aspectRatio;

            FirstRow.Height = new GridLength(previewHeight);
            LayoutGapRow.Height = new GridLength(LayoutGap);
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
            var usableWidth = Math.Max(1, contentWidth - LayoutGap);
            var minimumAnswerWidth = Math.Min(280, Math.Max(180, usableWidth * 0.55));
            var previewWidthLimit = Math.Max(1, usableWidth - minimumAnswerWidth);
            previewWidth = Math.Min(previewWidthLimit, contentHeight * aspectRatio);
            previewHeight = previewWidth / aspectRatio;

            FirstRow.Height = new GridLength(1, GridUnitType.Star);
            LayoutGapRow.Height = new GridLength(0);
            SecondRow.Height = new GridLength(0);
            FirstColumn.Width = new GridLength(previewWidth);
            LayoutGapColumn.Width = new GridLength(LayoutGap);
            SecondColumn.Width = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(PreviewContainer, 0);
            Grid.SetColumn(PreviewContainer, 0);
            Grid.SetRow(AnswerPane, 0);
            Grid.SetColumn(AnswerPane, 2);
        }

        PreviewContainer.Width = previewWidth;
        PreviewContainer.Height = previewHeight;
        PreviewImage.Width = previewWidth;
        PreviewImage.Height = previewHeight;
    }

    private void SetProgrammaticSize(double width, double height)
    {
        var generation = ++programmaticResizeGeneration;
        programmaticResizePending = true;
        Width = width;
        Height = height;
        ClearProgrammaticResizeFlag(generation);
    }

    private void SetProgrammaticHeight(double height)
    {
        var generation = ++programmaticResizeGeneration;
        programmaticResizePending = true;
        Height = height;
        ClearProgrammaticResizeFlag(generation);
    }

    private void ClearProgrammaticResizeFlag(int generation)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (generation == programmaticResizeGeneration)
                programmaticResizePending = false;
        }));
    }

    private void ScheduleAnswerResize()
    {
        if (currentLayout is null || string.IsNullOrWhiteSpace(answer))
            return;
        if (windowMoveInProgress)
        {
            resizeAfterWindowMove = true;
            return;
        }
        if (resizeQueued)
            return;
        resizeQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            resizeQueued = false;
            if (windowMoveInProgress)
            {
                resizeAfterWindowMove = true;
                return;
            }
            if (currentLayout is null || instructionMode || string.IsNullOrWhiteSpace(answer))
                return;
            lastMeasuredAnswerLength = answer.Length;
            AnswerDocument.ApplyTemplate();
            AnswerDocument.UpdateLayout();
            var viewer = FindVisualChild<ScrollViewer>(AnswerDocument);
            var contentHeight = viewer is null || !double.IsFinite(viewer.ExtentHeight)
                ? PreparingResponseHeight
                : viewer.ExtentHeight + AnswerContentTopSpacing;
            ResizeAnswerPane(Math.Max(MinimumResponseHeight, contentHeight));
        }));
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                return match;
            if (FindVisualChild<T>(child) is { } nested)
                return nested;
        }
        return null;
    }

    private void HeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        var moved = false;
        windowMoveInProgress = true;
        try
        {
            DragMove();
            moved = true;
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            CompleteWindowMove(moved);
        }
    }

    private void CompleteWindowMove(bool notifyMoved)
    {
        windowMoveInProgress = false;
        if (notifyMoved)
            MovedByUser?.Invoke();
        var shouldResize = resizeAfterWindowMove || !string.IsNullOrWhiteSpace(answer);
        resizeAfterWindowMove = false;
        if (shouldResize)
            ScheduleAnswerResize();
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        Hide();
        DismissRequested?.Invoke();
        e.Handled = true;
    }

    private void InstructionTextChanged(object sender, TextChangedEventArgs e) => UpdateControls();

    private void InstructionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        SubmitInstruction();
        e.Handled = true;
    }

    private void InstructionSendClick(object sender, RoutedEventArgs e) => SubmitInstruction();

    private void SubmitInstruction()
    {
        if (!InstructionSendButton.IsEnabled)
            return;
        var instruction = InstructionTextBox.Text.Trim();
        instructionMode = false;
        instructionReady = false;
        InstructionComposer.Visibility = Visibility.Collapsed;
        AnswerTopRow.Height = new GridLength(0);
        ResizeAnswerPane(PreparingResponseHeight);
        SetStatus("Asking ChatGPT…");
        InstructionSubmitted?.Invoke(instruction);
    }

    private void ResetInstructionEntry()
    {
        instructionMode = false;
        instructionReady = false;
        InstructionTextBox.Text = string.Empty;
        InstructionComposer.Visibility = Visibility.Collapsed;
        AnswerTopRow.Height = new GridLength(0);
    }

    private void MoreClick(object sender, RoutedEventArgs e)
    {
        OpenActionsMenu(MoreButton, System.Windows.Controls.Primitives.PlacementMode.Bottom);
    }

    private void CapturedImageRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Preview is null)
            return;
        OpenActionsMenu(PreviewContainer, System.Windows.Controls.Primitives.PlacementMode.MousePoint);
        e.Handled = true;
    }

    private void OpenActionsMenu(UIElement target, System.Windows.Controls.Primitives.PlacementMode placement)
    {
        ConfigureActionsMenu(target, placement);
        ActionsMenu.IsOpen = true;
    }

    private void ConfigureActionsMenu(UIElement target, System.Windows.Controls.Primitives.PlacementMode placement)
    {
        ActionsMenu.PlacementTarget = target;
        ActionsMenu.Placement = placement;
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
    private void AboutClick(object sender, RoutedEventArgs e) => AboutRequested?.Invoke();
    private void StartupClick(object sender, RoutedEventArgs e) => StartupChanged?.Invoke(!startupEnabled);
    private void StopClick(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    private void CopyImageClick(object sender, RoutedEventArgs e) => CopyImageToClipboard();
    private void SaveImageClick(object sender, RoutedEventArgs e) => SaveImageRequested?.Invoke();
    private void CopyAnswerClick(object sender, RoutedEventArgs e) => CopyAnswerToClipboard();
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

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

    private void ResetAnswerSizing()
    {
        lastMeasuredAnswerLength = 0;
        currentAnswerPaneHeight = 0;
        resizeAfterWindowMove = false;
    }

    private static string FormatWindow(TimeSpan duration) => duration.TotalHours >= 1
        ? $"{duration.TotalHours:0.#}-hour"
        : $"{duration.TotalMinutes:0}-minute";

    private static string FormatReset(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return "soon";
        if (remaining.TotalDays >= 1)
            return $"in {(int)remaining.TotalDays}d {remaining.Hours}h";
        if (remaining.TotalHours >= 1)
            return $"in {(int)remaining.TotalHours}h {remaining.Minutes}m";
        return $"in {Math.Max(1, remaining.Minutes)}m";
    }
}

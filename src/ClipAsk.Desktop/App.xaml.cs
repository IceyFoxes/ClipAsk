using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using ClipAsk.Core.Capture;
using ClipAsk.Core.Diagnostics;
using ClipAsk.Core.Providers;
using ClipAsk.Core.Protocol;

namespace ClipAsk.Desktop;

public partial class App : System.Windows.Application
{
    private const int HotkeyId = 1901;

    static App()
    {
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
    }

    private Mutex? instanceMutex;
    private bool ownsInstanceMutex;
    private HwndSource? hotkeySource;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private Forms.ToolStripMenuItem? trayStartupItem;
    private ResultWindow? result;
    private AboutWindow? aboutWindow;
    private CaptureOverlay? overlay;
    private CodexAnswerProvider? provider;
    private BitmapSource? currentImage;
    private byte[]? currentPng;
    private IntPtr originalForeground;
    private readonly RequestGeneration generation = new();
    private CancellationTokenSource? answerCancellation;
    private CaptureTiming? timing;
    private string answerInstruction = string.Empty;
    private string currentInstruction = string.Empty;
    private bool currentInstructionUsesDefault = true;
    private string? selectedModel;
    private bool connected;
    private bool launchInBackground;
    private bool startupEnabled;
    private bool centerResultOnResize;
    private bool positioningResult;
    private double resultDipScale = 1;
    private System.Drawing.Rectangle? resultWorkArea;
    private bool shuttingDown;
    private bool cleanupDone;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        launchInBackground = e.Args.Any(argument => argument.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        try
        {
            if (TryHandleOfflineMode(e.Args))
                return;
            instanceMutex = new Mutex(true, "ClipAsk.Desktop.CurrentUser", out ownsInstanceMutex);
            if (!ownsInstanceMutex)
            {
                if (!launchInBackground)
                    MessageBox.Show("ClipAsk is already running in the tray.", "ClipAsk", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            InitializeApplication();
        }
        catch (Exception exception)
        {
            if (!launchInBackground)
                MessageBox.Show(DescribeUiException(exception), "ClipAsk could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private bool TryHandleOfflineMode(string[] args)
    {
        if (args.Length == 0)
            return false;
        if (args[0].Equals("--smoke-test", StringComparison.OrdinalIgnoreCase) && args.Length == 2)
        {
            SmokeRenderer.Run(args[1]);
            Shutdown();
            return true;
        }
        if (args[0].Equals("--check-provider", StringComparison.OrdinalIgnoreCase) && args.Length == 2)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
            {
                try
                {
                    await RunProviderProbeAsync(args[1]);
                    Shutdown();
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(DescribeUiException(exception));
                    Shutdown(1);
                }
            }));
            return true;
        }
        if (args[0].Equals("--uninstall-sign-out", StringComparison.OrdinalIgnoreCase) && args.Length == 1)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await using var cleanupProvider = CodexAnswerProvider.CreateDefault();
                    await cleanupProvider.DisconnectAsync(timeout.Token);
                    Shutdown();
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(DescribeUiException(exception));
                    Shutdown(1);
                }
            }));
            return true;
        }
        return false;
    }

    private void InitializeApplication()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        provider = CodexAnswerProvider.CreateDefault();
        provider.AccountChanged += OnAccountChanged;
        provider.RateLimitsChanged += OnRateLimitsChanged;
        result = new ResultWindow();
        result.CaptureRequested += () => RunUiAsync(BeginCaptureAsync);
        result.ConnectRequested += () => RunUiAsync(ToggleConnectionAsync);
        result.AnswerRequested += () => RunUiAsync(AnswerCurrentAsync);
        result.AnswerOptionsRequested += () => RunUiAsync(OpenAnswerOptionsAsync);
        result.AboutRequested += ShowAbout;
        result.SaveImageRequested += () => RunUiAsync(SaveCurrentImageAsync);
        result.InstructionSubmitted += instruction => RunUiAsync(() => SubmitInstructionAsync(instruction));
        result.StartupChanged += SetStartupEnabled;
        result.StopRequested += () => StopAnswer(true);
        result.DismissRequested += () => StopAnswer(true);
        result.MovedByUser += () => centerResultOnResize = false;
        result.SizeChanged += ResultSizeChanged;
        result.SetWelcome("Select anything on screen with Ctrl+Alt+S.");
        result.SetAccount("Checking ChatGPT account…", false);
        result.SetStatus("Ready");
        MainWindow = result;
        try
        {
            StartupRegistration.MigrateLegacyRegistration();
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            result.SetStatus("Could not update the old startup entry; check startup settings.");
        }
        startupEnabled = StartupRegistration.IsEnabled();
        result.SetStartupEnabled(startupEnabled);
        if (!launchInBackground)
            result.Show();
        trayIcon = LoadTrayIcon();
        tray = new Forms.NotifyIcon
        {
            Icon = trayIcon,
            Text = "ClipAsk",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        tray.DoubleClick += (_, _) => ShowResult();
        hotkeySource = new HwndSource(new HwndSourceParameters("ClipAskHotkey")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
            Width = 0,
            Height = 0
        });
        hotkeySource.AddHook(HotkeyWindowProc);
        if (!NativeMethods.RegisterHotKey(hotkeySource.Handle, HotkeyId, NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat, (uint)System.Windows.Forms.Keys.S))
        {
            result.SetStatus("Ctrl+Alt+S is unavailable; use Capture from the tray.");
            tray.Text = "ClipAsk — shortcut unavailable";
            if (launchInBackground)
            {
                tray.BalloonTipTitle = "ClipAsk shortcut unavailable";
                tray.BalloonTipText = "Ctrl+Alt+S is already in use. Choose Capture from the ClipAsk tray menu.";
                tray.BalloonTipIcon = Forms.ToolTipIcon.Warning;
                tray.ShowBalloonTip(8000);
            }
        }
        _ = RefreshAccountAsync();
        if (!launchInBackground && FirstRunStateStore.ShouldShow())
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ShowFirstRunGuidance));
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Capture", null, (_, _) => RunUiAsync(BeginCaptureAsync));
        menu.Items.Add("Show", null, (_, _) => ShowResult());
        menu.Items.Add("Connect ChatGPT", null, (_, _) => RunUiAsync(ToggleConnectionAsync));
        menu.Items.Add(new Forms.ToolStripSeparator());
        trayStartupItem = new Forms.ToolStripMenuItem("Start ClipAsk on startup")
        {
            Checked = startupEnabled,
            CheckOnClick = false
        };
        trayStartupItem.Click += (_, _) => SetStartupEnabled(!startupEnabled);
        menu.Items.Add(trayStartupItem);
        menu.Items.Add("About ClipAsk & licenses…", null, (_, _) => ShowAbout());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => RunUiAsync(ExitAsync));
        return menu;
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/clipask.ico", UriKind.Absolute))
            ?? throw new InvalidOperationException("The embedded ClipAsk icon could not be loaded.");
        using var stream = resource.Stream;
        using var source = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)source.Clone();
    }

    private void ShowAbout()
    {
        if (aboutWindow is not null)
        {
            aboutWindow.Activate();
            return;
        }
        aboutWindow = new AboutWindow();
        if (result?.IsVisible == true)
        {
            aboutWindow.Owner = result;
            aboutWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        aboutWindow.Closed += (_, _) => aboutWindow = null;
        aboutWindow.Show();
        aboutWindow.Activate();
    }

    private void ShowFirstRunGuidance()
    {
        if (result is null || !FirstRunStateStore.ShouldShow())
            return;
        var welcome = new FirstRunWindow { Owner = result };
        welcome.Closed += (_, _) => FirstRunStateStore.MarkSeen();
        welcome.ShowDialog();
    }

    private void SetStartupEnabled(bool enabled)
    {
        try
        {
            StartupRegistration.SetEnabled(enabled);
            startupEnabled = StartupRegistration.IsEnabled();
            result?.SetStartupEnabled(startupEnabled);
            if (trayStartupItem is not null)
                trayStartupItem.Checked = startupEnabled;
            result?.SetStatus(startupEnabled ? "ClipAsk will start on startup" : "Start on startup disabled");
        }
        catch (Exception exception)
        {
            result?.SetStartupEnabled(startupEnabled);
            if (trayStartupItem is not null)
                trayStartupItem.Checked = startupEnabled;
            result?.SetStatus(DescribeUiException(exception));
        }
    }

    private IntPtr HotkeyWindowProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            RunUiAsync(BeginCaptureAsync);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private async Task BeginCaptureAsync()
    {
        if (overlay is not null)
            return;
        generation.Next();
        StopAnswer(false);
        originalForeground = NativeMethods.GetForegroundWindow();
        result?.Hide();
        NativeMethods.DwmFlush();
        var monitor = NativeMethods.GetMonitorUnderCursor();
        var source = NativeMethods.CaptureMonitor(monitor.Bounds);
        currentPng = null;
        overlay = new CaptureOverlay(source, monitor.Bounds, (rect, intent) => CompleteSelection(source, monitor, rect, intent), CancelOverlay);
        overlay.Show();
        overlay.Activate();
        await Task.CompletedTask;
    }

    private void CompleteSelection(BitmapSource source, (System.Drawing.Rectangle Bounds, System.Drawing.Rectangle WorkArea) monitor, PixelRect crop, CaptureIntent intent)
    {
        timing = new CaptureTiming();
        var overlayHandle = overlay is null ? IntPtr.Zero : new WindowInteropHelper(overlay).Handle;
        var dpi = overlayHandle == IntPtr.Zero ? 96u : NativeMethods.GetDpiForWindow(overlayHandle);
        currentImage = new CroppedBitmap(source, new Int32Rect(crop.X, crop.Y, crop.Width, crop.Height));
        currentImage.Freeze();
        currentInstructionUsesDefault = intent == CaptureIntent.Automatic;
        currentInstruction = currentInstructionUsesDefault ? answerInstruction : string.Empty;
        overlay?.Close();
        overlay = null;
        var dipScale = 96d / dpi;
        var layout = ResultLayoutCalculator.Calculate(new(
            crop.Width * dipScale,
            crop.Height * dipScale,
            monitor.WorkArea.Width * dipScale,
            monitor.WorkArea.Height * dipScale));
        result!.SetPreview(currentImage, layout);
        result.CopyImageToClipboard();
        if (intent == CaptureIntent.Prompted)
            result.BeginInstructionEntry(false);
        ShowResultCentered(monitor.WorkArea, dipScale, intent == CaptureIntent.Prompted);
        if (intent == CaptureIntent.Prompted)
            result.FocusInstruction();
        else if (originalForeground != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(originalForeground);
        var captureTiming = timing;
        _ = EncodeAndAnswerAsync(currentImage, intent, captureTiming, generation.Next());
    }

    private async Task EncodeAndAnswerAsync(BitmapSource? image, CaptureIntent intent, CaptureTiming? captureTiming, long requestGeneration)
    {
        try
        {
            if (image is null)
                throw new InvalidOperationException("No screenshot is selected.");
            var png = await Task.Run(() => EncodePng(image)).ConfigureAwait(true);
            if (!generation.IsCurrent(requestGeneration))
                return;
            currentPng = png;
            if (intent == CaptureIntent.Automatic && connected)
                await StartAnswerAsync(requestGeneration, captureTiming, currentInstruction).ConfigureAwait(true);
            else if (intent == CaptureIntent.Prompted)
            {
                result?.SetTiming(null);
                result?.SetInstructionReady(true);
            }
            else
            {
                result?.SetTiming(null);
                result?.SetStatus("Connect ChatGPT, then click Analyze");
            }
        }
        catch (Exception exception)
        {
            if (generation.IsCurrent(requestGeneration))
                result?.SetStatus(DescribeUiException(exception));
        }
    }

    private async Task AnswerCurrentAsync()
    {
        if (currentPng is null)
        {
            result?.SetStatus("Capture a region first");
            return;
        }
        if (!connected)
        {
            result?.SetStatus("Connect ChatGPT before analyzing");
            return;
        }
        result?.ClearAnswer();
        timing = null;
        result?.SetTiming(null);
        await StartAnswerAsync(generation.Next(), null, currentInstruction);
    }

    private async Task SubmitInstructionAsync(string instruction)
    {
        if (currentPng is null)
        {
            result?.SetStatus("The selected image is still being prepared");
            return;
        }
        if (!connected)
        {
            result?.SetStatus("Connect ChatGPT before analyzing");
            return;
        }
        currentInstructionUsesDefault = false;
        currentInstruction = instruction;
        result?.ClearAnswer();
        timing = null;
        result?.SetTiming(null);
        await StartAnswerAsync(generation.Next(), null, currentInstruction);
    }

    private async Task SaveCurrentImageAsync()
    {
        var image = currentImage;
        if (image is null || result is null)
        {
            result?.SetStatus("Capture an image first");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save captured image",
            Filter = "PNG image (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"ClipAsk-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        };
        var initialDirectory = SaveLocationStore.Read();
        if (initialDirectory is null)
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(pictures))
                initialDirectory = pictures;
        }
        if (initialDirectory is not null)
            dialog.InitialDirectory = initialDirectory;
        if (dialog.ShowDialog(result) != true)
            return;

        var png = await Task.Run(() => EncodePng(image)).ConfigureAwait(true);
        await File.WriteAllBytesAsync(dialog.FileName, png).ConfigureAwait(true);
        SaveLocationStore.Remember(Path.GetDirectoryName(dialog.FileName));
        result.SetStatus("Image saved");
    }

    private async Task StartAnswerAsync(long requestGeneration, CaptureTiming? requestTiming, string? instruction)
    {
        result?.SetBusy(true);
        var localCancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref answerCancellation, localCancellation);
        previous?.Cancel();
        try
        {
            var options = new AnswerRequestOptions(instruction, selectedModel);
            await foreach (var update in provider!.AnswerAsync(currentPng!, options, localCancellation.Token, requestTiming).ConfigureAwait(true))
            {
                if (!generation.IsCurrent(requestGeneration))
                    return;
                switch (update.Kind)
                {
                    case AnswerUpdateKind.Text:
                        result?.SetAnswer(update.Text);
                        result?.SetTiming(requestTiming);
                        break;
                    case AnswerUpdateKind.Completed:
                        result?.SetAnswer(update.Text, true);
                        result?.SetTiming(requestTiming);
                        result?.SetStatus("Response complete");
                        _ = RefreshRateLimitsAsync();
                        break;
                    case AnswerUpdateKind.Failed:
                        result?.SetStatus(update.Text);
                        break;
                    case AnswerUpdateKind.Cancelled:
                        result?.SetStatus("Stopped");
                        break;
                    case AnswerUpdateKind.Status:
                        result?.SetStatus(update.Text);
                        break;
                    case AnswerUpdateKind.Model:
                        result?.SetModel(update.Text);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (generation.IsCurrent(requestGeneration))
                result?.SetStatus("Stopped");
        }
        catch (Exception exception)
        {
            if (generation.IsCurrent(requestGeneration))
                result?.SetStatus(DescribeUiException(exception));
        }
        finally
        {
            if (generation.IsCurrent(requestGeneration))
                result?.SetBusy(false);
            if (ReferenceEquals(answerCancellation, localCancellation))
                answerCancellation = null;
            localCancellation.Dispose();
        }
    }

    private async Task ToggleConnectionAsync()
    {
        if (connected)
        {
            StopAnswer(true);
            await provider!.DisconnectAsync();
            connected = false;
            result?.SetAccount("Disconnected", false);
            result?.SetRateLimits(null);
            result?.SetStatus("Stopped");
            return;
        }
        var uri = await provider!.BeginLoginAsync();
        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        result?.SetStatus("Complete sign-in in the system browser");
    }

    private async Task OpenAnswerOptionsAsync()
    {
        if (result is null || provider is null)
            return;
        IReadOnlyList<CodexModelSelection> models = Array.Empty<CodexModelSelection>();
        CodexModelSelection? automaticModel = null;
        if (connected)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                automaticModel = await provider.GetAutomaticModelAsync(timeout.Token).ConfigureAwait(true);
                models = await provider.GetAvailableModelsAsync(timeout.Token).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                result.SetStatus(DescribeUiException(exception));
            }
        }

        var dialog = new AnswerOptionsWindow(answerInstruction, selectedModel, automaticModel, models) { Owner = result };
        if (dialog.ShowDialog() != true)
            return;
        answerInstruction = dialog.Instruction;
        if (currentInstructionUsesDefault)
            currentInstruction = answerInstruction;
        selectedModel = dialog.SelectedModel;
        var selected = models.FirstOrDefault(model => model.Model == selectedModel);
        result.SetModel(selected is null
            ? automaticModel is null ? "Automatic (resolved per capture)" : $"Automatic → {automaticModel.DisplayName} · {automaticModel.ReasoningEffort}"
            : $"{selected.DisplayName} · {selected.ReasoningEffort}");
        result.SetStatus("Response options updated");
    }

    private async Task RefreshAccountAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var account = await provider!.GetAccountAsync(timeout.Token).ConfigureAwait(true);
            connected = account.IsConnected;
            result?.SetAccount(account.IsConnected ? $"ChatGPT · {account.Plan}" : "Disconnected", account.IsConnected);
            result?.SetStatus(account.IsConnected ? "Ready" : "Connect ChatGPT to analyze");
            if (account.IsConnected)
                await RefreshRateLimitsAsync(timeout.Token).ConfigureAwait(true);
            else
                result?.SetRateLimits(null);
        }
        catch (Exception exception)
        {
            connected = false;
            result?.SetAccount("Disconnected", false);
            result?.SetRateLimits(null);
            result?.SetStatus(DescribeUiException(exception));
        }
    }

    private void OnAccountChanged() => Dispatcher.BeginInvoke(new Action(() => _ = RefreshAccountAsync()));

    private void OnRateLimitsChanged(AccountRateLimits? limits) =>
        Dispatcher.BeginInvoke(new Action(() => result?.SetRateLimits(limits)));

    private async Task RefreshRateLimitsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            result?.SetRateLimits(await provider!.GetRateLimitsAsync(timeout.Token).ConfigureAwait(true));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result?.SetRateLimits(null);
        }
        catch
        {
            result?.SetRateLimits(null);
        }
    }

    private void StopAnswer(bool showStatus)
    {
        generation.Next();
        var cancellation = Interlocked.Exchange(ref answerCancellation, null);
        cancellation?.Cancel();
        result?.SetBusy(false);
        if (showStatus)
            result?.SetStatus("Stopped");
    }

    private void CancelOverlay()
    {
        if (overlay is null)
            return;
        overlay = null;
        result?.SetStatus("Stopped");
    }

    private void ShowResultCentered(System.Drawing.Rectangle workArea, double dipScale, bool activate)
    {
        if (result is null)
            return;
        resultWorkArea = workArea;
        resultDipScale = dipScale;
        centerResultOnResize = true;
        result.ShowActivated = activate;
        if (!result.IsVisible)
            result.Show();
        result.WindowState = WindowState.Normal;
        NativeMethods.RestoreWindow(result, activate);
        CenterResultWindow(result.Width, result.Height);
        if (activate)
            result.Activate();
        result.ShowActivated = false;
    }

    private void ResultSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (centerResultOnResize)
            CenterResultWindow(e.NewSize.Width, e.NewSize.Height);
        else
            KeepResultWindowVisible(e.NewSize.Width, e.NewSize.Height);
    }

    private void KeepResultWindowVisible(double widthDip, double heightDip)
    {
        if (result is null || !result.IsVisible || resultWorkArea is not { } workArea || positioningResult ||
            !NativeMethods.TryGetWindowBounds(result, out var currentBounds))
            return;
        var scale = resultDipScale > 0 ? resultDipScale : 1;
        var width = Math.Max(1, (int)Math.Round(widthDip / scale));
        var height = Math.Max(1, (int)Math.Round(heightDip / scale));
        var placement = PanelPlacement.KeepVisible(
            new(currentBounds.X, currentBounds.Y, currentBounds.Width, currentBounds.Height),
            new(workArea.X, workArea.Y, workArea.Width, workArea.Height),
            width,
            height);
        if (placement.X == currentBounds.X && placement.Y == currentBounds.Y &&
            placement.Width == currentBounds.Width && placement.Height == currentBounds.Height)
            return;
        positioningResult = true;
        try
        {
            NativeMethods.PositionWindow(result, new System.Drawing.Rectangle(placement.X, placement.Y, placement.Width, placement.Height));
        }
        finally
        {
            positioningResult = false;
        }
    }

    private void CenterResultWindow(double widthDip, double heightDip)
    {
        if (result is null || !result.IsVisible || resultWorkArea is not { } workArea || positioningResult)
            return;
        var scale = resultDipScale > 0 ? resultDipScale : 1;
        var width = Math.Max(1, (int)Math.Round(widthDip / scale));
        var height = Math.Max(1, (int)Math.Round(heightDip / scale));
        var placement = PanelPlacement.PlaceCentered(new(workArea.X, workArea.Y, workArea.Width, workArea.Height), width, height);
        positioningResult = true;
        try
        {
            NativeMethods.PositionWindow(result, new System.Drawing.Rectangle(placement.X, placement.Y, placement.Width, placement.Height));
        }
        finally
        {
            positioningResult = false;
        }
    }

    private void ShowResult()
    {
        if (result is null)
            return;
        result.Show();
        result.WindowState = WindowState.Normal;
        NativeMethods.RestoreWindow(result, true);
        result.Activate();
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
        return stream.ToArray();
    }

    private async Task RunProviderProbeAsync(string outputPath)
    {
        var state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipAsk", "Diagnostics", Guid.NewGuid().ToString("N"));
        var executable = Environment.GetEnvironmentVariable("CLIPASK_CODEX_PATH") ?? Path.Combine(AppContext.BaseDirectory, "codex.exe");
        await using var probe = new CodexAnswerProvider(new CodexLaunchOptions(executable, state));
        var account = await probe.GetAccountAsync();
        var config = await probe.ReadConfigAsync();
        var snapshot = CodexPolicySnapshot.Create(account, config);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task ExitAsync()
    {
        if (shuttingDown)
            return;
        shuttingDown = true;
        StopAnswer(false);
        overlay?.Close();
        overlay = null;
        if (provider is not null)
            await provider.DisposeAsync().ConfigureAwait(true);
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!cleanupDone)
        {
            cleanupDone = true;
            if (hotkeySource is not null)
                NativeMethods.UnregisterHotKey(hotkeySource.Handle, HotkeyId);
            hotkeySource?.Dispose();
            tray?.Dispose();
            trayIcon?.Dispose();
            if (!shuttingDown && provider is not null)
            {
                provider.StopOwnedProcessForShutdown();
                _ = provider.DisposeAsync().AsTask().ContinueWith(_ => { }, TaskScheduler.Default);
            }
            if (ownsInstanceMutex)
                instanceMutex?.ReleaseMutex();
            instanceMutex?.Dispose();
        }
        base.OnExit(e);
    }

    private static string DescribeUiException(Exception exception) => exception switch
    {
        JsonRpcException rpc => $"Codex operation failed ({rpc.Code}).",
        TimeoutException => "Codex operation timed out.",
        _ => exception.Message
    };

    private void RunUiAsync(Func<Task> action) => _ = RunUiBoundaryAsync(action);

    private async Task RunUiBoundaryAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            result?.SetStatus(DescribeUiException(exception));
        }
    }
}

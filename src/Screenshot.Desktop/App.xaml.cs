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
using Screenshot.Core.Capture;
using Screenshot.Core.Diagnostics;
using Screenshot.Core.Providers;
using Screenshot.Core.Protocol;

namespace Screenshot.Desktop;

public partial class App : System.Windows.Application
{
    private const int HotkeyId = 1901;
    private Mutex? instanceMutex;
    private bool ownsInstanceMutex;
    private HwndSource? hotkeySource;
    private Forms.NotifyIcon? tray;
    private ResultWindow? result;
    private CaptureOverlay? overlay;
    private CodexAnswerProvider? provider;
    private BitmapSource? currentImage;
    private byte[]? currentPng;
    private System.Drawing.Rectangle currentMonitor;
    private IntPtr originalForeground;
    private readonly RequestGeneration generation = new();
    private CancellationTokenSource? answerCancellation;
    private CaptureTiming? timing;
    private string answerInstruction = string.Empty;
    private string? selectedModel;
    private bool connected;
    private bool shuttingDown;
    private bool cleanupDone;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (TryHandleOfflineMode(e.Args))
                return;
            instanceMutex = new Mutex(true, "Screenshot.Desktop.CurrentUser", out ownsInstanceMutex);
            if (!ownsInstanceMutex)
            {
                MessageBox.Show("Screenshot is already running in the tray.", "Screenshot", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            InitializeApplication();
        }
        catch (Exception exception)
        {
            MessageBox.Show(DescribeUiException(exception), "Screenshot could not start", MessageBoxButton.OK, MessageBoxImage.Error);
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
        return false;
    }

    private void InitializeApplication()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        provider = CodexAnswerProvider.CreateDefault();
        provider.AccountChanged += OnAccountChanged;
        result = new ResultWindow();
        result.CaptureRequested += () => RunUiAsync(BeginCaptureAsync);
        result.ConnectRequested += () => RunUiAsync(ToggleConnectionAsync);
        result.AnswerRequested += () => RunUiAsync(AnswerCurrentAsync);
        result.AnswerOptionsRequested += () => RunUiAsync(OpenAnswerOptionsAsync);
        result.StopRequested += () => StopAnswer(true);
        result.DismissRequested += () => StopAnswer(true);
        result.SetWelcome("Capture a question with Ctrl+Alt+S.");
        result.SetAccount("Checking ChatGPT account…", false);
        result.SetStatus("Ready");
        result.Show();
        MainWindow = result;
        tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Screenshot",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        tray.DoubleClick += (_, _) => ShowResult();
        hotkeySource = new HwndSource(new HwndSourceParameters("ScreenshotHotkey")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0,
            Width = 0,
            Height = 0
        });
        hotkeySource.AddHook(HotkeyWindowProc);
        if (!NativeMethods.RegisterHotKey(hotkeySource.Handle, HotkeyId, NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat, (uint)System.Windows.Forms.Keys.S))
            result.SetStatus("Ctrl+Alt+S is unavailable; use Capture from the tray.");
        _ = RefreshAccountAsync();
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Capture", null, (_, _) => RunUiAsync(BeginCaptureAsync));
        menu.Items.Add("Show", null, (_, _) => ShowResult());
        menu.Items.Add("Connect ChatGPT", null, (_, _) => RunUiAsync(ToggleConnectionAsync));
        menu.Items.Add("Exit", null, (_, _) => RunUiAsync(ExitAsync));
        return menu;
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
        currentMonitor = monitor.Bounds;
        overlay = new CaptureOverlay(source, monitor.Bounds, rect => CompleteSelection(source, monitor, rect), CancelOverlay);
        overlay.Show();
        overlay.Activate();
        await Task.CompletedTask;
    }

    private void CompleteSelection(BitmapSource source, (System.Drawing.Rectangle Bounds, System.Drawing.Rectangle WorkArea) monitor, PixelRect crop)
    {
        timing = new CaptureTiming();
        var overlayHandle = overlay is null ? IntPtr.Zero : new WindowInteropHelper(overlay).Handle;
        var dpi = overlayHandle == IntPtr.Zero ? 96u : NativeMethods.GetDpiForWindow(overlayHandle);
        currentMonitor = monitor.Bounds;
        currentImage = new CroppedBitmap(source, new Int32Rect(crop.X, crop.Y, crop.Width, crop.Height));
        currentImage.Freeze();
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
        var desiredWidth = (int)Math.Round(layout.WindowWidth / dipScale);
        var desiredHeight = (int)Math.Round(layout.WindowHeight / dipScale);
        ShowResultBesideSelection(monitor.WorkArea, crop, desiredWidth, desiredHeight);
        if (originalForeground != IntPtr.Zero)
            NativeMethods.SetForegroundWindow(originalForeground);
        var autoAnswer = connected;
        var captureTiming = timing;
        _ = EncodeAndAnswerAsync(currentImage, autoAnswer, captureTiming, generation.Next());
    }

    private async Task EncodeAndAnswerAsync(BitmapSource? image, bool autoAnswer, CaptureTiming? captureTiming, long requestGeneration)
    {
        try
        {
            if (image is null)
                throw new InvalidOperationException("No screenshot is selected.");
            var png = await Task.Run(() => EncodePng(image)).ConfigureAwait(true);
            if (!generation.IsCurrent(requestGeneration))
                return;
            currentPng = png;
            if (autoAnswer && connected)
                await StartAnswerAsync(requestGeneration, captureTiming).ConfigureAwait(true);
            else
            {
                result?.SetTiming(null);
                result?.SetStatus("Connect ChatGPT, then click Answer");
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
            result?.SetStatus("Connect ChatGPT before answering");
            return;
        }
        result?.ClearAnswer();
        timing = null;
        result?.SetTiming(null);
        await StartAnswerAsync(generation.Next(), null);
    }

    private async Task StartAnswerAsync(long requestGeneration, CaptureTiming? requestTiming)
    {
        result?.SetBusy(true);
        var localCancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref answerCancellation, localCancellation);
        previous?.Cancel();
        try
        {
            var options = new AnswerRequestOptions(answerInstruction, selectedModel);
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
                        result?.SetAnswer(update.Text);
                        result?.SetTiming(requestTiming);
                        result?.SetStatus("Response complete");
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
        if (connected)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                models = await provider.GetAvailableModelsAsync(timeout.Token).ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                result.SetStatus(DescribeUiException(exception));
            }
        }

        var dialog = new AnswerOptionsWindow(answerInstruction, selectedModel, models) { Owner = result };
        if (dialog.ShowDialog() != true)
            return;
        answerInstruction = dialog.Instruction;
        selectedModel = dialog.SelectedModel;
        var selected = models.FirstOrDefault(model => model.Model == selectedModel);
        result.SetModel(selected is null ? "Automatic" : $"{selected.DisplayName} · {selected.ReasoningEffort}");
        result.SetStatus("Answer options updated");
    }

    private async Task RefreshAccountAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var account = await provider!.GetAccountAsync(timeout.Token).ConfigureAwait(true);
            connected = account.IsConnected;
            result?.SetAccount(account.IsConnected ? $"ChatGPT · {account.Plan}" : "Disconnected", account.IsConnected);
            result?.SetStatus(account.IsConnected ? "Ready" : "Connect ChatGPT to answer");
        }
        catch (Exception exception)
        {
            connected = false;
            result?.SetAccount("Disconnected", false);
            result?.SetStatus(DescribeUiException(exception));
        }
    }

    private void OnAccountChanged() => Dispatcher.BeginInvoke(new Action(() => _ = RefreshAccountAsync()));

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

    private void ShowResultBesideSelection(System.Drawing.Rectangle workArea, PixelRect crop, int desiredWidth, int desiredHeight)
    {
        if (result is null)
            return;
        if (!result.IsVisible)
            result.Show();
        var globalCrop = new PixelRect(currentMonitor.X + crop.X, currentMonitor.Y + crop.Y, crop.Width, crop.Height);
        var placement = PanelPlacement.Place(globalCrop, new(workArea.X, workArea.Y, workArea.Width, workArea.Height), desiredWidth, desiredHeight);
        NativeMethods.PositionWindow(result, new System.Drawing.Rectangle(placement.X, placement.Y, placement.Width, placement.Height));
        result.ShowActivated = false;
    }

    private void ShowResult()
    {
        if (result is null)
            return;
        result.Show();
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
        var state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Screenshot", "Diagnostics", Guid.NewGuid().ToString("N"));
        var executable = Environment.GetEnvironmentVariable("SCREENSHOT_CODEX_PATH") ?? Path.Combine(AppContext.BaseDirectory, "codex.exe");
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

using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Screenshot.Desktop;

internal static class SmokeRenderer
{
    private const string UnicodeAnswer = "“42” — that’s the answer…";

    public static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var source = CreateSource();
        Save(source, Path.Combine(outputDirectory, "smoke-source.png"));

        var window = new ResultWindow();
        var dismissed = false;
        window.DismissRequested += () => dismissed = true;
        window.SetPreview(source);
        window.SetAccount("ChatGPT · free", true);
        window.SetAnswer("42.\n17 + 25 = 42.");
        window.SetStatus("Demo - not an AI response");
        var compact = RenderWindow(window);
        var previewPixelWidth = window.Preview?.PixelWidth ?? 0;
        var previewPixelHeight = window.Preview?.PixelHeight ?? 0;
        if (window.Answer != "42.\n17 + 25 = 42." || previewPixelWidth != 800 || previewPixelHeight != 300 || !window.IsCopyEnabled || window.StopVisibility != Visibility.Collapsed || window.PrimaryActionVisibility != Visibility.Collapsed || window.IsActionsMenuOpen)
            throw new InvalidOperationException("Compact ResultWindow smoke state did not match expected completed-card state.");
        Save(compact, Path.Combine(outputDirectory, "smoke-render.png"));

        window.SetAnswer(UnicodeAnswer);
        window.SetStatus("Demo - not an AI response");
        if (window.Answer != UnicodeAnswer)
            throw new InvalidOperationException("Compact Unicode smoke answer did not round-trip.");
        Save(RenderWindow(window), Path.Combine(outputDirectory, "compact-unicode.png"));

        window.DismissForSmoke();
        if (!dismissed)
            throw new InvalidOperationException("ResultWindow dismiss event did not fire.");
        window.ClearPreview();
        window.SetAccount("Disconnected", false);
        window.SetWelcome("Capture a question with Ctrl+Alt+S.");
        window.SetStatus("Ready");
        Save(RenderWindow(window), Path.Combine(outputDirectory, "compact-initial.png"));
        window.DismissForSmoke();

        var overlayCancelled = 0;
        var overlayClosed = false;
        var overlay = new CaptureOverlay(source, new System.Drawing.Rectangle(-32000, -32000, 800, 300), _ => { }, () => overlayCancelled++);
        overlay.ShowActivated = false;
        overlay.Closed += (_, _) => overlayClosed = true;
        overlay.Show();
        overlay.CancelSelection();
        overlay.CancelSelection();
        var overlayVisibleAfterCancel = overlay.IsVisible;
        if (!overlayClosed || overlayVisibleAfterCancel || overlayCancelled != 1)
            throw new InvalidOperationException("CaptureOverlay smoke cancellation did not close exactly once.");
        var diagnostics = JsonSerializer.Serialize(new
        {
            passed = true,
            source = "smoke-source.png",
            render = "smoke-render.png",
            compactUnicode = "compact-unicode.png",
            compactInitial = "compact-initial.png",
            answer = "42.\n17 + 25 = 42.",
            actualResultWindow = true,
            previewPixelWidth,
            previewPixelHeight,
            copyEnabled = true,
            stopCollapsed = true,
            primaryCollapsed = true,
            actionsMenuClosed = true,
            dismissed,
            reusable = window.Preview is null || window.Preview.PixelWidth == 800,
            overlayClosed,
            overlayVisibleAfterCancel,
            overlayCancelledOnce = overlayCancelled == 1,
            overlaySecondCancelNoDuplicate = overlayCancelled == 1
        });
        File.WriteAllText(Path.Combine(outputDirectory, "smoke-diagnostics.json"), diagnostics);
    }

    private static RenderTargetBitmap RenderWindow(ResultWindow window)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(ResultWindow.CardWidth, ResultWindow.CardHeight));
        content.Arrange(new Rect(0, 0, ResultWindow.CardWidth, ResultWindow.CardHeight));
        content.UpdateLayout();
        if (content.ActualWidth <= 0 || content.ActualHeight <= 0)
            throw new InvalidOperationException("Compact ResultWindow content did not measure as expected.");
        var render = new RenderTargetBitmap((int)ResultWindow.CardWidth, (int)ResultWindow.CardHeight, 96, 96, PixelFormats.Pbgra32);
        render.Render(content);
        render.Freeze();
        return render;
    }

    private static RenderTargetBitmap CreateSource()
    {
        var source = new RenderTargetBitmap(800, 300, 96, 96, PixelFormats.Pbgra32);
        var sourceVisual = new DrawingVisual();
        using (var drawing = sourceVisual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 800, 300));
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            drawing.DrawText(new FormattedText("What is 17 + 25?", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 48, Brushes.Black, 1), new Point(40, 80));
        }
        source.Render(sourceVisual);
        source.Freeze();
        return source;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }
}

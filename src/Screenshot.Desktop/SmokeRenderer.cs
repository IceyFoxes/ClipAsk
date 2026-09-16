using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Screenshot.Core.Capture;
using Screenshot.Core.Providers;

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
        window.SetPreview(source, ResultLayoutCalculator.Calculate(new(800, 300, 1920, 1080)));
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
            throw new InvalidOperationException("Unicode smoke answer did not round-trip.");
        Save(RenderWindow(window), Path.Combine(outputDirectory, "compact-unicode.png"));

        window.DismissForSmoke();
        if (!dismissed)
            throw new InvalidOperationException("ResultWindow dismiss event did not fire.");

        var tallSource = CreateSource(450, 1200, "Explain this tall panel");
        window.SetPreview(tallSource, ResultLayoutCalculator.Calculate(new(450, 1200, 1920, 1080)));
        window.SetAccount("ChatGPT · free", true);
        window.SetStatus("Preparing answer…");
        window.SetBusy(true);
        Save(RenderWindow(window), Path.Combine(outputDirectory, "split-preparing.png"));
        window.SetAnswer("This layout keeps the tall capture readable while reserving a stable answer column.");
        window.SetBusy(false);
        window.SetStatus("Response complete");
        Save(RenderWindow(window), Path.Combine(outputDirectory, "split-complete.png"));

        var stripSource = CreateSource(1600, 120, "What does this banner mean?");
        window.SetPreview(stripSource, ResultLayoutCalculator.Calculate(new(1600, 120, 1920, 1080)));
        window.SetAnswer("The result is $17 + 25 = 42$.\n\n```csharp\nvar answer = 17 + 25;\n```");
        window.SetStatus("Response complete");
        Save(RenderWindow(window), Path.Combine(outputDirectory, "formatted-complete.png"));

        window.ClearPreview();
        window.SetAccount("Disconnected", false);
        window.SetWelcome("Capture a question with Ctrl+Alt+S.");
        window.SetStatus("Ready");
        Save(RenderWindow(window), Path.Combine(outputDirectory, "compact-initial.png"));
        window.DismissForSmoke();

        var options = new AnswerOptionsWindow(
            "Explain each step and keep the answer concise.",
            "gpt-5.6-terra",
            [new CodexModelSelection("gpt-5.6-terra", "low", "GPT-5.6 Terra")]);
        Save(RenderWindow(options), Path.Combine(outputDirectory, "answer-options.png"));

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
            splitPreparing = "split-preparing.png",
            splitComplete = "split-complete.png",
            formattedComplete = "formatted-complete.png",
            answerOptions = "answer-options.png",
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

    private static RenderTargetBitmap RenderWindow(Window window)
    {
        var content = (FrameworkElement)window.Content;
        var width = window.Width;
        var height = window.Height;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        if (content.ActualWidth <= 0 || content.ActualHeight <= 0)
            throw new InvalidOperationException("ResultWindow content did not measure as expected.");
        var render = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        render.Render(content);
        render.Freeze();
        return render;
    }

    private static RenderTargetBitmap CreateSource() => CreateSource(800, 300, "What is 17 + 25?");

    private static RenderTargetBitmap CreateSource(int width, int height, string text)
    {
        var source = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var sourceVisual = new DrawingVisual();
        using (var drawing = sourceVisual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var fontSize = Math.Min(48, Math.Max(18, height * 0.16));
            drawing.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, 1), new Point(24, Math.Max(24, height * 0.20)));
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipAsk.Core.Capture;
using ClipAsk.Core.Providers;

namespace ClipAsk.Desktop;

internal static class SmokeRenderer
{
    private const string UnicodeAnswer = "“42” — that’s the answer…";
    private const string ThreePhaseCommitQuestion = """
        Step 4(c) is modified so the new coordinator sends GLOBAL-COMMIT immediately when any participant is in PRECOMMIT.

        Is the modified 3PC recovery protocol safe? Give a concrete failure sequence or a proof.
        """;
    private const string ThreePhaseCommitAnswer = """
        The modified step 4(c) is unsafe because it can cause some participants to commit while others later abort.

        ## Counterexample

        1. Participants include \(P_1,P_2,P_3\). Initially, \(P_1\) is in `PRECOMMIT`, while \(P_2,P_3\) are in `READY`.
        2. The new coordinator applies modified 4(c) and sends **Global-commit**.
        3. Only \(P_1\) receives it and commits; the coordinator then crashes.
        4. \(P_2\) and \(P_3\), unable to communicate with \(P_1\), elect another coordinator.
        5. Their states are both `READY`. Since no reachable participant is in `PRECOMMIT` or `COMMIT`, step 4(b) makes them abort.

        Thus \(P_1\) commits while \(P_2,P_3\) abort, violating atomicity.

        The original step 4(c) prevents this by first moving every `READY` participant to `PRECOMMIT` and collecting acknowledgements before broadcasting **Global-commit**. Consequently, once any participant can commit, the remaining participants cannot subsequently use the “no `PRECOMMIT`” rule to abort.
        """;

    public static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var source = CreateSource();
        Save(source, Path.Combine(outputDirectory, "smoke-source.png"));
        var normalizedMath = AnswerDocumentRenderer.NormalizeMathDelimiters("Math \\(P_1\\), inline `\\(literal\\)`, and fenced:\n```text\n\\(literal\\)\n```");
        if (!normalizedMath.Contains("Math $P_1$", StringComparison.Ordinal) ||
            !normalizedMath.Contains("`\\(literal\\)`", StringComparison.Ordinal) ||
            !normalizedMath.Contains("```text\n\\(literal\\)\n```", StringComparison.Ordinal))
            throw new InvalidOperationException("Alternate LaTeX delimiters were not normalized safely around code.");
        var normalizedCurrencyMath = AnswerDocumentRenderer.NormalizeMathDelimiters("Premium: $200 total, or $2 per share. Equation: $\\$100 + \\$2 = \\$102$.");
        if (normalizedCurrencyMath != "Premium: \\$200 total, or \\$2 per share. Equation: $100 + 2 = 102$.")
            throw new InvalidOperationException($"Currency and inline math were not disambiguated safely: {normalizedCurrencyMath}");
        var normalizedDollarMath = AnswerDocumentRenderer.NormalizeMathDelimiters("Math $P_1$, total $100 + 2 = 102$, and `price $200`.");
        if (normalizedDollarMath != "Math $P_1$, total $100 + 2 = 102$, and `price $200`.")
            throw new InvalidOperationException("Valid dollar-delimited math or inline code changed during currency normalization.");
        var currencyDocument = AnswerDocumentRenderer.Create("**Premium paid:** $200 total, or $2 per share.\n\nThe break-even calculation is $\\$100 + \\$2 = \\$102$.");
        var currencyDocumentText = new System.Windows.Documents.TextRange(currencyDocument.ContentStart, currencyDocument.ContentEnd).Text;
        if (!currencyDocumentText.Contains("$200 total, or $2 per share.", StringComparison.Ordinal))
            throw new InvalidOperationException("Currency prose was incorrectly rendered as inline math.");
        var startupCommand = StartupRegistration.BuildCommand(@"C:\Program Files\ClipAsk\ClipAsk.exe");
        if (startupCommand != "\"C:\\Program Files\\ClipAsk\\ClipAsk.exe\" --startup")
            throw new InvalidOperationException("Startup registration command was not quoted safely.");
        var saveLocationState = Path.Combine(outputDirectory, "save-location-state");
        SaveLocationStore.Remember(outputDirectory, saveLocationState);
        if (!string.Equals(SaveLocationStore.Read(saveLocationState), Path.GetFullPath(outputDirectory), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The last successful save folder did not persist.");
        var firstRunState = Path.Combine(outputDirectory, "first-run-state", Guid.NewGuid().ToString("N"));
        if (!FirstRunStateStore.ShouldShow(firstRunState))
            throw new InvalidOperationException("A new profile did not request first-run guidance.");
        FirstRunStateStore.MarkSeen(firstRunState);
        if (FirstRunStateStore.ShouldShow(firstRunState))
            throw new InvalidOperationException("First-run guidance was not remembered after dismissal.");

        var window = new ResultWindow();
        if (!window.ShowInTaskbar)
            throw new InvalidOperationException("ResultWindow must be available from the taskbar when minimized.");
        if (window.Topmost)
            throw new InvalidOperationException("ResultWindow must use normal desktop z-order so other apps can cover it.");
        var dismissed = false;
        window.DismissRequested += () => dismissed = true;
        window.SetPreview(source, ResultLayoutCalculator.Calculate(new(800, 300, 1920, 1080)));
        window.SetAccount("ChatGPT · free", true);
        window.PrepareImageActionsForSmoke();
        if (!window.IsActionsMenuTargetingPreview)
            throw new InvalidOperationException("The screenshot preview did not target the shared actions menu.");
        Save(RenderElement(window.ActionsMenuForSmoke), Path.Combine(outputDirectory, "actions-menu.png"));
        var saveImageEnabled = window.IsSaveImageEnabled;
        if (!saveImageEnabled)
            throw new InvalidOperationException("Save image must be enabled when a capture is present.");
        window.SetAnswer("42.\n17 + 25 = 42.", true);
        window.SetStatus("Demo - not an AI response");
        var compact = RenderWindow(window);
        var previewPixelWidth = window.Preview?.PixelWidth ?? 0;
        var previewPixelHeight = window.Preview?.PixelHeight ?? 0;
        if (window.Answer != "42.\n17 + 25 = 42." || previewPixelWidth != 800 || previewPixelHeight != 300 || !window.IsCopyEnabled || window.StopVisibility != Visibility.Collapsed || window.PrimaryActionVisibility != Visibility.Collapsed || window.IsActionsMenuOpen)
            throw new InvalidOperationException("Compact ResultWindow smoke state did not match expected completed-card state.");
        Save(compact, Path.Combine(outputDirectory, "smoke-render.png"));
        var stableShortAnswerHeight = window.Height;
        for (var measurement = 0; measurement < 3; measurement++)
        {
            window.SetAnswer("42.\n17 + 25 = 42.", true);
            _ = RenderWindow(window);
        }
        if (window.Height > stableShortAnswerHeight + 0.5)
            throw new InvalidOperationException("Repeated response measurement added empty space below a stable short answer.");

        var resizableWindow = new ResultWindow();
        resizableWindow.SetPreview(source, ResultLayoutCalculator.Calculate(new(800, 300, 1920, 1080)));
        resizableWindow.SetAccount("ChatGPT · free", true);
        resizableWindow.SetAnswer("The preview scales with the window while preserving its aspect ratio.", true);
        resizableWindow.ResizeForSmoke(760, 480);
        var resizedRender = RenderWindow(resizableWindow);
        var resizedAspectRatio = resizableWindow.PreviewDisplayWidth / resizableWindow.PreviewDisplayHeight;
        if (!resizableWindow.IsUserResizeEnabled || Math.Abs(resizedAspectRatio - (800d / 300d)) > 0.0001)
            throw new InvalidOperationException("The resizable result window did not preserve the screenshot aspect ratio.");
        Save(resizedRender, Path.Combine(outputDirectory, "resizable-window.png"));
        resizableWindow.DismissForSmoke();

        window.SetAnswer(UnicodeAnswer, true);
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
        window.SetStatus("Asking ChatGPT…");
        window.SetBusy(true);
        Save(RenderWindow(window), Path.Combine(outputDirectory, "split-preparing.png"));
        window.SetAnswer("This layout keeps the tall capture readable while reserving a stable answer column.", true);
        window.SetBusy(false);
        window.SetStatus("Response complete");
        Save(RenderWindow(window), Path.Combine(outputDirectory, "split-complete.png"));
        window.ResizeForSmoke(900, 600);
        var resizedSplitRender = RenderWindow(window);
        var resizedSplitAspectRatio = window.PreviewDisplayWidth / window.PreviewDisplayHeight;
        if (Math.Abs(resizedSplitAspectRatio - (450d / 1200d)) > 0.0001)
            throw new InvalidOperationException("The resized split layout did not preserve the screenshot aspect ratio.");
        Save(resizedSplitRender, Path.Combine(outputDirectory, "resizable-split-window.png"));

        var stripSource = CreateSource(1600, 120, "What does this banner mean?");
        window.SetPreview(stripSource, ResultLayoutCalculator.Calculate(new(1600, 120, 1920, 1080)));
        window.SetAnswer("## Diagnosis\n\n**Result:** The value is $17 + 25 = 42$.\n\n- Preserves *emphasis*\n- Renders `inline code`\n\n> Use the fenced example when copying.\n\n```csharp\nvar answer = 17 + 25;\n```\n\n| Input | Output |\n| --- | ---: |\n| 17 + 25 | **42** |", true);
        window.SetStatus("Response complete");
        Save(RenderWindow(window), Path.Combine(outputDirectory, "formatted-complete.png"));
        window.SetAnswer("```csharp\nvar answer = 17 + 25;\n```\n\n| Input | Output |\n| --- | ---: |\n| 17 + 25 | **42** |", true);
        Save(RenderWindow(window), Path.Combine(outputDirectory, "formatted-code-table.png"));
        window.SetAnswer("**Premium paid:** $200 total, or $2 per share.\n\nThe break-even calculation is $\\$100 + \\$2 = \\$102$.", true);
        Save(RenderWindow(window), Path.Combine(outputDirectory, "currency-math-response.png"));

        window.ClearAnswer();
        window.SetBusy(true);
        window.BeginWindowMoveForSmoke();
        window.SetAnswer(ThreePhaseCommitAnswer, false);
        var streamingHeightBeforeLayout = window.Height;
        window.EndWindowMoveForSmoke();
        var streamingRender = RenderWindow(window);
        if (window.Height <= streamingHeightBeforeLayout)
            throw new InvalidOperationException("A long streaming response did not expand after a window move completed.");
        Save(streamingRender, Path.Combine(outputDirectory, "streaming-expanded.png"));
        window.SetAnswer(ThreePhaseCommitAnswer, true);
        window.SetBusy(false);
        Save(RenderWindow(window), Path.Combine(outputDirectory, "latex-parentheses-response.png"));

        window.SetPreview(source, ResultLayoutCalculator.Calculate(new(800, 300, 1920, 1080)));
        window.SetAccount("ChatGPT · free", true);
        var unpromptedHeight = window.Height;
        window.BeginInstructionEntry(true);
        if (window.InstructionComposerVisibility != Visibility.Visible || window.IsInstructionSendEnabled || window.Height >= unpromptedHeight)
            throw new InvalidOperationException("Prompted capture smoke state did not begin with an empty instruction.");
        window.SetInstructionForSmoke("Explain the highlighted error and suggest a fix.");
        if (!window.IsInstructionSendEnabled)
            throw new InvalidOperationException("Prompted capture smoke state did not enable Send for a valid instruction.");
        Save(RenderWindow(window), Path.Combine(outputDirectory, "prompted-capture.png"));
        var promptedWidth = window.Width;
        var promptedHeight = window.Height;
        var promptedPreviewWidth = window.PreviewDisplayWidth;
        var promptedPreviewHeight = window.PreviewDisplayHeight;
        window.ResizeForSmoke(promptedWidth, promptedHeight);
        _ = RenderWindow(window);
        if (Math.Abs(window.PreviewDisplayWidth - promptedPreviewWidth) > 0.5 ||
            Math.Abs(window.PreviewDisplayHeight - promptedPreviewHeight) > 0.5)
            throw new InvalidOperationException("Beginning a manual resize changed the compact prompted layout before the window size changed.");

        window.ClearPreview();
        window.SetAccount("Disconnected", false);
        window.SetWelcome("Select anything on screen with Ctrl+Alt+S.");
        window.SetStatus("Ready");
        if (window.WindowStartupLocation != WindowStartupLocation.CenterScreen)
            throw new InvalidOperationException("The result window is not configured to open centered on screen.");
        Save(RenderWindow(window), Path.Combine(outputDirectory, "compact-initial.png"));
        var restartManagerShutdownRequests = 0;
        window.RestartManagerShutdownRequested += () => restartManagerShutdownRequests++;
        var messageHandled = false;
        var queryResult = window.ProcessNativeMessageForSmoke(NativeMethods.WmQueryEndSession, IntPtr.Zero, new IntPtr(NativeMethods.EndSessionCloseApp), ref messageHandled);
        if (!messageHandled || queryResult != new IntPtr(1) || restartManagerShutdownRequests != 0)
            throw new InvalidOperationException("Restart Manager shutdown query was not acknowledged safely.");
        messageHandled = false;
        _ = window.ProcessNativeMessageForSmoke(NativeMethods.WmEndSession, new IntPtr(1), new IntPtr(NativeMethods.EndSessionCloseApp), ref messageHandled);
        if (!messageHandled || restartManagerShutdownRequests != 1)
            throw new InvalidOperationException("Restart Manager shutdown was not forwarded exactly once.");
        window.DismissForSmoke();

        var options = new AnswerOptionsWindow(
            "Explain the chart in plain English and calculate the break-even price.",
            "gpt-5.6-luna",
            "low",
            new CodexModelSelection("gpt-5.6-terra", "low", "GPT-5.6 Terra"),
            [
                new CodexModelSelection("gpt-5.6-terra", "low", "GPT-5.6 Terra", ["low", "medium", "high"]),
                new CodexModelSelection("gpt-5.6-luna", "low", "GPT-5.6 Luna", ["low", "medium", "high"])
            ]);
        Save(RenderWindow(options), Path.Combine(outputDirectory, "answer-options.png"));

        var firstRun = new FirstRunWindow();
        if (firstRun.WindowStartupLocation != WindowStartupLocation.CenterScreen)
            throw new InvalidOperationException("First-run guidance is not configured to open centered on screen.");
        Save(RenderWindow(firstRun), Path.Combine(outputDirectory, "first-run.png"));

        var about = new AboutWindow();
        if (!about.LicenseSummary.Contains("GPL-3.0-only", StringComparison.Ordinal))
            throw new InvalidOperationException("About did not identify the ClipAsk license.");
        Save(RenderWindow(about), Path.Combine(outputDirectory, "about-licenses.png"));

        CreateReadmeDemo(outputDirectory);

        var overlayCancelled = 0;
        var overlayClosed = false;
        if (CaptureOverlay.IntentForButton(System.Windows.Input.MouseButton.Left) != CaptureIntent.Prompted ||
            CaptureOverlay.IntentForButton(System.Windows.Input.MouseButton.Right) != CaptureIntent.Automatic)
            throw new InvalidOperationException("Capture buttons did not map left-drag to prompted and right-drag to automatic.");
        var overlay = new CaptureOverlay(source, new System.Drawing.Rectangle(-32000, -32000, 800, 300), (_, _) => { }, () => overlayCancelled++);
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
            resizableWindow = "resizable-window.png",
            compactUnicode = "compact-unicode.png",
            compactInitial = "compact-initial.png",
            splitPreparing = "split-preparing.png",
            splitComplete = "split-complete.png",
            resizableSplitWindow = "resizable-split-window.png",
            formattedComplete = "formatted-complete.png",
            formattedCodeTable = "formatted-code-table.png",
            currencyMathResponse = "currency-math-response.png",
            streamingExpanded = "streaming-expanded.png",
            latexParenthesesResponse = "latex-parentheses-response.png",
            promptedCapture = "prompted-capture.png",
            actionsMenu = "actions-menu.png",
            answerOptions = "answer-options.png",
            firstRun = "first-run.png",
            aboutLicenses = "about-licenses.png",
            answer = "42.\n17 + 25 = 42.",
            actualResultWindow = true,
            previewPixelWidth,
            previewPixelHeight,
            copyEnabled = true,
            stopCollapsed = true,
            primaryCollapsed = true,
            actionsMenuClosed = true,
            showInTaskbar = true,
            userResizeEnabled = true,
            resizedPreviewAspectRatioPreserved = true,
            leftDragPrompted = true,
            rightDragAutomatic = true,
            saveImageEnabled,
            saveLocationRemembered = true,
            streamingExpandedAfterMove = true,
            dismissed,
            reusable = window.Preview is null || window.Preview.PixelWidth == 800,
            overlayClosed,
            overlayVisibleAfterCancel,
            overlayCancelledOnce = overlayCancelled == 1,
            overlaySecondCancelNoDuplicate = overlayCancelled == 1,
            startupCommandQuoted = true,
            firstRunRemembered = true,
            aboutLicenseShown = true,
            readmeDemo = "readme-demo.gif"
        });
        File.WriteAllText(Path.Combine(outputDirectory, "smoke-diagnostics.json"), diagnostics);
    }

    private static void CreateReadmeDemo(string outputDirectory)
    {
        const int canvasWidth = 1280;
        const int canvasHeight = 720;
        var source = CreateSource(960, 300, ThreePhaseCommitQuestion, 31);
        var window = new ResultWindow();
        var layout = ResultLayoutCalculator.Calculate(new(source.PixelWidth, source.PixelHeight, 1920, 1080));
        var frames = new List<(BitmapSource Bitmap, int DelayCentiseconds)>();

        window.SetPreview(source, layout);
        window.SetAccount("ChatGPT connected", true);
        window.BeginInstructionEntry(true);
        window.SetInstructionForSmoke(string.Empty);
        frames.Add((RenderOnCanvas(RenderWindow(window), canvasWidth, canvasHeight), 100));

        const string instruction = "Explain whether this is safe. Give a concrete counterexample.";
        for (var length = 8; length < instruction.Length; length += 8)
        {
            window.SetInstructionForSmoke(instruction[..length]);
            frames.Add((RenderOnCanvas(RenderWindow(window), canvasWidth, canvasHeight), 12));
        }
        window.SetInstructionForSmoke(instruction);
        frames.Add((RenderOnCanvas(RenderWindow(window), canvasWidth, canvasHeight), 110));

        window.SetPreview(source, layout);
        window.SetAccount("ChatGPT connected", true);
        window.SetStatus("Asking ChatGPT…");
        window.SetBusy(true);
        var asking = RenderOnCanvas(RenderWindow(window), canvasWidth, canvasHeight);
        frames.Add((asking, 180));

        var streamedAnswers = new[]
        {
            "The modified step 4(c) is **unsafe** because it can cause some participants to commit while others later abort.",
            "The modified step 4(c) is **unsafe** because it can cause some participants to commit while others later abort.\n\n## Counterexample\n\n1. $P_1$ is in `PRECOMMIT`; $P_2$ and $P_3$ are in `READY`.",
            "The modified step 4(c) is **unsafe** because it can cause some participants to commit while others later abort.\n\n## Counterexample\n\n1. $P_1$ is in `PRECOMMIT`; $P_2$ and $P_3$ are in `READY`.\n2. The new coordinator sends **GLOBAL-COMMIT** immediately.\n3. Only $P_1$ receives it and commits; the coordinator crashes.",
            "The modified step 4(c) is **unsafe** because it can cause some participants to commit while others later abort.\n\n## Counterexample\n\n1. $P_1$ is in `PRECOMMIT`; $P_2$ and $P_3$ are in `READY`.\n2. The new coordinator sends **GLOBAL-COMMIT** immediately.\n3. Only $P_1$ receives it and commits; the coordinator crashes.\n4. $P_2$ and $P_3$ elect another coordinator. Seeing only `READY`, they abort.",
            ThreePhaseCommitAnswer
        };

        foreach (var answer in streamedAnswers)
        {
            window.SetAnswer(answer, false);
            var streaming = RenderOnCanvas(RenderWindow(window), canvasWidth, canvasHeight);
            frames.Add((streaming, 45));
        }

        window.SetAnswer(ThreePhaseCommitAnswer, true);
        window.SetBusy(false);
        window.SetStatus("Response complete");
        var complete = RenderOnCanvas(RenderWindow(window), canvasWidth, canvasHeight);
        frames.Add((complete, 400));

        SaveAnimatedGif(Path.Combine(outputDirectory, "readme-demo.gif"), frames);
        window.DismissForSmoke();
    }

    private static RenderTargetBitmap RenderWindow(Window window)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(window.Width, window.Height));
        content.Arrange(new Rect(0, 0, window.Width, window.Height));
        content.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
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

    private static RenderTargetBitmap RenderElement(FrameworkElement element)
    {
        element.ApplyTemplate();
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = Math.Max(1, element.DesiredSize.Width);
        var height = Math.Max(1, element.DesiredSize.Height);
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var render = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        render.Render(element);
        render.Freeze();
        return render;
    }

    private static RenderTargetBitmap RenderOnCanvas(BitmapSource bitmap, int width, int height)
    {
        const double padding = 36;
        var scale = Math.Min(1, Math.Min((width - (padding * 2)) / bitmap.PixelWidth, (height - (padding * 2)) / bitmap.PixelHeight));
        var renderedWidth = bitmap.PixelWidth * scale;
        var renderedHeight = bitmap.PixelHeight * scale;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(15, 17, 21)), null, new Rect(0, 0, width, height));
            drawing.DrawImage(bitmap, new Rect((width - renderedWidth) / 2, (height - renderedHeight) / 2, renderedWidth, renderedHeight));
        }
        var canvas = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        canvas.Render(visual);
        canvas.Freeze();
        return canvas;
    }

    private static RenderTargetBitmap CreateSource() => CreateSource(800, 300, "What is 17 + 25?");

    private static RenderTargetBitmap CreateSource(int width, int height, string text, double? fontSizeOverride = null)
    {
        var source = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var sourceVisual = new DrawingVisual();
        using (var drawing = sourceVisual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var fontSize = fontSizeOverride ?? Math.Min(48, Math.Max(18, height * 0.16));
            var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, 1)
            {
                MaxTextWidth = width - 48,
                MaxTextHeight = height - 48,
                Trimming = TextTrimming.WordEllipsis
            };
            drawing.DrawText(formatted, new Point(24, 24));
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

    private static void SaveAnimatedGif(string path, IReadOnlyList<(BitmapSource Bitmap, int DelayCentiseconds)> frames)
    {
        var encoder = new GifBitmapEncoder();
        foreach (var frame in frames)
        {
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", (ushort)frame.DelayCentiseconds);
            metadata.SetQuery("/grctlext/Disposal", (byte)2);
            encoder.Frames.Add(BitmapFrame.Create(frame.Bitmap, null, metadata, null));
        }

        using var encoded = new MemoryStream();
        encoder.Save(encoded);
        var bytes = encoded.ToArray();
        var searchOffset = 0;
        foreach (var frame in frames)
        {
            var extensionOffset = FindGraphicControlExtension(bytes, searchOffset);
            if (extensionOffset < 0)
                throw new InvalidOperationException("The GIF encoder omitted frame timing metadata.");

            var delay = checked((ushort)frame.DelayCentiseconds);
            bytes[extensionOffset + 4] = (byte)(delay & 0xff);
            bytes[extensionOffset + 5] = (byte)(delay >> 8);
            searchOffset = extensionOffset + 8;
        }

        using var stream = File.Create(path);
        stream.Write(bytes);
    }

    private static int FindGraphicControlExtension(byte[] bytes, int startIndex)
    {
        for (var index = startIndex; index <= bytes.Length - 8; index++)
        {
            if (bytes[index] == 0x21 && bytes[index + 1] == 0xf9 && bytes[index + 2] == 0x04)
                return index;
        }

        return -1;
    }
}

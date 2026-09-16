namespace Screenshot.Core.Capture;

public enum ResultLayoutMode
{
    Stacked,
    Split
}

public readonly record struct ResultLayoutInput(
    double SourceWidthDip,
    double SourceHeightDip,
    double WorkAreaWidthDip,
    double WorkAreaHeightDip);

public readonly record struct ResultLayout(
    ResultLayoutMode Mode,
    double WindowWidth,
    double WindowHeight,
    double PreviewWidth,
    double PreviewHeight,
    double AnswerWidth,
    double AnswerHeight);

public static class ResultLayoutCalculator
{
    private const double SafeArea = 32;
    private const double HeaderHeight = 40;
    private const double Insets = 32;
    private const double Gap = 12;
    private const double MinimumWindowWidth = 360;
    private const double MinimumAnswerWidth = 280;
    private const double MinimumAnswerHeight = 112;
    private const double PreferredAnswerHeight = 168;
    private const double MaximumAnswerWidth = 620;

    public static ResultLayout Calculate(ResultLayoutInput input)
    {
        if (input.SourceWidthDip <= 0 || input.SourceHeightDip <= 0)
            throw new ArgumentOutOfRangeException(nameof(input), "Source dimensions must be positive.");
        if (input.WorkAreaWidthDip <= SafeArea || input.WorkAreaHeightDip <= SafeArea)
            throw new ArgumentOutOfRangeException(nameof(input), "Work area is too small.");

        var ratio = input.SourceWidthDip / input.SourceHeightDip;
        var stacked = CalculateStacked(input);
        var split = CalculateSplit(input);

        if (ratio >= 1.35 || input.SourceHeightDip <= 180 || split is null)
            return stacked;
        if (ratio <= 0.80)
            return split.Value;

        var stackedScale = stacked.PreviewWidth / input.SourceWidthDip;
        var splitScale = split.Value.PreviewWidth / input.SourceWidthDip;
        return splitScale >= stackedScale ? split.Value : stacked;
    }

    private static ResultLayout CalculateStacked(ResultLayoutInput input)
    {
        var maxWindowWidth = Math.Max(1, Math.Min(1200, input.WorkAreaWidthDip - SafeArea));
        var maxWindowHeight = Math.Max(1, input.WorkAreaHeightDip - SafeArea);
        var maxMediaWidth = Math.Max(1, maxWindowWidth - Insets);
        var maxMediaHeight = Math.Max(1, maxWindowHeight - HeaderHeight - Insets - Gap - MinimumAnswerHeight);
        var scale = Math.Min(1, Math.Min(maxMediaWidth / input.SourceWidthDip, maxMediaHeight / input.SourceHeightDip));
        var previewWidth = input.SourceWidthDip * scale;
        var previewHeight = input.SourceHeightDip * scale;
        var windowWidth = Math.Min(maxWindowWidth, Math.Max(Math.Min(MinimumWindowWidth, maxWindowWidth), previewWidth + Insets));
        var answerWidth = Math.Min(MaximumAnswerWidth, Math.Max(1, windowWidth - Insets));
        var availableAnswerHeight = Math.Max(MinimumAnswerHeight, maxWindowHeight - HeaderHeight - Insets - Gap - previewHeight);
        var answerHeight = Math.Min(PreferredAnswerHeight, availableAnswerHeight);
        var windowHeight = Math.Min(maxWindowHeight, HeaderHeight + Insets + previewHeight + Gap + answerHeight);

        return new(ResultLayoutMode.Stacked, windowWidth, windowHeight, previewWidth, previewHeight, answerWidth, answerHeight);
    }

    private static ResultLayout? CalculateSplit(ResultLayoutInput input)
    {
        var maxWindowWidth = Math.Max(1, Math.Min(760, input.WorkAreaWidthDip - SafeArea));
        var maxWindowHeight = Math.Max(1, Math.Min(640, input.WorkAreaHeightDip - SafeArea));
        var availableContentWidth = maxWindowWidth - Insets - Gap;
        if (availableContentWidth < MinimumAnswerWidth + 1)
            return null;

        var maxPreviewWidth = Math.Min(input.SourceWidthDip, maxWindowWidth * 0.44);
        var maxPreviewHeight = Math.Max(1, maxWindowHeight - HeaderHeight - Insets);
        var scale = Math.Min(1, Math.Min(maxPreviewWidth / input.SourceWidthDip, maxPreviewHeight / input.SourceHeightDip));
        var previewWidth = input.SourceWidthDip * scale;
        var previewHeight = input.SourceHeightDip * scale;
        var answerWidth = Math.Min(MaximumAnswerWidth, availableContentWidth - previewWidth);
        if (answerWidth < MinimumAnswerWidth)
        {
            previewWidth = Math.Max(1, availableContentWidth - MinimumAnswerWidth);
            scale = Math.Min(scale, previewWidth / input.SourceWidthDip);
            previewWidth = input.SourceWidthDip * scale;
            previewHeight = input.SourceHeightDip * scale;
            answerWidth = Math.Min(MaximumAnswerWidth, availableContentWidth - previewWidth);
        }

        var windowWidth = Math.Min(maxWindowWidth, Insets + previewWidth + Gap + answerWidth);
        var contentHeight = Math.Max(previewHeight, Math.Min(PreferredAnswerHeight, maxPreviewHeight));
        var windowHeight = Math.Min(maxWindowHeight, HeaderHeight + Insets + contentHeight);
        var answerHeight = Math.Max(MinimumAnswerHeight, windowHeight - HeaderHeight - Insets);
        return new(ResultLayoutMode.Split, windowWidth, windowHeight, previewWidth, previewHeight, answerWidth, answerHeight);
    }
}

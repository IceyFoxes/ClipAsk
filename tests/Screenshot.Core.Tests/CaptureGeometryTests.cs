using Screenshot.Core.Capture;
using Xunit;

namespace Screenshot.Core.Tests;

public sealed class CaptureGeometryTests
{
    [Fact]
    public void PixelRectNormalizesAndClampsDrag()
    {
        Assert.Equal(new PixelRect(10, 20, 70, 70), PixelRect.FromDrag(80, 90, 10, 20, 100, 100));
        Assert.Equal(new PixelRect(0, 0, 100, 100), PixelRect.FromDrag(-10, -5, 120, 150, 100, 100));
        Assert.False(PixelRect.FromDrag(10, 10, 12, 12, 100, 100).IsUsable);
        Assert.False(PixelRect.FromDrag(10, 10, 10, 10, 100, 100).IsUsable);
    }

    [Fact]
    public void PanelPlacementPrefersRightThenLeftAndClamps()
    {
        var work = new PixelRect(0, 0, 1000, 800);
        Assert.Equal(new PixelRect(512, 50, 300, 200), PanelPlacement.Place(new(100, 50, 400, 200), work, 300, 200));
        Assert.Equal(new PixelRect(388, 50, 300, 200), PanelPlacement.Place(new(700, 50, 100, 200), work, 300, 200));
        Assert.Equal(new PixelRect(0, 0, 1000, 800), PanelPlacement.Place(new(-500, -100, 50, 50), work, 1300, 900));
        Assert.Equal(new PixelRect(700, 0, 300, 200), PanelPlacement.Place(new(2000, 0, 10, 50), work, 300, 200));
        var negative = new PixelRect(-1920, -100, 1920, 1080);
        Assert.Equal(new PixelRect(-988, 100, 800, 420), PanelPlacement.Place(new(-1200, 100, 200, 200), negative, 800, 420));
    }

    [Fact]
    public void PanelPlacementUsesVerticalEdgesWhenSidesDoNotFit()
    {
        var work = new PixelRect(0, 0, 1000, 800);

        Assert.Equal(new PixelRect(350, 312, 400, 200), PanelPlacement.Place(new(350, 100, 300, 200), work, 400, 200));
        Assert.Equal(new PixelRect(350, 88, 400, 200), PanelPlacement.Place(new(350, 300, 300, 500), work, 400, 200));
    }

    [Fact]
    public void RequestGenerationInvalidatesOlderRequests()
    {
        var generation = new RequestGeneration();
        var first = generation.Next();
        var second = generation.Next();
        Assert.False(generation.IsCurrent(first));
        Assert.True(generation.IsCurrent(second));
    }

    [Theory]
    [InlineData(1600, 120, ResultLayoutMode.Stacked)]
    [InlineData(1200, 450, ResultLayoutMode.Stacked)]
    [InlineData(450, 1200, ResultLayoutMode.Split)]
    [InlineData(180, 80, ResultLayoutMode.Stacked)]
    public void ResultLayoutChoosesExpectedModeAndPreservesAspectRatio(double width, double height, ResultLayoutMode expected)
    {
        var layout = ResultLayoutCalculator.Calculate(new(width, height, 1920, 1080));

        Assert.Equal(expected, layout.Mode);
        Assert.InRange(Math.Abs((layout.PreviewWidth / layout.PreviewHeight) - (width / height)), 0, 0.0001);
        Assert.True(layout.WindowWidth <= 1888);
        Assert.True(layout.WindowHeight <= 1048);
        Assert.True(layout.PreviewWidth <= width);
        Assert.True(layout.PreviewHeight <= height);
        Assert.True(layout.AnswerWidth >= 280);
        Assert.True(layout.AnswerHeight >= 112);
    }

    [Fact]
    public void ResultLayoutKeepsTinyCaptureAtNaturalSize()
    {
        var layout = ResultLayoutCalculator.Calculate(new(180, 80, 1920, 1080));

        Assert.Equal(180, layout.PreviewWidth);
        Assert.Equal(80, layout.PreviewHeight);
    }

    [Fact]
    public void ResultLayoutConstrainsLargeCaptureToWorkArea()
    {
        var layout = ResultLayoutCalculator.Calculate(new(2560, 1440, 1280, 720));

        Assert.True(layout.WindowWidth <= 1248);
        Assert.True(layout.WindowHeight <= 688);
        Assert.Equal(16d / 9d, layout.PreviewWidth / layout.PreviewHeight, 6);
    }
}

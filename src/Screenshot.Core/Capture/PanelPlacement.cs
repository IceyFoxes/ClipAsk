namespace Screenshot.Core.Capture;

public static class PanelPlacement
{
    public static PixelRect Place(PixelRect crop, PixelRect workArea, int desiredWidth, int desiredHeight, int gap = 12)
    {
        var width = Math.Clamp(desiredWidth, 1, workArea.Width);
        var height = Math.Clamp(desiredHeight, 1, workArea.Height);
        var x = crop.Right + gap;
        if (x < workArea.X || x + width > workArea.Right)
        {
            var left = crop.X - gap - width;
            x = left >= workArea.X && left + width <= workArea.Right ? left : Math.Clamp(x, workArea.X, workArea.Right - width);
        }

        var y = Math.Clamp(crop.Y, workArea.Y, workArea.Bottom - height);
        return new(x, y, width, height);
    }
}

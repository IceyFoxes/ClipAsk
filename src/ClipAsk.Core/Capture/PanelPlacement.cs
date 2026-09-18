namespace ClipAsk.Core.Capture;

public static class PanelPlacement
{
    public static PixelRect PlaceCentered(PixelRect workArea, int desiredWidth, int desiredHeight)
    {
        var width = Math.Clamp(desiredWidth, 1, workArea.Width);
        var height = Math.Clamp(desiredHeight, 1, workArea.Height);
        return new(
            workArea.X + ((workArea.Width - width) / 2),
            workArea.Y + ((workArea.Height - height) / 2),
            width,
            height);
    }

    public static PixelRect PlaceAtSelectionTopLeft(PixelRect selection, PixelRect workArea, int desiredWidth, int desiredHeight)
    {
        var width = Math.Clamp(desiredWidth, 1, workArea.Width);
        var height = Math.Clamp(desiredHeight, 1, workArea.Height);
        return new(
            Math.Clamp(selection.X, workArea.X, workArea.Right - width),
            Math.Clamp(selection.Y, workArea.Y, workArea.Bottom - height),
            width,
            height);
    }

    public static PixelRect KeepVisible(PixelRect currentBounds, PixelRect workArea, int desiredWidth, int desiredHeight)
    {
        var width = Math.Clamp(desiredWidth, 1, workArea.Width);
        var height = Math.Clamp(desiredHeight, 1, workArea.Height);
        return new(
            Math.Clamp(currentBounds.X, workArea.X, workArea.Right - width),
            Math.Clamp(currentBounds.Y, workArea.Y, workArea.Bottom - height),
            width,
            height);
    }

    public static PixelRect Place(PixelRect crop, PixelRect workArea, int desiredWidth, int desiredHeight, int gap = 12)
    {
        var width = Math.Clamp(desiredWidth, 1, workArea.Width);
        var height = Math.Clamp(desiredHeight, 1, workArea.Height);
        var candidates = new[]
        {
            new PixelRect(crop.Right + gap, crop.Y, width, height),
            new PixelRect(crop.X - gap - width, crop.Y, width, height),
            new PixelRect(crop.X, crop.Bottom + gap, width, height),
            new PixelRect(crop.X, crop.Y - gap - height, width, height)
        };

        foreach (var candidate in candidates)
        {
            if (Contains(workArea, candidate) && OverlapArea(candidate, crop) == 0)
                return candidate;
        }

        return candidates
            .Select((candidate, index) => new
            {
                Rect = new PixelRect(
                    Math.Clamp(candidate.X, workArea.X, workArea.Right - width),
                    Math.Clamp(candidate.Y, workArea.Y, workArea.Bottom - height),
                    width,
                    height),
                Index = index
            })
            .OrderBy(candidate => OverlapArea(candidate.Rect, crop))
            .ThenBy(candidate => candidate.Index)
            .Select(candidate => candidate.Rect)
            .First();
    }

    private static bool Contains(PixelRect outer, PixelRect inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private static long OverlapArea(PixelRect first, PixelRect second)
    {
        var width = Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.X, second.X));
        var height = Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Y, second.Y));
        return (long)width * height;
    }
}

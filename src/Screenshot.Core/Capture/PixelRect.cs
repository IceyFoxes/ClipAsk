namespace Screenshot.Core.Capture;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsUsable => Width >= 4 && Height >= 4;

    public static PixelRect FromDrag(int x1, int y1, int x2, int y2, int width, int height)
    {
        x1 = Math.Clamp(x1, 0, width);
        x2 = Math.Clamp(x2, 0, width);
        y1 = Math.Clamp(y1, 0, height);
        y2 = Math.Clamp(y2, 0, height);
        return new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;

namespace ClipAsk.Desktop;

internal static class NativeMethods
{
    internal const int ModAlt = 0x0001;
    internal const int ModControl = 0x0002;
    internal const int WmQueryEndSession = 0x0011;
    internal const int WmEndSession = 0x0016;
    internal const int WmHotkey = 0x0312;
    internal const long EndSessionCloseApp = 0x00000001;
    internal const uint ModNoRepeat = 0x4000;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    private const int SwShowNoActivate = 4;
    private const int SwRestore = 9;
    internal static readonly IntPtr HwndTopmost = new(-1);
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerPreferenceRound = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point(int x, int y)
    {
        internal int X = x;
        internal int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        internal int Size;
        internal Rect Monitor;
        internal Rect Work;
        internal uint Flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr handle, int id);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr handle);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr handle);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(IntPtr handle);

    internal static (System.Drawing.Rectangle Bounds, System.Drawing.Rectangle WorkArea) GetMonitorUnderCursor()
    {
        if (!GetCursorPos(out var cursor))
            throw new InvalidOperationException("Could not locate the pointer.");
        var monitor = MonitorFromPoint(cursor, 2);
        if (monitor == IntPtr.Zero)
            throw new InvalidOperationException("Could not locate the monitor under the pointer.");
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            throw new InvalidOperationException("Could not read the monitor bounds.");
        return (ToRectangle(info.Monitor), ToRectangle(info.Work));
    }

    internal static BitmapSource CaptureMonitor(System.Drawing.Rectangle bounds)
    {
        using var bitmap = new DrawingBitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = DrawingGraphics.FromImage(bitmap))
            graphics.CopyFromScreen(new DrawingPoint(bounds.X, bounds.Y), System.Drawing.Point.Empty, new DrawingSize(bounds.Width, bounds.Height), System.Drawing.CopyPixelOperation.SourceCopy);
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    internal static void PositionWindow(Window window, System.Drawing.Rectangle bounds)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;
        SetWindowPos(handle, HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height, SwpNoActivate | SwpShowWindow);
    }

    internal static void RestoreWindow(Window window, bool activate)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
            _ = ShowWindow(handle, activate ? SwRestore : SwShowNoActivate);
    }

    internal static bool TryGetWindowBounds(Window window, out System.Drawing.Rectangle bounds)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero && GetWindowRect(handle, out var rect))
        {
            bounds = ToRectangle(rect);
            return true;
        }
        bounds = default;
        return false;
    }

    internal static void EnableRoundedCorners(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;
        var preference = DwmCornerPreferenceRound;
        _ = DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref preference, sizeof(int));
    }

    internal static bool IsRestartManagerMessage(int message, IntPtr parameter) =>
        (message is WmQueryEndSession or WmEndSession) &&
        (parameter.ToInt64() & EndSessionCloseApp) != 0;

    private static System.Drawing.Rectangle ToRectangle(Rect rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using InputKeyEventArgs = System.Windows.Input.KeyEventArgs;
using InputMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using ClipAsk.Core.Capture;

namespace ClipAsk.Desktop;

internal enum CaptureIntent
{
    Automatic,
    Prompted
}

internal sealed class CaptureOverlay : Window
{
    private readonly BitmapSource source;
    private readonly Action<PixelRect, CaptureIntent> selectionCompleted;
    private readonly Action cancelled;
    private readonly Canvas canvas = new();
    private readonly Path dim = new() { Fill = new SolidColorBrush(Color.FromArgb(105, 0, 0, 0)) };
    private readonly WpfRectangle selection = new() { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2, Fill = Brushes.Transparent };
    private TextBlock? hint;
    private WpfPoint start;
    private MouseButton dragButton;
    private bool dragging;
    private bool escapePressed;
    private bool finished;

    public CaptureOverlay(BitmapSource source, System.Drawing.Rectangle monitorBounds, Action<PixelRect, CaptureIntent> selectionCompleted, Action cancelled)
    {
        this.source = source;
        this.selectionCompleted = selectionCompleted;
        this.cancelled = cancelled;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        Background = Brushes.Black;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = true;
        Cursor = Cursors.Cross;
        Content = canvas;
        SourceInitialized += (_, _) => NativeMethods.PositionTopmostWindow(this, monitorBounds);
        Loaded += (_, _) => BuildVisuals();
        MouseLeftButtonDown += OnMouseButtonDown;
        MouseRightButtonDown += OnMouseButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseButtonUp;
        MouseRightButtonUp += OnMouseButtonUp;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        Closed += (_, _) =>
        {
            if (!finished)
            {
                finished = true;
                cancelled();
            }
        };
        Deactivated += (_, _) =>
        {
            if (!finished)
                CancelSelection();
        };
        LostMouseCapture += (_, _) =>
        {
            if (dragging && !finished)
                CancelSelection();
        };
    }

    private void BuildVisuals()
    {
        canvas.Children.Clear();
        var image = new Image { Source = source, Stretch = Stretch.Fill, Width = ActualWidth, Height = ActualHeight };
        canvas.Children.Add(image);
        canvas.Children.Add(dim);
        canvas.Children.Add(selection);
        UpdateDim();
        hint = new TextBlock
        {
            Text = "Left-drag to add an instruction · Right-drag to ask instantly · Esc cancels",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(190, 20, 25, 35)),
            Padding = new Thickness(10, 6, 10, 6),
            FontSize = 13
        };
        Canvas.SetLeft(hint, 16);
        Canvas.SetTop(hint, 16);
        canvas.Children.Add(hint);
    }

    private void OnMouseButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (dragging || args.ChangedButton is not (MouseButton.Left or MouseButton.Right))
            return;
        dragging = true;
        finished = false;
        dragButton = args.ChangedButton;
        start = args.GetPosition(canvas);
        selection.Width = 0;
        selection.Height = 0;
        selection.Stroke = dragButton == MouseButton.Left
            ? new SolidColorBrush(Color.FromRgb(181, 155, 255))
            : new SolidColorBrush(Color.FromRgb(120, 169, 255));
        if (hint is not null)
            hint.Text = dragButton == MouseButton.Left
                ? "Release to add an instruction · Esc cancels"
                : "Release to ask instantly · Esc cancels";
        UpdateDim();
        CaptureMouse();
        args.Handled = true;
    }

    private void OnMouseMove(object sender, InputMouseEventArgs args)
    {
        if (!dragging)
            return;
        UpdateSelection(args.GetPosition(canvas));
    }

    private void OnMouseButtonUp(object sender, MouseButtonEventArgs args)
    {
        if (!dragging || args.ChangedButton != dragButton)
            return;
        UpdateSelection(args.GetPosition(canvas));
        dragging = false;
        ReleaseMouseCapture();
        var end = args.GetPosition(canvas);
        var x1 = ToPixelX(start.X);
        var y1 = ToPixelY(start.Y);
        var x2 = ToPixelX(end.X);
        var y2 = ToPixelY(end.Y);
        var rect = PixelRect.FromDrag(x1, y1, x2, y2, source.PixelWidth, source.PixelHeight);
        if (rect.IsUsable)
        {
            finished = true;
            selectionCompleted(rect, IntentForButton(dragButton));
        }
        else
        {
            CancelSelection();
        }
        args.Handled = true;
    }

    internal static CaptureIntent IntentForButton(MouseButton button) => button switch
    {
        MouseButton.Left => CaptureIntent.Prompted,
        MouseButton.Right => CaptureIntent.Automatic,
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Only left and right drag buttons are supported.")
    };

    private void UpdateSelection(WpfPoint current)
    {
        var x = Math.Min(start.X, current.X);
        var y = Math.Min(start.Y, current.Y);
        x = Math.Clamp(x, 0, ActualWidth);
        y = Math.Clamp(y, 0, ActualHeight);
        var right = Math.Clamp(Math.Max(start.X, current.X), 0, ActualWidth);
        var bottom = Math.Clamp(Math.Max(start.Y, current.Y), 0, ActualHeight);
        Canvas.SetLeft(selection, x);
        Canvas.SetTop(selection, y);
        selection.Width = Math.Max(0, right - x);
        selection.Height = Math.Max(0, bottom - y);
        UpdateDim();
    }

    private void UpdateDim()
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        var x = Canvas.GetLeft(selection);
        var y = Canvas.GetTop(selection);
        if (double.IsNaN(x)) x = 0;
        if (double.IsNaN(y)) y = 0;
        group.Children.Add(new RectangleGeometry(new Rect(x, y, selection.Width, selection.Height)));
        dim.Data = group;
    }

    private void OnPreviewKeyDown(object sender, InputKeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            escapePressed = true;
            args.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object sender, InputKeyEventArgs args)
    {
        if (args.Key != Key.Escape || !escapePressed)
            return;
        args.Handled = true;
        escapePressed = false;
        CancelSelection();
    }

    internal void CancelSelection()
    {
        if (finished)
            return;
        finished = true;
        dragging = false;
        ReleaseMouseCapture();
        Close();
        cancelled();
    }

    private int ToPixelX(double value) => (int)Math.Round(value * source.PixelWidth / Math.Max(1, ActualWidth));

    private int ToPixelY(double value) => (int)Math.Round(value * source.PixelHeight / Math.Max(1, ActualHeight));
}

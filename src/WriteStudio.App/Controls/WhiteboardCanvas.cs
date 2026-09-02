using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;
using WriteStudio.Whiteboard.Geometry;

#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
#endif

namespace WriteStudio.App.Controls;

#if WINDOWS
public class WhiteboardCanvas : Canvas
{
    public static readonly DependencyProperty WhiteboardServiceProperty =
        DependencyProperty.Register(
            nameof(WhiteboardService), 
            typeof(IWhiteboardService), 
            typeof(WhiteboardCanvas), 
            new FrameworkPropertyMetadata(null, OnWhiteboardServiceChanged));

    public IWhiteboardService? WhiteboardService
    {
        get => (IWhiteboardService?)GetValue(WhiteboardServiceProperty);
        set => SetValue(WhiteboardServiceProperty, value);
    }

    private Guid? _currentStrokeId;
    private Point? _shapeStartPoint;

    public WhiteboardCanvas()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        Focusable = true;
    }

    private static void OnWhiteboardServiceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WhiteboardCanvas canvas)
        {
            if (e.OldValue is IWhiteboardService oldSvc)
            {
                oldSvc.PageChanged -= canvas.OnPageChanged;
                oldSvc.StrokeAdded -= canvas.OnStrokeChanged;
                oldSvc.StrokeCompleted -= canvas.OnStrokeChanged;
                oldSvc.StrokesErased -= canvas.OnStrokesErased;
                oldSvc.PageCleared -= canvas.OnPageCleared;
                oldSvc.BackgroundChanged -= canvas.OnBackgroundChanged;
            }

            if (e.NewValue is IWhiteboardService newSvc)
            {
                newSvc.PageChanged += canvas.OnPageChanged;
                newSvc.StrokeAdded += canvas.OnStrokeChanged;
                newSvc.StrokeCompleted += canvas.OnStrokeChanged;
                newSvc.StrokesErased += canvas.OnStrokesErased;
                newSvc.PageCleared += canvas.OnPageCleared;
                newSvc.BackgroundChanged += canvas.OnBackgroundChanged;
            }

            canvas.InvalidateVisual();
        }
    }

    private void OnPageChanged(object? sender, int page) => InvalidateVisual();
    private void OnStrokeChanged(object? sender, DrawingStroke stroke) => InvalidateVisual();
    private void OnStrokesErased(object? sender, IReadOnlyList<Guid> ids) => InvalidateVisual();
    private void OnPageCleared(object? sender, int page) => InvalidateVisual();
    private void OnBackgroundChanged(object? sender, BackgroundStyle bg) => InvalidateVisual();

    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        base.OnStylusDown(e);
        CaptureStylus();
        var pt = e.GetPosition(this);
        float pressure = (float)e.GetStylusPoints(this)[0].PressureFactor;
        HandlePointerDown(pt.X, pt.Y, pressure);
    }

    protected override void OnStylusMove(StylusEventArgs e)
    {
        base.OnStylusMove(e);
        var pt = e.GetPosition(this);
        float pressure = (float)e.GetStylusPoints(this)[0].PressureFactor;
        HandlePointerMove(pt.X, pt.Y, pressure);
    }

    protected override void OnStylusUp(StylusEventArgs e)
    {
        base.OnStylusUp(e);
        ReleaseStylusCapture();
        HandlePointerUp();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            CaptureMouse();
            var pt = e.GetPosition(this);
            HandlePointerDown(pt.X, pt.Y, 0.5f);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            var pt = e.GetPosition(this);
            HandlePointerMove(pt.X, pt.Y, 0.5f);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            HandlePointerUp();
        }
    }

    private void HandlePointerDown(double x, double y, float pressure)
    {
        if (WhiteboardService == null) return;

        if (WhiteboardService.ActiveTool == StrokeToolType.Eraser)
        {
            WhiteboardService.EraseAt(x, y, WhiteboardService.ActiveThickness * 2.0);
            InvalidateVisual();
            return;
        }

        if (IsGeometricShape(WhiteboardService.ActiveTool))
        {
            _shapeStartPoint = new Point(x, y);
            return;
        }

        var stroke = WhiteboardService.StartStroke(x, y, pressure, TimeSpan.Zero);
        _currentStrokeId = stroke.Id;
        InvalidateVisual();
    }

    private void HandlePointerMove(double x, double y, float pressure)
    {
        if (WhiteboardService == null) return;

        if (WhiteboardService.ActiveTool == StrokeToolType.Eraser)
        {
            WhiteboardService.EraseAt(x, y, WhiteboardService.ActiveThickness * 2.0);
            InvalidateVisual();
            return;
        }

        if (_currentStrokeId.HasValue)
        {
            WhiteboardService.AppendPoint(_currentStrokeId.Value, x, y, pressure, TimeSpan.Zero);
            InvalidateVisual();
        }
    }

    private void HandlePointerUp()
    {
        if (WhiteboardService == null) return;

        if (_currentStrokeId.HasValue)
        {
            WhiteboardService.CompleteStroke(_currentStrokeId.Value);
            _currentStrokeId = null;
            InvalidateVisual();
        }
        else if (_shapeStartPoint.HasValue)
        {
            // Complete geometric shape
            _shapeStartPoint = null;
            InvalidateVisual();
        }
    }

    private bool IsGeometricShape(StrokeToolType tool) =>
        tool is StrokeToolType.Line or StrokeToolType.Rectangle or StrokeToolType.Circle or StrokeToolType.Arrow;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (WhiteboardService == null) return;

        var page = WhiteboardService.CurrentPage;
        if (page == null) return;

        // Render Background
        DrawBackground(dc, page.Background);

        // Render Page Strokes
        foreach (var stroke in page.Strokes)
        {
            DrawStroke(dc, stroke);
        }
    }

    private void DrawBackground(DrawingContext dc, BackgroundStyle background)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        Brush bgBrush = background switch
        {
            BackgroundStyle.Blackboard or BackgroundStyle.DarkGrid or BackgroundStyle.DarkRuled 
                => new SolidColorBrush(Color.FromRgb(28, 33, 39)),
            _ => Brushes.White
        };

        dc.DrawRectangle(bgBrush, null, bounds);

        if (background is BackgroundStyle.LightGrid or BackgroundStyle.DarkGrid)
        {
            var pen = new Pen(background == BackgroundStyle.LightGrid 
                ? new SolidColorBrush(Color.FromRgb(230, 235, 240)) 
                : new SolidColorBrush(Color.FromRgb(50, 58, 69)), 1.0);

            for (double x = 0; x < ActualWidth; x += 40)
                dc.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
            for (double y = 0; y < ActualHeight; y += 40)
                dc.DrawLine(pen, new Point(0, y), new Point(ActualWidth, y));
        }
        else if (background is BackgroundStyle.Ruled or BackgroundStyle.DarkRuled)
        {
            var pen = new Pen(background == BackgroundStyle.Ruled 
                ? new SolidColorBrush(Color.FromRgb(210, 225, 245)) 
                : new SolidColorBrush(Color.FromRgb(50, 58, 69)), 1.2);

            for (double y = 80; y < ActualHeight; y += 36)
                dc.DrawLine(pen, new Point(0, y), new Point(ActualWidth, y));
        }
    }

    private void DrawStroke(DrawingContext dc, DrawingStroke stroke)
    {
        if (stroke.Points.Count == 0) return;

        var color = Color.FromArgb((byte)(stroke.Color.A * stroke.Opacity), stroke.Color.R, stroke.Color.G, stroke.Color.B);
        var brush = new SolidColorBrush(color);
        var pen = new Pen(brush, stroke.Thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        if (stroke.Points.Count == 1)
        {
            dc.DrawEllipse(brush, null, new Point(stroke.Points[0].X, stroke.Points[0].Y), stroke.Thickness / 2, stroke.Thickness / 2);
            return;
        }

        for (int i = 0; i < stroke.Points.Count - 1; i++)
        {
            var p1 = stroke.Points[i];
            var p2 = stroke.Points[i + 1];
            dc.DrawLine(pen, new Point(p1.X, p1.Y), new Point(p2.X, p2.Y));
        }
    }
}
#endif

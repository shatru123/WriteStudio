using SkiaSharp;
using WriteStudio.Core.Models;

namespace WriteStudio.Rendering;

public class SkiaFrameRenderer : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly SKBitmap _bitmap;
    private readonly SKCanvas _canvas;
    private bool _isDisposed;

    public int Width => _width;
    public int Height => _height;

    public SkiaFrameRenderer(int width = 1920, int height = 1080)
    {
        _width = width;
        _height = height;
        _bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _canvas = new SKCanvas(_bitmap);
    }

    public byte[] RenderFrame(WhiteboardRenderState state, double sourceCanvasWidth = 1920, double sourceCanvasHeight = 1080)
    {
        _canvas.Clear(SKColors.Transparent);

        // Compute scaling factor between source canvas and target export resolution
        float scaleX = (float)(_width / sourceCanvasWidth);
        float scaleY = (float)(_height / sourceCanvasHeight);

        // 1. Render Background
        RenderBackground(state.Background);

        // 2. Render Strokes
        _canvas.Save();
        _canvas.Scale(scaleX, scaleY);

        foreach (var stroke in state.VisibleStrokes)
        {
            RenderStroke(stroke);
        }

        _canvas.Restore();

        // 3. Render Webcam Layer if visible
        if (state.CameraLayout.IsVisible && state.CameraLayout.Preset != CameraPositionPreset.Hidden)
        {
            RenderWebcamLayer(state.CameraLayout);
        }

        _canvas.Flush();
        return _bitmap.Bytes;
    }

    private void RenderBackground(BackgroundStyle background)
    {
        switch (background)
        {
            case BackgroundStyle.Blackboard:
                _canvas.Clear(new SKColor(28, 33, 39));
                break;

            case BackgroundStyle.DarkGrid:
                _canvas.Clear(new SKColor(28, 33, 39));
                DrawGrid(new SKColor(50, 58, 69), 40);
                break;

            case BackgroundStyle.LightGrid:
                _canvas.Clear(SKColors.White);
                DrawGrid(new SKColor(230, 235, 240), 40);
                break;

            case BackgroundStyle.Ruled:
                _canvas.Clear(SKColors.White);
                DrawRuledLines(new SKColor(210, 225, 245), 36);
                break;

            case BackgroundStyle.DarkRuled:
                _canvas.Clear(new SKColor(28, 33, 39));
                DrawRuledLines(new SKColor(50, 58, 69), 36);
                break;

            case BackgroundStyle.White:
            default:
                _canvas.Clear(SKColors.White);
                break;
        }
    }

    private void DrawGrid(SKColor gridColor, float spacing)
    {
        using var paint = new SKPaint
        {
            Color = gridColor,
            StrokeWidth = 1,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        for (float x = 0; x < _width; x += spacing)
        {
            _canvas.DrawLine(x, 0, x, _height, paint);
        }

        for (float y = 0; y < _height; y += spacing)
        {
            _canvas.DrawLine(0, y, _width, y, paint);
        }
    }

    private void DrawRuledLines(SKColor lineColor, float spacing)
    {
        using var paint = new SKPaint
        {
            Color = lineColor,
            StrokeWidth = 1.2f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        for (float y = 80; y < _height; y += spacing)
        {
            _canvas.DrawLine(0, y, _width, y, paint);
        }
    }

    private void RenderStroke(DrawingStroke stroke)
    {
        if (stroke.Points.Count == 0 && stroke.ToolType != StrokeToolType.Text)
            return;

        var color = new SKColor(stroke.Color.R, stroke.Color.G, stroke.Color.B, (byte)(stroke.Color.A * stroke.Opacity));

        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = (float)stroke.Thickness,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Style = SKPaintStyle.Stroke
        };

        if (stroke.IsHighlighter)
        {
            paint.BlendMode = SKBlendMode.SrcOver;
        }

        if (stroke.ToolType == StrokeToolType.Text && !string.IsNullOrEmpty(stroke.TextContent))
        {
            paint.Style = SKPaintStyle.Fill;
            paint.TextSize = (float)stroke.FontSize;
            if (stroke.Points.Count > 0)
            {
                _canvas.DrawText(stroke.TextContent, (float)stroke.Points[0].X, (float)stroke.Points[0].Y, paint);
            }
            return;
        }

        if (stroke.Points.Count == 1)
        {
            paint.Style = SKPaintStyle.Fill;
            _canvas.DrawCircle((float)stroke.Points[0].X, (float)stroke.Points[0].Y, (float)(stroke.Thickness / 2.0), paint);
            return;
        }

        // Connect points with pressure sensitivity
        for (int i = 0; i < stroke.Points.Count - 1; i++)
        {
            var p1 = stroke.Points[i];
            var p2 = stroke.Points[i + 1];

            float avgPressure = (p1.Pressure + p2.Pressure) / 2.0f;
            paint.StrokeWidth = (float)(stroke.Thickness * (0.5 + 0.8 * avgPressure));

            _canvas.DrawLine((float)p1.X, (float)p1.Y, (float)p2.X, (float)p2.Y, paint);
        }
    }

    private void RenderWebcamLayer(CameraLayout layout)
    {
        float x = (float)(layout.NormalizedX * _width);
        float y = (float)(layout.NormalizedY * _height);
        float w = (float)(layout.NormalizedWidth * _width);
        float h = (float)(layout.NormalizedHeight * _height);
        float radius = (float)layout.CornerRadius;

        var rect = new SKRect(x, y, x + w, y + h);

        using var bgPaint = new SKPaint
        {
            Color = new SKColor(18, 24, 38),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        using var borderPaint = new SKPaint
        {
            Color = new SKColor(52, 131, 235),
            StrokeWidth = (float)layout.BorderThickness,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        if (radius > 0)
        {
            _canvas.DrawRoundRect(rect, radius, radius, bgPaint);
            if (layout.HasBorder)
            {
                _canvas.DrawRoundRect(rect, radius, radius, borderPaint);
            }
        }
        else
        {
            _canvas.DrawRect(rect, bgPaint);
            if (layout.HasBorder)
            {
                _canvas.DrawRect(rect, borderPaint);
            }
        }

        // Render simulated camera watermark / avatar text in PiP
        using var textPaint = new SKPaint
        {
            Color = SKColors.LightGray,
            TextSize = Math.Max(12, h * 0.12f),
            IsAntialias = true,
            TextAlign = SKTextAlign.Center
        };

        _canvas.DrawText("Presenter Camera", rect.MidX, rect.MidY, textPaint);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _canvas.Dispose();
        _bitmap.Dispose();
        _isDisposed = true;
    }
}

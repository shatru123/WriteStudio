using SkiaSharp;
using WriteStudio.Core.Models;

namespace WriteStudio.Rendering;

public class SkiaFrameRenderer : IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly SKBitmap _bitmap;
    private readonly SKCanvas _canvas;
    private readonly byte[] _frameBuffer;
    private bool _isDisposed;

    public int Width => _width;
    public int Height => _height;

    public SkiaFrameRenderer(int width = 1920, int height = 1080)
    {
        _width = width;
        _height = height;
        _bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _canvas = new SKCanvas(_bitmap);
        _frameBuffer = new byte[width * height * 4];
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

        // Copy directly to reusable frameBuffer
        _bitmap.GetPixelSpan().CopyTo(_frameBuffer);
        return _frameBuffer;
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

    private void DrawGrid(SKColor color, int spacing)
    {
        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = 1.0f,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke
        };

        for (int x = 0; x < _width; x += spacing)
        {
            _canvas.DrawLine(x, 0, x, _height, paint);
        }

        for (int y = 0; y < _height; y += spacing)
        {
            _canvas.DrawLine(0, y, _width, y, paint);
        }
    }

    private void DrawRuledLines(SKColor color, int spacing)
    {
        using var paint = new SKPaint
        {
            Color = color,
            StrokeWidth = 1.2f,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke
        };

        for (int y = 80; y < _height; y += spacing)
        {
            _canvas.DrawLine(0, y, _width, y, paint);
        }
    }

    private void RenderStroke(DrawingStroke stroke)
    {
        if (stroke.Points == null || stroke.Points.Count == 0) return;

        var skColor = new SKColor(stroke.Color.R, stroke.Color.G, stroke.Color.B, (byte)(stroke.Color.A * stroke.Opacity));

        if (stroke.ToolType == StrokeToolType.Text && !string.IsNullOrWhiteSpace(stroke.TextContent))
        {
            using var textPaint = new SKPaint
            {
                Color = skColor,
                TextSize = (float)stroke.FontSize,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName(stroke.FontFamily ?? "sans-serif")
            };
            var p0 = stroke.Points[0];
            _canvas.DrawText(stroke.TextContent, (float)p0.X, (float)p0.Y, textPaint);
            return;
        }

        using var paint = new SKPaint
        {
            Color = skColor,
            StrokeWidth = (float)stroke.Thickness,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };

        if (stroke.ToolType == StrokeToolType.Highlighter)
        {
            paint.BlendMode = SKBlendMode.SrcOver;
        }

        if (stroke.Points.Count == 1)
        {
            var p = stroke.Points[0];
            using var dotPaint = new SKPaint
            {
                Color = skColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            _canvas.DrawCircle((float)p.X, (float)p.Y, (float)(stroke.Thickness / 2.0), dotPaint);
            return;
        }

        using var path = new SKPath();
        path.MoveTo((float)stroke.Points[0].X, (float)stroke.Points[0].Y);

        for (int i = 1; i < stroke.Points.Count; i++)
        {
            var p = stroke.Points[i];
            path.LineTo((float)p.X, (float)p.Y);
        }

        _canvas.DrawPath(path, paint);
    }

    private void RenderWebcamLayer(CameraLayout layout)
    {
        float pipW = (float)(_width * layout.NormalizedWidth);
        float pipH = (float)(_height * layout.NormalizedHeight);
        float pipX = (float)(_width * layout.NormalizedX);
        float pipY = (float)(_height * layout.NormalizedY);

        var rect = new SKRect(pipX, pipY, pipX + pipW, pipY + pipH);

        // Draw camera frame placeholder
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(15, 23, 42, 230),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        _canvas.DrawRoundRect(rect, (float)layout.CornerRadius, (float)layout.CornerRadius, bgPaint);

        if (layout.HasBorder)
        {
            using var borderPaint = new SKPaint
            {
                Color = new SKColor(56, 189, 248),
                StrokeWidth = (float)layout.BorderThickness,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            _canvas.DrawRoundRect(rect, (float)layout.CornerRadius, (float)layout.CornerRadius, borderPaint);
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _canvas.Dispose();
            _bitmap.Dispose();
            _isDisposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

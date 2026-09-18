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

        // 2. Render Page Question if present
        if (state.Question != null)
        {
            _canvas.Save();
            _canvas.Scale(scaleX, scaleY);
            RenderQuestion(state.Question, state.Background);
            _canvas.Restore();
        }

        // 3. Render Strokes
        _canvas.Save();
        _canvas.Scale(scaleX, scaleY);

        foreach (var stroke in state.VisibleStrokes)
        {
            RenderStroke(stroke);
        }

        _canvas.Restore();

        // 4. Render Webcam Layer if visible
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

    private void RenderQuestion(QuestionItem question, BackgroundStyle background)
    {
        if (question == null || string.IsNullOrWhiteSpace(question.QuestionText)) return;

        bool isDarkBg = background == BackgroundStyle.Blackboard || background == BackgroundStyle.DarkGrid || background == BackgroundStyle.DarkRuled;

        float cardX = (float)question.X;
        float cardY = (float)question.Y;
        float cardW = (float)Math.Max(500, question.Width);
        float padding = 24f;
        float innerW = cardW - (padding * 2);
        float currentY = cardY + padding;

        float fontSize = (float)Math.Max(16, question.FontSize);
        float titleFontSize = fontSize * 0.75f;
        float optionFontSize = fontSize * 0.85f;

        var fontFamily = question.FontFamily ?? "sans-serif";

        using var titlePaint = new SKPaint
        {
            Color = isDarkBg ? new SKColor(56, 189, 248) : new SKColor(2, 132, 199),
            TextSize = titleFontSize,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };

        using var qTextPaint = new SKPaint
        {
            Color = isDarkBg ? SKColors.White : new SKColor(15, 23, 42),
            TextSize = fontSize,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };

        using var optionTextPaint = new SKPaint
        {
            Color = isDarkBg ? new SKColor(241, 245, 249) : new SKColor(30, 41, 59),
            TextSize = optionFontSize,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };

        using var badgeTextPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = optionFontSize * 0.9f,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        };

        // Helper to wrap text into lines
        List<string> WrapText(string text, SKPaint paint, float maxWidth)
        {
            var lines = new List<string>();
            var words = text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            string curLine = "";

            foreach (var word in words)
            {
                string testLine = string.IsNullOrEmpty(curLine) ? word : curLine + " " + word;
                float w = paint.MeasureText(testLine);
                if (w > maxWidth && !string.IsNullOrEmpty(curLine))
                {
                    lines.Add(curLine);
                    curLine = word;
                }
                else
                {
                    curLine = testLine;
                }
            }
            if (!string.IsNullOrEmpty(curLine)) lines.Add(curLine);
            return lines;
        }

        var qLines = WrapText(question.QuestionText, qTextPaint, innerW);
        float qLineHeight = fontSize * 1.35f;

        // Calculate card height dynamically
        float estimatedH = padding;
        if (!string.IsNullOrEmpty(question.QuestionNumber)) estimatedH += titleFontSize + 12f;
        estimatedH += (qLines.Count * qLineHeight) + 16f;

        float optHeight = optionFontSize * 2.2f;
        float optSpacing = 10f;
        if (question.Options != null)
        {
            estimatedH += (question.Options.Count * (optHeight + optSpacing));
        }

        if (question.IsAnswerRevealed && !string.IsNullOrEmpty(question.CorrectAnswer))
        {
            estimatedH += 36f;
        }
        estimatedH += padding;

        var cardRect = new SKRect(cardX, cardY, cardX + cardW, cardY + estimatedH);

        // 1. Draw Card Container
        using (var cardBgPaint = new SKPaint
        {
            Color = isDarkBg ? new SKColor(30, 41, 59, 240) : new SKColor(255, 255, 255, 245),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        })
        {
            _canvas.DrawRoundRect(cardRect, 14f, 14f, cardBgPaint);
        }

        using (var cardBorderPaint = new SKPaint
        {
            Color = isDarkBg ? new SKColor(56, 189, 248, 180) : new SKColor(203, 213, 225, 220),
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        })
        {
            _canvas.DrawRoundRect(cardRect, 14f, 14f, cardBorderPaint);
        }

        // 2. Draw Question Number Title
        if (!string.IsNullOrEmpty(question.QuestionNumber))
        {
            _canvas.DrawText(question.QuestionNumber.ToUpperInvariant(), cardX + padding, currentY + titleFontSize, titlePaint);
            currentY += titleFontSize + 12f;
        }

        // 3. Draw Question Text Lines
        foreach (var line in qLines)
        {
            _canvas.DrawText(line, cardX + padding, currentY + fontSize, qTextPaint);
            currentY += qLineHeight;
        }
        currentY += 12f;

        // 4. Draw Options
        if (question.Options != null)
        {
            for (int i = 0; i < question.Options.Count; i++)
            {
                var opt = question.Options[i];
                var optRect = new SKRect(cardX + padding, currentY, cardX + cardW - padding, currentY + optHeight);
                bool isCorrect = question.IsAnswerRevealed && string.Equals(question.CorrectAnswer, opt.Label, StringComparison.OrdinalIgnoreCase);

                // Option Background
                using (var optBgPaint = new SKPaint
                {
                    Color = isCorrect
                        ? (isDarkBg ? new SKColor(22, 101, 52, 230) : new SKColor(220, 252, 231))
                        : (isDarkBg ? new SKColor(15, 23, 42, 220) : new SKColor(248, 250, 252)),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                })
                {
                    _canvas.DrawRoundRect(optRect, 8f, 8f, optBgPaint);
                }

                // Option Border
                using (var optBorderPaint = new SKPaint
                {
                    Color = isCorrect
                        ? new SKColor(34, 197, 94)
                        : (isDarkBg ? new SKColor(51, 65, 85) : new SKColor(226, 232, 240)),
                    StrokeWidth = isCorrect ? 2f : 1f,
                    Style = SKPaintStyle.Stroke,
                    IsAntialias = true
                })
                {
                    _canvas.DrawRoundRect(optRect, 8f, 8f, optBorderPaint);
                }

                // Letter Badge Pill (e.g. A, B, C, D)
                float badgeSize = optHeight - 12f;
                var badgeRect = new SKRect(cardX + padding + 8f, currentY + 6f, cardX + padding + 8f + badgeSize, currentY + 6f + badgeSize);
                using (var badgeBg = new SKPaint
                {
                    Color = isCorrect ? new SKColor(34, 197, 94) : (isDarkBg ? new SKColor(56, 189, 248) : new SKColor(2, 132, 199)),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                })
                {
                    _canvas.DrawRoundRect(badgeRect, 6f, 6f, badgeBg);
                }

                float labelW = badgeTextPaint.MeasureText(opt.Label);
                _canvas.DrawText(opt.Label, badgeRect.MidX - (labelW / 2), badgeRect.MidY + (badgeTextPaint.TextSize / 3), badgeTextPaint);

                // Option Text
                _canvas.DrawText(opt.Text, badgeRect.Right + 12f, currentY + (optHeight / 2) + (optionFontSize / 3), optionTextPaint);

                // Checkmark icon if correct and revealed
                if (isCorrect)
                {
                    using var checkPaint = new SKPaint
                    {
                        Color = new SKColor(34, 197, 94),
                        TextSize = optionFontSize * 1.1f,
                        IsAntialias = true,
                        Typeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    };
                    _canvas.DrawText("✓ Correct", optRect.Right - 90f, currentY + (optHeight / 2) + (optionFontSize / 3), checkPaint);
                }

                currentY += optHeight + optSpacing;
            }
        }

        // 5. Draw Answer Banner if Revealed
        if (question.IsAnswerRevealed && !string.IsNullOrEmpty(question.CorrectAnswer))
        {
            using var ansBannerPaint = new SKPaint
            {
                Color = new SKColor(34, 197, 94),
                TextSize = optionFontSize * 1.05f,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName(fontFamily, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            };
            _canvas.DrawText($"✓ Correct Answer: Option {question.CorrectAnswer}", cardX + padding, currentY + 16f, ansBannerPaint);
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

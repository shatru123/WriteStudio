using System.Text.Json.Serialization;

namespace WriteStudio.Core.Models;

/// <summary>
/// A vector stroke, geometric shape, or text annotation on the whiteboard.
/// Structured data enables deterministic reconstruction of the canvas at any timeline position.
/// </summary>
public class DrawingStroke
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int PageIndex { get; set; }
    public TimeSpan StartTime { get; set; } = TimeSpan.Zero;
    public TimeSpan EndTime { get; set; } = TimeSpan.Zero;
    public ColorInfo Color { get; set; } = ColorInfo.Black;
    public double Thickness { get; set; } = 3.0;
    public double Opacity { get; set; } = 1.0;
    public StrokeToolType ToolType { get; set; } = StrokeToolType.Pen;
    public List<DrawingPoint> Points { get; set; } = new();
    
    /// <summary>
    /// Optional text content when ToolType is Text.
    /// </summary>
    public string? TextContent { get; set; }
    public double FontSize { get; set; } = 18.0;
    public string FontFamily { get; set; } = "Arial";

    [JsonIgnore]
    public bool IsHighlighter => ToolType == StrokeToolType.Highlighter;

    [JsonIgnore]
    public RectBounds Bounds => RectBounds.FromPoints(Points, Thickness / 2.0);

    public DrawingStroke Clone()
    {
        return new DrawingStroke
        {
            Id = Id,
            PageIndex = PageIndex,
            StartTime = StartTime,
            EndTime = EndTime,
            Color = Color,
            Thickness = Thickness,
            Opacity = Opacity,
            ToolType = ToolType,
            Points = Points.Select(p => p with { }).ToList(),
            TextContent = TextContent,
            FontSize = FontSize,
            FontFamily = FontFamily
        };
    }
}

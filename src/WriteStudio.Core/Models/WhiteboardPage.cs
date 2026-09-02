namespace WriteStudio.Core.Models;

/// <summary>
/// Represents a discrete page in a multi-page whiteboard lesson.
/// </summary>
public class WhiteboardPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Index { get; set; }
    public string Title { get; set; } = "Page 1";
    public BackgroundStyle Background { get; set; } = BackgroundStyle.White;
    public List<DrawingStroke> Strokes { get; set; } = new();

    public WhiteboardPage Clone()
    {
        return new WhiteboardPage
        {
            Id = Id,
            Index = Index,
            Title = Title,
            Background = Background,
            Strokes = Strokes.Select(s => s.Clone()).ToList()
        };
    }
}

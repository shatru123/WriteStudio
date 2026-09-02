using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;

namespace WriteStudio.Whiteboard.Commands;

public class AddStrokeCommand : IUndoableCommand
{
    private readonly WhiteboardPage _page;
    private readonly DrawingStroke _stroke;
    private readonly Action<DrawingStroke> _onAdded;
    private readonly Action<Guid> _onRemoved;

    public string Description => $"Add Stroke ({_stroke.ToolType})";

    public AddStrokeCommand(
        WhiteboardPage page, 
        DrawingStroke stroke, 
        Action<DrawingStroke> onAdded, 
        Action<Guid> onRemoved)
    {
        _page = page;
        _stroke = stroke;
        _onAdded = onAdded;
        _onRemoved = onRemoved;
    }

    public void Execute()
    {
        if (!_page.Strokes.Any(s => s.Id == _stroke.Id))
        {
            _page.Strokes.Add(_stroke);
        }
        _onAdded(_stroke);
    }

    public void Undo()
    {
        _page.Strokes.RemoveAll(s => s.Id == _stroke.Id);
        _onRemoved(_stroke.Id);
    }
}

public class EraseStrokesCommand : IUndoableCommand
{
    private readonly WhiteboardPage _page;
    private readonly List<DrawingStroke> _erasedStrokes;
    private readonly Action<IReadOnlyList<DrawingStroke>> _onRestored;
    private readonly Action<IReadOnlyList<Guid>> _onErased;

    public string Description => $"Erase {_erasedStrokes.Count} strokes";

    public EraseStrokesCommand(
        WhiteboardPage page,
        IEnumerable<DrawingStroke> erasedStrokes,
        Action<IReadOnlyList<DrawingStroke>> onRestored,
        Action<IReadOnlyList<Guid>> onErased)
    {
        _page = page;
        _erasedStrokes = erasedStrokes.ToList();
        _onRestored = onRestored;
        _onErased = onErased;
    }

    public void Execute()
    {
        var erasedIds = _erasedStrokes.Select(s => s.Id).ToHashSet();
        _page.Strokes.RemoveAll(s => erasedIds.Contains(s.Id));
        _onErased(erasedIds.ToList());
    }

    public void Undo()
    {
        foreach (var stroke in _erasedStrokes)
        {
            if (!_page.Strokes.Any(s => s.Id == stroke.Id))
            {
                _page.Strokes.Add(stroke);
            }
        }
        _onRestored(_erasedStrokes);
    }
}

public class ClearPageCommand : IUndoableCommand
{
    private readonly WhiteboardPage _page;
    private readonly List<DrawingStroke> _previousStrokes;
    private readonly Action<int> _onCleared;
    private readonly Action<IReadOnlyList<DrawingStroke>> _onRestored;

    public string Description => $"Clear Page {_page.Index + 1}";

    public ClearPageCommand(
        WhiteboardPage page,
        Action<int> onCleared,
        Action<IReadOnlyList<DrawingStroke>> onRestored)
    {
        _page = page;
        _previousStrokes = page.Strokes.Select(s => s.Clone()).ToList();
        _onCleared = onCleared;
        _onRestored = onRestored;
    }

    public void Execute()
    {
        _page.Strokes.Clear();
        _onCleared(_page.Index);
    }

    public void Undo()
    {
        _page.Strokes.Clear();
        _page.Strokes.AddRange(_previousStrokes.Select(s => s.Clone()));
        _onRestored(_previousStrokes);
    }
}

public class ChangeBackgroundCommand : IUndoableCommand
{
    private readonly WhiteboardPage _page;
    private readonly BackgroundStyle _oldBackground;
    private readonly BackgroundStyle _newBackground;
    private readonly Action<BackgroundStyle> _onChanged;

    public string Description => $"Change Background to {_newBackground}";

    public ChangeBackgroundCommand(
        WhiteboardPage page,
        BackgroundStyle newBackground,
        Action<BackgroundStyle> onChanged)
    {
        _page = page;
        _oldBackground = page.Background;
        _newBackground = newBackground;
        _onChanged = onChanged;
    }

    public void Execute()
    {
        _page.Background = _newBackground;
        _onChanged(_newBackground);
    }

    public void Undo()
    {
        _page.Background = _oldBackground;
        _onChanged(_oldBackground);
    }
}

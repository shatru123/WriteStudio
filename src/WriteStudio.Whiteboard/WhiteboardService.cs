using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;
using WriteStudio.Whiteboard.Commands;
using WriteStudio.Whiteboard.Geometry;

namespace WriteStudio.Whiteboard;

public class WhiteboardService : IWhiteboardService
{
    private readonly IUndoRedoManager _undoRedoManager;
    private readonly List<WhiteboardPage> _pages = new();
    private readonly Dictionary<Guid, DrawingStroke> _activeInFlightStrokes = new();
    private int _currentPageIndex = 0;

    public IReadOnlyList<WhiteboardPage> Pages => _pages;
    public int CurrentPageIndex => _currentPageIndex;
    public WhiteboardPage CurrentPage => _pages.Count > 0 && _currentPageIndex < _pages.Count 
        ? _pages[_currentPageIndex] 
        : EnsureInitialPage();

    public StrokeToolType ActiveTool { get; set; } = StrokeToolType.Pen;
    public ColorInfo ActiveColor { get; set; } = ColorInfo.Black;
    public double ActiveThickness { get; set; } = 3.0;
    public double ActiveOpacity { get; set; } = 1.0;
    public BackgroundStyle ActiveBackground => CurrentPage.Background;

    public event EventHandler<int>? PageChanged;
    public event EventHandler<DrawingStroke>? StrokeAdded;
    public event EventHandler<DrawingStroke>? StrokeUpdated;
    public event EventHandler<DrawingStroke>? StrokeCompleted;
    public event EventHandler<IReadOnlyList<Guid>>? StrokesErased;
    public event EventHandler<int>? PageCleared;
    public event EventHandler<BackgroundStyle>? BackgroundChanged;

    public WhiteboardService(IUndoRedoManager undoRedoManager)
    {
        _undoRedoManager = undoRedoManager ?? throw new ArgumentNullException(nameof(undoRedoManager));
        EnsureInitialPage();
    }

    private WhiteboardPage EnsureInitialPage()
    {
        if (_pages.Count == 0)
        {
            var page = new WhiteboardPage { Index = 0, Title = "Page 1", Background = BackgroundStyle.White };
            _pages.Add(page);
            _currentPageIndex = 0;
        }
        return _pages[_currentPageIndex];
    }

    public void SetActivePage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pages.Count || pageIndex == _currentPageIndex)
            return;

        _currentPageIndex = pageIndex;
        PageChanged?.Invoke(this, _currentPageIndex);
    }

    public WhiteboardPage AddPage(BackgroundStyle background = BackgroundStyle.White)
    {
        int newIndex = _pages.Count;
        var page = new WhiteboardPage
        {
            Index = newIndex,
            Title = $"Page {newIndex + 1}",
            Background = background
        };
        _pages.Add(page);
        SetActivePage(newIndex);
        return page;
    }

    public bool RemovePage(int pageIndex)
    {
        if (_pages.Count <= 1 || pageIndex < 0 || pageIndex >= _pages.Count)
            return false;

        _pages.RemoveAt(pageIndex);
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Index = i;
            _pages[i].Title = $"Page {i + 1}";
        }

        int targetIndex = Math.Min(_currentPageIndex, _pages.Count - 1);
        _currentPageIndex = -1;
        SetActivePage(targetIndex);
        return true;
    }

    public void ClearCurrentPage()
    {
        if (CurrentPage.Strokes.Count == 0) return;

        var command = new ClearPageCommand(
            CurrentPage,
            pageIdx => PageCleared?.Invoke(this, pageIdx),
            restoredStrokes =>
            {
                foreach (var s in restoredStrokes)
                    StrokeAdded?.Invoke(this, s);
            }
        );

        _undoRedoManager.Execute(command);
    }

    public void SetBackground(BackgroundStyle background)
    {
        if (CurrentPage.Background == background) return;

        var command = new ChangeBackgroundCommand(
            CurrentPage,
            background,
            bg => BackgroundChanged?.Invoke(this, bg)
        );

        _undoRedoManager.Execute(command);
    }

    public DrawingStroke StartStroke(double x, double y, float pressure, TimeSpan timestamp)
    {
        var stroke = new DrawingStroke
        {
            Id = Guid.NewGuid(),
            PageIndex = CurrentPageIndex,
            StartTime = timestamp,
            EndTime = timestamp,
            Color = ActiveTool == StrokeToolType.Highlighter ? ColorInfo.HighlighterYellow : ActiveColor,
            Thickness = ActiveTool == StrokeToolType.Highlighter ? Math.Max(ActiveThickness * 3.0, 16.0) : ActiveThickness,
            Opacity = ActiveTool == StrokeToolType.Highlighter ? 0.45 : ActiveOpacity,
            ToolType = ActiveTool,
            Points = new List<DrawingPoint> { DrawingPoint.Create(x, y, pressure, timestamp) }
        };

        _activeInFlightStrokes[stroke.Id] = stroke;
        StrokeAdded?.Invoke(this, stroke);
        return stroke;
    }

    public void AppendPoint(Guid strokeId, double x, double y, float pressure, TimeSpan timestamp)
    {
        if (!_activeInFlightStrokes.TryGetValue(strokeId, out var stroke)) return;

        var point = DrawingPoint.Create(x, y, pressure, timestamp);
        stroke.Points.Add(point);
        stroke.EndTime = timestamp;

        StrokeUpdated?.Invoke(this, stroke);
    }

    public DrawingStroke? CompleteStroke(Guid strokeId)
    {
        if (!_activeInFlightStrokes.Remove(strokeId, out var stroke)) return null;

        var page = CurrentPage;
        var command = new AddStrokeCommand(
            page,
            stroke,
            s => StrokeCompleted?.Invoke(this, s),
            removedId => StrokesErased?.Invoke(this, new[] { removedId })
        );

        _undoRedoManager.Execute(command);
        return stroke;
    }

    public void AddStroke(DrawingStroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        var page = CurrentPage;
        var command = new AddStrokeCommand(
            page,
            stroke,
            s => StrokeAdded?.Invoke(this, s),
            removedId => StrokesErased?.Invoke(this, new[] { removedId })
        );
        _undoRedoManager.Execute(command);
    }

    public bool RemoveStroke(Guid strokeId)
    {
        var stroke = CurrentPage.Strokes.FirstOrDefault(s => s.Id == strokeId);
        if (stroke == null) return false;

        var command = new EraseStrokesCommand(
            CurrentPage,
            new[] { stroke },
            restored => { foreach (var s in restored) StrokeAdded?.Invoke(this, s); },
            erasedIds => StrokesErased?.Invoke(this, erasedIds)
        );

        _undoRedoManager.Execute(command);
        return true;
    }

    public int EraseAt(double x, double y, double radius)
    {
        var page = CurrentPage;
        var matchingStrokes = page.Strokes
            .Where(s => StrokeGeometryHelper.IsStrokeNearPoint(s, x, y, radius))
            .ToList();

        if (matchingStrokes.Count == 0) return 0;

        var command = new EraseStrokesCommand(
            page,
            matchingStrokes,
            restored => { foreach (var s in restored) StrokeAdded?.Invoke(this, s); },
            erasedIds => StrokesErased?.Invoke(this, erasedIds)
        );

        _undoRedoManager.Execute(command);
        return matchingStrokes.Count;
    }

    public void LoadPages(IEnumerable<WhiteboardPage> pages, int initialPageIndex = 0)
    {
        _pages.Clear();
        _pages.AddRange(pages);
        EnsureInitialPage();
        _undoRedoManager.Clear();
        _currentPageIndex = Math.Clamp(initialPageIndex, 0, _pages.Count - 1);
        PageChanged?.Invoke(this, _currentPageIndex);
    }
}

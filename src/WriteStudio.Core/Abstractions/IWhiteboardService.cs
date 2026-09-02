using WriteStudio.Core.Models;

namespace WriteStudio.Core.Abstractions;

public interface IWhiteboardService
{
    IReadOnlyList<WhiteboardPage> Pages { get; }
    int CurrentPageIndex { get; }
    WhiteboardPage CurrentPage { get; }
    StrokeToolType ActiveTool { get; set; }
    ColorInfo ActiveColor { get; set; }
    double ActiveThickness { get; set; }
    double ActiveOpacity { get; set; }
    BackgroundStyle ActiveBackground { get; }

    event EventHandler<int>? PageChanged;
    event EventHandler<DrawingStroke>? StrokeAdded;
    event EventHandler<DrawingStroke>? StrokeUpdated;
    event EventHandler<DrawingStroke>? StrokeCompleted;
    event EventHandler<IReadOnlyList<Guid>>? StrokesErased;
    event EventHandler<int>? PageCleared;
    event EventHandler<BackgroundStyle>? BackgroundChanged;

    void SetActivePage(int pageIndex);
    WhiteboardPage AddPage(BackgroundStyle background = BackgroundStyle.White);
    bool RemovePage(int pageIndex);
    void ClearCurrentPage();
    void SetBackground(BackgroundStyle background);

    DrawingStroke StartStroke(double x, double y, float pressure, TimeSpan timestamp);
    void AppendPoint(Guid strokeId, double x, double y, float pressure, TimeSpan timestamp);
    DrawingStroke? CompleteStroke(Guid strokeId);
    void AddStroke(DrawingStroke stroke);
    bool RemoveStroke(Guid strokeId);
    int EraseAt(double x, double y, double radius);
    void LoadPages(IEnumerable<WhiteboardPage> pages, int initialPageIndex = 0);
}

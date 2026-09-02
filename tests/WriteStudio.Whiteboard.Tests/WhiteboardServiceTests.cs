using FluentAssertions;
using WriteStudio.Core.Models;
using WriteStudio.Whiteboard.Geometry;
using WriteStudio.Whiteboard.UndoRedo;
using Xunit;

namespace WriteStudio.Whiteboard.Tests;

public class WhiteboardServiceTests
{
    [Fact]
    public void WhiteboardService_InitializesWithDefaultPage()
    {
        var undoRedo = new UndoRedoManager();
        var service = new WhiteboardService(undoRedo);

        service.Pages.Should().HaveCount(1);
        service.CurrentPageIndex.Should().Be(0);
        service.CurrentPage.Should().NotBeNull();
        service.CurrentPage.Background.Should().Be(BackgroundStyle.White);
    }

    [Fact]
    public void WhiteboardService_AddAndCompleteStroke_AddsToPageAndSupportsUndoRedo()
    {
        var undoRedo = new UndoRedoManager();
        var service = new WhiteboardService(undoRedo);

        var stroke = service.StartStroke(100, 100, 0.8f, TimeSpan.FromSeconds(1));
        service.AppendPoint(stroke.Id, 110, 110, 0.85f, TimeSpan.FromSeconds(1.1));
        service.AppendPoint(stroke.Id, 120, 120, 0.9f, TimeSpan.FromSeconds(1.2));
        var completed = service.CompleteStroke(stroke.Id);

        completed.Should().NotBeNull();
        service.CurrentPage.Strokes.Should().HaveCount(1);
        undoRedo.CanUndo.Should().BeTrue();

        undoRedo.Undo();
        service.CurrentPage.Strokes.Should().BeEmpty();
        undoRedo.CanRedo.Should().BeTrue();

        undoRedo.Redo();
        service.CurrentPage.Strokes.Should().HaveCount(1);
    }

    [Fact]
    public void WhiteboardService_EraseAt_RemovesIntersectingStrokes()
    {
        var undoRedo = new UndoRedoManager();
        var service = new WhiteboardService(undoRedo);

        var stroke = service.StartStroke(50, 50, 0.5f, TimeSpan.Zero);
        service.AppendPoint(stroke.Id, 50, 100, 0.5f, TimeSpan.FromSeconds(0.1));
        service.CompleteStroke(stroke.Id);

        service.CurrentPage.Strokes.Should().HaveCount(1);

        // Erase away from stroke (radius 10) -> count 0
        int erasedMiss = service.EraseAt(200, 200, 10);
        erasedMiss.Should().Be(0);
        service.CurrentPage.Strokes.Should().HaveCount(1);

        // Erase on stroke segment (50, 75) -> count 1
        int erasedHit = service.EraseAt(52, 75, 5);
        erasedHit.Should().Be(1);
        service.CurrentPage.Strokes.Should().BeEmpty();

        // Undo erase
        undoRedo.Undo();
        service.CurrentPage.Strokes.Should().HaveCount(1);
    }

    [Fact]
    public void WhiteboardService_PageManagement_AddsAndRemovesPages()
    {
        var undoRedo = new UndoRedoManager();
        var service = new WhiteboardService(undoRedo);

        var page2 = service.AddPage(BackgroundStyle.Blackboard);
        service.Pages.Should().HaveCount(2);
        service.CurrentPageIndex.Should().Be(1);
        service.CurrentPage.Background.Should().Be(BackgroundStyle.Blackboard);

        service.SetActivePage(0);
        service.CurrentPageIndex.Should().Be(0);

        bool removed = service.RemovePage(1);
        removed.Should().BeTrue();
        service.Pages.Should().HaveCount(1);
    }

    [Fact]
    public void StrokeGeometryHelper_ShapeGenerators_GenerateValidPoints()
    {
        var rectPoints = StrokeGeometryHelper.GenerateRectanglePoints(10, 10, 110, 60, TimeSpan.Zero);
        rectPoints.Should().HaveCount(5); // Closed rectangle

        var circlePoints = StrokeGeometryHelper.GenerateCirclePoints(0, 0, 100, 100, TimeSpan.Zero, 16);
        circlePoints.Should().HaveCount(17);

        var arrowPoints = StrokeGeometryHelper.GenerateArrowPoints(10, 10, 100, 10, TimeSpan.Zero);
        arrowPoints.Should().HaveCount(5);
    }
}

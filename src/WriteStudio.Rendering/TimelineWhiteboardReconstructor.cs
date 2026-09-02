using WriteStudio.Core.Models;

namespace WriteStudio.Rendering;

public record WhiteboardRenderState(
    int PageIndex,
    BackgroundStyle Background,
    IReadOnlyList<DrawingStroke> VisibleStrokes,
    CameraLayout CameraLayout
);

public class TimelineWhiteboardReconstructor
{
    private readonly RecordingSession _session;

    public TimelineWhiteboardReconstructor(RecordingSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public WhiteboardRenderState ReconstructAt(TimeSpan time)
    {
        int activePageIndex = 0;
        var pageBackgrounds = new Dictionary<int, BackgroundStyle>();
        var pageStrokes = new Dictionary<int, Dictionary<Guid, DrawingStroke>>();
        var inFlightStrokes = new Dictionary<Guid, DrawingStroke>();
        var cameraLayout = CameraLayout.FromPreset(CameraPositionPreset.BottomRight);

        // Initialize pages from session
        for (int i = 0; i < _session.Pages.Count; i++)
        {
            var p = _session.Pages[i];
            pageBackgrounds[p.Index] = p.Background;
            pageStrokes[p.Index] = new Dictionary<Guid, DrawingStroke>();
        }

        // Process timeline events up to 'time'
        var applicableEvents = _session.Events
            .Where(e => e.Timestamp <= time)
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.WallClockUtc);

        foreach (var evt in applicableEvents)
        {
            switch (evt)
            {
                case PageChangedTimelineEvent pc:
                    activePageIndex = pc.NewPageIndex;
                    if (!pageStrokes.ContainsKey(activePageIndex))
                        pageStrokes[activePageIndex] = new Dictionary<Guid, DrawingStroke>();
                    break;

                case BackgroundChangedTimelineEvent bc:
                    pageBackgrounds[bc.PageIndex] = bc.NewBackground;
                    break;

                case PageClearedTimelineEvent pclr:
                    if (pageStrokes.TryGetValue(pclr.PageIndex, out var strokesToClear))
                    {
                        strokesToClear.Clear();
                    }
                    break;

                case StrokeStartedTimelineEvent ss:
                    var clone = ss.Stroke.Clone();
                    inFlightStrokes[clone.Id] = clone;
                    if (!pageStrokes.ContainsKey(clone.PageIndex))
                        pageStrokes[clone.PageIndex] = new Dictionary<Guid, DrawingStroke>();
                    pageStrokes[clone.PageIndex][clone.Id] = clone;
                    break;

                case StrokePointAddedTimelineEvent spa:
                    if (inFlightStrokes.TryGetValue(spa.StrokeId, out var inFlight))
                    {
                        if (spa.Point.Timestamp <= time)
                        {
                            inFlight.Points.Add(spa.Point with { });
                        }
                    }
                    break;

                case StrokeCompletedTimelineEvent sc:
                    inFlightStrokes.Remove(sc.StrokeId);
                    break;

                case StrokesErasedTimelineEvent se:
                    if (pageStrokes.TryGetValue(se.PageIndex, out var targetPage))
                    {
                        foreach (var id in se.ErasedStrokeIds)
                        {
                            targetPage.Remove(id);
                            inFlightStrokes.Remove(id);
                        }
                    }
                    break;

                case CameraLayoutChangedTimelineEvent cl:
                    cameraLayout = cl.Layout with { };
                    break;
            }
        }

        // Ensure active page collections exist
        if (!pageBackgrounds.TryGetValue(activePageIndex, out var currentBg))
            currentBg = BackgroundStyle.White;

        if (!pageStrokes.TryGetValue(activePageIndex, out var currentStrokesDict))
            currentStrokesDict = new Dictionary<Guid, DrawingStroke>();

        // Build list of visible strokes with points <= time
        var visibleStrokes = new List<DrawingStroke>();
        foreach (var stroke in currentStrokesDict.Values)
        {
            var strokeClone = stroke.Clone();
            strokeClone.Points = strokeClone.Points.Where(p => p.Timestamp <= time).ToList();
            if (strokeClone.Points.Count > 0 || strokeClone.ToolType == StrokeToolType.Text)
            {
                visibleStrokes.Add(strokeClone);
            }
        }

        return new WhiteboardRenderState(
            PageIndex: activePageIndex,
            Background: currentBg,
            VisibleStrokes: visibleStrokes,
            CameraLayout: cameraLayout
        );
    }
}

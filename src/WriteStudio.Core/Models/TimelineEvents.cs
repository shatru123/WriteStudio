using System.Text.Json.Serialization;

namespace WriteStudio.Core.Models;

/// <summary>
/// Base class for all timeline events recorded during a studio session.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$eventType")]
[JsonDerivedType(typeof(StrokeStartedTimelineEvent), "StrokeStarted")]
[JsonDerivedType(typeof(StrokePointAddedTimelineEvent), "StrokePointAdded")]
[JsonDerivedType(typeof(StrokeCompletedTimelineEvent), "StrokeCompleted")]
[JsonDerivedType(typeof(StrokesErasedTimelineEvent), "StrokesErased")]
[JsonDerivedType(typeof(PageChangedTimelineEvent), "PageChanged")]
[JsonDerivedType(typeof(PageClearedTimelineEvent), "PageCleared")]
[JsonDerivedType(typeof(BackgroundChangedTimelineEvent), "BackgroundChanged")]
[JsonDerivedType(typeof(CameraLayoutChangedTimelineEvent), "CameraLayoutChanged")]
[JsonDerivedType(typeof(RecordingStateChangedTimelineEvent), "RecordingStateChanged")]
public abstract class TimelineEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Synchronized continuous recording session timestamp (excluding paused duration).
    /// </summary>
    public TimeSpan Timestamp { get; set; }

    /// <summary>
    /// Wall clock UTC timestamp when event occurred.
    /// </summary>
    public DateTime WallClockUtc { get; set; } = DateTime.UtcNow;
}

public class StrokeStartedTimelineEvent : TimelineEvent
{
    public DrawingStroke Stroke { get; set; } = new();
}

public class StrokePointAddedTimelineEvent : TimelineEvent
{
    public Guid StrokeId { get; set; }
    public DrawingPoint Point { get; set; } = new(0, 0, 0.5f, TimeSpan.Zero);
}

public class StrokeCompletedTimelineEvent : TimelineEvent
{
    public Guid StrokeId { get; set; }
}

public class StrokesErasedTimelineEvent : TimelineEvent
{
    public int PageIndex { get; set; }
    public List<Guid> ErasedStrokeIds { get; set; } = new();
}

public class PageChangedTimelineEvent : TimelineEvent
{
    public int PreviousPageIndex { get; set; }
    public int NewPageIndex { get; set; }
}

public class PageClearedTimelineEvent : TimelineEvent
{
    public int PageIndex { get; set; }
}

public class BackgroundChangedTimelineEvent : TimelineEvent
{
    public int PageIndex { get; set; }
    public BackgroundStyle NewBackground { get; set; }
}

public class CameraLayoutChangedTimelineEvent : TimelineEvent
{
    public CameraLayout Layout { get; set; } = new();
}

public class RecordingStateChangedTimelineEvent : TimelineEvent
{
    public RecordingState OldState { get; set; }
    public RecordingState NewState { get; set; }
}

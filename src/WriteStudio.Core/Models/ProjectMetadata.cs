namespace WriteStudio.Core.Models;

/// <summary>
/// Metadata describing a WriteStudio project (.wstudio).
/// </summary>
public class ProjectMetadata
{
    public string ProjectId { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Lesson";
    public string Author { get; set; } = Environment.UserName;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    
    // Canvas & Video targets
    public int CanvasWidth { get; set; } = 1920;
    public int CanvasHeight { get; set; } = 1080;
    public int TargetFps { get; set; } = 30;
    public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    
    public int TotalPages { get; set; } = 1;
    public bool HasAudioTrack { get; set; }
    public bool HasWebcamTrack { get; set; }
    public string AppVersion { get; set; } = "1.0.0";
}

public class RecordingSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public ProjectMetadata Metadata { get; set; } = new();
    public List<WhiteboardPage> Pages { get; set; } = new() { new WhiteboardPage { Index = 0, Title = "Page 1" } };
    public List<TimelineEvent> Events { get; set; } = new();
    public List<PauseInterval> PauseIntervals { get; set; } = new();
}

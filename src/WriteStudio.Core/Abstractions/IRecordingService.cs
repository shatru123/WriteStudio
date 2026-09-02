using WriteStudio.Core.Models;
using WriteStudio.Core.Time;

namespace WriteStudio.Core.Abstractions;

public interface IRecordingService : IAsyncDisposable
{
    RecordingState State { get; }
    IRecordingClock Clock { get; }
    RecordingSession CurrentSession { get; }
    string? CurrentSessionDirectory { get; }

    event EventHandler<RecordingState>? StateChanged;
    event EventHandler<TimelineEvent>? TimelineEventRecorded;
    event EventHandler<TimeSpan>? DurationUpdated;
    event EventHandler<Exception>? ErrorOccurred;

    Task StartRecordingAsync(string projectDirectory, CancellationToken cancellationToken = default);
    Task PauseRecordingAsync();
    Task ResumeRecordingAsync();
    Task<RecordingSession> StopRecordingAsync();
    void RecordEvent(TimelineEvent timelineEvent);
}

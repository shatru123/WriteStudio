using System.Text.Json;
using Microsoft.Extensions.Logging;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;
using WriteStudio.Core.Time;

namespace WriteStudio.Recording;

public class RecordingService : IRecordingService
{
    private readonly ILogger<RecordingService>? _logger;
    private readonly IRecordingClock _clock;
    private readonly IWhiteboardService _whiteboardService;
    private readonly IAudioCaptureService _audioService;
    private readonly ICameraService _cameraService;
    private readonly object _lock = new();

    private RecordingSession _currentSession = new();
    private string? _currentSessionDirectory;
    private bool _isDisposed;

    public RecordingState State => _clock.State;
    public IRecordingClock Clock => _clock;
    public RecordingSession CurrentSession => _currentSession;
    public string? CurrentSessionDirectory => _currentSessionDirectory;

    public event EventHandler<RecordingState>? StateChanged;
    public event EventHandler<TimelineEvent>? TimelineEventRecorded;
    public event EventHandler<TimeSpan>? DurationUpdated;
    public event EventHandler<Exception>? ErrorOccurred;

    public RecordingService(
        IRecordingClock clock,
        IWhiteboardService whiteboardService,
        IAudioCaptureService audioService,
        ICameraService cameraService,
        ILogger<RecordingService>? logger = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _whiteboardService = whiteboardService ?? throw new ArgumentNullException(nameof(whiteboardService));
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _logger = logger;

        _clock.StateChanged += OnClockStateChanged;
        _clock.Tick += OnClockTick;

        SubscribeToWhiteboardEvents();
        SubscribeToCameraEvents();
    }

    private void OnClockStateChanged(object? sender, RecordingState newState)
    {
        StateChanged?.Invoke(this, newState);
    }

    private void OnClockTick(object? sender, TimeSpan elapsed)
    {
        DurationUpdated?.Invoke(this, elapsed);
    }

    private void SubscribeToWhiteboardEvents()
    {
        _whiteboardService.StrokeAdded += (s, stroke) =>
        {
            if (State != RecordingState.Stopped)
            {
                RecordEvent(new StrokeStartedTimelineEvent
                {
                    Timestamp = _clock.ElapsedTime,
                    Stroke = stroke.Clone()
                });
            }
        };

        _whiteboardService.StrokeUpdated += (s, stroke) =>
        {
            if (State != RecordingState.Stopped && stroke.Points.Count > 0)
            {
                var lastPoint = stroke.Points[^1];
                RecordEvent(new StrokePointAddedTimelineEvent
                {
                    Timestamp = _clock.ElapsedTime,
                    StrokeId = stroke.Id,
                    Point = lastPoint with { }
                });
            }
        };

        _whiteboardService.StrokeCompleted += (s, stroke) =>
        {
            if (State != RecordingState.Stopped)
            {
                RecordEvent(new StrokeCompletedTimelineEvent
                {
                    Timestamp = _clock.ElapsedTime,
                    StrokeId = stroke.Id
                });
            }
        };

        _whiteboardService.StrokesErased += (s, erasedIds) =>
        {
            if (State != RecordingState.Stopped)
            {
                RecordEvent(new StrokesErasedTimelineEvent
                {
                    Timestamp = _clock.ElapsedTime,
                    PageIndex = _whiteboardService.CurrentPageIndex,
                    ErasedStrokeIds = erasedIds.ToList()
                });
            }
        };

        _whiteboardService.PageChanged += (s, newIdx) =>
        {
            if (State != RecordingState.Stopped)
            {
                RecordEvent(new PageChangedTimelineEvent
                {
                    Timestamp = _clock.ElapsedTime,
                    PreviousPageIndex = _whiteboardService.CurrentPageIndex,
                    NewPageIndex = newIdx
                });
            }
        };

        _whiteboardService.PageCleared += (s, pageIdx) =>
        {
            if (State != RecordingState.Stopped)
            {
                RecordEvent(new PageClearedTimelineEvent
                {
                    Timestamp = _clock.ElapsedTime,
                    PageIndex = pageIdx
                });
            }
        };

        _whiteboardService.BackgroundChanged += (s, bg) =>
        {
            if (State != RecordingState.Stopped)
            {
                RecordEvent(new BackgroundChangedTimelineEvent
                {
                    Timestamp = _clock.ElapsedTime,
                    PageIndex = _whiteboardService.CurrentPageIndex,
                    NewBackground = bg
                });
            }
        };
    }

    private void SubscribeToCameraEvents()
    {
        _cameraService.LayoutChanged += (s, layout) =>
        {
            if (State != RecordingState.Stopped)
            {
                RecordEvent(new CameraLayoutChangedTimelineEvent
                {
                    Timestamp = _clock.ElapsedTime,
                    Layout = layout with { }
                });
            }
        };
    }

    public async Task StartRecordingAsync(string projectDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        lock (_lock)
        {
            if (State != RecordingState.Stopped)
                throw new InvalidOperationException("Recording session is already active.");

            _currentSessionDirectory = projectDirectory;
            if (!Directory.Exists(projectDirectory))
            {
                Directory.CreateDirectory(projectDirectory);
            }

            string audioDir = Path.Combine(projectDirectory, "audio");
            Directory.CreateDirectory(audioDir);

            _currentSession = new RecordingSession
            {
                SessionId = Guid.NewGuid().ToString("N"),
                Metadata = new ProjectMetadata
                {
                    ProjectId = Guid.NewGuid().ToString("N"),
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow,
                    HasAudioTrack = true,
                    HasWebcamTrack = _cameraService.IsPreviewActive && _cameraService.CurrentLayout.IsVisible
                },
                Pages = _whiteboardService.Pages.Select(p => p.Clone()).ToList()
            };
        }

        try
        {
            string wavPath = Path.Combine(_currentSessionDirectory, "audio", "recording.wav");
            await _audioService.StartCaptureAsync(wavPath, cancellationToken);

            if (_cameraService.CurrentLayout.IsVisible)
            {
                string videoDir = Path.Combine(_currentSessionDirectory, "video");
                Directory.CreateDirectory(videoDir);
                string camPath = Path.Combine(videoDir, "webcam.mp4");
                await _cameraService.StartCaptureAsync(camPath, cancellationToken);
            }

            _clock.Start();

            RecordEvent(new RecordingStateChangedTimelineEvent
            {
                Timestamp = TimeSpan.Zero,
                OldState = RecordingState.Stopped,
                NewState = RecordingState.Recording
            });

            RecordEvent(new CameraLayoutChangedTimelineEvent
            {
                Timestamp = TimeSpan.Zero,
                Layout = _cameraService.CurrentLayout with { }
            });

            _logger?.LogInformation("Recording session started in {Directory}", projectDirectory);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start recording session.");
            _clock.Reset();
            ErrorOccurred?.Invoke(this, ex);
            throw;
        }
    }

    public async Task PauseRecordingAsync()
    {
        if (State != RecordingState.Recording) return;

        RecordEvent(new RecordingStateChangedTimelineEvent
        {
            Timestamp = _clock.ElapsedTime,
            OldState = RecordingState.Recording,
            NewState = RecordingState.Paused
        });

        _clock.Pause();
        await _audioService.PauseCaptureAsync();

        _logger?.LogInformation("Recording session paused at {Time}", _clock.ElapsedTime);
    }

    public async Task ResumeRecordingAsync()
    {
        if (State != RecordingState.Paused) return;

        _clock.Resume();
        await _audioService.ResumeCaptureAsync();

        RecordEvent(new RecordingStateChangedTimelineEvent
        {
            Timestamp = _clock.ElapsedTime,
            OldState = RecordingState.Paused,
            NewState = RecordingState.Recording
        });

        _logger?.LogInformation("Recording session resumed at {Time}", _clock.ElapsedTime);
    }

    public async Task<RecordingSession> StopRecordingAsync()
    {
        if (State == RecordingState.Stopped)
            return _currentSession;

        var finalDuration = _clock.ElapsedTime;

        RecordEvent(new RecordingStateChangedTimelineEvent
        {
            Timestamp = finalDuration,
            OldState = State,
            NewState = RecordingState.Stopped
        });

        _clock.Stop();
        await _audioService.StopCaptureAsync();
        await _cameraService.StopCaptureAsync();

        lock (_lock)
        {
            _currentSession.Metadata.Duration = finalDuration;
            _currentSession.Metadata.ModifiedAt = DateTime.UtcNow;
            _currentSession.Metadata.TotalPages = _whiteboardService.Pages.Count;
            _currentSession.Pages = _whiteboardService.Pages.Select(p => p.Clone()).ToList();
            _currentSession.PauseIntervals = _clock.PauseIntervals.ToList();
        }

        _logger?.LogInformation("Recording session stopped. Duration: {Duration}", finalDuration);
        return _currentSession;
    }

    public void RecordEvent(TimelineEvent timelineEvent)
    {
        ArgumentNullException.ThrowIfNull(timelineEvent);

        lock (_lock)
        {
            _currentSession.Events.Add(timelineEvent);
        }

        TimelineEventRecorded?.Invoke(this, timelineEvent);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;

        _clock.StateChanged -= OnClockStateChanged;
        _clock.Tick -= OnClockTick;

        if (State != RecordingState.Stopped)
        {
            await StopRecordingAsync();
        }

        _isDisposed = true;
        GC.SuppressFinalize(this);
    }
}

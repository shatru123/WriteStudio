namespace WriteStudio.Core.Models;

public enum RecordingState
{
    Stopped,
    Recording,
    Paused
}

/// <summary>
/// Record representing an interval during which the recording session was paused.
/// Used to eliminate dead air / gaps from exported videos and synchronize tracks.
/// </summary>
public record PauseInterval(TimeSpan SessionPauseTime, DateTime WallClockPauseStartUtc, DateTime? WallClockPauseEndUtc)
{
    public TimeSpan Duration => WallClockPauseEndUtc.HasValue 
        ? WallClockPauseEndUtc.Value - WallClockPauseStartUtc 
        : TimeSpan.Zero;
}

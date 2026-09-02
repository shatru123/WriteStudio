using WriteStudio.Core.Models;

namespace WriteStudio.Core.Time;

public interface IRecordingClock
{
    RecordingState State { get; }
    TimeSpan ElapsedTime { get; }
    IReadOnlyList<PauseInterval> PauseIntervals { get; }
    
    event EventHandler<TimeSpan>? Tick;
    event EventHandler<RecordingState>? StateChanged;

    void Start();
    void Pause();
    void Resume();
    void Stop();
    void Reset();

    /// <summary>
    /// Calculates the effective recording timestamp for a given wall-clock time,
    /// deducting any pause durations that occurred prior to that time.
    /// </summary>
    TimeSpan CalculateEffectiveTime(DateTime wallClockUtc);
}

public class RecordingClock : IRecordingClock
{
    private readonly object _lock = new();
    private readonly List<PauseInterval> _pauseIntervals = new();
    private DateTime? _sessionStartUtc;
    private DateTime? _currentPauseStartUtc;
    private TimeSpan _accumulatedPausedDuration = TimeSpan.Zero;
    private RecordingState _state = RecordingState.Stopped;

    public RecordingState State
    {
        get { lock (_lock) return _state; }
        private set
        {
            RecordingState oldState;
            lock (_lock)
            {
                if (_state == value) return;
                oldState = _state;
                _state = value;
            }
            StateChanged?.Invoke(this, value);
        }
    }

    public TimeSpan ElapsedTime
    {
        get
        {
            lock (_lock)
            {
                if (_state == RecordingState.Stopped || !_sessionStartUtc.HasValue)
                    return TimeSpan.Zero;

                DateTime now = _state == RecordingState.Paused && _currentPauseStartUtc.HasValue
                    ? _currentPauseStartUtc.Value
                    : DateTime.UtcNow;

                TimeSpan totalWallTime = now - _sessionStartUtc.Value;
                TimeSpan effective = totalWallTime - _accumulatedPausedDuration;
                return effective < TimeSpan.Zero ? TimeSpan.Zero : effective;
            }
        }
    }

    public IReadOnlyList<PauseInterval> PauseIntervals
    {
        get { lock (_lock) return _pauseIntervals.ToList(); }
    }

    public event EventHandler<TimeSpan>? Tick;
    public event EventHandler<RecordingState>? StateChanged;

    private System.Threading.Timer? _tickTimer;

    private void StartTickTimer()
    {
        _tickTimer?.Dispose();
        _tickTimer = new System.Threading.Timer(_ =>
        {
            if (State == RecordingState.Recording)
            {
                Tick?.Invoke(this, ElapsedTime);
            }
        }, null, 0, 50);
    }

    private void StopTickTimer()
    {
        _tickTimer?.Dispose();
        _tickTimer = null;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_state != RecordingState.Stopped) return;
            _sessionStartUtc = DateTime.UtcNow;
            _accumulatedPausedDuration = TimeSpan.Zero;
            _pauseIntervals.Clear();
            _currentPauseStartUtc = null;
        }
        State = RecordingState.Recording;
        StartTickTimer();
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_state != RecordingState.Recording) return;
            _currentPauseStartUtc = DateTime.UtcNow;
        }
        State = RecordingState.Paused;
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (_state != RecordingState.Paused || !_currentPauseStartUtc.HasValue) return;

            DateTime now = DateTime.UtcNow;
            TimeSpan pauseDuration = now - _currentPauseStartUtc.Value;
            _accumulatedPausedDuration += pauseDuration;

            _pauseIntervals.Add(new PauseInterval(
                SessionPauseTime: ElapsedTime,
                WallClockPauseStartUtc: _currentPauseStartUtc.Value,
                WallClockPauseEndUtc: now
            ));

            _currentPauseStartUtc = null;
        }
        State = RecordingState.Recording;
    }

    public void Stop()
    {
        StopTickTimer();
        lock (_lock)
        {
            if (_state == RecordingState.Paused && _currentPauseStartUtc.HasValue)
            {
                DateTime now = DateTime.UtcNow;
                _pauseIntervals.Add(new PauseInterval(
                    SessionPauseTime: ElapsedTime,
                    WallClockPauseStartUtc: _currentPauseStartUtc.Value,
                    WallClockPauseEndUtc: now
                ));
            }
            _currentPauseStartUtc = null;
        }
        State = RecordingState.Stopped;
    }

    public void Reset()
    {
        StopTickTimer();
        lock (_lock)
        {
            _sessionStartUtc = null;
            _currentPauseStartUtc = null;
            _accumulatedPausedDuration = TimeSpan.Zero;
            _pauseIntervals.Clear();
        }
        State = RecordingState.Stopped;
    }

    public TimeSpan CalculateEffectiveTime(DateTime wallClockUtc)
    {
        lock (_lock)
        {
            if (!_sessionStartUtc.HasValue || wallClockUtc < _sessionStartUtc.Value)
                return TimeSpan.Zero;

            TimeSpan wallElapsed = wallClockUtc - _sessionStartUtc.Value;
            TimeSpan pauseDeductions = TimeSpan.Zero;

            foreach (var interval in _pauseIntervals)
            {
                if (wallClockUtc <= interval.WallClockPauseStartUtc)
                    break;

                if (interval.WallClockPauseEndUtc.HasValue && wallClockUtc >= interval.WallClockPauseEndUtc.Value)
                {
                    pauseDeductions += interval.Duration;
                }
                else
                {
                    pauseDeductions += (wallClockUtc - interval.WallClockPauseStartUtc);
                }
            }

            if (_state == RecordingState.Paused && _currentPauseStartUtc.HasValue && wallClockUtc > _currentPauseStartUtc.Value)
            {
                pauseDeductions += (wallClockUtc - _currentPauseStartUtc.Value);
            }

            TimeSpan effective = wallElapsed - pauseDeductions;
            return effective < TimeSpan.Zero ? TimeSpan.Zero : effective;
        }
    }
}

using System.Windows.Input;
using WriteStudio.App.Mvvm;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;

namespace WriteStudio.App.ViewModels;

public class RecordingViewModel : ObservableObject
{
    private readonly IRecordingService _recordingService;
    private readonly IProjectStorageService _projectStorage;
    private RecordingState _state = RecordingState.Stopped;
    private string _formattedDuration = "00:00:00";
    private string _statusText = "Ready to record";
    private string _currentProjectDirectory = string.Empty;

    public RecordingState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsStopped));
                OnPropertyChanged(nameof(IsRecording));
                OnPropertyChanged(nameof(IsPaused));
                UpdateStatusText();
            }
        }
    }

    public bool IsStopped => State == RecordingState.Stopped;
    public bool IsRecording => State == RecordingState.Recording;
    public bool IsPaused => State == RecordingState.Paused;

    public string FormattedDuration
    {
        get => _formattedDuration;
        private set => SetProperty(ref _formattedDuration, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ICommand RecordCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand StopCommand { get; }

    public event EventHandler? RecordingCompleted;

    public RecordingViewModel(
        IRecordingService recordingService,
        IProjectStorageService projectStorage)
    {
        _recordingService = recordingService ?? throw new ArgumentNullException(nameof(recordingService));
        _projectStorage = projectStorage ?? throw new ArgumentNullException(nameof(projectStorage));

        _recordingService.StateChanged += (s, state) => State = state;
        _recordingService.DurationUpdated += (s, duration) =>
        {
            FormattedDuration = $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        };

        RecordCommand = new AsyncRelayCommand(async () =>
        {
            if (State != RecordingState.Stopped) return;

            string projectDir = Path.Combine(_projectStorage.DefaultProjectsDirectory, $"Lesson_{DateTime.Now:yyyyMMdd_HHmmss}");
            _currentProjectDirectory = projectDir;
            await _recordingService.StartRecordingAsync(projectDir);
        }, () => IsStopped);

        PauseCommand = new AsyncRelayCommand(async () =>
        {
            if (State == RecordingState.Recording)
            {
                await _recordingService.PauseRecordingAsync();
            }
        }, () => IsRecording);

        ResumeCommand = new AsyncRelayCommand(async () =>
        {
            if (State == RecordingState.Paused)
            {
                await _recordingService.ResumeRecordingAsync();
            }
        }, () => IsPaused);

        StopCommand = new AsyncRelayCommand(async () =>
        {
            if (State != RecordingState.Stopped)
            {
                var session = await _recordingService.StopRecordingAsync();
                await _projectStorage.SaveProjectAsync(session, _currentProjectDirectory);
                RecordingCompleted?.Invoke(this, EventArgs.Empty);
            }
        }, () => !IsStopped);

        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        StatusText = State switch
        {
            RecordingState.Recording => "● RECORDING",
            RecordingState.Paused => "❚❚ PAUSED",
            _ => "● READY"
        };
    }
}

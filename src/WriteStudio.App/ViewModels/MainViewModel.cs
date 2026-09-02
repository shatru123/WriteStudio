using System.Windows.Input;
using Microsoft.Extensions.Logging;
using WriteStudio.App.Mvvm;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;

namespace WriteStudio.App.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly IProjectStorageService _projectStorage;
    private readonly IRecoveryService _recoveryService;
    private readonly IWhiteboardService _whiteboardService;
    private readonly ILogger<MainViewModel>? _logger;

    private string _windowTitle = "WriteStudio — Teaching & Lecture Studio";
    private string _currentProjectPath = string.Empty;
    private string _statusMessage = "Ready";
    private bool _isExportDialogOpen;
    private bool _isRecoveryDialogOpen;

    public string WindowTitle
    {
        get => _windowTitle;
        private set => SetProperty(ref _windowTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsExportDialogOpen
    {
        get => _isExportDialogOpen;
        set => SetProperty(ref _isExportDialogOpen, value);
    }

    public bool IsRecoveryDialogOpen
    {
        get => _isRecoveryDialogOpen;
        set => SetProperty(ref _isRecoveryDialogOpen, value);
    }

    public WhiteboardViewModel Whiteboard { get; }
    public SlidesViewModel Slides { get; }
    public AudioViewModel Audio { get; }
    public CameraViewModel Camera { get; }
    public RecordingViewModel Recording { get; }
    public ExportViewModel Export { get; }

    public ICommand NewProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand SaveProjectCommand { get; }
    public ICommand OpenExportDialogCommand { get; }
    public ICommand CloseExportDialogCommand { get; }
    public ICommand CheckRecoveryCommand { get; }

    public MainViewModel(
        WhiteboardViewModel whiteboard,
        SlidesViewModel slides,
        AudioViewModel audio,
        CameraViewModel camera,
        RecordingViewModel recording,
        ExportViewModel export,
        IProjectStorageService projectStorage,
        IRecoveryService recoveryService,
        IWhiteboardService whiteboardService,
        ILogger<MainViewModel>? logger = null)
    {
        Whiteboard = whiteboard ?? throw new ArgumentNullException(nameof(whiteboard));
        Slides = slides ?? throw new ArgumentNullException(nameof(slides));
        Audio = audio ?? throw new ArgumentNullException(nameof(audio));
        Camera = camera ?? throw new ArgumentNullException(nameof(camera));
        Recording = recording ?? throw new ArgumentNullException(nameof(recording));
        Export = export ?? throw new ArgumentNullException(nameof(export));
        _projectStorage = projectStorage ?? throw new ArgumentNullException(nameof(projectStorage));
        _recoveryService = recoveryService ?? throw new ArgumentNullException(nameof(recoveryService));
        _whiteboardService = whiteboardService ?? throw new ArgumentNullException(nameof(whiteboardService));
        _logger = logger;

        Recording.RecordingCompleted += (s, e) =>
        {
            StatusMessage = "Recording completed. Ready to export.";
            IsExportDialogOpen = true;
        };

        NewProjectCommand = new AsyncRelayCommand(async () =>
        {
            var session = await _projectStorage.CreateNewProjectAsync("New Lesson");
            _whiteboardService.LoadPages(session.Pages);
            _currentProjectPath = string.Empty;
            WindowTitle = "WriteStudio — New Lesson";
            StatusMessage = "Created new project";
        });

        OpenProjectCommand = new AsyncRelayCommand(async param =>
        {
            if (param is string path && Directory.Exists(path))
            {
                var session = await _projectStorage.LoadProjectAsync(path);
                _whiteboardService.LoadPages(session.Pages);
                _currentProjectPath = path;
                WindowTitle = $"WriteStudio — {session.Metadata.Title}";
                StatusMessage = $"Loaded project: {session.Metadata.Title}";
            }
        });

        SaveProjectCommand = new AsyncRelayCommand(async () =>
        {
            if (string.IsNullOrEmpty(_currentProjectPath))
            {
                _currentProjectPath = Path.Combine(_projectStorage.DefaultProjectsDirectory, $"Lesson_{DateTime.Now:yyyyMMdd_HHmmss}");
            }

            var session = new RecordingSession
            {
                Metadata = new ProjectMetadata { Title = "Saved Lesson", ModifiedAt = DateTime.UtcNow },
                Pages = _whiteboardService.Pages.Select(p => p.Clone()).ToList()
            };

            await _projectStorage.SaveProjectAsync(session, _currentProjectPath);
            StatusMessage = "Project saved successfully.";
        });

        OpenExportDialogCommand = new RelayCommand(() => IsExportDialogOpen = true);
        CloseExportDialogCommand = new RelayCommand(() => IsExportDialogOpen = false);

        CheckRecoveryCommand = new AsyncRelayCommand(async () =>
        {
            var sessions = await _recoveryService.CheckForRecoverableSessionsAsync();
            if (sessions.Count > 0)
            {
                IsRecoveryDialogOpen = true;
            }
        });
    }

    public async Task InitializeAsync()
    {
        try
        {
            var recoverable = await _recoveryService.CheckForRecoverableSessionsAsync();
            if (recoverable.Count > 0)
            {
                _logger?.LogInformation("Found {Count} recoverable recording sessions.", recoverable.Count);
                IsRecoveryDialogOpen = true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking crash recovery on startup.");
        }
    }
}

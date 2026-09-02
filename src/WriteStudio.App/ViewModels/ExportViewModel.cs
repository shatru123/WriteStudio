using System.Windows.Input;
using WriteStudio.App.Mvvm;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;

namespace WriteStudio.App.ViewModels;

public class ExportViewModel : ObservableObject
{
    private readonly IRenderingService _renderingService;
    private readonly IRecordingService _recordingService;
    private readonly IFFmpegService _ffmpegService;

    private int _selectedResolutionWidth = 1920;
    private int _selectedResolutionHeight = 1080;
    private int _selectedFps = 30;
    private int _bitrateKbps = 4000;
    private bool _includeWebcam = true;
    private string _outputPath = string.Empty;
    private bool _isExporting;
    private double _progressPercentage;
    private string _progressText = "Ready to export";
    private string _currentFrameInfo = string.Empty;

    public int SelectedResolutionWidth
    {
        get => _selectedResolutionWidth;
        set => SetProperty(ref _selectedResolutionWidth, value);
    }

    public int SelectedResolutionHeight
    {
        get => _selectedResolutionHeight;
        set => SetProperty(ref _selectedResolutionHeight, value);
    }

    public int SelectedFps
    {
        get => _selectedFps;
        set => SetProperty(ref _selectedFps, value);
    }

    public int BitrateKbps
    {
        get => _bitrateKbps;
        set => SetProperty(ref _bitrateKbps, value);
    }

    public bool IncludeWebcam
    {
        get => _includeWebcam;
        set => SetProperty(ref _includeWebcam, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    public bool IsExporting
    {
        get => _isExporting;
        private set
        {
            if (SetProperty(ref _isExporting, value))
            {
                OnPropertyChanged(nameof(CanStartExport));
            }
        }
    }

    public bool CanStartExport => !IsExporting;

    public double ProgressPercentage
    {
        get => _progressPercentage;
        private set => SetProperty(ref _progressPercentage, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public string CurrentFrameInfo
    {
        get => _currentFrameInfo;
        private set => SetProperty(ref _currentFrameInfo, value);
    }

    public ICommand StartExportCommand { get; }
    public ICommand CancelExportCommand { get; }

    public ExportViewModel(
        IRenderingService renderingService,
        IRecordingService recordingService,
        IFFmpegService ffmpegService)
    {
        _renderingService = renderingService ?? throw new ArgumentNullException(nameof(renderingService));
        _recordingService = recordingService ?? throw new ArgumentNullException(nameof(recordingService));
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));

        string defaultExportDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "WriteStudio"
        );
        OutputPath = Path.Combine(defaultExportDir, $"WriteStudio_Lesson_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

        _renderingService.ProgressChanged += (s, report) =>
        {
            ProgressPercentage = report.Percentage;
            ProgressText = report.StatusMessage;
            CurrentFrameInfo = $"Frame {report.CurrentFrame}/{report.TotalFrames} @ {report.CurrentFps:F1} FPS";
        };

        StartExportCommand = new AsyncRelayCommand(async () =>
        {
            if (IsExporting) return;

            IsExporting = true;
            ProgressPercentage = 0;
            ProgressText = "Initializing FFmpeg encoder...";

            var settings = new ExportSettings(
                OutputFilePath: OutputPath,
                Width: SelectedResolutionWidth,
                Height: SelectedResolutionHeight,
                FrameRate: SelectedFps,
                VideoBitrateKbps: BitrateKbps,
                IncludeWebcam: IncludeWebcam
            );

            try
            {
                var session = _recordingService.CurrentSession;
                string projectDir = _recordingService.CurrentSessionDirectory 
                    ?? Path.GetDirectoryName(OutputPath)!;

                bool success = await _renderingService.RenderProjectAsync(session, projectDir, settings);
                ProgressText = success ? "Export Completed Successfully!" : "Export Cancelled.";
            }
            catch (Exception ex)
            {
                ProgressText = $"Export Failed: {ex.Message}";
            }
            finally
            {
                IsExporting = false;
            }
        }, () => CanStartExport);

        CancelExportCommand = new AsyncRelayCommand(async () =>
        {
            if (IsExporting)
            {
                await _renderingService.CancelRenderingAsync();
            }
        }, () => IsExporting);
    }
}

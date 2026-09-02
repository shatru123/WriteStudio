using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;

namespace WriteStudio.Rendering;

public class RenderingService : IRenderingService
{
    private readonly ILogger<RenderingService>? _logger;
    private readonly IFFmpegService _ffmpegService;
    private readonly object _lock = new();
    private bool _isRendering;
    private CancellationTokenSource? _renderCts;

    public bool IsRendering
    {
        get { lock (_lock) return _isRendering; }
        private set { lock (_lock) _isRendering = value; }
    }

    public event EventHandler<ExportProgressReport>? ProgressChanged;
    public event EventHandler<Exception>? ErrorOccurred;

    public RenderingService(IFFmpegService ffmpegService, ILogger<RenderingService>? logger = null)
    {
        _ffmpegService = ffmpegService ?? throw new ArgumentNullException(nameof(ffmpegService));
        _logger = logger;
    }

    public async Task<bool> RenderProjectAsync(
        RecordingSession session,
        string projectDirectory,
        ExportSettings settings,
        IProgress<ExportProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);

        lock (_lock)
        {
            if (_isRendering)
                throw new InvalidOperationException("Export rendering is already in progress.");

            _renderCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isRendering = true;
        }

        try
        {
            if (!_ffmpegService.IsFFmpegAvailable)
            {
                bool found = await _ffmpegService.ProbeFFmpegAsync();
                if (!found)
                {
                    throw new FileNotFoundException("FFmpeg executable was not found. Please install FFmpeg or set its path in Settings.");
                }
            }

            // Find audio file if recorded
            string? audioPath = FindAudioFile(projectDirectory);

            // Find webcam file if recorded
            string? webcamPath = FindWebcamFile(projectDirectory);

            string? outputDir = Path.GetDirectoryName(settings.OutputFilePath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var reconstructor = new TimelineWhiteboardReconstructor(session);
            var initialRenderState = reconstructor.ReconstructAt(TimeSpan.Zero);
            var cameraLayout = initialRenderState.CameraLayout;

            string ffmpegArgs = _ffmpegService.BuildEncodingArguments(
                width: settings.Width,
                height: settings.Height,
                fps: settings.FrameRate,
                audioFilePath: audioPath,
                outputFilePath: settings.OutputFilePath,
                webcamFilePath: settings.IncludeWebcam ? webcamPath : null,
                cameraLayout: cameraLayout,
                videoBitrateKbps: settings.VideoBitrateKbps,
                audioBitrateKbps: settings.AudioBitrateKbps,
                videoCodec: settings.VideoCodec,
                audioCodec: settings.AudioCodec,
                preset: string.IsNullOrEmpty(settings.Preset) || settings.Preset == "fast" ? "ultrafast" : settings.Preset,
                crf: settings.Crf > 0 ? settings.Crf : 22
            );

            _logger?.LogInformation("Starting FFmpeg with arguments: {Args}", ffmpegArgs);

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegService.FFmpegPath ?? "ffmpeg",
                Arguments = ffmpegArgs,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi) 
                ?? throw new InvalidOperationException("Failed to start FFmpeg process.");

            using var frameRenderer = new SkiaFrameRenderer(settings.Width, settings.Height);

            double totalSeconds = session.Metadata.Duration.TotalSeconds;
            if (totalSeconds < 0.1)
            {
                totalSeconds = 1.0;
            }

            int totalFrames = (int)Math.Ceiling(totalSeconds * settings.FrameRate);
            var stopwatch = Stopwatch.StartNew();

            using var stdin = process.StandardInput.BaseStream;

            bool hasLiveWebcamFile = !string.IsNullOrEmpty(webcamPath) && File.Exists(webcamPath);

            int lastStrokeCount = -1;
            int lastTotalPoints = -1;
            BackgroundStyle lastBg = (BackgroundStyle)(-1);
            byte[]? cachedFrameBytes = null;

            for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
            {
                _renderCts.Token.ThrowIfCancellationRequested();

                double currentTimeSec = (double)frameIdx / settings.FrameRate;
                var currentTimestamp = TimeSpan.FromSeconds(currentTimeSec);

                var renderState = reconstructor.ReconstructAt(currentTimestamp);

                if (hasLiveWebcamFile)
                {
                    renderState = renderState with { CameraLayout = CameraLayout.HiddenLayout };
                }

                // Check if whiteboard state changed
                int currentStrokeCount = renderState.VisibleStrokes.Count;
                int currentTotalPoints = 0;
                for (int s = 0; s < currentStrokeCount; s++)
                {
                    currentTotalPoints += renderState.VisibleStrokes[s].Points.Count;
                }

                bool stateChanged = cachedFrameBytes == null ||
                                    currentStrokeCount != lastStrokeCount ||
                                    currentTotalPoints != lastTotalPoints ||
                                    renderState.Background != lastBg;

                if (stateChanged)
                {
                    cachedFrameBytes = frameRenderer.RenderFrame(
                        renderState,
                        session.Metadata.CanvasWidth,
                        session.Metadata.CanvasHeight
                    );
                    lastStrokeCount = currentStrokeCount;
                    lastTotalPoints = currentTotalPoints;
                    lastBg = renderState.Background;
                }

                await stdin.WriteAsync(cachedFrameBytes!, 0, cachedFrameBytes!.Length, _renderCts.Token);

                if (frameIdx % 15 == 0 || frameIdx == totalFrames - 1)
                {
                    double percent = ((double)(frameIdx + 1) / totalFrames) * 100.0;
                    double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                    double currentFps = elapsedSec > 0 ? (frameIdx + 1) / elapsedSec : 0;

                    var report = new ExportProgressReport(
                        Percentage: percent,
                        ProcessedDuration: currentTimestamp,
                        TotalDuration: session.Metadata.Duration,
                        CurrentFrame: frameIdx + 1,
                        TotalFrames: totalFrames,
                        CurrentFps: currentFps,
                        StatusMessage: $"Rendering frame {frameIdx + 1}/{totalFrames} ({percent:F1}%)"
                    );

                    progress?.Report(report);
                    ProgressChanged?.Invoke(this, report);
                }
            }

            await stdin.FlushAsync(_renderCts.Token);
            stdin.Close();

            await process.WaitForExitAsync(_renderCts.Token);

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"FFmpeg export failed with exit code {process.ExitCode}: {error}");
            }

            _logger?.LogInformation("Export completed in {Elapsed}s at {Fps:F1} FPS: {Path}", 
                stopwatch.Elapsed.TotalSeconds, totalFrames / Math.Max(0.01, stopwatch.Elapsed.TotalSeconds), settings.OutputFilePath);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Export was cancelled by user.");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during video export rendering.");
            ErrorOccurred?.Invoke(this, ex);
            throw;
        }
        finally
        {
            lock (_lock)
            {
                _isRendering = false;
                _renderCts?.Dispose();
                _renderCts = null;
            }
        }
    }

    private static string? FindAudioFile(string projectDirectory)
    {
        string[] candidates = {
            Path.Combine(projectDirectory, "audio", "recording.wav"),
            Path.Combine(projectDirectory, "audio", "recording.webm"),
            Path.Combine(projectDirectory, "audio", "recording.mp3"),
            Path.Combine(projectDirectory, "audio", "recording.m4a"),
            Path.Combine(projectDirectory, "audio", "recording.ogg"),
            Path.Combine(projectDirectory, "audio", "recording.aac")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindWebcamFile(string projectDirectory)
    {
        string[] candidates = {
            Path.Combine(projectDirectory, "video", "webcam.mp4"),
            Path.Combine(projectDirectory, "video", "webcam.webm"),
            Path.Combine(projectDirectory, "video", "webcam.mov"),
            Path.Combine(projectDirectory, "video", "webcam.mkv")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public Task CancelRenderingAsync()
    {
        lock (_lock)
        {
            _renderCts?.Cancel();
        }
        return Task.CompletedTask;
    }
}

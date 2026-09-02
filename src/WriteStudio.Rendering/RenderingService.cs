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

            string? audioPath = Path.Combine(projectDirectory, "audio", "recording.wav");
            if (!File.Exists(audioPath))
            {
                audioPath = null;
            }

            string? outputDir = Path.GetDirectoryName(settings.OutputFilePath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            string ffmpegArgs = _ffmpegService.BuildEncodingArguments(
                settings.Width,
                settings.Height,
                settings.FrameRate,
                audioPath,
                settings.OutputFilePath,
                settings.VideoBitrateKbps,
                settings.AudioBitrateKbps,
                settings.VideoCodec,
                settings.AudioCodec,
                settings.Preset,
                settings.Crf
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

            var reconstructor = new TimelineWhiteboardReconstructor(session);
            using var frameRenderer = new SkiaFrameRenderer(settings.Width, settings.Height);

            double totalSeconds = session.Metadata.Duration.TotalSeconds;
            if (totalSeconds < 0.1)
            {
                totalSeconds = 1.0; // Minimum 1 second duration
            }

            int totalFrames = (int)Math.Ceiling(totalSeconds * settings.FrameRate);
            var stopwatch = Stopwatch.StartNew();

            using var stdin = process.StandardInput.BaseStream;

            for (int frameIdx = 0; frameIdx < totalFrames; frameIdx++)
            {
                _renderCts.Token.ThrowIfCancellationRequested();

                double currentTimeSec = (double)frameIdx / settings.FrameRate;
                var currentTimestamp = TimeSpan.FromSeconds(currentTimeSec);

                var renderState = reconstructor.ReconstructAt(currentTimestamp);
                byte[] rawFrameBytes = frameRenderer.RenderFrame(
                    renderState,
                    session.Metadata.CanvasWidth,
                    session.Metadata.CanvasHeight
                );

                await stdin.WriteAsync(rawFrameBytes, 0, rawFrameBytes.Length, _renderCts.Token);

                if (frameIdx % 10 == 0 || frameIdx == totalFrames - 1)
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

            _logger?.LogInformation("Export completed successfully: {Path}", settings.OutputFilePath);
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

    public Task CancelRenderingAsync()
    {
        lock (_lock)
        {
            _renderCts?.Cancel();
        }
        return Task.CompletedTask;
    }
}

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WriteStudio.Core.Abstractions;

namespace WriteStudio.Rendering;

public class FFmpegService : IFFmpegService
{
    private readonly ILogger<FFmpegService>? _logger;
    private string? _ffmpegPath;
    private bool _isAvailable;

    public bool IsFFmpegAvailable => _isAvailable;
    public string? FFmpegPath => _ffmpegPath;

    public FFmpegService(ILogger<FFmpegService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<bool> ProbeFFmpegAsync(string? customPath = null)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(customPath))
        {
            candidates.Add(customPath);
        }

        // Common platform paths
        candidates.Add("ffmpeg");
        candidates.Add("/opt/homebrew/bin/ffmpeg");
        candidates.Add("/usr/local/bin/ffmpeg");
        candidates.Add("/usr/bin/ffmpeg");
        candidates.Add(@"C:\ffmpeg\bin\ffmpeg.exe");
        candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"));
        candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg"));

        foreach (var candidate in candidates)
        {
            if (await TestFFmpegBinaryAsync(candidate))
            {
                _ffmpegPath = candidate;
                _isAvailable = true;
                _logger?.LogInformation("FFmpeg found at {Path}", _ffmpegPath);
                return true;
            }
        }

        _isAvailable = false;
        _logger?.LogWarning("FFmpeg was not detected on this system.");
        return false;
    }

    private async Task<bool> TestFFmpegBinaryAsync(string path)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(cts.Token);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public string BuildEncodingArguments(
        int width,
        int height,
        int fps,
        string? audioFilePath,
        string outputFilePath,
        int videoBitrateKbps = 4000,
        int audioBitrateKbps = 192,
        string videoCodec = "libx264",
        string audioCodec = "aac",
        string preset = "fast",
        int crf = 18)
    {
        var args = new System.Text.StringBuilder();

        // Raw BGRA video input from pipe:0
        args.Append($"-f rawvideo -pix_fmt bgra -s {width}x{height} -r {fps} -i pipe:0 ");

        // Audio input if available
        bool hasAudio = !string.IsNullOrWhiteSpace(audioFilePath) && File.Exists(audioFilePath);
        if (hasAudio)
        {
            args.Append($"-i \"{audioFilePath}\" ");
        }

        // Video codec & quality settings
        args.Append($"-c:v {videoCodec} -preset {preset} -crf {crf} -pix_fmt yuv420p ");

        // Audio codec & mux settings
        if (hasAudio)
        {
            args.Append($"-c:a {audioCodec} -b:a {audioBitrateKbps}k -shortest ");
        }

        // Faststart for streaming/web playback
        args.Append($"-movflags +faststart -y \"{outputFilePath}\"");

        return args.ToString();
    }
}

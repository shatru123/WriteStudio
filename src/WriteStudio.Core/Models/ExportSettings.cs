namespace WriteStudio.Core.Models;

public record ExportSettings(
    string OutputFilePath,
    int Width = 1920,
    int Height = 1080,
    int FrameRate = 30,
    string VideoCodec = "libx264",
    string AudioCodec = "aac",
    int VideoBitrateKbps = 4000,
    int AudioBitrateKbps = 192,
    bool IncludeWebcam = true,
    string Preset = "fast",
    int Crf = 18
)
{
    public static ExportSettings Default1080p(string outputPath) =>
        new(outputPath, 1920, 1080, 30);

    public static ExportSettings Default720p(string outputPath) =>
        new(outputPath, 1280, 720, 30, VideoBitrateKbps: 2500);

    public static ExportSettings HighQuality4K(string outputPath) =>
        new(outputPath, 3840, 2160, 60, VideoBitrateKbps: 15000);
}

public record ExportProgressReport(
    double Percentage,
    TimeSpan ProcessedDuration,
    TimeSpan TotalDuration,
    int CurrentFrame,
    int TotalFrames,
    double CurrentFps,
    string StatusMessage
);

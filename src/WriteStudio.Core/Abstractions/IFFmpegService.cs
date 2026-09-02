namespace WriteStudio.Core.Abstractions;

public interface IFFmpegService
{
    bool IsFFmpegAvailable { get; }
    string? FFmpegPath { get; }

    Task<bool> ProbeFFmpegAsync(string? customPath = null);
    string BuildEncodingArguments(
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
        int crf = 18);
}

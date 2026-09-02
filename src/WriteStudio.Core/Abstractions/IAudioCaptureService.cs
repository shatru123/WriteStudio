using WriteStudio.Core.Models;

namespace WriteStudio.Core.Abstractions;

public interface IAudioCaptureService : IAsyncDisposable
{
    bool IsCapturing { get; }
    AudioDeviceInfo? SelectedDevice { get; }
    
    event EventHandler<AudioLevelEventArgs>? LevelUpdated;
    event EventHandler<AudioDeviceInfo>? DeviceDisconnected;
    event EventHandler<Exception>? ErrorOccurred;

    Task<IReadOnlyList<AudioDeviceInfo>> EnumerateDevicesAsync();
    Task SelectDeviceAsync(AudioDeviceInfo? device);
    Task StartCaptureAsync(string outputWavFilePath, CancellationToken cancellationToken = default);
    Task PauseCaptureAsync();
    Task ResumeCaptureAsync();
    Task StopCaptureAsync();
}

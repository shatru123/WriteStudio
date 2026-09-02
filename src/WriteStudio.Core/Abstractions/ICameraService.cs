using WriteStudio.Core.Models;

namespace WriteStudio.Core.Abstractions;

public interface ICameraService : IAsyncDisposable
{
    bool IsRunning { get; }
    bool IsPreviewActive { get; }
    CameraDeviceInfo? SelectedDevice { get; }
    CameraLayout CurrentLayout { get; }

    event EventHandler<CameraDeviceInfo>? DeviceChanged;
    event EventHandler<CameraLayout>? LayoutChanged;
    event EventHandler<Exception>? ErrorOccurred;

    Task<IReadOnlyList<CameraDeviceInfo>> EnumerateDevicesAsync();
    Task SelectDeviceAsync(CameraDeviceInfo? device);
    void SetLayout(CameraLayout layout);
    void SetPreset(CameraPositionPreset preset);
    void SetMirror(bool isMirrored);
    void SetVisibility(bool isVisible);
    Task StartCaptureAsync(string? outputVideoTrackPath = null, CancellationToken cancellationToken = default);
    Task StopCaptureAsync();
}

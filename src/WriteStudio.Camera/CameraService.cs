using Microsoft.Extensions.Logging;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;

namespace WriteStudio.Camera;

public class CameraService : ICameraService
{
    private readonly ILogger<CameraService>? _logger;
    private readonly object _lock = new();
    private CameraDeviceInfo? _selectedDevice;
    private CameraLayout _currentLayout = CameraLayout.FromPreset(CameraPositionPreset.BottomRight);
    private bool _isRunning;
    private bool _isPreviewActive;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;

    public bool IsRunning
    {
        get { lock (_lock) return _isRunning; }
        private set { lock (_lock) _isRunning = value; }
    }

    public bool IsPreviewActive
    {
        get { lock (_lock) return _isPreviewActive; }
        private set { lock (_lock) _isPreviewActive = value; }
    }

    public CameraDeviceInfo? SelectedDevice
    {
        get { lock (_lock) return _selectedDevice; }
        private set { lock (_lock) _selectedDevice = value; }
    }

    public CameraLayout CurrentLayout
    {
        get { lock (_lock) return _currentLayout; }
        private set
        {
            lock (_lock) _currentLayout = value;
            LayoutChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<CameraDeviceInfo>? DeviceChanged;
    public event EventHandler<CameraLayout>? LayoutChanged;
    public event EventHandler<Exception>? ErrorOccurred;

    public CameraService(ILogger<CameraService>? logger = null)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<CameraDeviceInfo>> EnumerateDevicesAsync()
    {
        var devices = new List<CameraDeviceInfo>();
        try
        {
            devices.Add(new CameraDeviceInfo("cam_default", "Integrated HD Camera / Webcam", IsDefault: true));
            devices.Add(new CameraDeviceInfo("cam_usb_1", "USB 1080p Pro Stream Webcam", IsDefault: false));
            devices.Add(new CameraDeviceInfo("cam_virtual", "Virtual Presenter Camera", IsDefault: false));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error enumerating camera devices.");
            ErrorOccurred?.Invoke(this, ex);
        }

        if (SelectedDevice == null && devices.Count > 0)
        {
            SelectedDevice = devices[0];
        }

        return Task.FromResult<IReadOnlyList<CameraDeviceInfo>>(devices);
    }

    public Task SelectDeviceAsync(CameraDeviceInfo? device)
    {
        SelectedDevice = device;
        _logger?.LogInformation("Camera selected: {Name}", device?.Name ?? "None");
        if (device != null)
        {
            DeviceChanged?.Invoke(this, device);
        }
        return Task.CompletedTask;
    }

    public void SetLayout(CameraLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        CurrentLayout = layout;
        _logger?.LogInformation("Camera layout set to: {Preset}", layout.Preset);
    }

    public void SetPreset(CameraPositionPreset preset)
    {
        var newLayout = CameraLayout.FromPreset(preset, CurrentLayout.IsMirrored);
        CurrentLayout = newLayout;
    }

    public void SetMirror(bool isMirrored)
    {
        var layout = CurrentLayout.Clone();
        layout.IsMirrored = isMirrored;
        CurrentLayout = layout;
    }

    public void SetVisibility(bool isVisible)
    {
        var layout = CurrentLayout.Clone();
        layout.IsVisible = isVisible;
        CurrentLayout = layout;
    }

    public Task StartCaptureAsync(string? outputVideoTrackPath = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isRunning) return Task.CompletedTask;
            _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isRunning = true;
            _isPreviewActive = true;
        }

        _logger?.LogInformation("Starting camera capture stream (Path: {Path})", outputVideoTrackPath ?? "Preview-only");

        _captureTask = Task.Run(async () =>
        {
            try
            {
                while (!_captureCts.Token.IsCancellationRequested)
                {
                    // Simulated camera frame ticker (30 FPS)
                    await Task.Delay(33, _captureCts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in camera capture loop.");
                ErrorOccurred?.Invoke(this, ex);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    public async Task StopCaptureAsync()
    {
        Task? toWait = null;
        lock (_lock)
        {
            if (!_isRunning) return;
            _isRunning = false;
            _isPreviewActive = false;
            _captureCts?.Cancel();
            toWait = _captureTask;
        }

        if (toWait != null)
        {
            try { await toWait; } catch { }
        }

        lock (_lock)
        {
            _captureCts?.Dispose();
            _captureCts = null;
            _captureTask = null;
        }

        _logger?.LogInformation("Camera capture stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopCaptureAsync();
        GC.SuppressFinalize(this);
    }
}

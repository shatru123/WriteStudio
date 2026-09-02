using Microsoft.Extensions.Logging;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;

namespace WriteStudio.Audio;

public class AudioCaptureService : IAudioCaptureService
{
    private readonly ILogger<AudioCaptureService>? _logger;
    private readonly object _lock = new();
    private WavFileWriter? _wavWriter;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private AudioDeviceInfo? _selectedDevice;
    private bool _isCapturing;
    private bool _isPaused;

    public bool IsCapturing
    {
        get { lock (_lock) return _isCapturing; }
        private set { lock (_lock) _isCapturing = value; }
    }

    public AudioDeviceInfo? SelectedDevice
    {
        get { lock (_lock) return _selectedDevice; }
        private set { lock (_lock) _selectedDevice = value; }
    }

    public event EventHandler<AudioLevelEventArgs>? LevelUpdated;
    public event EventHandler<AudioDeviceInfo>? DeviceDisconnected;
    public event EventHandler<Exception>? ErrorOccurred;

    public AudioCaptureService(ILogger<AudioCaptureService>? logger = null)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<AudioDeviceInfo>> EnumerateDevicesAsync()
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            // Default system input device
            devices.Add(new AudioDeviceInfo("default", "Default Microphone (System Audio)", IsDefault: true, IsUsbDevice: false));
            devices.Add(new AudioDeviceInfo("usb_mic_1", "USB Studio Microphone (e.g. Blue Yeti / Rode)", IsDefault: false, IsUsbDevice: true));
            devices.Add(new AudioDeviceInfo("usb_headset", "USB Headset Microphone", IsDefault: false, IsUsbDevice: true));
            devices.Add(new AudioDeviceInfo("line_in", "Built-in Line In / Microphone", IsDefault: false, IsUsbDevice: false));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error enumerating audio devices.");
            ErrorOccurred?.Invoke(this, ex);
        }

        if (SelectedDevice == null && devices.Count > 0)
        {
            SelectedDevice = devices[0];
        }

        return Task.FromResult<IReadOnlyList<AudioDeviceInfo>>(devices);
    }

    public Task SelectDeviceAsync(AudioDeviceInfo? device)
    {
        SelectedDevice = device;
        _logger?.LogInformation("Selected audio device: {DeviceName} (USB: {IsUsb})", device?.Name ?? "None", device?.IsUsbDevice ?? false);
        return Task.CompletedTask;
    }

    public void NotifyDeviceDisconnected(AudioDeviceInfo device)
    {
        _logger?.LogWarning("Audio device disconnected: {DeviceName}", device.Name);
        DeviceDisconnected?.Invoke(this, device);
    }

    public Task StartCaptureAsync(string outputWavFilePath, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isCapturing)
                throw new InvalidOperationException("Audio capture is already in progress.");

            _wavWriter = new WavFileWriter(outputWavFilePath);
            _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isCapturing = true;
            _isPaused = false;
        }

        _logger?.LogInformation("Starting audio capture to {Path}", outputWavFilePath);
        _captureTask = Task.Run(() => CaptureLoopAsync(_captureCts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        const int sampleRate = 48000;
        const int channels = 2;
        const int bytesPerSample = 2; // 16-bit
        const int chunkSizeMs = 50; // 50ms buffer chunks
        int bufferSize = (sampleRate * channels * bytesPerSample * chunkSizeMs) / 1000;
        byte[] buffer = new byte[bufferSize];

        double phase = 0.0;
        var random = new Random();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var loopStart = DateTime.UtcNow;

                if (!_isPaused)
                {
                    // Synthesize live microphone audio stream (voice frequency simulation 200Hz - 2kHz)
                    for (int i = 0; i < bufferSize / 4; i++)
                    {
                        // Voice carrier + ambient breath/subtle noise
                        double signal = Math.Sin(phase) * 0.25 + (random.NextDouble() - 0.5) * 0.03;
                        short sampleVal = (short)(signal * 32767);

                        buffer[i * 4] = (byte)(sampleVal & 0xFF);
                        buffer[i * 4 + 1] = (byte)((sampleVal >> 8) & 0xFF);
                        buffer[i * 4 + 2] = buffer[i * 4];
                        buffer[i * 4 + 3] = buffer[i * 4 + 1];

                        phase += (2.0 * Math.PI * 440.0) / sampleRate;
                        if (phase > 2.0 * Math.PI) phase -= 2.0 * Math.PI;
                    }

                    _wavWriter?.WriteSampleData(buffer, 0, buffer.Length);

                    var levels = AudioLevelCalculator.ComputeLevels(buffer, 0, buffer.Length);
                    LevelUpdated?.Invoke(this, levels);
                }
                else
                {
                    // While paused, emit low/silence meter
                    LevelUpdated?.Invoke(this, new AudioLevelEventArgs(0.0f, 0.0f));
                }

                var elapsed = (DateTime.UtcNow - loopStart).TotalMilliseconds;
                int sleepTime = Math.Max(1, (int)(chunkSizeMs - elapsed));
                await Task.Delay(sleepTime, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Exception in audio capture loop.");
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    public Task PauseCaptureAsync()
    {
        lock (_lock)
        {
            _isPaused = true;
        }
        _logger?.LogInformation("Audio capture paused.");
        return Task.CompletedTask;
    }

    public Task ResumeCaptureAsync()
    {
        lock (_lock)
        {
            _isPaused = false;
        }
        _logger?.LogInformation("Audio capture resumed.");
        return Task.CompletedTask;
    }

    public async Task StopCaptureAsync()
    {
        Task? taskToWait = null;
        lock (_lock)
        {
            if (!_isCapturing) return;
            _isCapturing = false;
            _isPaused = false;
            _captureCts?.Cancel();
            taskToWait = _captureTask;
        }

        if (taskToWait != null)
        {
            try
            {
                await taskToWait;
            }
            catch (Exception) { /* Ignored */ }
        }

        lock (_lock)
        {
            _wavWriter?.Dispose();
            _wavWriter = null;
            _captureCts?.Dispose();
            _captureCts = null;
            _captureTask = null;
        }

        _logger?.LogInformation("Audio capture stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopCaptureAsync();
        GC.SuppressFinalize(this);
    }
}

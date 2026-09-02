using System.Collections.ObjectModel;
using WriteStudio.App.Mvvm;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;

namespace WriteStudio.App.ViewModels;

public class AudioViewModel : ObservableObject
{
    private readonly IAudioCaptureService _audioService;
    private AudioDeviceInfo? _selectedDevice;
    private float _peakLevel;
    private float _rmsLevel;
    private float _decibels = -60.0f;
    private bool _isUsbMicrophone;
    private string _statusMessage = "Microphone Ready";

    public AudioDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                _ = _audioService.SelectDeviceAsync(value);
                IsUsbMicrophone = value?.IsUsbDevice ?? false;
                StatusMessage = value != null ? $"Connected: {value.Name}" : "No microphone selected";
            }
        }
    }

    public float PeakLevel
    {
        get => _peakLevel;
        private set => SetProperty(ref _peakLevel, value);
    }

    public float RmsLevel
    {
        get => _rmsLevel;
        private set => SetProperty(ref _rmsLevel, value);
    }

    public float Decibels
    {
        get => _decibels;
        private set => SetProperty(ref _decibels, value);
    }

    public bool IsUsbMicrophone
    {
        get => _isUsbMicrophone;
        private set => SetProperty(ref _isUsbMicrophone, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ObservableCollection<AudioDeviceInfo> Devices { get; } = new();

    public AudioViewModel(IAudioCaptureService audioService)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));

        _audioService.LevelUpdated += (s, e) =>
        {
            PeakLevel = e.Peak;
            RmsLevel = e.Rms;
            Decibels = e.Decibels;
        };

        _audioService.DeviceDisconnected += (s, dev) =>
        {
            StatusMessage = $"Warning: Device {dev.Name} was disconnected!";
        };

        _ = RefreshDevicesAsync();
    }

    public async Task RefreshDevicesAsync()
    {
        var devices = await _audioService.EnumerateDevicesAsync();
        Devices.Clear();
        foreach (var d in devices) Devices.Add(d);

        if (_audioService.SelectedDevice != null)
        {
            SelectedDevice = _audioService.SelectedDevice;
        }
        else if (Devices.Count > 0)
        {
            SelectedDevice = Devices[0];
        }
    }
}

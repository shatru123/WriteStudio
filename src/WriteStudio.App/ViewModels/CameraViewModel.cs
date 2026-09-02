using System.Collections.ObjectModel;
using System.Windows.Input;
using WriteStudio.App.Mvvm;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;

namespace WriteStudio.App.ViewModels;

public class CameraViewModel : ObservableObject
{
    private readonly ICameraService _cameraService;
    private CameraDeviceInfo? _selectedDevice;
    private CameraLayout _currentLayout;
    private bool _isPreviewActive;
    private bool _isMirrored = true;
    private bool _isVisible = true;

    public CameraDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                _ = _cameraService.SelectDeviceAsync(value);
            }
        }
    }

    public CameraLayout CurrentLayout
    {
        get => _currentLayout;
        private set => SetProperty(ref _currentLayout, value);
    }

    public bool IsPreviewActive
    {
        get => _isPreviewActive;
        set
        {
            if (SetProperty(ref _isPreviewActive, value))
            {
                if (value) _ = _cameraService.StartCaptureAsync();
                else _ = _cameraService.StopCaptureAsync();
            }
        }
    }

    public bool IsMirrored
    {
        get => _isMirrored;
        set
        {
            if (SetProperty(ref _isMirrored, value))
            {
                _cameraService.SetMirror(value);
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                _cameraService.SetVisibility(value);
            }
        }
    }

    public ObservableCollection<CameraDeviceInfo> Devices { get; } = new();

    public ICommand SelectPresetCommand { get; }
    public ICommand ToggleMirrorCommand { get; }
    public ICommand ToggleVisibilityCommand { get; }

    public CameraViewModel(ICameraService cameraService)
    {
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _currentLayout = _cameraService.CurrentLayout;
        _isMirrored = _currentLayout.IsMirrored;
        _isVisible = _currentLayout.IsVisible;

        _cameraService.LayoutChanged += (s, layout) =>
        {
            CurrentLayout = layout;
            _isMirrored = layout.IsMirrored;
            _isVisible = layout.IsVisible;
            OnPropertyChanged(nameof(IsMirrored));
            OnPropertyChanged(nameof(IsVisible));
        };

        SelectPresetCommand = new RelayCommand(p =>
        {
            if (p is CameraPositionPreset preset) _cameraService.SetPreset(preset);
            else if (p is string presetStr && Enum.TryParse<CameraPositionPreset>(presetStr, true, out var parsed))
                _cameraService.SetPreset(parsed);
        });

        ToggleMirrorCommand = new RelayCommand(() => IsMirrored = !IsMirrored);
        ToggleVisibilityCommand = new RelayCommand(() => IsVisible = !IsVisible);

        _ = RefreshDevicesAsync();
    }

    public async Task RefreshDevicesAsync()
    {
        var devices = await _cameraService.EnumerateDevicesAsync();
        Devices.Clear();
        foreach (var d in devices) Devices.Add(d);

        if (_cameraService.SelectedDevice != null)
        {
            SelectedDevice = _cameraService.SelectedDevice;
        }
        else if (Devices.Count > 0)
        {
            SelectedDevice = Devices[0];
        }
    }
}

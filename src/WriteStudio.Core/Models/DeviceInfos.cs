namespace WriteStudio.Core.Models;

public record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsDefault = false,
    bool IsUsbDevice = false
);

public record CameraDeviceInfo(
    string Id,
    string Name,
    bool IsDefault = false
);

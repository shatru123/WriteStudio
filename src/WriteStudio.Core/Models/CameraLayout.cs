namespace WriteStudio.Core.Models;

public enum CameraPositionPreset
{
    BottomRight,
    BottomLeft,
    TopRight,
    TopLeft,
    Fullscreen,
    Hidden,
    Custom
}

/// <summary>
/// Camera position, dimensions (normalized 0.0 to 1.0), and presentation options.
/// </summary>
public record CameraLayout(
    CameraPositionPreset Preset = CameraPositionPreset.BottomRight,
    double NormalizedX = 0.78,
    double NormalizedY = 0.72,
    double NormalizedWidth = 0.20,
    double NormalizedHeight = 0.25,
    bool IsMirrored = true,
    bool IsVisible = true,
    double CornerRadius = 12.0,
    bool HasBorder = true,
    double BorderThickness = 2.0
)
{
    public static CameraLayout HiddenLayout => new(CameraPositionPreset.Hidden, IsVisible: false);

    public static CameraLayout FromPreset(CameraPositionPreset preset, bool mirrored = true)
    {
        return preset switch
        {
            CameraPositionPreset.BottomRight => new CameraLayout(
                Preset: CameraPositionPreset.BottomRight,
                NormalizedX: 0.78,
                NormalizedY: 0.72,
                NormalizedWidth: 0.20,
                NormalizedHeight: 0.25,
                IsMirrored: mirrored,
                IsVisible: true),

            CameraPositionPreset.BottomLeft => new CameraLayout(
                Preset: CameraPositionPreset.BottomLeft,
                NormalizedX: 0.02,
                NormalizedY: 0.72,
                NormalizedWidth: 0.20,
                NormalizedHeight: 0.25,
                IsMirrored: mirrored,
                IsVisible: true),

            CameraPositionPreset.TopRight => new CameraLayout(
                Preset: CameraPositionPreset.TopRight,
                NormalizedX: 0.78,
                NormalizedY: 0.03,
                NormalizedWidth: 0.20,
                NormalizedHeight: 0.25,
                IsMirrored: mirrored,
                IsVisible: true),

            CameraPositionPreset.TopLeft => new CameraLayout(
                Preset: CameraPositionPreset.TopLeft,
                NormalizedX: 0.02,
                NormalizedY: 0.03,
                NormalizedWidth: 0.20,
                NormalizedHeight: 0.25,
                IsMirrored: mirrored,
                IsVisible: true),

            CameraPositionPreset.Fullscreen => new CameraLayout(
                Preset: CameraPositionPreset.Fullscreen,
                NormalizedX: 0.0,
                NormalizedY: 0.0,
                NormalizedWidth: 1.0,
                NormalizedHeight: 1.0,
                IsMirrored: mirrored,
                IsVisible: true,
                CornerRadius: 0.0,
                HasBorder: false),

            CameraPositionPreset.Hidden => HiddenLayout,

            _ => new CameraLayout(Preset: preset, IsMirrored: mirrored, IsVisible: true)
        };
    }
}

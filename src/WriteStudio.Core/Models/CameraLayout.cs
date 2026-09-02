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
public class CameraLayout
{
    public CameraPositionPreset Preset { get; set; } = CameraPositionPreset.BottomRight;
    public double NormalizedX { get; set; } = 0.78;
    public double NormalizedY { get; set; } = 0.72;
    public double NormalizedWidth { get; set; } = 0.20;
    public double NormalizedHeight { get; set; } = 0.25;
    public bool IsMirrored { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public double CornerRadius { get; set; } = 12.0;
    public bool HasBorder { get; set; } = true;
    public double BorderThickness { get; set; } = 2.0;

    public CameraLayout() { }

    public CameraLayout(
        CameraPositionPreset preset = CameraPositionPreset.BottomRight,
        double normalizedX = 0.78,
        double normalizedY = 0.72,
        double normalizedWidth = 0.20,
        double normalizedHeight = 0.25,
        bool isMirrored = true,
        bool isVisible = true,
        double cornerRadius = 12.0,
        bool hasBorder = true,
        double borderThickness = 2.0)
    {
        Preset = preset;
        NormalizedX = normalizedX;
        NormalizedY = normalizedY;
        NormalizedWidth = normalizedWidth;
        NormalizedHeight = normalizedHeight;
        IsMirrored = isMirrored;
        IsVisible = isVisible;
        CornerRadius = cornerRadius;
        HasBorder = hasBorder;
        BorderThickness = borderThickness;
    }

    public static CameraLayout HiddenLayout => new(CameraPositionPreset.Hidden, isVisible: false);

    public static CameraLayout FromPreset(CameraPositionPreset preset, bool mirrored = true)
    {
        return preset switch
        {
            CameraPositionPreset.BottomRight => new CameraLayout(
                preset: CameraPositionPreset.BottomRight,
                normalizedX: 0.78,
                normalizedY: 0.72,
                normalizedWidth: 0.20,
                normalizedHeight: 0.25,
                isMirrored: mirrored,
                isVisible: true),

            CameraPositionPreset.BottomLeft => new CameraLayout(
                preset: CameraPositionPreset.BottomLeft,
                normalizedX: 0.02,
                normalizedY: 0.72,
                normalizedWidth: 0.20,
                normalizedHeight: 0.25,
                isMirrored: mirrored,
                isVisible: true),

            CameraPositionPreset.TopRight => new CameraLayout(
                preset: CameraPositionPreset.TopRight,
                normalizedX: 0.78,
                normalizedY: 0.03,
                normalizedWidth: 0.20,
                normalizedHeight: 0.25,
                isMirrored: mirrored,
                isVisible: true),

            CameraPositionPreset.TopLeft => new CameraLayout(
                preset: CameraPositionPreset.TopLeft,
                normalizedX: 0.02,
                normalizedY: 0.03,
                normalizedWidth: 0.20,
                normalizedHeight: 0.25,
                isMirrored: mirrored,
                isVisible: true),

            CameraPositionPreset.Fullscreen => new CameraLayout(
                preset: CameraPositionPreset.Fullscreen,
                normalizedX: 0.0,
                normalizedY: 0.0,
                normalizedWidth: 1.0,
                normalizedHeight: 1.0,
                isMirrored: mirrored,
                isVisible: true,
                cornerRadius: 0.0,
                hasBorder: false),

            CameraPositionPreset.Hidden => HiddenLayout,

            _ => new CameraLayout(preset: preset, isMirrored: mirrored, isVisible: true)
        };
    }

    public CameraLayout Clone() => new()
    {
        Preset = Preset,
        NormalizedX = NormalizedX,
        NormalizedY = NormalizedY,
        NormalizedWidth = NormalizedWidth,
        NormalizedHeight = NormalizedHeight,
        IsMirrored = IsMirrored,
        IsVisible = IsVisible,
        CornerRadius = CornerRadius,
        HasBorder = HasBorder,
        BorderThickness = BorderThickness
    };
}

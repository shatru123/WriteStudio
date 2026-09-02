namespace WriteStudio.Core.Models;

/// <summary>
/// A high-precision 2D point in whiteboard canvas space, containing pressure and recording timestamp.
/// </summary>
public record DrawingPoint(double X, double Y, float Pressure, TimeSpan Timestamp)
{
    public static DrawingPoint Create(double x, double y, float pressure = 0.5f, TimeSpan? timestamp = null)
    {
        return new DrawingPoint(x, y, Math.Clamp(pressure, 0.01f, 1.0f), timestamp ?? TimeSpan.Zero);
    }
}

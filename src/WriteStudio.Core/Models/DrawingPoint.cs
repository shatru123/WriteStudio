namespace WriteStudio.Core.Models;

/// <summary>
/// A high-precision 2D point in whiteboard canvas space, containing pressure and recording timestamp.
/// </summary>
public class DrawingPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public float Pressure { get; set; } = 0.5f;
    public TimeSpan Timestamp { get; set; } = TimeSpan.Zero;

    public DrawingPoint() { }

    public DrawingPoint(double x, double y, float pressure = 0.5f, TimeSpan? timestamp = null)
    {
        X = x;
        Y = y;
        Pressure = Math.Clamp(pressure, 0.01f, 1.0f);
        Timestamp = timestamp ?? TimeSpan.Zero;
    }

    public static DrawingPoint Create(double x, double y, float pressure = 0.5f, TimeSpan? timestamp = null) =>
        new(x, y, pressure, timestamp);

    public DrawingPoint Clone() =>
        new(X, Y, Pressure, Timestamp);
}

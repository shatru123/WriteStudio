namespace WriteStudio.Core.Models;

/// <summary>
/// Axis-aligned bounding box for vector strokes and canvas elements.
/// </summary>
public record RectBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Contains(double px, double py) =>
        px >= X && px <= Right && py >= Y && py <= Bottom;

    public bool IntersectsWith(RectBounds other) =>
        X < other.Right && Right > other.X &&
        Y < other.Bottom && Bottom > other.Y;

    public static RectBounds FromPoints(IEnumerable<DrawingPoint> points, double padding = 0)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool hasPoints = false;

        foreach (var p in points)
        {
            hasPoints = true;
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        if (!hasPoints) return new RectBounds(0, 0, 0, 0);

        return new RectBounds(
            minX - padding,
            minY - padding,
            Math.Max(0, (maxX - minX) + 2 * padding),
            Math.Max(0, (maxY - minY) + 2 * padding)
        );
    }
}

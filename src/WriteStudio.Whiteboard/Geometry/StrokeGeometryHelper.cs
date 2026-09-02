using WriteStudio.Core.Models;

namespace WriteStudio.Whiteboard.Geometry;

public static class StrokeGeometryHelper
{
    public static double Distance(double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1;
        double dy = y2 - y1;
        double lengthSq = dx * dx + dy * dy;

        if (lengthSq < 0.000001)
        {
            return Distance(px, py, x1, y1);
        }

        // Projection of point onto line segment: t = [(p - p1) . (p2 - p1)] / |p2 - p1|^2
        double t = Math.Clamp(((px - x1) * dx + (py - y1) * dy) / lengthSq, 0.0, 1.0);
        double projX = x1 + t * dx;
        double projY = y1 + t * dy;

        return Distance(px, py, projX, projY);
    }

    public static bool IsStrokeNearPoint(DrawingStroke stroke, double px, double py, double radius)
    {
        if (stroke.Points.Count == 0) return false;

        double totalThreshold = radius + (stroke.Thickness / 2.0);

        // Fast bounding box reject
        var bounds = stroke.Bounds;
        if (px < bounds.X - totalThreshold || px > bounds.Right + totalThreshold ||
            py < bounds.Y - totalThreshold || py > bounds.Bottom + totalThreshold)
        {
            return false;
        }

        if (stroke.Points.Count == 1)
        {
            return Distance(px, py, stroke.Points[0].X, stroke.Points[0].Y) <= totalThreshold;
        }

        for (int i = 0; i < stroke.Points.Count - 1; i++)
        {
            var p1 = stroke.Points[i];
            var p2 = stroke.Points[i + 1];

            if (DistanceToSegment(px, py, p1.X, p1.Y, p2.X, p2.Y) <= totalThreshold)
            {
                return true;
            }
        }

        return false;
    }

    public static List<DrawingPoint> GenerateLinePoints(double x1, double y1, double x2, double y2, TimeSpan timestamp, float pressure = 0.5f)
    {
        return new List<DrawingPoint>
        {
            DrawingPoint.Create(x1, y1, pressure, timestamp),
            DrawingPoint.Create(x2, y2, pressure, timestamp)
        };
    }

    public static List<DrawingPoint> GenerateRectanglePoints(double x1, double y1, double x2, double y2, TimeSpan timestamp, float pressure = 0.5f)
    {
        double left = Math.Min(x1, x2);
        double right = Math.Max(x1, x2);
        double top = Math.Min(y1, y2);
        double bottom = Math.Max(y1, y2);

        return new List<DrawingPoint>
        {
            DrawingPoint.Create(left, top, pressure, timestamp),
            DrawingPoint.Create(right, top, pressure, timestamp),
            DrawingPoint.Create(right, bottom, pressure, timestamp),
            DrawingPoint.Create(left, bottom, pressure, timestamp),
            DrawingPoint.Create(left, top, pressure, timestamp)
        };
    }

    public static List<DrawingPoint> GenerateCirclePoints(double x1, double y1, double x2, double y2, TimeSpan timestamp, int segments = 48, float pressure = 0.5f)
    {
        double centerX = (x1 + x2) / 2.0;
        double centerY = (y1 + y2) / 2.0;
        double radiusX = Math.Abs(x2 - x1) / 2.0;
        double radiusY = Math.Abs(y2 - y1) / 2.0;

        var points = new List<DrawingPoint>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            double angle = (2.0 * Math.PI * i) / segments;
            double px = centerX + radiusX * Math.Cos(angle);
            double py = centerY + radiusY * Math.Sin(angle);
            points.Add(DrawingPoint.Create(px, py, pressure, timestamp));
        }

        return points;
    }

    public static List<DrawingPoint> GenerateArrowPoints(double x1, double y1, double x2, double y2, TimeSpan timestamp, double headLength = 20.0, double headAngleDeg = 30.0, float pressure = 0.5f)
    {
        var points = new List<DrawingPoint>();
        points.Add(DrawingPoint.Create(x1, y1, pressure, timestamp));
        points.Add(DrawingPoint.Create(x2, y2, pressure, timestamp));

        double lineAngle = Math.Atan2(y2 - y1, x2 - x1);
        double angleRad = headAngleDeg * Math.PI / 180.0;

        double arrow1X = x2 - headLength * Math.Cos(lineAngle - angleRad);
        double arrow1Y = y2 - headLength * Math.Sin(lineAngle - angleRad);

        double arrow2X = x2 - headLength * Math.Cos(lineAngle + angleRad);
        double arrow2Y = y2 - headLength * Math.Sin(lineAngle + angleRad);

        // Arrow head strokes: x2,y2 -> arrow1, return to x2,y2, -> arrow2
        points.Add(DrawingPoint.Create(arrow1X, arrow1Y, pressure, timestamp));
        points.Add(DrawingPoint.Create(x2, y2, pressure, timestamp));
        points.Add(DrawingPoint.Create(arrow2X, arrow2Y, pressure, timestamp));

        return points;
    }
}

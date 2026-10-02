namespace Cad.Geometry;

public readonly record struct Circle2D(Vector2 Center, double Radius)
{
    public BoundingBox Bounds => new(
        Center - new Vector2(Radius, Radius),
        Center + new Vector2(Radius, Radius));

    public Vector2 PointAt(double angle) => Center + Vector2.FromPolar(Radius, angle);

    public Vector2 ClosestPoint(Vector2 p)
    {
        var d = p - Center;
        return Tolerance.IsZero(d.Length) ? PointAt(0) : Center + d.Normalized() * Radius;
    }

    public double DistanceTo(Vector2 p) => Math.Abs(Vector2.Distance(p, Center) - Radius);
}

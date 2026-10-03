namespace Cad.Geometry;

/// <summary>
/// Segmento finito tra due punti.
/// </summary>
public readonly record struct Segment2D(Vector2 Start, Vector2 End)
{
    public Vector2 Direction => End - Start;
    public double Length => Direction.Length;
    public Vector2 Midpoint => (Start + End) / 2;
    public BoundingBox Bounds => BoundingBox.FromPoints(Start, End);

    /// <summary>Punto del segmento a parametro <paramref name="t"/> (0 = inizio, 1 = fine).</summary>
    public Vector2 PointAt(double t) => Vector2.Lerp(Start, End, t);

    /// <summary>Parametro della proiezione di <paramref name="p"/> sulla retta del segmento, non limitato a [0, 1].</summary>
    public double Project(Vector2 p)
    {
        var d = Direction;
        var lengthSquared = d.LengthSquared;
        return Tolerance.IsZero(lengthSquared) ? 0 : Vector2.Dot(p - Start, d) / lengthSquared;
    }

    public Vector2 ClosestPoint(Vector2 p) => PointAt(Math.Clamp(Project(p), 0, 1));

    public double DistanceTo(Vector2 p) => Vector2.Distance(p, ClosestPoint(p));

    public Segment2D Transform(Matrix2D m) => new(m.Transform(Start), m.Transform(End));
}

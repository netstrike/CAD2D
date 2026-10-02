namespace Cad.Geometry;

/// <summary>
/// Intersezioni tra primitive. Restituiscono sempre una lista, vuota se non ci sono punti in comune.
/// </summary>
public static class Intersections
{
    /// <summary>
    /// Intersezione tra due segmenti. Con <paramref name="extend"/> = true i segmenti sono trattati come rette infinite
    /// (utile per lo snap "intersezione apparente" e per ESTENDI). Segmenti paralleli o sovrapposti non producono punti.
    /// </summary>
    public static IReadOnlyList<Vector2> SegmentSegment(Segment2D a, Segment2D b, bool extend = false)
    {
        var r = a.Direction;
        var s = b.Direction;
        var denominator = Vector2.Cross(r, s);
        if (Tolerance.IsZero(denominator, 1e-12 * r.Length * s.Length))
        {
            return [];
        }

        var qp = b.Start - a.Start;
        var t = Vector2.Cross(qp, s) / denominator;
        var u = Vector2.Cross(qp, r) / denominator;

        if (!extend && !(InUnitRange(t, a.Length) && InUnitRange(u, b.Length)))
        {
            return [];
        }

        return [a.PointAt(t)];
    }

    /// <summary>Intersezione tra un segmento (o la sua retta se <paramref name="extend"/>) e una circonferenza.</summary>
    public static IReadOnlyList<Vector2> SegmentCircle(Segment2D segment, Circle2D circle, bool extend = false)
    {
        var d = segment.Direction;
        var f = segment.Start - circle.Center;
        var a = d.LengthSquared;
        if (Tolerance.IsZero(a))
        {
            return [];
        }

        var b = 2 * Vector2.Dot(f, d);
        var c = f.LengthSquared - circle.Radius * circle.Radius;
        var discriminant = b * b - 4 * a * c;
        var tangentTolerance = 1e-12 * Math.Max(1, b * b);
        if (discriminant < -tangentTolerance)
        {
            return [];
        }

        var result = new List<Vector2>(2);
        if (discriminant <= tangentTolerance)
        {
            AddIfInRange(-b / (2 * a));
        }
        else
        {
            var root = Math.Sqrt(discriminant);
            AddIfInRange((-b - root) / (2 * a));
            AddIfInRange((-b + root) / (2 * a));
        }

        return result;

        void AddIfInRange(double t)
        {
            if (extend || InUnitRange(t, segment.Length))
            {
                result.Add(segment.PointAt(t));
            }
        }
    }

    /// <summary>Intersezione tra due circonferenze. Circonferenze concentriche non producono punti.</summary>
    public static IReadOnlyList<Vector2> CircleCircle(Circle2D a, Circle2D b)
    {
        var delta = b.Center - a.Center;
        var distance = delta.Length;
        if (Tolerance.IsZero(distance))
        {
            return [];
        }

        if (distance > a.Radius + b.Radius + Tolerance.Default ||
            distance < Math.Abs(a.Radius - b.Radius) - Tolerance.Default)
        {
            return [];
        }

        var along = (a.Radius * a.Radius - b.Radius * b.Radius + distance * distance) / (2 * distance);
        var heightSquared = a.Radius * a.Radius - along * along;
        var basePoint = a.Center + delta * (along / distance);
        if (heightSquared <= Tolerance.Default)
        {
            return [basePoint];
        }

        var offset = delta.Perpendicular() * (Math.Sqrt(heightSquared) / distance);
        return [basePoint + offset, basePoint - offset];
    }

    /// <summary>Vero se il parametro t cade nel segmento, con la tolleranza espressa in unità di lunghezza.</summary>
    private static bool InUnitRange(double t, double length)
    {
        var slack = length > 0 ? Tolerance.Default / length : 0;
        return t >= -slack && t <= 1 + slack;
    }
}

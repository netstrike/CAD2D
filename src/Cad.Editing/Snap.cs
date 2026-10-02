using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

[Flags]
public enum SnapModes
{
    None = 0,
    Endpoint = 1,
    Midpoint = 2,
    Center = 4,
    Intersection = 8,
    Perpendicular = 16,
    Quadrant = 32,
    Node = 64,
    Tangent = 128,
    Nearest = 256,
    Default = Endpoint | Midpoint | Center | Intersection | Perpendicular | Node | Quadrant | Tangent,
}

public readonly record struct SnapResult(Vector2 Point, SnapModes Kind);

/// <summary>
/// Trova il punto notevole più vicino al cursore entro l'apertura, tra quelli delle entità vicine.
/// </summary>
public static class SnapEngine
{
    /// <summary>A parità di distanza vince il tipo più "forte": un estremo batte un'intersezione, che batte un perpendicolare.</summary>
    private static readonly SnapModes[] Priority =
    [
        SnapModes.Endpoint, SnapModes.Node, SnapModes.Center, SnapModes.Midpoint,
        SnapModes.Quadrant, SnapModes.Intersection, SnapModes.Perpendicular, SnapModes.Tangent, SnapModes.Nearest,
    ];

    /// <param name="candidates">Entità vicine al cursore (già filtrate con l'indice spaziale).</param>
    /// <param name="cursor">Posizione del cursore in coordinate mondo.</param>
    /// <param name="aperture">Raggio di cattura in unità di disegno.</param>
    /// <param name="basePoint">Ultimo punto del comando in corso, necessario per lo snap perpendicolare.</param>
    public static SnapResult? Find(IEnumerable<Entity> candidates, Vector2 cursor, double aperture, SnapModes modes, Vector2? basePoint = null)
    {
        if (modes == SnapModes.None)
        {
            return null;
        }

        var primitives = new List<Primitive>();
        var best = (SnapResult?)null;
        var bestDistance = double.PositiveInfinity;

        // "Vicino" vale solo se non c'è nessun punto notevole: altrimenti vincerebbe sempre.
        Vector2? nearest = null;
        var nearestDistance = double.PositiveInfinity;

        void Consider(Vector2 point, SnapModes kind)
        {
            if ((modes & kind) == 0)
            {
                return;
            }

            var distance = Vector2.Distance(point, cursor);
            if (distance > aperture)
            {
                return;
            }

            // Tolleranza piccola rispetto all'apertura: punti quasi coincidenti si risolvono per priorità.
            var tie = aperture * 1e-3;
            if (distance < bestDistance - tie ||
                (distance <= bestDistance + tie && best is { } current && Array.IndexOf(Priority, kind) < Array.IndexOf(Priority, current.Kind)))
            {
                best = new SnapResult(point, kind);
                bestDistance = Math.Min(distance, bestDistance);
            }
        }

        foreach (var entity in candidates)
        {
            switch (entity)
            {
                case PointEntity point:
                    Consider(point.Position, SnapModes.Node);
                    continue;
                case TextEntity text:
                    Consider(text.Position, SnapModes.Node);
                    continue;
                case InsertEntity insert:
                    Consider(insert.Position, SnapModes.Node);
                    break;
                case EllipseEntity ellipse:
                    Consider(ellipse.Center, SnapModes.Center);
                    break;
            }

            foreach (var primitive in EntityGeometry.Decompose(entity))
            {
                if (primitive.DistanceTo(cursor) > aperture)
                {
                    continue;
                }

                primitives.Add(primitive);
                if ((modes & SnapModes.Nearest) != 0)
                {
                    var near = Nearest(primitive, cursor);
                    if (Vector2.Distance(near, cursor) < nearestDistance)
                    {
                        nearestDistance = Vector2.Distance(near, cursor);
                        nearest = near;
                    }
                }

                switch (primitive)
                {
                    case SegmentPrimitive { Segment: var s }:
                        Consider(s.Start, SnapModes.Endpoint);
                        Consider(s.End, SnapModes.Endpoint);
                        Consider(s.Midpoint, SnapModes.Midpoint);
                        if (basePoint is { } b)
                        {
                            var t = s.Project(b);
                            if (t is >= 0 and <= 1)
                            {
                                Consider(s.PointAt(t), SnapModes.Perpendicular);
                            }
                        }

                        break;
                    case ArcPrimitive arcPrimitive:
                    {
                        var arc = arcPrimitive.Arc;
                        Consider(arc.Center, SnapModes.Center);
                        if (!arcPrimitive.IsFullCircle)
                        {
                            Consider(arc.StartPoint, SnapModes.Endpoint);
                            Consider(arc.EndPoint, SnapModes.Endpoint);
                            Consider(arc.Midpoint, SnapModes.Midpoint);
                        }

                        for (var q = 0; q < 4; q++)
                        {
                            if (arc.ContainsAngle(q * Math.PI / 2))
                            {
                                Consider(arc.Center + Vector2.FromPolar(arc.Radius, q * Math.PI / 2), SnapModes.Quadrant);
                            }
                        }

                        if (basePoint is { } tangentFrom && (modes & SnapModes.Tangent) != 0)
                        {
                            foreach (var point in TangentPoints(arc, tangentFrom))
                            {
                                Consider(point, SnapModes.Tangent);
                            }
                        }

                        if (basePoint is { } from && !from.IsAlmostEqual(arc.Center))
                        {
                            var foot = arc.Center + (from - arc.Center).Normalized() * arc.Radius;
                            if (arc.ContainsAngle((foot - arc.Center).Angle))
                            {
                                Consider(foot, SnapModes.Perpendicular);
                            }
                        }

                        break;
                    }
                }
            }
        }

        // Il centro di un cerchio si aggancia anche col cursore lontano dal centro ma vicino alla circonferenza.
        if ((modes & SnapModes.Center) != 0 && best is null)
        {
            foreach (var arc in primitives.OfType<ArcPrimitive>())
            {
                if (arc.DistanceTo(cursor) <= aperture)
                {
                    best = new SnapResult(arc.Arc.Center, SnapModes.Center);
                    break;
                }
            }
        }

        if ((modes & SnapModes.Intersection) != 0)
        {
            for (var i = 0; i < primitives.Count; i++)
            {
                for (var j = i + 1; j < primitives.Count; j++)
                {
                    foreach (var point in Intersect(primitives[i], primitives[j]))
                    {
                        Consider(point, SnapModes.Intersection);
                    }
                }
            }
        }

        if (best is null && nearest is { } fallback)
        {
            best = new SnapResult(fallback, SnapModes.Nearest);
        }

        return best;
    }

    /// <summary>Punti di tangenza sull'arco delle rette che passano per <paramref name="from"/>.</summary>
    private static IEnumerable<Vector2> TangentPoints(Arc2D arc, Vector2 from)
    {
        var d = Vector2.Distance(from, arc.Center);
        if (d <= arc.Radius + Tolerance.Default)
        {
            yield break;
        }

        var toFrom = (from - arc.Center).Angle;
        var spread = Math.Acos(arc.Radius / d);
        foreach (var angle in new[] { toFrom + spread, toFrom - spread })
        {
            if (arc.ContainsAngle(angle))
            {
                yield return arc.Center + Vector2.FromPolar(arc.Radius, angle);
            }
        }
    }

    /// <summary>Punto della primitiva più vicino al cursore.</summary>
    private static Vector2 Nearest(Primitive primitive, Vector2 cursor)
    {
        if (primitive is SegmentPrimitive s)
        {
            return s.Segment.ClosestPoint(cursor);
        }

        var arc = ((ArcPrimitive)primitive).Arc;
        var angle = (cursor - arc.Center).Angle;
        if (arc.ContainsAngle(angle))
        {
            return arc.Center + Vector2.FromPolar(arc.Radius, angle);
        }

        return Vector2.Distance(cursor, arc.StartPoint) <= Vector2.Distance(cursor, arc.EndPoint) ? arc.StartPoint : arc.EndPoint;
    }

    public static IReadOnlyList<Vector2> Intersect(Primitive a, Primitive b) => (a, b) switch
    {
        (SegmentPrimitive s1, SegmentPrimitive s2) => Intersections.SegmentSegment(s1.Segment, s2.Segment),
        (SegmentPrimitive s, ArcPrimitive c) => OnArc(Intersections.SegmentCircle(s.Segment, ToCircle(c)), c),
        (ArcPrimitive c, SegmentPrimitive s) => OnArc(Intersections.SegmentCircle(s.Segment, ToCircle(c)), c),
        (ArcPrimitive c1, ArcPrimitive c2) => OnArc(OnArc(Intersections.CircleCircle(ToCircle(c1), ToCircle(c2)), c1), c2),
        _ => [],
    };

    private static Circle2D ToCircle(ArcPrimitive arc) => new(arc.Arc.Center, arc.Arc.Radius);

    private static IReadOnlyList<Vector2> OnArc(IReadOnlyList<Vector2> points, ArcPrimitive arc) =>
        arc.IsFullCircle ? points : [.. points.Where(p => arc.Arc.ContainsAngle((p - arc.Arc.Center).Angle))];
}

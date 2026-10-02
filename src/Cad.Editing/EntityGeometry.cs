using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>Pezzo elementare di un'entità: un segmento oppure un arco di circonferenza.</summary>
public abstract record Primitive
{
    public abstract double DistanceTo(Vector2 p);
}

public sealed record SegmentPrimitive(Segment2D Segment) : Primitive
{
    public override double DistanceTo(Vector2 p) => Segment.DistanceTo(p);
}

public sealed record ArcPrimitive(Arc2D Arc) : Primitive
{
    public bool IsFullCircle => Arc.Sweep >= Math.Tau - Tolerance.Default;

    public override double DistanceTo(Vector2 p)
    {
        var direction = (p - Arc.Center).Angle;
        if (Arc.ContainsAngle(direction))
        {
            return Math.Abs(Vector2.Distance(p, Arc.Center) - Arc.Radius);
        }

        return Math.Min(Vector2.Distance(p, Arc.StartPoint), Vector2.Distance(p, Arc.EndPoint));
    }
}

/// <summary>
/// Scompone le entità in segmenti e archi: la base comune per selezione, snap e intersezioni.
/// </summary>
public static class EntityGeometry
{
    private const int CurveSteps = 64;
    private const int MaxDepth = 16;

    public static IEnumerable<Primitive> Decompose(Entity entity) => Decompose(entity, 0);

    private static IEnumerable<Primitive> Decompose(Entity entity, int depth)
    {
        switch (entity)
        {
            case LineEntity line:
                yield return new SegmentPrimitive(line.Segment);
                break;
            case CircleEntity circle:
                yield return new ArcPrimitive(new Arc2D(circle.Center, circle.Radius, 0, Math.Tau));
                break;
            case ArcEntity arc:
                yield return new ArcPrimitive(arc.Arc);
                break;
            case PolylineEntity polyline:
                for (var i = 0; i < polyline.SegmentCount; i++)
                {
                    var (segment, arc) = polyline.GetSegment(i);
                    yield return arc is { } a ? new ArcPrimitive(a) : new SegmentPrimitive(segment);
                }

                break;
            case EllipseEntity ellipse:
                var previous = ellipse.PointAt(ellipse.StartParameter);
                for (var i = 1; i <= CurveSteps; i++)
                {
                    var next = ellipse.PointAt(ellipse.StartParameter + ellipse.Sweep * i / CurveSteps);
                    yield return new SegmentPrimitive(new Segment2D(previous, next));
                    previous = next;
                }

                break;
            case PolylinePathEntity path:
                for (var i = 1; i < path.Points.Count; i++)
                {
                    yield return new SegmentPrimitive(new Segment2D(path.Points[i - 1], path.Points[i]));
                }

                if (path.IsClosed && path.Points.Count > 2)
                {
                    yield return new SegmentPrimitive(new Segment2D(path.Points[^1], path.Points[0]));
                }

                break;
            case DimensionEntity { Graphics: null } dimension when depth < MaxDepth:
                foreach (var part in dimension.Explode())
                {
                    foreach (var primitive in Decompose(part, depth + 1))
                    {
                        yield return primitive;
                    }
                }

                break;
            case DimensionEntity { Graphics: { } graphics } dimension when depth < MaxDepth:
                foreach (var child in graphics.Entities)
                {
                    foreach (var primitive in Decompose(child.Transformed(dimension.GraphicsTransform), depth + 1))
                    {
                        yield return primitive;
                    }
                }

                break;
            case SolidEntity solid:
                for (var i = 0; i < solid.Corners.Count; i++)
                {
                    yield return new SegmentPrimitive(new Segment2D(solid.Corners[i], solid.Corners[(i + 1) % solid.Corners.Count]));
                }

                break;
            case HatchEntity hatch:
                foreach (var loop in hatch.Loops)
                {
                    foreach (var primitive in Decompose(new PolylineEntity(hatch.Layer, loop, isClosed: true), depth + 1))
                    {
                        yield return primitive;
                    }
                }

                break;
            case InsertEntity insert when depth < MaxDepth:
                foreach (var child in insert.Block.Entities)
                {
                    foreach (var primitive in Decompose(child.Transformed(insert.Transform), depth + 1))
                    {
                        yield return primitive;
                    }
                }

                break;
        }
    }

    /// <summary>Distanza dal punto all'entità; per testi e punti si usa l'ingombro.</summary>
    public static double DistanceTo(Entity entity, Vector2 p) => entity switch
    {
        TextEntity or PointEntity => DistanceToBox(entity.Bounds, p),
        _ => Decompose(entity).Select(primitive => primitive.DistanceTo(p)).DefaultIfEmpty(double.PositiveInfinity).Min(),
    };

    /// <summary>Vero se l'entità tocca il rettangolo (selezione "interseca").</summary>
    public static bool Crosses(Entity entity, BoundingBox window)
    {
        var bounds = entity.Bounds;
        if (!window.Intersects(bounds))
        {
            return false;
        }

        if (window.Contains(bounds) || entity is TextEntity or PointEntity)
        {
            return true;
        }

        var corners = new[] { window.Min, new Vector2(window.Max.X, window.Min.Y), window.Max, new Vector2(window.Min.X, window.Max.Y) };
        var edges = Enumerable.Range(0, 4).Select(i => new Segment2D(corners[i], corners[(i + 1) % 4])).ToArray();
        foreach (var primitive in Decompose(entity))
        {
            foreach (var segment in ToSegments(primitive))
            {
                if (window.Contains(segment.Start) || window.Contains(segment.End) ||
                    edges.Any(edge => Intersections.SegmentSegment(segment, edge).Count > 0))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static IEnumerable<Segment2D> ToSegments(Primitive primitive)
    {
        if (primitive is SegmentPrimitive s)
        {
            yield return s.Segment;
            yield break;
        }

        var arc = ((ArcPrimitive)primitive).Arc;
        var steps = Math.Max(4, (int)Math.Ceiling(arc.Sweep / (Math.PI / 32)));
        var previous = arc.PointAt(0);
        for (var i = 1; i <= steps; i++)
        {
            var next = arc.PointAt((double)i / steps);
            yield return new Segment2D(previous, next);
            previous = next;
        }
    }

    private static double DistanceToBox(BoundingBox box, Vector2 p)
    {
        var dx = Math.Max(Math.Max(box.Min.X - p.X, 0), p.X - box.Max.X);
        var dy = Math.Max(Math.Max(box.Min.Y - p.Y, 0), p.Y - box.Max.Y);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

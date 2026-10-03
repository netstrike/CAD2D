using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>Ricerca dei contorni per TRATTEGGIO.</summary>
public static class Hatching
{
    private const int EllipseSteps = 72;

    /// <summary>Contorno chiuso di un'entità (cerchio, polilinea chiusa, ellisse intera, spline chiusa), o null.</summary>
    public static IReadOnlyList<PolylineVertex>? ClosedLoop(Entity entity)
    {
        switch (entity)
        {
            case CircleEntity circle:
                // Due semicirconferenze: bulge 1 = mezzo giro antiorario.
                return [new(circle.Center + new Vector2(circle.Radius, 0), 1), new(circle.Center - new Vector2(circle.Radius, 0), 1)];

            case PolylineEntity polyline when polyline.Vertices.Count >= 2:
            {
                var vertices = polyline.Vertices;
                if (polyline.IsClosed)
                {
                    return vertices.Count >= 3 || vertices.Any(v => v.Bulge != 0) ? vertices : null;
                }

                // Polilinea aperta che torna al punto di partenza.
                return vertices.Count >= 4 && vertices[0].Position.IsAlmostEqual(vertices[^1].Position, 1e-7)
                    ? vertices.Take(vertices.Count - 1).ToList()
                    : null;
            }

            case EllipseEntity ellipse when ellipse.Sweep >= Math.Tau - 1e-9:
                return [.. Enumerable.Range(0, EllipseSteps).Select(i => new PolylineVertex(ellipse.PointAt(ellipse.StartParameter + Math.Tau * i / EllipseSteps)))];

            case SplineEntity spline when spline.IsClosed || spline.StartPoint.IsAlmostEqual(spline.EndPoint, 1e-7):
                return [.. spline.Flatten().SkipLast(1).Select(p => new PolylineVertex(p))];

            case PolylinePathEntity path when path.IsClosed && path.Points.Count >= 3:
                return [.. path.Points.Select(p => new PolylineVertex(p))];

            default:
                return null;
        }
    }

    /// <summary>
    /// Contorni per un punto interno: il più piccolo contorno chiuso che contiene il punto, più tutti quelli al suo
    /// interno come isole (con la regola pari/dispari un'isola dentro un'isola torna tratteggiata, come lo stile "Normale").
    /// Null se nessun contorno chiuso contiene il punto.
    /// </summary>
    /// <param name="query">Entità il cui ingombro tocca un rettangolo (di solito l'indice spaziale).</param>
    public static List<IReadOnlyList<PolylineVertex>>? FindBoundary(Vector2 point, Func<BoundingBox, IEnumerable<Entity>> query)
    {
        var containing = LoopsOf(query(new BoundingBox(point, point)))
            .Where(l => Contains(l.Polygon, point))
            .OrderBy(l => Math.Abs(Area(l.Polygon)))
            .ToList();
        if (containing.Count == 0)
        {
            return null;
        }

        var outer = containing[0];
        var outerBounds = BoundingBox.FromPoints(outer.Polygon);
        var loops = LoopsOf(query(outerBounds));
        var result = new List<IReadOnlyList<PolylineVertex>> { outer.Loop };
        foreach (var other in loops)
        {
            if (other.Loop.SequenceEqual(outer.Loop) || Math.Abs(Area(other.Polygon)) >= Math.Abs(Area(outer.Polygon)))
            {
                continue;
            }

            if (outerBounds.Contains(BoundingBox.FromPoints(other.Polygon)) && other.Polygon.All(p => Contains(outer.Polygon, p) || OnBoundary(outer.Polygon, p)))
            {
                result.Add(other.Loop);
            }
        }

        return result;
    }

    private static List<(IReadOnlyList<PolylineVertex> Loop, List<Vector2> Polygon)> LoopsOf(IEnumerable<Entity> entities) =>
        [.. entities.Select(ClosedLoop).Where(l => l is not null).Select(l => (l!, Polygon(l!)))];

    /// <summary>Contorno approssimato con una spezzata (archi a passi di 5°).</summary>
    public static List<Vector2> Polygon(IReadOnlyList<PolylineVertex> loop)
    {
        var result = new List<Vector2>();
        for (var i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            result.Add(a.Position);
            if (Arc2D.FromBulge(a.Position, b.Position, a.Bulge) is { } arc)
            {
                var steps = Math.Max(2, (int)Math.Ceiling(arc.Sweep / (Math.PI / 36)));
                for (var k = 1; k < steps; k++)
                {
                    var t = (double)k / steps;
                    result.Add(a.Bulge > 0 ? arc.PointAt(t) : arc.PointAt(1 - t));
                }
            }
        }

        return result;
    }

    public static double Area(IReadOnlyList<Vector2> polygon)
    {
        var area = 0.0;
        for (var i = 0; i < polygon.Count; i++)
        {
            area += Vector2.Cross(polygon[i], polygon[(i + 1) % polygon.Count]);
        }

        return area / 2;
    }

    public static bool Contains(IReadOnlyList<Vector2> polygon, Vector2 p)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if (a.Y > p.Y != b.Y > p.Y && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool OnBoundary(IReadOnlyList<Vector2> polygon, Vector2 p)
    {
        for (var i = 0; i < polygon.Count; i++)
        {
            if (new Segment2D(polygon[i], polygon[(i + 1) % polygon.Count]).DistanceTo(p) <= 1e-7)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Crea il tratteggio con il motivo, la scala e l'angolo (radianti) dati.</summary>
    public static HatchEntity Create(Layer layer, IEnumerable<IReadOnlyList<PolylineVertex>> loops, string pattern, double scale, double angle)
    {
        var solid = pattern.Equals(HatchPatterns.Solid, StringComparison.OrdinalIgnoreCase);
        return new HatchEntity(layer, loops, solid ? HatchPatterns.Solid : pattern.ToUpperInvariant(), solid, solid ? [] : HatchPatterns.Build(pattern, scale, angle))
        {
            PatternScale = scale,
            PatternAngle = angle,
        };
    }
}

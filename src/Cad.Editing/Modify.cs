using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>
/// Algoritmi dei comandi di modifica (taglia, estendi, offset, raccorda, cima, esplodi), separati dai comandi per poterli testare.
/// </summary>
public static class Modify
{
    private const double JoinTolerance = 1e-7;

    // ---------- TAGLIA ----------

    /// <summary>
    /// Toglie la parte di <paramref name="entity"/> che contiene <paramref name="pick"/>, delimitata dalle intersezioni con
    /// <paramref name="boundaries"/>. Restituisce i pezzi che restano (lista vuota: l'entità va cancellata perché non
    /// incontra nessun bordo) oppure null se l'entità non si può tagliare.
    /// </summary>
    public static IReadOnlyList<Entity>? Trim(Entity entity, Vector2 pick, IEnumerable<Primitive> boundaries)
    {
        if (Curve.From(entity) is not { } curve)
        {
            return null;
        }

        var n = curve.MaxParameter;
        var cuts = curve.IntersectionParameters(boundaries);
        var t = curve.Project(pick);

        if (curve.IsClosed)
        {
            if (cuts.Count == 0)
            {
                return [];
            }

            if (cuts.Count == 1)
            {
                return null;
            }

            List<CurvePiece> kept;
            if (t < cuts[0] || t >= cuts[^1])
            {
                kept = curve.Extract(cuts[0], cuts[^1]);
            }
            else
            {
                var i = cuts.FindLastIndex(c => c <= t);
                kept = curve.Extract(cuts[i + 1], cuts[i] + n);
            }

            return Curve.ToEntity(kept, entity) is { } piece ? [piece] : [];
        }

        var interior = cuts.Where(c => c > 1e-9 && c < n - 1e-9).ToList();
        if (interior.Count == 0)
        {
            return [];
        }

        var before = interior.Where(c => c < t).DefaultIfEmpty(0).Max();
        var after = interior.Where(c => c > t).DefaultIfEmpty(n).Min();
        var result = new List<Entity>(2);
        if (before > 0 && Curve.ToEntity(curve.Extract(0, before), entity) is { } first)
        {
            result.Add(first);
        }

        if (after < n && Curve.ToEntity(curve.Extract(after, n), entity) is { } second)
        {
            result.Add(second);
        }

        return result;
    }

    // ---------- ESTENDI ----------

    /// <summary>
    /// Allunga l'estremo di <paramref name="entity"/> più vicino a <paramref name="pick"/> fino al primo bordo che incontra.
    /// Restituisce null se non c'è nessun bordo sulla prolunga o se l'entità non si può estendere.
    /// </summary>
    public static Entity? Extend(Entity entity, Vector2 pick, IEnumerable<Primitive> boundaries)
    {
        if (Curve.From(entity) is not { IsClosed: false } curve)
        {
            return null;
        }

        var pieces = curve.Pieces.ToList();
        var atStart = curve.Project(pick) < curve.MaxParameter / 2;
        var index = atStart ? 0 : pieces.Count - 1;
        var piece = atStart ? pieces[0].Reversed() : pieces[^1];

        CurvePiece? best = null;
        var bestAmount = double.PositiveInfinity;
        foreach (var boundary in boundaries)
        {
            foreach (var point in ExtensionHits(piece, boundary))
            {
                if (piece.IsArc)
                {
                    // Angolo da percorrere oltre la fine, nel verso dell'arco.
                    var endAngle = piece.StartAngle + piece.Sweep;
                    var angle = (point - piece.Center).Angle;
                    var extra = piece.Sweep >= 0 ? Arc2D.NormalizeAngle(angle - endAngle) : Arc2D.NormalizeAngle(endAngle - angle);
                    if (extra > 1e-9 && extra < Math.Tau - Math.Abs(piece.Sweep) - 1e-9 && extra < bestAmount)
                    {
                        bestAmount = extra;
                        best = CurvePiece.Arc(piece.Center, piece.Radius, piece.StartAngle, piece.Sweep + Math.Sign(piece.Sweep) * extra);
                    }
                }
                else
                {
                    var u = piece.Project(point);
                    if (u > 1 + 1e-9 && u < bestAmount)
                    {
                        bestAmount = u;
                        best = CurvePiece.Line(piece.Start, point);
                    }
                }
            }
        }

        if (best is not { } extended)
        {
            return null;
        }

        pieces[index] = atStart ? extended.Reversed() : extended;
        return Curve.ToEntity(pieces, entity);
    }

    /// <summary>Punti in cui la prolunga del tratto (retta o circonferenza intera) incontra la primitiva di bordo.</summary>
    private static IEnumerable<Vector2> ExtensionHits(CurvePiece piece, Primitive boundary)
    {
        if (!piece.IsArc)
        {
            var line = new Segment2D(piece.Start, piece.End);
            return boundary switch
            {
                SegmentPrimitive s => Intersections.SegmentSegment(line, s.Segment, extend: true)
                    .Where(p => s.Segment.DistanceTo(p) <= JoinTolerance),
                ArcPrimitive a => Intersections.SegmentCircle(line, new Circle2D(a.Arc.Center, a.Arc.Radius), extend: true)
                    .Where(p => a.IsFullCircle || a.Arc.ContainsAngle((p - a.Arc.Center).Angle)),
                _ => [],
            };
        }

        var circle = new ArcPrimitive(new Arc2D(piece.Center, piece.Radius, 0, Math.Tau));
        return SnapEngine.Intersect(circle, boundary);
    }

    /// <summary>Primitive di tutte le entità date tranne <paramref name="exclude"/>: i bordi di TAGLIA ed ESTENDI.</summary>
    public static IEnumerable<Primitive> Boundaries(IEnumerable<Entity> entities, Entity exclude) =>
        entities.Where(e => !ReferenceEquals(e, exclude)).SelectMany(EntityGeometry.Decompose);

    // ---------- OFFSET ----------

    /// <summary>
    /// Copia parallela di <paramref name="entity"/> a distanza <paramref name="distance"/>, dalla parte di <paramref name="side"/>.
    /// Restituisce null se l'entità non si può spostare in parallelo o se la distanza fa sparire un arco.
    /// </summary>
    public static Entity? Offset(Entity entity, double distance, Vector2 side)
    {
        if (distance <= Tolerance.Default)
        {
            return null;
        }

        if (entity is CircleEntity circle)
        {
            var radius = Vector2.Distance(side, circle.Center) < circle.Radius ? circle.Radius - distance : circle.Radius + distance;
            return radius > Tolerance.Default ? WithStyle(new CircleEntity(circle.Layer, circle.Center, radius), entity) : null;
        }

        if (Curve.From(entity) is not { } curve)
        {
            return null;
        }

        // A sinistra o a destra del verso di percorrenza, nel tratto più vicino al punto indicato.
        var t = curve.Project(side);
        var index = Math.Min((int)Math.Floor(t), curve.Pieces.Count - 1);
        var u = t - index;
        var piece = curve.Pieces[index];
        var sign = Math.Sign(Vector2.Cross(piece.TangentAt(u), side - piece.PointAt(u)));
        if (sign == 0)
        {
            return null;
        }

        var offset = OffsetPieces(curve.Pieces, curve.IsClosed, distance * sign);
        return offset is null ? null : Curve.ToEntity(offset, entity, curve.IsClosed);
    }

    /// <summary>Sposta ogni tratto a sinistra di <paramref name="left"/> (negativo = a destra) e ricuce i giunti.</summary>
    public static List<CurvePiece>? OffsetPieces(IReadOnlyList<CurvePiece> pieces, bool closed, double left)
    {
        var moved = new List<CurvePiece>(pieces.Count);
        foreach (var p in pieces)
        {
            if (p.IsArc)
            {
                // Antiorario: la sinistra è verso il centro.
                var radius = p.Sweep >= 0 ? p.Radius - left : p.Radius + left;
                if (radius <= Tolerance.Default)
                {
                    return null;
                }

                moved.Add(CurvePiece.Arc(p.Center, radius, p.StartAngle, p.Sweep));
            }
            else
            {
                var normal = (p.End - p.Start).Normalized().Perpendicular() * left;
                moved.Add(CurvePiece.Line(p.Start + normal, p.End + normal));
            }
        }

        var result = new List<CurvePiece>(moved.Count * 2);
        var joins = closed ? moved.Count : moved.Count - 1;
        var current = moved.ToArray();
        var bridges = new CurvePiece?[moved.Count];
        for (var i = 0; i < joins; i++)
        {
            var j = (i + 1) % moved.Count;
            var a = current[i];
            var b = current[j];
            if (a.End.IsAlmostEqual(b.Start, JoinTolerance))
            {
                continue;
            }

            var vertex = pieces[i].End;
            var hit = ExtendedIntersections(a, b).OrderBy(p => Vector2.Distance(p, vertex)).Cast<Vector2?>().FirstOrDefault();
            if (hit is { } corner)
            {
                current[i] = WithEnd(a, corner);
                current[j] = WithStart(current[j], corner);
            }
            else
            {
                bridges[i] = CurvePiece.Line(a.End, b.Start);
            }
        }

        for (var i = 0; i < current.Length; i++)
        {
            result.Add(current[i]);
            if (bridges[i] is { } bridge)
            {
                result.Add(bridge);
            }
        }

        return result;
    }

    /// <summary>Intersezioni tra le prolunghe di due tratti (rette e circonferenze intere).</summary>
    private static IReadOnlyList<Vector2> ExtendedIntersections(CurvePiece a, CurvePiece b) => (a.IsArc, b.IsArc) switch
    {
        (false, false) => Intersections.SegmentSegment(new Segment2D(a.Start, a.End), new Segment2D(b.Start, b.End), extend: true),
        (false, true) => Intersections.SegmentCircle(new Segment2D(a.Start, a.End), new Circle2D(b.Center, b.Radius), extend: true),
        (true, false) => Intersections.SegmentCircle(new Segment2D(b.Start, b.End), new Circle2D(a.Center, a.Radius), extend: true),
        _ => Intersections.CircleCircle(new Circle2D(a.Center, a.Radius), new Circle2D(b.Center, b.Radius)),
    };

    private static CurvePiece WithEnd(CurvePiece p, Vector2 end)
    {
        if (!p.IsArc)
        {
            return CurvePiece.Line(p.Start, end);
        }

        var delta = SignedAngle(p.StartAngle + p.Sweep, (end - p.Center).Angle);
        return CurvePiece.Arc(p.Center, p.Radius, p.StartAngle, p.Sweep + delta);
    }

    private static CurvePiece WithStart(CurvePiece p, Vector2 start)
    {
        if (!p.IsArc)
        {
            return CurvePiece.Line(start, p.End);
        }

        var delta = SignedAngle(p.StartAngle, (start - p.Center).Angle);
        return CurvePiece.Arc(p.Center, p.Radius, p.StartAngle + delta, p.Sweep - delta);
    }

    /// <summary>Differenza tra due angoli in (-π, π].</summary>
    private static double SignedAngle(double from, double to)
    {
        var d = Arc2D.NormalizeAngle(to - from);
        return d > Math.PI ? d - Math.Tau : d;
    }

    // ---------- RACCORDA e CIMA ----------

    /// <summary>Risultato di un raccordo o di una cima tra due linee: le linee accorciate o allungate e il tratto di unione.</summary>
    public sealed record CornerResult(LineEntity First, LineEntity Second, Entity? Joint);

    /// <summary>
    /// Raccorda due linee con un arco di raggio <paramref name="radius"/> (0 = le porta a incontrarsi nello spigolo).
    /// I punti cliccati indicano quale parte di ciascuna linea tenere. Restituisce un messaggio d'errore se non si può.
    /// </summary>
    public static (CornerResult? Result, string? Error) Fillet(LineEntity first, Vector2 pick1, LineEntity second, Vector2 pick2, double radius)
    {
        var corner = Corner(first, pick1, second, pick2);
        if (corner.Error is not null)
        {
            return (null, corner.Error);
        }

        var (p, d1, d2, keep1, keep2) = corner.Value;
        if (radius <= Tolerance.Default)
        {
            return (new CornerResult(Trimmed(first, keep1, p), Trimmed(second, keep2, p), null), null);
        }

        var theta = Math.Acos(Math.Clamp(Vector2.Dot(d1, d2), -1, 1));
        var along = radius / Math.Tan(theta / 2);
        if (along > Vector2.Dot(keep1 - p, d1) + 1e-9 || along > Vector2.Dot(keep2 - p, d2) + 1e-9)
        {
            return (null, "Il raggio è troppo grande per queste linee.");
        }

        var t1 = p + d1 * along;
        var t2 = p + d2 * along;
        var bisector = (d1 + d2).Normalized();
        var center = p + bisector * (radius / Math.Sin(theta / 2));
        var arc = Arc2D.FromThreePoints(t1, center - bisector * radius, t2);
        if (arc is not { } a)
        {
            return (null, "Impossibile costruire il raccordo.");
        }

        var joint = WithStyle(new ArcEntity(first.Layer, a.Center, a.Radius, a.StartAngle, a.EndAngle), first);
        return (new CornerResult(Trimmed(first, keep1, t1), Trimmed(second, keep2, t2), joint), null);
    }

    /// <summary>Smussa lo spigolo tra due linee tagliandole alle distanze date dallo spigolo e unendole con una linea.</summary>
    public static (CornerResult? Result, string? Error) Chamfer(LineEntity first, Vector2 pick1, LineEntity second, Vector2 pick2, double distance1, double distance2)
    {
        var corner = Corner(first, pick1, second, pick2);
        if (corner.Error is not null)
        {
            return (null, corner.Error);
        }

        var (p, d1, d2, keep1, keep2) = corner.Value;
        if (distance1 <= Tolerance.Default && distance2 <= Tolerance.Default)
        {
            return (new CornerResult(Trimmed(first, keep1, p), Trimmed(second, keep2, p), null), null);
        }

        if (distance1 > Vector2.Dot(keep1 - p, d1) + 1e-9 || distance2 > Vector2.Dot(keep2 - p, d2) + 1e-9)
        {
            return (null, "Le distanze sono troppo grandi per queste linee.");
        }

        var a1 = p + d1 * distance1;
        var a2 = p + d2 * distance2;
        var joint = WithStyle(new LineEntity(first.Layer, a1, a2), first);
        return (new CornerResult(Trimmed(first, keep1, a1), Trimmed(second, keep2, a2), joint), null);
    }

    private readonly record struct CornerGeometry(Vector2 Corner, Vector2 Direction1, Vector2 Direction2, Vector2 Keep1, Vector2 Keep2);

    /// <summary>Spigolo tra le rette delle due linee, direzioni verso le parti da tenere e loro estremi lontani.</summary>
    private static (CornerGeometry Value, string? Error) Corner(LineEntity first, Vector2 pick1, LineEntity second, Vector2 pick2)
    {
        if (ReferenceEquals(first, second))
        {
            return (default, "Servono due linee diverse.");
        }

        var hits = Intersections.SegmentSegment(first.Segment, second.Segment, extend: true);
        if (hits.Count == 0)
        {
            return (default, "Le linee sono parallele.");
        }

        var p = hits[0];
        var d1 = Side(first, pick1, p);
        var d2 = Side(second, pick2, p);
        var keep1 = Vector2.Dot(first.Start - p, d1) >= Vector2.Dot(first.End - p, d1) ? first.Start : first.End;
        var keep2 = Vector2.Dot(second.Start - p, d2) >= Vector2.Dot(second.End - p, d2) ? second.Start : second.End;
        return (new CornerGeometry(p, d1, d2, keep1, keep2), null);
    }

    /// <summary>Direzione dallo spigolo verso la parte cliccata della linea.</summary>
    private static Vector2 Side(LineEntity line, Vector2 pick, Vector2 corner)
    {
        var direction = (line.End - line.Start).Normalized();
        var projected = Vector2.Dot(line.Segment.ClosestPoint(pick) - corner, direction);
        if (Tolerance.IsZero(projected, 1e-9))
        {
            // Clic proprio sullo spigolo: si tiene la parte più lunga.
            projected = Vector2.Dot(line.Segment.Midpoint - corner, direction);
        }

        return projected >= 0 ? direction : -direction;
    }

    /// <summary>La linea ridotta al tratto da <paramref name="keep"/> a <paramref name="end"/>, con layer e colore originali.</summary>
    private static LineEntity Trimmed(LineEntity line, Vector2 keep, Vector2 end) =>
        (LineEntity)(keep == line.Start ? line.WithGripMoved(2, end) : line.WithGripMoved(0, end));

    /// <summary>
    /// Raccorda tutti gli spigoli tra tratti dritti di una polilinea. Restituisce la nuova polilinea e il numero di spigoli
    /// saltati perché il raggio non ci stava.
    /// </summary>
    public static (PolylineEntity Result, int Filleted, int Skipped) FilletPolyline(PolylineEntity polyline, double radius)
    {
        var vertices = polyline.Vertices;
        var n = vertices.Count;
        var segments = polyline.SegmentCount;
        var cutStart = new double[n];
        var cutEnd = new double[n];
        var corners = new (Vector2 T1, Vector2 T2, double Bulge)?[n];
        var filleted = 0;
        var skipped = 0;

        for (var v = 0; v < n; v++)
        {
            var hasPrevious = polyline.IsClosed || v > 0;
            var hasNext = polyline.IsClosed ? true : v < n - 1;
            if (!hasPrevious || !hasNext || segments < 2)
            {
                continue;
            }

            var previous = (v - 1 + n) % n;
            if (vertices[previous].Bulge != 0 || vertices[v].Bulge != 0)
            {
                continue;
            }

            var p = vertices[v].Position;
            var a = vertices[previous].Position;
            var b = vertices[(v + 1) % n].Position;
            var lengthIn = Vector2.Distance(a, p);
            var lengthOut = Vector2.Distance(p, b);
            if (lengthIn <= Tolerance.Default || lengthOut <= Tolerance.Default)
            {
                continue;
            }

            var d1 = (a - p) / lengthIn;
            var d2 = (b - p) / lengthOut;
            var theta = Math.Acos(Math.Clamp(Vector2.Dot(d1, d2), -1, 1));
            if (theta < 1e-6 || Math.PI - theta < 1e-6)
            {
                continue;
            }

            var along = radius / Math.Tan(theta / 2);
            if (along > lengthIn - cutStart[previous] + 1e-9 || along > lengthOut - cutEnd[v] + 1e-9)
            {
                skipped++;
                continue;
            }

            cutEnd[previous] += along;
            cutStart[v] += along;
            // Svolta a sinistra (antioraria) = bulge positivo; l'arco copre π - θ.
            var turn = Math.Sign(Vector2.Cross(p - a, b - p));
            corners[v] = (p + d1 * along, p + d2 * along, turn * Math.Tan((Math.PI - theta) / 4));
            filleted++;
        }

        var result = new List<PolylineVertex>(n + filleted);
        for (var v = 0; v < n; v++)
        {
            if (corners[v] is { } c)
            {
                result.Add(new PolylineVertex(c.T1, c.Bulge));
                result.Add(new PolylineVertex(c.T2, vertices[v].Bulge));
            }
            else
            {
                result.Add(vertices[v]);
            }
        }

        var polylineResult = new PolylineEntity(polyline.Layer, result, polyline.IsClosed) { Color = polyline.Color };
        return (polylineResult, filleted, skipped);
    }

    // ---------- ESPLODI ----------

    /// <summary>
    /// Scompone un blocco nelle sue entità (il layer "0" e il colore DaBlocco prendono quelli dell'inserimento)
    /// o una polilinea in linee e archi. Restituisce null per le entità che non si scompongono.
    /// </summary>
    public static IReadOnlyList<Entity>? Explode(Entity entity)
    {
        switch (entity)
        {
            case InsertEntity insert:
            {
                var result = new List<Entity>();
                foreach (var child in insert.Block.Entities)
                {
                    var exploded = child.Transformed(insert.Transform);
                    if (child.Layer.Name == Layer.DefaultName)
                    {
                        exploded.Layer = insert.Layer;
                    }

                    if (child.Color.Source == ColorSource.ByBlock)
                    {
                        exploded.Color = insert.Color;
                    }

                    result.Add(exploded);
                }

                // Gli attributi diventano testi semplici: non devono derivare dall'ATTRIB originale, che vive solo dentro un inserimento.
                result.AddRange(insert.Attributes.Select(a => a is TextEntity text ? CopyText(text) : a.Transformed(Matrix2D.Identity)));
                return result;
            }

            case PolylineEntity polyline when Curve.From(polyline) is { } curve:
                return [.. curve.Pieces.Select(p => Curve.ToEntity([p], new LineEntity(polyline.Layer, p.Start, p.End) { Color = polyline.Color })!)];

            default:
                return null;
        }
    }

    private static TextEntity CopyText(TextEntity text) => new(text.Layer, text.Position, text.Height, text.Value)
    {
        Color = text.Color,
        Rotation = text.Rotation,
        WidthFactor = text.WidthFactor,
        LineSpacing = text.LineSpacing,
        HorizontalAlignment = text.HorizontalAlignment,
        VerticalAlignment = text.VerticalAlignment,
    };

    private static T WithStyle<T>(T entity, Entity template) where T : Entity
    {
        entity.Layer = template.Layer;
        entity.Color = template.Color;
        return entity;
    }
}

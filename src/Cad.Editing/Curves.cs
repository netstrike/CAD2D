using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>
/// Tratto orientato di una curva: un segmento da <see cref="Start"/> a <see cref="End"/>, oppure un arco con
/// <see cref="Sweep"/> con segno (positivo = antiorario). Base di TAGLIA, ESTENDI e OFFSET.
/// </summary>
public readonly record struct CurvePiece(Vector2 Start, Vector2 End, Vector2 Center, double Radius, double StartAngle, double Sweep, bool IsArc)
{
    public static CurvePiece Line(Vector2 start, Vector2 end) => new(start, end, default, 0, 0, 0, false);

    public static CurvePiece Arc(Vector2 center, double radius, double startAngle, double sweep) => new(
        center + Vector2.FromPolar(radius, startAngle),
        center + Vector2.FromPolar(radius, startAngle + sweep),
        center, radius, startAngle, sweep, true);

    public double Length => IsArc ? Math.Abs(Sweep) * Radius : Vector2.Distance(Start, End);

    public Vector2 PointAt(double u) => IsArc
        ? Center + Vector2.FromPolar(Radius, StartAngle + Sweep * u)
        : Vector2.Lerp(Start, End, u);

    /// <summary>Direzione di percorrenza (non normalizzata per i segmenti) nel punto di parametro u.</summary>
    public Vector2 TangentAt(double u) => IsArc
        ? Vector2.FromPolar(1, StartAngle + Sweep * u).Perpendicular() * Math.Sign(Sweep)
        : End - Start;

    /// <summary>Parametro (non limitato a [0, 1] per i segmenti) del punto della curva più vicino a <paramref name="p"/>.</summary>
    public double Project(Vector2 p)
    {
        if (!IsArc)
        {
            return new Segment2D(Start, End).Project(p);
        }

        // Angolo percorso dall'inizio nel verso dell'arco, in [0, 2π).
        var angle = (p - Center).Angle;
        var travelled = Sweep >= 0 ? Arc2D.NormalizeAngle(angle - StartAngle) : Arc2D.NormalizeAngle(StartAngle - angle);
        var u = travelled / Math.Abs(Sweep);
        if (u <= 1)
        {
            return u;
        }

        // Fuori dall'arco: si sceglie l'estremo più vicino lungo la circonferenza.
        var beyondEnd = travelled - Math.Abs(Sweep);
        var beforeStart = Math.Tau - travelled;
        return beyondEnd < beforeStart ? u : -beforeStart / Math.Abs(Sweep);
    }

    /// <summary>Parte del tratto tra i parametri u0 e u1.</summary>
    public CurvePiece Sub(double u0, double u1) => IsArc
        ? Arc(Center, Radius, StartAngle + Sweep * u0, Sweep * (u1 - u0))
        : Line(PointAt(u0), PointAt(u1));

    public CurvePiece Reversed() => IsArc ? Arc(Center, Radius, StartAngle + Sweep, -Sweep) : Line(End, Start);

    /// <summary>Primitiva non orientata per intersezioni e snap.</summary>
    public Primitive ToPrimitive() => IsArc
        ? new ArcPrimitive(Sweep >= 0
            ? new Arc2D(Center, Radius, StartAngle, StartAngle + Sweep)
            : new Arc2D(Center, Radius, StartAngle + Sweep, StartAngle))
        : new SegmentPrimitive(new Segment2D(Start, End));

    /// <summary>Bulge del vertice iniziale di una polilinea che percorre questo tratto.</summary>
    public double Bulge => IsArc ? Math.Tan(Sweep / 4) : 0;
}

/// <summary>
/// Curva come sequenza di tratti, con un parametro globale: il tratto k copre [k, k+1].
/// Ricavata da linee, archi, cerchi e polilinee; si riconverte in entità dopo i tagli.
/// </summary>
public sealed class Curve
{
    private Curve(IReadOnlyList<CurvePiece> pieces, bool isClosed, bool isCircle)
    {
        Pieces = pieces;
        IsClosed = isClosed;
        IsCircle = isCircle;
    }

    public IReadOnlyList<CurvePiece> Pieces { get; }
    public bool IsClosed { get; }

    /// <summary>Cerchio completo: un solo tratto da 0 a 2π.</summary>
    public bool IsCircle { get; }

    public double MaxParameter => Pieces.Count;

    public static Curve? From(Entity entity) => entity switch
    {
        LineEntity line => new Curve([CurvePiece.Line(line.Start, line.End)], false, false),
        CircleEntity circle => new Curve([CurvePiece.Arc(circle.Center, circle.Radius, 0, Math.Tau)], true, true),
        ArcEntity arc => new Curve([CurvePiece.Arc(arc.Center, arc.Radius, arc.StartAngle, arc.Arc.Sweep)], false, false),
        PolylineEntity { SegmentCount: > 0 } polyline => new Curve(PolylinePieces(polyline), polyline.IsClosed, false),
        _ => null,
    };

    private static List<CurvePiece> PolylinePieces(PolylineEntity polyline)
    {
        var pieces = new List<CurvePiece>(polyline.SegmentCount);
        for (var i = 0; i < polyline.SegmentCount; i++)
        {
            var a = polyline.Vertices[i];
            var b = polyline.Vertices[(i + 1) % polyline.Vertices.Count];
            if (Arc2D.FromBulge(a.Position, b.Position, a.Bulge) is { } arc)
            {
                var sweep = a.Bulge > 0 ? arc.Sweep : -arc.Sweep;
                var start = a.Bulge > 0 ? arc.StartAngle : arc.EndAngle;
                pieces.Add(CurvePiece.Arc(arc.Center, arc.Radius, start, sweep));
            }
            else
            {
                pieces.Add(CurvePiece.Line(a.Position, b.Position));
            }
        }

        return pieces;
    }

    public Vector2 PointAt(double t)
    {
        var n = Pieces.Count;
        if (IsClosed)
        {
            t -= Math.Floor(t / n) * n;
        }

        var index = Math.Clamp((int)Math.Floor(t), 0, n - 1);
        return Pieces[index].PointAt(t - index);
    }

    /// <summary>Parametro globale del punto della curva più vicino a <paramref name="p"/>.</summary>
    public double Project(Vector2 p)
    {
        var best = 0.0;
        var bestDistance = double.PositiveInfinity;
        for (var k = 0; k < Pieces.Count; k++)
        {
            var u = Math.Clamp(Pieces[k].Project(p), 0, 1);
            var distance = Vector2.Distance(Pieces[k].PointAt(u), p);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = k + u;
            }
        }

        return best;
    }

    /// <summary>Parametri dei punti in cui la curva incontra le primitive date, ordinati e senza doppioni.</summary>
    public List<double> IntersectionParameters(IEnumerable<Primitive> others)
    {
        var result = new List<double>();
        var otherList = others.ToList();
        for (var k = 0; k < Pieces.Count; k++)
        {
            var mine = Pieces[k].ToPrimitive();
            foreach (var other in otherList)
            {
                foreach (var point in SnapEngine.Intersect(mine, other))
                {
                    result.Add(k + Math.Clamp(Pieces[k].Project(point), 0, 1));
                }
            }
        }

        // Due tratti consecutivi dello stesso oggetto toccano il bordo nello stesso vertice: un solo punto.
        result.Sort();
        var unique = new List<double>(result.Count);
        foreach (var t in result)
        {
            if (unique.Count == 0 || !PointAt(t).IsAlmostEqual(PointAt(unique[^1]), 1e-7))
            {
                unique.Add(t);
            }
        }

        if (IsClosed && unique.Count > 1 && PointAt(unique[0]).IsAlmostEqual(PointAt(unique[^1]), 1e-7))
        {
            unique.RemoveAt(unique.Count - 1);
        }

        return unique;
    }

    /// <summary>Tratti tra i parametri t0 e t1 (t1 &gt; t0; per le curve chiuse t1 può superare la fine e ricominciare).</summary>
    public List<CurvePiece> Extract(double t0, double t1)
    {
        var result = new List<CurvePiece>();
        var n = Pieces.Count;
        var t = t0;
        var lastSource = -1;
        while (t1 - t > 1e-12)
        {
            var index = (int)Math.Floor(t + 1e-12);
            var u0 = t - index;
            var stop = Math.Min(t1, index + 1);
            var u1 = stop - index;
            var source = ((index % n) + n) % n;
            var piece = Pieces[source];
            if (u1 - u0 > 1e-12)
            {
                var sub = piece.Sub(u0, u1);
                if (sub.Length > Tolerance.Default)
                {
                    // Su una curva chiusa si può ripassare dallo stesso tratto (fine e inizio): i due pezzi tornano uno solo.
                    if (result.Count > 0 && lastSource == source && sub.IsArc && result[^1].IsArc)
                    {
                        var previous = result[^1];
                        result[^1] = CurvePiece.Arc(previous.Center, previous.Radius, previous.StartAngle, previous.Sweep + sub.Sweep);
                    }
                    else if (result.Count > 0 && lastSource == source && !sub.IsArc && !result[^1].IsArc)
                    {
                        result[^1] = CurvePiece.Line(result[^1].Start, sub.End);
                    }
                    else
                    {
                        result.Add(sub);
                    }

                    lastSource = source;
                }
            }

            t = stop;
        }

        return result;
    }

    /// <summary>Converte dei tratti contigui in un'entità: linea, arco o polilinea.</summary>
    public static Entity? ToEntity(IReadOnlyList<CurvePiece> pieces, Entity template, bool closed = false)
    {
        if (pieces.Count == 0)
        {
            return null;
        }

        Entity result;
        if (pieces.Count == 1 && !closed && template is not PolylineEntity)
        {
            var p = pieces[0];
            if (p.IsArc)
            {
                var start = p.Sweep >= 0 ? p.StartAngle : p.StartAngle + p.Sweep;
                result = new ArcEntity(template.Layer, p.Center, p.Radius, Arc2D.NormalizeAngle(start), Arc2D.NormalizeAngle(start + Math.Abs(p.Sweep)));
            }
            else
            {
                result = new LineEntity(template.Layer, p.Start, p.End);
            }
        }
        else
        {
            var vertices = pieces.Select(p => new PolylineVertex(p.Start, p.Bulge)).ToList();
            if (!closed)
            {
                vertices.Add(new PolylineVertex(pieces[^1].End));
            }

            result = new PolylineEntity(template.Layer, vertices, closed);
        }

        result.Color = template.Color;
        return result;
    }
}

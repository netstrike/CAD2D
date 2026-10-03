using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>ELLISSE, SPLINE, POLIGONO e PUNTO, con le stesse richieste di DraftSight.</summary>
public static class ShapeCommands
{
    public const int MinSides = 3;
    public const int MaxSides = 1024;

    /// <summary>Numero di lati proposto da POLIGONO: l'ultimo usato.</summary>
    private static int _sides = 4;

    /// <summary>Inscritto (vertici sul cerchio) o circoscritto (lati tangenti): l'ultima scelta.</summary>
    private static bool _inscribed = true;

    internal static void Register(Editor editor)
    {
        editor.RegisterCommand("ELLISSE", Ellipse, "EL", "ELLIPSE");
        editor.RegisterCommand("SPLINE", Spline, "SPL");
        editor.RegisterCommand("POLIGONO", Polygon, "POL", "POLYGON");
        editor.RegisterCommand("PUNTO", Point, "PO", "POINT");
    }

    // ---------- ELLISSE ----------

    private static async Task Ellipse(Editor ed)
    {
        var arc = false;
        var first = await ed.GetPointAsync("Estremo dell'asse:", null, null, "Arco", "Centro");
        if (first.Status == PromptStatus.Keyword && first.Keyword == "Arco")
        {
            arc = true;
            first = await ed.GetPointAsync("Estremo dell'asse dell'arco ellittico:", null, null, "Centro");
        }

        Vector2 center, axis;
        if (first.Status == PromptStatus.Keyword && first.Keyword == "Centro")
        {
            var c = await ed.GetPointAsync("Centro:");
            if (!c.IsOk)
            {
                return;
            }

            var end = await ed.GetPointAsync("Estremo dell'asse:", c.Point, p => [new LineEntity(ed.CurrentLayer, c.Point, p)]);
            if (!end.IsOk)
            {
                return;
            }

            center = c.Point;
            axis = end.Point - center;
        }
        else if (first.IsOk)
        {
            var a = first.Point;
            var end = await ed.GetPointAsync("Altro estremo dell'asse:", a, p => [new LineEntity(ed.CurrentLayer, a, p)]);
            if (!end.IsOk)
            {
                return;
            }

            center = Vector2.Lerp(a, end.Point, 0.5);
            axis = end.Point - center;
        }
        else
        {
            return;
        }

        if (axis.Length <= Tolerance.Default)
        {
            ed.Write("L'asse ha lunghezza nulla.");
            return;
        }

        var other = await ed.GetDistanceAsync("Distanza dall'altro asse:", center, p => Preview(ed, center, axis, Vector2.Distance(center, p)));
        if (!other.IsOk)
        {
            return;
        }

        if (other.Value <= Tolerance.Default)
        {
            ed.Write("Il secondo semiasse deve essere maggiore di zero.");
            return;
        }

        var full = Full(ed.CurrentLayer, center, axis, other.Value);
        if (!arc)
        {
            ed.Document.Edit("ELLISSE", e => e.Add(ed.Styled(full)));
            return;
        }

        // Gli angoli sono direzioni dal centro: si convertono nei parametri dell'ellisse.
        var start = await ed.GetAngleAsync("Angolo iniziale:", center, p => [new LineEntity(ed.CurrentLayer, center, p), full]);
        if (!start.IsOk)
        {
            return;
        }

        var t0 = ParameterAt(full, start.Value);
        var end2 = await ed.GetAngleAsync("Angolo finale:", center, p => [Partial(full, t0, ParameterAt(full, (p - center).Angle))]);
        if (!end2.IsOk)
        {
            return;
        }

        var t1 = ParameterAt(full, end2.Value);
        if (Math.Abs(Arc2D.NormalizeAngle(t1 - t0)) <= Tolerance.Default)
        {
            ed.Write("Angolo iniziale e finale coincidono: si disegna l'ellisse intera.");
            ed.Document.Edit("ELLISSE", e => e.Add(ed.Styled(full)));
            return;
        }

        ed.Document.Edit("ELLISSE", e => e.Add(ed.Styled(Partial(full, t0, t1))));
    }

    private static IEnumerable<Entity> Preview(Editor ed, Vector2 center, Vector2 axis, double other) =>
        other > Tolerance.Default ? [Full(ed.CurrentLayer, center, axis, other)] : [new LineEntity(ed.CurrentLayer, center - axis, center + axis)];

    /// <summary>
    /// Ellisse completa da un semiasse (vettore) e la lunghezza dell'altro. Come nei DXF l'asse maggiore è il più
    /// lungo e il minore sta a 90° in senso antiorario.
    /// </summary>
    public static EllipseEntity Full(Layer layer, Vector2 center, Vector2 axis, double other)
    {
        var length = axis.Length;
        var major = other > length ? axis.Perpendicular().Normalized() * other : axis;
        var minor = major.Perpendicular().Normalized() * Math.Min(length, other);
        return new EllipseEntity(layer, center, major, minor, 0, Math.Tau);
    }

    /// <summary>Parametro t del punto dell'ellisse nella direzione <paramref name="angle"/> vista dal centro.</summary>
    public static double ParameterAt(EllipseEntity ellipse, double angle)
    {
        var local = angle - ellipse.MajorAxis.Angle;
        var t = Math.Atan2(Math.Sin(local) / ellipse.MinorAxis.Length, Math.Cos(local) / ellipse.MajorAxis.Length);
        return Arc2D.NormalizeAngle(t);
    }

    private static EllipseEntity Partial(EllipseEntity full, double t0, double t1) =>
        new(full.Layer, full.Center, full.MajorAxis, full.MinorAxis, t0, t1 > t0 ? t1 : t1 + Math.Tau);

    // ---------- SPLINE ----------

    private static async Task Spline(Editor ed)
    {
        var first = await ed.GetPointAsync("Primo punto:");
        if (!first.IsOk)
        {
            return;
        }

        var points = new List<Vector2> { first.Point };
        var closed = false;
        while (true)
        {
            var keywords = points.Count >= 3 ? new[] { "Chiudi", "Annulla" } : points.Count >= 2 ? new[] { "Annulla" } : [];
            var next = await ed.GetPointAsync(
                points.Count >= 2 ? "Punto successivo (Invio per finire):" : "Punto successivo:",
                points[^1],
                p => PreviewSpline(ed.CurrentLayer, [.. points, p]),
                keywords);
            if (next.Status == PromptStatus.Keyword && next.Keyword == "Chiudi")
            {
                closed = true;
                break;
            }

            if (next.Status == PromptStatus.Keyword && next.Keyword == "Annulla")
            {
                points.RemoveAt(points.Count - 1);
                continue;
            }

            if (next.Status == PromptStatus.Cancel)
            {
                return;
            }

            if (!next.IsOk)
            {
                break;
            }

            if (!next.Point.IsAlmostEqual(points[^1]))
            {
                points.Add(next.Point);
            }
        }

        if (points.Count < 2)
        {
            return;
        }

        ed.Document.Edit("SPLINE", e => e.Add(ed.Styled(SplineEntity.Through(ed.CurrentLayer, points, closed))));
    }

    private static IEnumerable<Entity> PreviewSpline(Layer layer, List<Vector2> points)
    {
        if (points.Count < 2 || points[^1].IsAlmostEqual(points[^2]))
        {
            return [];
        }

        return [SplineEntity.Through(layer, points, closed: false)];
    }

    // ---------- POLIGONO ----------

    private static async Task Polygon(Editor ed)
    {
        var sides = _sides;
        while (true)
        {
            var count = await ed.GetNumberAsync($"Numero di lati <{sides}>:");
            if (count.Status == PromptStatus.None)
            {
                break;
            }

            if (!count.IsOk)
            {
                return;
            }

            if (count.Value == Math.Round(count.Value) && count.Value is >= MinSides and <= MaxSides)
            {
                sides = (int)count.Value;
                break;
            }

            ed.Write($"Il numero di lati deve essere intero, da {MinSides} a {MaxSides}.");
        }

        _sides = sides;
        var center = await ed.GetPointAsync("Centro del poligono:", null, null, "Lato");
        if (center.Status == PromptStatus.Keyword)
        {
            await PolygonByEdge(ed, sides);
            return;
        }

        if (!center.IsOk)
        {
            return;
        }

        var c = center.Point;
        var mode = await ed.GetKeywordAsync($"Tipo <{(_inscribed ? "Inscritto" : "Circoscritto")}>:", "Inscritto", "Circoscritto");
        if (mode.Status == PromptStatus.Keyword)
        {
            _inscribed = mode.Keyword == "Inscritto";
        }
        else if (mode.Status != PromptStatus.None)
        {
            return;
        }

        var inscribed = _inscribed;
        var radius = await ed.GetDistanceAsync(
            "Raggio:",
            c,
            p => Vector2.Distance(c, p) > Tolerance.Default ? [ByCenter(ed.CurrentLayer, sides, c, Vector2.Distance(c, p), (p - c).Angle, inscribed)] : []);
        if (!radius.IsOk)
        {
            return;
        }

        if (radius.Value <= Tolerance.Default)
        {
            ed.Write("Il raggio deve essere maggiore di zero.");
            return;
        }

        // Con il raggio scritto il lato in basso resta orizzontale; con un punto il punto dà la rotazione.
        var picked = Math.Abs(Vector2.Distance(c, radius.Point) - radius.Value) <= Tolerance.Default;
        var angle = picked ? (radius.Point - c).Angle : -Math.PI / 2 + (inscribed ? Math.PI / sides : 0);
        ed.Document.Edit("POLIGONO", e => e.Add(ed.Styled(ByCenter(ed.CurrentLayer, sides, c, radius.Value, angle, inscribed))));
    }

    private static async Task PolygonByEdge(Editor ed, int sides)
    {
        var first = await ed.GetPointAsync("Primo estremo del lato:");
        if (!first.IsOk)
        {
            return;
        }

        var a = first.Point;
        var second = await ed.GetPointAsync("Secondo estremo del lato:", a, p => p.IsAlmostEqual(a) ? [] : [ByEdge(ed.CurrentLayer, sides, a, p)]);
        if (!second.IsOk)
        {
            return;
        }

        if (second.Point.IsAlmostEqual(a))
        {
            ed.Write("Il lato ha lunghezza nulla.");
            return;
        }

        ed.Document.Edit("POLIGONO", e => e.Add(ed.Styled(ByEdge(ed.CurrentLayer, sides, a, second.Point))));
    }

    /// <summary>
    /// Poligono regolare chiuso. Inscritto: <paramref name="angle"/> è la direzione del primo vertice, a distanza
    /// <paramref name="radius"/>. Circoscritto: è la direzione del punto medio del primo lato, a distanza <paramref name="radius"/>.
    /// </summary>
    public static PolylineEntity ByCenter(Layer layer, int sides, Vector2 center, double radius, double angle, bool inscribed)
    {
        var step = Math.Tau / sides;
        var vertexRadius = inscribed ? radius : radius / Math.Cos(step / 2);
        var first = inscribed ? angle : angle - step / 2;
        return new PolylineEntity(
            layer,
            Enumerable.Range(0, sides).Select(i => new PolylineVertex(center + Vector2.FromPolar(vertexRadius, first + step * i))),
            isClosed: true);
    }

    /// <summary>Poligono regolare con il lato da a a b; gli altri vertici seguono in senso antiorario.</summary>
    public static PolylineEntity ByEdge(Layer layer, int sides, Vector2 a, Vector2 b)
    {
        var vertices = new List<PolylineVertex> { new(a), new(b) };
        var direction = b - a;
        var turn = Math.Tau / sides;
        var current = b;
        for (var i = 2; i < sides; i++)
        {
            direction = Matrix2D.Rotation(turn).TransformVector(direction);
            current += direction;
            vertices.Add(new PolylineVertex(current));
        }

        return new PolylineEntity(layer, vertices, isClosed: true);
    }

    // ---------- PUNTO ----------

    private static async Task Point(Editor ed)
    {
        while (true)
        {
            var position = await ed.GetPointAsync("Posizione del punto (Invio per finire):");
            if (!position.IsOk)
            {
                return;
            }

            ed.Document.Edit("PUNTO", e => e.Add(ed.Styled(new PointEntity(ed.CurrentLayer, position.Point))));
        }
    }
}

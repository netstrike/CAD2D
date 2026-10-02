using Cad.Document;
using Cad.Geometry;

namespace Cad.Rendering;

/// <summary>Tipi di linea e motivi di tratteggio trasformati in segmenti da disegnare.</summary>
public static class Patterns
{
    /// <summary>Oltre questo numero di ripetizioni del motivo per entità si disegna la linea continua, come fanno gli altri CAD.</summary>
    public const int MaxDashCycles = 20000;

    /// <summary>Limite di segmenti per tratteggio: oltre, il motivo è troppo fitto per essere utile e si salta.</summary>
    public const int MaxHatchSegments = 200000;

    /// <summary>Lunghezza di un punto del motivo, in frazione della lunghezza del motivo: un tratto brevissimo ma visibile.</summary>
    private const double DotFraction = 0.02;

    /// <summary>
    /// Applica il motivo a una spezzata. Restituisce i tratti da disegnare, oppure la spezzata intera se il motivo è
    /// continuo o troppo fitto.
    /// </summary>
    public static List<Vector2[]> Dash(Vector2[] points, IReadOnlyList<double> pattern, double scale)
    {
        var patternLength = pattern.Sum(Math.Abs) * scale;
        if (points.Length < 2 || pattern.Count == 0 || patternLength <= Tolerance.Default || pattern.All(p => p >= 0))
        {
            return [points];
        }

        var total = 0.0;
        for (var i = 1; i < points.Length; i++)
        {
            total += Vector2.Distance(points[i - 1], points[i]);
        }

        if (total / patternLength > MaxDashCycles)
        {
            return [points];
        }

        var result = new List<Vector2[]>();
        var current = new List<Vector2>();
        var index = 0;
        var remaining = ElementLength(pattern[0], scale, patternLength);
        var drawing = pattern[0] >= 0;
        if (drawing)
        {
            current.Add(points[0]);
        }

        for (var i = 1; i < points.Length; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            var length = Vector2.Distance(from, to);
            var position = 0.0;
            // Margine per gli arrotondamenti: un motivo che finisce esattamente sul vertice non apre un tratto nullo.
            while (length - position > remaining + 1e-9)
            {
                position += remaining;
                var p = Vector2.Lerp(from, to, position / length);
                if (drawing)
                {
                    current.Add(p);
                    result.Add([.. current]);
                    current.Clear();
                }
                else
                {
                    current.Add(p);
                }

                index = (index + 1) % pattern.Count;
                drawing = pattern[index] >= 0;
                remaining = ElementLength(pattern[index], scale, patternLength);
                if (!drawing)
                {
                    current.Clear();
                }
            }

            remaining -= length - position;
            if (drawing)
            {
                current.Add(to);
            }
        }

        if (drawing && current.Count >= 2)
        {
            result.Add([.. current]);
        }

        return result;
    }

    private static double ElementLength(double element, double scale, double patternLength) =>
        element == 0 ? patternLength * DotFraction : Math.Abs(element) * scale;

    /// <summary>
    /// Segmenti di una famiglia di linee di tratteggio dentro i contorni (regola pari/dispari).
    /// <paramref name="budget"/> è il numero di segmenti ancora disponibili e viene scalato.
    /// </summary>
    public static IEnumerable<(Vector2 From, Vector2 To)> HatchLines(IReadOnlyList<Vector2[]> rings, HatchPatternLine line, Ref<int> budget)
    {
        var direction = Vector2.FromPolar(1, line.Angle);
        var normal = direction.Perpendicular();
        var spacing = Vector2.Dot(line.Offset, normal);
        if (Math.Abs(spacing) < Tolerance.Default)
        {
            yield break;
        }

        // Linee necessarie a coprire i contorni, misurate lungo la normale.
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        foreach (var ring in rings)
        {
            foreach (var p in ring)
            {
                var v = Vector2.Dot(p - line.BasePoint, normal);
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
        }

        var k0 = (long)Math.Floor(Math.Min(min / spacing, max / spacing));
        var k1 = (long)Math.Ceiling(Math.Max(min / spacing, max / spacing));
        if (k1 - k0 > budget.Value)
        {
            budget.Value = 0;
            yield break;
        }

        var patternLength = line.Dashes.Sum(Math.Abs);
        var dashed = line.Dashes.Count > 0 && line.Dashes.Any(d => d < 0) && patternLength > Tolerance.Default;
        var crossings = new List<double>();
        for (var k = k0; k <= k1; k++)
        {
            var origin = line.BasePoint + line.Offset * k;
            crossings.Clear();
            foreach (var ring in rings)
            {
                for (var i = 0; i < ring.Length; i++)
                {
                    var p = ring[i];
                    var q = ring[(i + 1) % ring.Length];
                    var sp = Vector2.Dot(p - origin, normal);
                    var sq = Vector2.Dot(q - origin, normal);
                    if (sp > 0 == sq > 0)
                    {
                        continue;
                    }

                    var hit = p + (q - p) * (sp / (sp - sq));
                    crossings.Add(Vector2.Dot(hit - origin, direction));
                }
            }

            crossings.Sort();
            for (var i = 0; i + 1 < crossings.Count; i += 2)
            {
                var t0 = crossings[i];
                var t1 = crossings[i + 1];
                if (!dashed)
                {
                    if (--budget.Value < 0)
                    {
                        yield break;
                    }

                    yield return (origin + direction * t0, origin + direction * t1);
                    continue;
                }

                // Il motivo parte dall'origine della linea: si trova la fase all'inizio dell'intervallo.
                var cycle = Math.Floor(t0 / patternLength) * patternLength;
                var t = cycle;
                var element = 0;
                while (t < t1)
                {
                    var value = line.Dashes[element];
                    var length = value == 0 ? patternLength * DotFraction : Math.Abs(value);
                    if (value >= 0)
                    {
                        var a = Math.Max(t, t0);
                        var b = Math.Min(t + length, t1);
                        if (b > a)
                        {
                            if (--budget.Value < 0)
                            {
                                yield break;
                            }

                            yield return (origin + direction * a, origin + direction * b);
                        }
                    }

                    // Un punto non fa avanzare il motivo: lo fa lo spazio che lo segue.
                    t += value == 0 ? 0 : length;

                    element = (element + 1) % line.Dashes.Count;
                }
            }
        }
    }

    /// <summary>
    /// Orienta gli anelli di un tratteggio pieno: antiorari quelli a profondità pari, orari le isole. Così la regola
    /// "diverso da zero" dà buchi corretti e più riempimenti sovrapposti non si annullano a vicenda.
    /// </summary>
    public static List<Vector2[]> OrientForWinding(IReadOnlyList<Vector2[]> rings)
    {
        var result = new List<Vector2[]>(rings.Count);
        for (var i = 0; i < rings.Count; i++)
        {
            var ring = rings[i];
            if (ring.Length < 3)
            {
                continue;
            }

            var depth = 0;
            for (var j = 0; j < rings.Count; j++)
            {
                if (j != i && rings[j].Length >= 3 && ContainsPoint(rings[j], ring[0]))
                {
                    depth++;
                }
            }

            var counterClockwise = SignedArea(ring) > 0;
            var wantCounterClockwise = depth % 2 == 0;
            result.Add(counterClockwise == wantCounterClockwise ? ring : [.. ring.Reverse()]);
        }

        return result;
    }

    public static double SignedArea(IReadOnlyList<Vector2> ring)
    {
        var area = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            area += Vector2.Cross(ring[i], ring[(i + 1) % ring.Count]);
        }

        return area / 2;
    }

    /// <summary>Punto dentro il poligono (regola pari/dispari).</summary>
    public static bool ContainsPoint(IReadOnlyList<Vector2> ring, Vector2 p)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if (a.Y > p.Y != b.Y > p.Y && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}

/// <summary>Contatore condiviso tra iteratori (gli iteratori non accettano parametri ref).</summary>
public sealed class Ref<T>(T value)
{
    public T Value { get; set; } = value;
}

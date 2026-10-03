using System.Globalization;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>Linea guida del tracciamento: parte da <see cref="Origin"/> verso <see cref="Direction"/> (versore).</summary>
public readonly record struct TrackingLine(Vector2 Origin, Vector2 Direction);

/// <summary>Punto proposto dal tracciamento, le linee guida da disegnare e l'etichetta per il cursore.</summary>
public sealed record TrackingResult(Vector2 Point, IReadOnlyList<TrackingLine> Lines, string Label);

/// <summary>
/// Tracciamento polare e di allineamento (ETrack), come in DraftSight: vicino a un angolo multiplo dell'incremento
/// dal punto base, o all'orizzontale e alla verticale di un punto acquisito, il cursore si aggancia alla linea guida;
/// dove due guide si incrociano si aggancia all'incrocio.
/// </summary>
public static class Tracking
{
    /// <param name="cursor">Posizione del cursore.</param>
    /// <param name="aperture">Distanza massima dalla guida, in unità di disegno.</param>
    /// <param name="basePoint">Punto base del comando (origine delle guide polari).</param>
    /// <param name="polarIncrement">Incremento angolare in radianti; null = tracciamento polare spento.</param>
    /// <param name="acquired">Punti acquisiti per l'allineamento orizzontale e verticale.</param>
    public static TrackingResult? Find(Vector2 cursor, double aperture, Vector2? basePoint, double? polarIncrement, IReadOnlyList<Vector2> acquired)
    {
        var candidates = new List<(TrackingLine Line, double Distance, bool Polar)>();

        void Consider(Vector2 origin, Vector2 direction, bool polar)
        {
            var offset = cursor - origin;
            var along = Vector2.Dot(offset, direction);
            if (along <= aperture)
            {
                return;
            }

            var distance = Math.Abs(Vector2.Cross(direction, offset));
            if (distance <= aperture)
            {
                candidates.Add((new TrackingLine(origin, direction), distance, polar));
            }
        }

        if (basePoint is { } b && polarIncrement is { } increment && increment > 0)
        {
            var angle = (cursor - b).Angle;
            var snapped = Math.Round(angle / increment) * increment;
            Consider(b, Vector2.FromPolar(1, snapped), polar: true);
        }

        foreach (var point in acquired)
        {
            Consider(point, cursor.X >= point.X ? Vector2.UnitX : -Vector2.UnitX, polar: false);
            Consider(point, cursor.Y >= point.Y ? Vector2.UnitY : -Vector2.UnitY, polar: false);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        candidates.Sort((x, y) => x.Distance.CompareTo(y.Distance));

        // Due guide che si incrociano vicino al cursore: vince l'incrocio.
        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                var (a, c) = (candidates[i].Line, candidates[j].Line);
                if (Intersect(a, c) is { } crossing && Vector2.Distance(crossing, cursor) <= aperture * 1.5)
                {
                    return new TrackingResult(crossing, [a, c], Describe(candidates[i].Polar ? a : c, crossing, candidates[i].Polar || candidates[j].Polar ? "Polare" : "Allineamento"));
                }
            }
        }

        var best = candidates[0];
        // Già sulla guida: il cursore resta esattamente dov'è.
        var projected = best.Distance < 1e-9
            ? cursor
            : best.Line.Origin + best.Line.Direction * Vector2.Dot(cursor - best.Line.Origin, best.Line.Direction);
        return new TrackingResult(projected, [best.Line], Describe(best.Line, projected, best.Polar ? "Polare" : "Allineamento"));
    }

    /// <summary>Punto della griglia più vicino.</summary>
    public static Vector2 SnapToGrid(Vector2 point, double spacing) => spacing <= 0
        ? point
        : new Vector2(Math.Round(point.X / spacing) * spacing, Math.Round(point.Y / spacing) * spacing);

    private static Vector2? Intersect(TrackingLine a, TrackingLine b)
    {
        var denominator = Vector2.Cross(a.Direction, b.Direction);
        if (Math.Abs(denominator) < 1e-9)
        {
            return null;
        }

        var t = Vector2.Cross(b.Origin - a.Origin, b.Direction) / denominator;
        return a.Origin + a.Direction * t;
    }

    private static string Describe(TrackingLine line, Vector2 point, string kind)
    {
        var distance = Vector2.Distance(line.Origin, point);
        var degrees = InputParser.RadiansToDegrees((point - line.Origin).Angle);
        degrees = ((degrees % 360) + 360) % 360;
        return string.Create(CultureInfo.InvariantCulture, $"{kind}: {distance:0.####} < {degrees:0.##}°");
    }
}

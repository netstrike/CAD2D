namespace Cad.Geometry;

/// <summary>
/// Arco di circonferenza percorso in senso antiorario da <see cref="StartAngle"/> a <see cref="EndAngle"/> (radianti).
/// </summary>
public readonly record struct Arc2D(Vector2 Center, double Radius, double StartAngle, double EndAngle)
{
    /// <summary>Ampiezza in (0, 2π]: un arco con inizio e fine coincidenti è una circonferenza completa.</summary>
    public double Sweep => NormalizeSweep(EndAngle - StartAngle);

    public Vector2 StartPoint => Center + Vector2.FromPolar(Radius, StartAngle);
    public Vector2 EndPoint => Center + Vector2.FromPolar(Radius, EndAngle);
    public Vector2 Midpoint => Center + Vector2.FromPolar(Radius, StartAngle + Sweep / 2);

    public Vector2 PointAt(double t) => Center + Vector2.FromPolar(Radius, StartAngle + Sweep * t);

    /// <summary>Vero se la direzione <paramref name="angle"/> cade dentro l'arco.</summary>
    public bool ContainsAngle(double angle) => NormalizeAngle(angle - StartAngle) <= Sweep + Tolerance.Default;

    public BoundingBox Bounds
    {
        get
        {
            var box = BoundingBox.FromPoints(StartPoint, EndPoint);
            for (var quadrant = 0; quadrant < 4; quadrant++)
            {
                var angle = quadrant * Math.PI / 2;
                if (ContainsAngle(angle))
                {
                    box = box.Union(Center + Vector2.FromPolar(Radius, angle));
                }
            }

            return box;
        }
    }

    /// <summary>Angolo ricondotto a [0, 2π).</summary>
    public static double NormalizeAngle(double angle)
    {
        var a = angle % Math.Tau;
        return a < 0 ? a + Math.Tau : a;
    }

    /// <summary>Ampiezza ricondotta a (0, 2π].</summary>
    public static double NormalizeSweep(double sweep)
    {
        var s = NormalizeAngle(sweep);
        return s <= Tolerance.Default ? Math.Tau : s;
    }

    /// <summary>
    /// Arco tra due vertici di polilinea con il bulge dato (tan di un quarto dell'angolo al centro, negativo = orario).
    /// Restituisce null per un tratto dritto.
    /// </summary>
    public static Arc2D? FromBulge(Vector2 start, Vector2 end, double bulge)
    {
        var chord = end - start;
        var chordLength = chord.Length;
        if (Tolerance.IsZero(bulge, 1e-12) || Tolerance.IsZero(chordLength))
        {
            return null;
        }

        var angle = 4 * Math.Atan(bulge);
        var radius = chordLength / (2 * Math.Abs(Math.Sin(angle / 2)));
        // Distanza con segno dal punto medio della corda al centro, lungo la normale sinistra.
        var apothem = chordLength / 2 / Math.Tan(angle / 2);
        var center = (start + end) / 2 + chord.Perpendicular() / chordLength * apothem;

        var startAngle = (start - center).Angle;
        var endAngle = (end - center).Angle;
        return bulge > 0
            ? new Arc2D(center, radius, startAngle, endAngle)
            : new Arc2D(center, radius, endAngle, startAngle);
    }
}

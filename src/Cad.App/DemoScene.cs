using Cad.Geometry;

namespace Cad.App;

/// <summary>
/// Scena di prova per la fase 0: segmenti pseudo-casuali ma deterministici, per verificare pan e zoom sotto carico.
/// Sparirà quando arriverà il modello del documento (fase 1).
/// </summary>
internal static class DemoScene
{
    public static IReadOnlyList<Segment2D> CreateLines(int count, int seed = 2026)
    {
        var random = new Random(seed);
        var lines = new List<Segment2D>(count + 4);

        // Cornice di un foglio A1 orizzontale (841 × 594 mm).
        var a = new Vector2(0, 0);
        var b = new Vector2(841, 0);
        var c = new Vector2(841, 594);
        var d = new Vector2(0, 594);
        lines.AddRange([new(a, b), new(b, c), new(c, d), new(d, a)]);

        for (var i = 0; i < count; i++)
        {
            var start = new Vector2(10 + random.NextDouble() * 821, 10 + random.NextDouble() * 574);
            var end = start + Vector2.FromPolar(2 + random.NextDouble() * 40, random.NextDouble() * Math.Tau);
            lines.Add(new Segment2D(start, end));
        }

        return lines;
    }
}

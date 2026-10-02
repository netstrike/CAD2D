using Cad.Geometry;

namespace Cad.Document;

/// <summary>
/// Motivi di tratteggio predefiniti, con i valori metrici di acadiso.pat. Ogni riga è: angolo (gradi), origine x, y,
/// spostamento lungo la linea, distanza tra le linee, poi tratti e spazi.
/// </summary>
public static class HatchPatterns
{
    public const string Solid = "SOLID";

    private static readonly Dictionary<string, (string Description, double[][] Lines)> Definitions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ANSI31"] = ("Ferro, mattoni e pietra (45°)", [[45, 0, 0, 0, 3.175]]),
        ["ANSI32"] = ("Acciaio", [[45, 0, 0, 0, 9.525], [45, 4.490128, 0, 0, 9.525]]),
        ["ANSI37"] = ("Piombo, zinco, magnesio (retino)", [[45, 0, 0, 0, 3.175], [135, 0, 0, 0, 3.175]]),
        ["ANSI38"] = ("Alluminio", [[45, 0, 0, 0, 3.175], [135, 0, 0, 6.35, 3.175, 7.9375, -4.7625]]),
        ["LINE"] = ("Linee orizzontali", [[0, 0, 0, 0, 3.175]]),
        ["NET"] = ("Griglia", [[0, 0, 0, 0, 3.175], [90, 0, 0, 0, 3.175]]),
        ["DOTS"] = ("Punti", [[0, 0, 0, 0.79375, 1.5875, 0, -1.5875]]),
        ["BRICK"] = ("Mattoni", [[0, 0, 0, 0, 6.35], [90, 0, 0, 6.35, 6.35, 6.35, -6.35]]),
    };

    /// <summary>Nomi disponibili, SOLID compreso.</summary>
    public static IEnumerable<string> Names => Definitions.Keys.Prepend(Solid);

    public static bool Exists(string name) => name.Equals(Solid, StringComparison.OrdinalIgnoreCase) || Definitions.ContainsKey(name);

    public static string Describe(string name) =>
        Definitions.TryGetValue(name, out var d) ? d.Description : name.Equals(Solid, StringComparison.OrdinalIgnoreCase) ? "Riempimento pieno" : name;

    /// <summary>Linee del motivo scalate e ruotate (angolo in radianti), pronte per un <see cref="HatchEntity"/>.</summary>
    public static IReadOnlyList<HatchPatternLine> Build(string name, double scale, double angle)
    {
        if (!Definitions.TryGetValue(name, out var definition))
        {
            return [];
        }

        var rotation = Matrix2D.Rotation(angle);
        var result = new List<HatchPatternLine>(definition.Lines.Length);
        foreach (var row in definition.Lines)
        {
            var lineAngle = row[0] * Math.PI / 180 + angle;
            var origin = rotation.Transform(new Vector2(row[1], row[2]) * scale);
            // Spostamento e distanza sono nel riferimento della linea: si ruotano con essa.
            var offset = Matrix2D.Rotation(lineAngle).TransformVector(new Vector2(row[3], row[4]) * scale);
            result.Add(new HatchPatternLine(lineAngle, origin, offset, [.. row.Skip(5).Select(d => d * scale)]));
        }

        return result;
    }
}

namespace Cad.Document;

/// <summary>
/// Tipo di linea: sequenza di tratti (positivi), spazi (negativi) e punti (zero) in unità di disegno, ripetuta lungo la curva.
/// </summary>
public sealed class Linetype(string name, string description, IReadOnlyList<double> pattern)
{
    public const string ContinuousName = "Continuous";

    /// <summary>Linea continua: nessun motivo.</summary>
    public static Linetype Continuous { get; } = new(ContinuousName, "Continua", []);

    /// <summary>Segnaposto per "DaBlocco": l'entità prende il tipo di linea dell'inserimento che la contiene.</summary>
    public static Linetype ByBlock { get; } = new("ByBlock", "DaBlocco", []);

    public string Name { get; } = name;
    public string Description { get; } = description;
    public IReadOnlyList<double> Pattern { get; } = pattern;

    public bool IsContinuous => Pattern.Count == 0 || Pattern.All(p => p >= 0);

    public double PatternLength => Pattern.Sum(Math.Abs);

    /// <summary>
    /// Tipi di linea standard (valori metrici di acadiso.lin), disponibili anche nei disegni nuovi.
    /// </summary>
    public static IReadOnlyList<Linetype> Standard { get; } =
    [
        new("DASHED", "Tratteggiata __ __ __", [12.7, -6.35]),
        new("HIDDEN", "Nascosta _ _ _ _", [6.35, -3.175]),
        new("CENTER", "Asse ____ _ ____ _", [31.75, -6.35, 6.35, -6.35]),
        new("DASHDOT", "Tratto punto __ . __ .", [12.7, -6.35, 0, -6.35]),
        new("PHANTOM", "Fantasma ____ _ _ ____", [31.75, -6.35, 6.35, -6.35, 6.35, -6.35]),
        new("DOT", "Punteggiata . . . .", [0, -6.35]),
    ];

    public override string ToString() => Name;
}

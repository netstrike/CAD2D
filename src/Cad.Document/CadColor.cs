namespace Cad.Document;

/// <summary>Colore RGB già risolto.</summary>
public readonly record struct CadColor(byte R, byte G, byte B)
{
    public static readonly CadColor White = new(255, 255, 255);

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

public enum ColorSource
{
    ByLayer,
    ByBlock,
    Explicit,
}

/// <summary>Colore di un'entità: dal layer, dal blocco che la contiene, oppure esplicito.</summary>
public readonly record struct EntityColor(ColorSource Source, CadColor Value)
{
    public static readonly EntityColor ByLayer = new(ColorSource.ByLayer, CadColor.White);
    public static readonly EntityColor ByBlock = new(ColorSource.ByBlock, CadColor.White);

    public static EntityColor Explicit(CadColor color) => new(ColorSource.Explicit, color);
}

/// <summary>I colori base dell'indice AutoCAD (ACI 1-9), con i nomi italiani.</summary>
public static class StandardColors
{
    public static IReadOnlyList<(string Name, CadColor Color)> All { get; } =
    [
        ("Rosso", new CadColor(255, 0, 0)),
        ("Giallo", new CadColor(255, 255, 0)),
        ("Verde", new CadColor(0, 255, 0)),
        ("Ciano", new CadColor(0, 255, 255)),
        ("Blu", new CadColor(0, 0, 255)),
        ("Magenta", new CadColor(255, 0, 255)),
        ("Bianco", new CadColor(255, 255, 255)),
        ("Grigio", new CadColor(128, 128, 128)),
        ("Grigio chiaro", new CadColor(192, 192, 192)),
    ];

    /// <summary>Colore scritto dall'utente: nome italiano, indice 1-9 oppure "r,g,b".</summary>
    public static CadColor? Parse(string text)
    {
        var compact = text.Replace(" ", "", StringComparison.Ordinal);
        foreach (var (name, color) in All)
        {
            if (string.Equals(name.Replace(" ", "", StringComparison.Ordinal), compact, StringComparison.OrdinalIgnoreCase))
            {
                return color;
            }
        }

        if (int.TryParse(compact, out var index) && index >= 1 && index <= All.Count)
        {
            return All[index - 1].Color;
        }

        var parts = compact.Split(',', ';');
        if (parts.Length == 3 && parts.All(p => byte.TryParse(p, out _)))
        {
            return new CadColor(byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]));
        }

        return null;
    }

    public static string Describe(EntityColor color) => color.Source switch
    {
        ColorSource.ByLayer => "DaLayer",
        ColorSource.ByBlock => "DaBlocco",
        _ => All.FirstOrDefault(c => c.Color == color.Value).Name ?? color.Value.ToString(),
    };
}

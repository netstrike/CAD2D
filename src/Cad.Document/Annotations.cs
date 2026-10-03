using Cad.Geometry;

namespace Cad.Document;

/// <summary>
/// Entità disegnata con entità più semplici (linee, frecce, testi) generate dai suoi dati: si visualizza, si aggancia
/// con gli snap e si esplode attraverso <see cref="Explode"/>.
/// </summary>
public interface ICompositeEntity
{
    /// <summary>Parti dell'entità, sul suo layer e con colore DaBlocco.</summary>
    IReadOnlyList<Entity> Explode();
}

/// <summary>Descrizioni dei gruppi senza nome creati dai comandi di annotazione, per riconoscerli in seguito.</summary>
public static class AnnotationGroups
{
    public const string Leader = "Direttrice";
    public const string Table = "Tabella";
    public const string Tolerance = "Tolleranza";
    public const string CenterMark = "Segno di centro";
    public const string CenterLine = "Asse";
}

/// <summary>Stile di testo: carattere, altezza fissa (0 = si chiede ogni volta), fattore di larghezza e inclinazione.</summary>
public sealed class TextStyle(string name)
{
    public const string DefaultName = "Standard";
    public const string DefaultFamily = "Arial";

    public string Name { get; internal set; } = name;

    /// <summary>Famiglia del carattere usata per disegnare il testo.</summary>
    public string FontFamily { get; set; } = DefaultFamily;

    /// <summary>File del carattere come scritto nel DXF (per esempio "arial.ttf"); null = ricavato dalla famiglia.</summary>
    public string? FontFile { get; set; }

    /// <summary>Altezza fissa del testo; 0 = la si chiede quando si scrive.</summary>
    public double Height { get; set; }

    public double WidthFactor { get; set; } = 1;

    /// <summary>Inclinazione dei caratteri in radianti (positiva verso destra).</summary>
    public double ObliqueAngle { get; set; }

    /// <summary>File del carattere da scrivere nel DXF.</summary>
    public string FileName => FontFile ?? FontFamily.ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal) switch
    {
        "arial" => "arial.ttf",
        "timesnewroman" => "times.ttf",
        "couriernew" => "cour.ttf",
        var other => other + ".ttf",
    };

    /// <summary>Famiglia da usare per un file di carattere letto dal DXF. I caratteri SHX diventano Arial.</summary>
    public static string FamilyFromFile(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return DefaultFamily;
        }

        var name = System.IO.Path.GetFileNameWithoutExtension(file.Trim());
        var extension = System.IO.Path.GetExtension(file.Trim()).ToLowerInvariant();
        if (extension is "" or ".shx" || name.Length == 0)
        {
            return DefaultFamily;
        }

        return name.ToLowerInvariant() switch
        {
            "arial" => "Arial",
            "arialbd" => "Arial",
            "times" => "Times New Roman",
            "cour" => "Courier New",
            "calibri" => "Calibri",
            "verdana" => "Verdana",
            "tahoma" => "Tahoma",
            "segoeui" => "Segoe UI",
            "consola" => "Consolas",
            "isocpeur" or "isocp" => "ISOCPEUR",
            _ => char.ToUpperInvariant(name[0]) + name[1..],
        };
    }

    public override string ToString() => Name;
}

/// <summary>
/// Direttrice: spezzata con la freccia sul primo punto. Il testo è un'entità a parte, nello stesso gruppo,
/// così si sposta insieme alla direttrice e si modifica come ogni altro testo.
/// </summary>
public sealed class LeaderEntity(Layer layer, IEnumerable<Vector2> vertices, DimensionStyle style) : Entity(layer), ICompositeEntity
{
    public IReadOnlyList<Vector2> Vertices { get; } = [.. vertices];
    public DimensionStyle Style { get; set; } = style;
    public bool HasArrow { get; set; } = true;

    public override IReadOnlyList<Vector2> Grips => Vertices;

    public override BoundingBox Bounds => Explode().Aggregate(BoundingBox.FromPoints(Vertices), (box, e) => box.Union(e.Bounds));

    protected override Entity TransformCore(Matrix2D m) =>
        new LeaderEntity(Layer, Vertices.Select(m.Transform), Style) { HasArrow = HasArrow };

    protected override Entity MoveGripCore(int index, Vector2 position) =>
        new LeaderEntity(Layer, Vertices.Select((v, i) => i == index ? position : v), Style) { HasArrow = HasArrow };

    public IReadOnlyList<Entity> Explode()
    {
        var parts = new List<Entity>();
        for (var i = 1; i < Vertices.Count; i++)
        {
            parts.Add(new LineEntity(Layer, Vertices[i - 1], Vertices[i]));
        }

        if (HasArrow && Vertices.Count > 1)
        {
            var pointing = (Vertices[0] - Vertices[1]).Normalized();
            if (pointing != Vector2.Zero)
            {
                parts.Add(Arrow(Layer, Vertices[0], pointing, Style.ArrowSize * Style.Scale));
            }
        }

        foreach (var part in parts)
        {
            part.Color = EntityColor.ByBlock;
        }

        return parts;
    }

    /// <summary>Freccia piena con la punta in <paramref name="tip"/>, rivolta nella direzione <paramref name="pointing"/>.</summary>
    internal static SolidEntity Arrow(Layer layer, Vector2 tip, Vector2 pointing, double size)
    {
        var back = tip - pointing * size;
        var side = pointing.Perpendicular() * (size / 6);
        return new SolidEntity(layer, [tip, back + side, back - side]);
    }
}

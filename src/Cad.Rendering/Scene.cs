using Cad.Document;
using Cad.Geometry;

namespace Cad.Rendering;

/// <summary>Spezzate dello stesso colore e spessore, in coordinate mondo: si disegnano con una sola chiamata.</summary>
public sealed class RenderBatch(CadColor color, double weight = LineWeight.DefaultValue / 100.0)
{
    public CadColor Color { get; } = color;

    /// <summary>Spessore di linea in millimetri.</summary>
    public double Weight { get; } = weight;
    public List<Vector2[]> Polylines { get; } = [];
}

/// <summary>Una riga di testo già posizionata in coordinate mondo, con l'origine sulla linea di base.</summary>
public sealed record RenderText(
    string Text,
    Vector2 Position,
    double Height,
    double Rotation,
    double WidthFactor,
    TextHorizontalAlignment Alignment,
    CadColor Color,
    BoundingBox Bounds);

/// <summary>
/// Aree piene dello stesso colore (riempimenti, frecce di quota). Gli anelli sono già orientati per la regola
/// "diverso da zero": antiorari i contorni, orari i buchi.
/// </summary>
public sealed class FillBatch(CadColor color)
{
    public CadColor Color { get; } = color;
    public List<Vector2[]> Rings { get; } = [];
}

public sealed record RenderPoint(Vector2 Position, CadColor Color);

/// <summary>
/// Il disegno pronto per essere visualizzato: blocchi esplosi, colori risolti, curve approssimate, layer spenti esclusi.
/// Si ricostruisce quando il documento o la visibilità dei layer cambiano; pan e zoom non lo toccano.
/// </summary>
public sealed class Scene
{
    public static readonly Scene Empty = new([], [], [], 0);

    public Scene(
        IReadOnlyList<RenderBatch> batches,
        IReadOnlyList<RenderText> texts,
        IReadOnlyList<RenderPoint> points,
        int entityCount,
        IReadOnlyList<FillBatch>? fills = null)
    {
        Batches = batches;
        Fills = fills ?? [];
        Texts = texts;
        Points = points;
        EntityCount = entityCount;
        TextIndex = new SpatialIndex<RenderText>(texts.Select(t => (t, t.Bounds)));

        var bounds = TextIndex.Bounds;
        foreach (var batch in batches)
        {
            foreach (var polyline in batch.Polylines)
            {
                bounds = bounds.Union(BoundingBox.FromPoints(polyline));
            }
        }

        foreach (var fill in Fills)
        {
            foreach (var ring in fill.Rings)
            {
                bounds = bounds.Union(BoundingBox.FromPoints(ring));
            }
        }

        Bounds = points.Aggregate(bounds, (b, p) => b.Union(p.Position));
    }

    public IReadOnlyList<FillBatch> Fills { get; }

    public IReadOnlyList<RenderBatch> Batches { get; }
    public IReadOnlyList<RenderText> Texts { get; }
    public IReadOnlyList<RenderPoint> Points { get; }

    /// <summary>Indice spaziale dei testi: a ogni fotogramma si disegnano solo quelli nella vista.</summary>
    public SpatialIndex<RenderText> TextIndex { get; }

    /// <summary>Entità visibili dopo l'esplosione dei blocchi.</summary>
    public int EntityCount { get; }

    public BoundingBox Bounds { get; }
}

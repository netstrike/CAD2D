using System.Runtime.CompilerServices;
using Cad.Document;
using Cad.Geometry;

namespace Cad.Rendering;

/// <summary>
/// Pezzi di scena già calcolati per le entità geometriche semplici (linee, cerchi, polilinee, tratteggi...), riusati da
/// una ricostruzione all'altra: dopo un comando si rifà solo quello che è cambiato. Le entità modificate dai comandi sono
/// oggetti nuovi, quindi un pezzo resta valido finché l'entità ha gli stessi layer, colore, tipo e spessore di linea e
/// i layer e la scala dei tipi di linea del disegno non cambiano.
/// </summary>
public sealed class SceneCache
{
    private ConditionalWeakTable<Entity, Fragment> _fragments = new();
    private List<object>? _signature;

    /// <summary>Pezzi riusati e ricalcolati nell'ultima ricostruzione, per le prove.</summary>
    public int Reused { get; internal set; }
    public int Computed { get; internal set; }

    /// <summary>Le entità i cui pezzi dipendono solo dalla loro geometria e dal loro aspetto.</summary>
    internal static bool IsCacheable(Entity entity) => entity is LineEntity or CircleEntity or ArcEntity or PolylineEntity
        or EllipseEntity or SplineEntity or PolylinePathEntity or HatchEntity or SolidEntity or PointEntity;

    /// <summary>Svuota tutto se è cambiato qualcosa che vale per tutte le entità (layer, scala dei tipi di linea, opzioni).</summary>
    internal void Validate(CadDocument document, bool exactArcs)
    {
        var signature = new List<object> { document.LinetypeScale, exactArcs };
        foreach (var layer in document.Layers)
        {
            signature.Add(layer);
            signature.Add(layer.Color);
            signature.Add(layer.IsVisible);
            signature.Add(layer.Linetype);
            signature.Add(layer.LineWeight);
        }

        if (_signature is null || !_signature.SequenceEqual(signature))
        {
            _fragments = new ConditionalWeakTable<Entity, Fragment>();
            _signature = signature;
        }

        Reused = 0;
        Computed = 0;
    }

    internal Fragment? Find(Entity entity)
    {
        if (_fragments.TryGetValue(entity, out var fragment) && fragment.Style == Style.Of(entity))
        {
            Reused++;
            return fragment;
        }

        return null;
    }

    internal void Store(Entity entity, Fragment fragment)
    {
        Computed++;
        _fragments.AddOrUpdate(entity, fragment);
    }

    internal readonly record struct Style(Layer Layer, EntityColor Color, Linetype? Linetype, double LinetypeScale, int LineWeight)
    {
        public static Style Of(Entity entity) => new(entity.Layer, entity.Color, entity.Linetype, entity.LinetypeScale, entity.LineWeight);
    }

    /// <summary>Il contributo di un'entità alla scena: spezzate e archi per colore e spessore, aree piene, punti.</summary>
    internal sealed record Fragment(
        Style Style,
        (CadColor Color, int Weight, Vector2[][] Polylines, RenderArc[] Arcs)[] Batches,
        (CadColor Color, Vector2[][] Rings)[] Fills,
        RenderPoint[] Points,
        int EntityCount);
}

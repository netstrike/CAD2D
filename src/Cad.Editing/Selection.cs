using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>Insieme delle entità selezionate, nell'ordine di selezione.</summary>
public sealed class SelectionSet
{
    private readonly List<Entity> _items = [];
    private readonly HashSet<Entity> _lookup = new(ReferenceEqualityComparer.Instance);

    public event EventHandler? Changed;

    public IReadOnlyList<Entity> Items => _items;
    public int Count => _items.Count;

    public bool Contains(Entity entity) => _lookup.Contains(entity);

    public void Add(IEnumerable<Entity> entities)
    {
        var changed = false;
        foreach (var entity in entities)
        {
            if (_lookup.Add(entity))
            {
                _items.Add(entity);
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Remove(IEnumerable<Entity> entities)
    {
        var changed = false;
        foreach (var entity in entities)
        {
            if (_lookup.Remove(entity))
            {
                _items.Remove(entity);
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        _lookup.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Toglie le entità che non sono più nel disegno (dopo cancella, annulla, ecc.).</summary>
    public void Prune(CadDocument document)
    {
        var present = new HashSet<Entity>(document.ModelSpace, ReferenceEqualityComparer.Instance);
        Remove(_items.Where(e => !present.Contains(e)).ToList());
    }
}

/// <summary>
/// Ricerca delle entità del modello per posizione, con un indice spaziale ricostruito quando il disegno cambia.
/// </summary>
public sealed class EntityLocator
{
    private readonly CadDocument _document;
    private SpatialIndex<Entity>? _index;

    public EntityLocator(CadDocument document)
    {
        _document = document;
        document.Changed += (_, _) => _index = null;
    }

    private SpatialIndex<Entity> Index => _index ??= new SpatialIndex<Entity>(
        _document.ModelSpace.Where(IsSelectable).Select(e => (e, e.Bounds)));

    /// <summary>Entità sui layer visibili e non bloccati: le uniche che si possono selezionare e agganciare.</summary>
    public static bool IsSelectable(Entity entity) => entity.Layer.IsVisible && !entity.Layer.IsLocked;

    /// <summary>Da chiamare quando cambia la visibilità o il blocco di un layer.</summary>
    public void Invalidate() => _index = null;

    public IEnumerable<Entity> Near(Vector2 point, double radius) =>
        Index.Query(new BoundingBox(point - new Vector2(radius, radius), point + new Vector2(radius, radius)));

    /// <summary>Entità selezionabili il cui ingombro tocca il rettangolo.</summary>
    public IEnumerable<Entity> Overlapping(BoundingBox box) => Index.Query(box);

    /// <summary>L'entità più vicina al punto entro il raggio, o null.</summary>
    public Entity? Pick(Vector2 point, double radius) => PickAll(point, radius).FirstOrDefault();

    /// <summary>Tutte le entità entro il raggio, dalla più vicina: per scorrere quelle sovrapposte.</summary>
    public IReadOnlyList<Entity> PickAll(Vector2 point, double radius) => Near(point, radius)
        .Select(e => (Entity: e, Distance: EntityGeometry.DistanceTo(e, point)))
        .Where(x => x.Distance <= radius)
        .OrderBy(x => x.Distance)
        .Select(x => x.Entity)
        .ToList();

    /// <summary>Selezione a finestra (solo entità interamente dentro) o interseca (anche quelle che toccano il bordo).</summary>
    public IEnumerable<Entity> InWindow(BoundingBox window, bool crossing) => Index.Query(window)
        .Where(e => crossing ? EntityGeometry.Crosses(e, window) : window.Contains(e.Bounds));
}

using Cad.Geometry;

namespace Cad.Document;

/// <summary>
/// Disegno aperto: layer, definizioni di blocco ed entità dello spazio modello.
/// </summary>
public sealed class CadDocument
{
    private readonly Dictionary<string, Layer> _layers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BlockDefinition> _blocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Linetype> _linetypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DimensionStyle> _dimensionStyles = new(StringComparer.OrdinalIgnoreCase);

    private int _savedDepth;
    private bool _savedStateLost;

    public CadDocument()
    {
        GetOrAddLayer(Layer.DefaultName);
        AddLinetype(Linetype.Continuous);
        foreach (var linetype in Linetype.Standard)
        {
            AddLinetype(linetype);
        }

        CurrentDimensionStyle = GetOrAddDimensionStyle(DimensionStyle.DefaultName);
        History = new UndoHistory(this);
    }

    public UndoHistory History { get; }

    /// <summary>Oggetto letto dal file (per esempio il documento ACadSharp), riusato al salvataggio. Null per un disegno nuovo.</summary>
    public object? Source { get; set; }

    /// <summary>Percorso del file da cui è stato aperto o in cui è stato salvato.</summary>
    public string? FilePath { get; set; }

    /// <summary>Vero se ci sono modifiche non salvate.</summary>
    public bool IsModified => _savedStateLost || History.Depth != _savedDepth;

    /// <summary>Sollevato dopo ogni modifica, annullamento o ripetizione.</summary>
    public event EventHandler? Changed;

    /// <summary>Applica un gruppo di modifiche come un'unica operazione annullabile.</summary>
    public void Edit(string name, Action<DocumentEditor> edit)
    {
        var changes = new List<Change>();
        edit(new DocumentEditor(this, changes));
        if (changes.Count == 0)
        {
            return;
        }

        // Una nuova modifica dopo un annulla rende irraggiungibile lo stato salvato se stava nella parte annullata.
        if (History.Depth < _savedDepth)
        {
            _savedStateLost = true;
        }

        History.Push(new UndoUnit(name, changes));
        RaiseChanged();
    }

    public void MarkSaved()
    {
        _savedDepth = History.Depth;
        _savedStateLost = false;
    }

    /// <summary>Modifica fuori dalla cronologia (proprietà dei layer, scala dei tipi di linea): va salvata, non si annulla.</summary>
    public void MarkModified()
    {
        _savedStateLost = true;
        RaiseChanged();
    }

    /// <summary>Da chiamare dopo modifiche fuori dalla cronologia, come accendere o spegnere un layer.</summary>
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public IEnumerable<Layer> Layers => _layers.Values;
    public IEnumerable<BlockDefinition> Blocks => _blocks.Values;
    public List<Entity> ModelSpace { get; } = [];

    /// <summary>Tipi di entità presenti nel file ma non ancora gestiti, con il loro numero.</summary>
    public Dictionary<string, int> UnsupportedEntities { get; } = new(StringComparer.Ordinal);

    public Layer GetOrAddLayer(string name)
    {
        if (!_layers.TryGetValue(name, out var layer))
        {
            layer = new Layer(name);
            _layers.Add(name, layer);
        }

        return layer;
    }

    public Layer? FindLayer(string name) => _layers.GetValueOrDefault(name);

    public IEnumerable<DimensionStyle> DimensionStyles => _dimensionStyles.Values;

    /// <summary>Stile delle quote nuove.</summary>
    public DimensionStyle CurrentDimensionStyle { get; set; }

    public DimensionStyle GetOrAddDimensionStyle(string name)
    {
        if (!_dimensionStyles.TryGetValue(name, out var style))
        {
            style = new DimensionStyle(name);
            _dimensionStyles.Add(name, style);
        }

        return style;
    }

    public IEnumerable<Linetype> Linetypes => _linetypes.Values;

    /// <summary>Scala globale dei tipi di linea (LTSCALE).</summary>
    public double LinetypeScale { get; set; } = 1;

    public Linetype? FindLinetype(string name) => _linetypes.GetValueOrDefault(name);

    /// <summary>Aggiunge o sostituisce un tipo di linea con lo stesso nome (quello letto dal file vince su quello standard).</summary>
    public Linetype AddLinetype(Linetype linetype)
    {
        _linetypes[linetype.Name] = linetype;
        return linetype;
    }

    public bool RemoveLayer(Layer layer) =>
        layer.Name != Layer.DefaultName &&
        !ModelSpace.Any(e => e.Layer == layer) &&
        !Blocks.Any(b => b.Entities.Any(e => e.Layer == layer)) &&
        _layers.Remove(layer.Name);

    public BlockDefinition GetOrAddBlock(string name)
    {
        if (!_blocks.TryGetValue(name, out var block))
        {
            block = new BlockDefinition(name);
            _blocks.Add(name, block);
        }

        return block;
    }

    public void AddUnsupported(string typeName) =>
        UnsupportedEntities[typeName] = UnsupportedEntities.GetValueOrDefault(typeName) + 1;

    public BoundingBox Bounds => ModelSpace.Aggregate(BoundingBox.Empty, (box, e) => box.Union(e.Bounds));
}

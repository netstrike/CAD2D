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
    private readonly Dictionary<string, TextStyle> _textStyles = new(StringComparer.OrdinalIgnoreCase);

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
        CurrentTextStyle = GetOrAddTextStyle(TextStyle.DefaultName);
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
        try
        {
            edit(new DocumentEditor(this, changes));
        }
        catch
        {
            // Un comando interrotto da un errore non lascia il disegno a metà.
            History.Revert(changes);
            throw;
        }

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

    /// <summary>
    /// Scala del disegno come rapporto tra misura reale e misura sulla carta (2 = 1:2, 0.5 = 2:1). Si ricava dallo stile
    /// di quota corrente: la scala globale (annotazioni più grandi, disegno in misura reale) per il fattore delle misure
    /// (disegno rimpicciolito, quote che mostrano le misure reali).
    /// </summary>
    public double DrawingScale => CurrentDimensionStyle.Scale * CurrentDimensionStyle.LinearFactor;

    /// <summary>Scala scritta come negli altri CAD: "1:2", "1:1", "5:1".</summary>
    public static string FormatScale(double scale)
    {
        static string Number(double v) => Math.Round(v, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        return scale >= 1 ? $"1:{Number(scale)}" : $"{Number(1 / scale)}:1";
    }

    /// <summary>Legge "1:2", "2:1", "1/50" oppure un numero (2 = 1:2); null se non è una scala valida.</summary>
    public static double? ParseScale(string text)
    {
        var parts = text.Trim().Replace(',', '.').Split(':', '/');
        var numbers = new List<double>();
        foreach (var part in parts)
        {
            if (!double.TryParse(part.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value <= 0)
            {
                return null;
            }

            numbers.Add(value);
        }

        return numbers.Count switch
        {
            1 => numbers[0],
            2 => numbers[1] / numbers[0],
            _ => null,
        };
    }

    public IEnumerable<TextStyle> TextStyles => _textStyles.Values;

    /// <summary>Stile dei testi nuovi.</summary>
    public TextStyle CurrentTextStyle { get; set; }

    public TextStyle? FindTextStyle(string name) => _textStyles.GetValueOrDefault(name);

    public TextStyle GetOrAddTextStyle(string name)
    {
        if (!_textStyles.TryGetValue(name, out var style))
        {
            style = new TextStyle(name);
            _textStyles.Add(name, style);
        }

        return style;
    }

    /// <summary>Rinomina uno stile di testo; false se il nome non è valido o è già usato. Standard non si rinomina.</summary>
    public bool RenameTextStyle(TextStyle style, string name)
    {
        if (!IsValidName(name) || style.Name.Equals(TextStyle.DefaultName, StringComparison.OrdinalIgnoreCase) ||
            (_textStyles.ContainsKey(name) && !name.Equals(style.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        _textStyles.Remove(style.Name);
        style.Name = name;
        _textStyles.Add(name, style);
        return true;
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

    /// <summary>Caratteri non ammessi nei nomi di layer e blocchi (come in DWG/DXF).</summary>
    public static bool IsValidName(string name) =>
        name.Length > 0 && name.Trim() == name && name.IndexOfAny(['<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`']) < 0;

    /// <summary>Rinomina un layer; falso se il nome non è valido, è già usato o si tratta del layer 0.</summary>
    public bool RenameLayer(Layer layer, string newName)
    {
        if (layer.Name == Layer.DefaultName || !IsValidName(newName) ||
            (_layers.TryGetValue(newName, out var other) && other != layer) || !_layers.Remove(layer.Name))
        {
            return false;
        }

        layer.Name = newName;
        _layers.Add(newName, layer);
        return true;
    }

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

    private readonly List<CadGroup> _groups = [];

    /// <summary>Gruppi del disegno; un gruppo senza membri nel modello resta finché non viene eliminato.</summary>
    public IReadOnlyList<CadGroup> Groups => _groups;

    public CadGroup? FindGroup(string name) =>
        _groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Nuovo gruppo; senza nome riceve "*A1", "*A2"... Null se il nome è già usato o non valido.</summary>
    public CadGroup? AddGroup(string? name = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            var index = 1;
            while (FindGroup($"*A{index}") is not null)
            {
                index++;
            }

            name = $"*A{index}";
        }
        else if (!(IsValidName(name) || (name.Length > 1 && name[0] == '*' && IsValidName(name[1..]))) || FindGroup(name) is not null)
        {
            return null;
        }

        var group = new CadGroup(name);
        _groups.Add(group);
        return group;
    }

    public bool RemoveGroup(CadGroup group) => _groups.Remove(group);

    public bool RenameGroup(CadGroup group, string name)
    {
        if (!IsValidName(name) || (FindGroup(name) is { } other && other != group))
        {
            return false;
        }

        group.Name = name;
        return true;
    }

    /// <summary>Entità del modello che appartengono al gruppo.</summary>
    public IEnumerable<Entity> Members(CadGroup group) => ModelSpace.Where(e => e.Group == group);

    public BoundingBox Bounds => ModelSpace.Aggregate(BoundingBox.Empty, (box, e) => box.Union(e.Bounds));
}

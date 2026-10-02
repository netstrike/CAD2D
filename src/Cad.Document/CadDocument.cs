using Cad.Geometry;

namespace Cad.Document;

/// <summary>
/// Disegno aperto: layer, definizioni di blocco ed entità dello spazio modello.
/// </summary>
public sealed class CadDocument
{
    private readonly Dictionary<string, Layer> _layers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BlockDefinition> _blocks = new(StringComparer.OrdinalIgnoreCase);

    public CadDocument()
    {
        GetOrAddLayer(Layer.DefaultName);
    }

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

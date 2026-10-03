using Cad.Geometry;

namespace Cad.Document;

/// <summary>
/// R-tree statico costruito con il metodo Sort-Tile-Recursive. Si ricostruisce quando il disegno cambia;
/// risponde a "quali oggetti toccano questo rettangolo" senza scorrere tutto il disegno.
/// </summary>
public sealed class SpatialIndex<T>
{
    private const int NodeCapacity = 16;

    private readonly Node? _root;

    public SpatialIndex(IEnumerable<(T Item, BoundingBox Bounds)> items)
    {
        var leaves = items
            .Where(i => !i.Bounds.IsEmpty)
            .Select(i => new Node(i.Bounds, i.Item, null))
            .ToList();
        Count = leaves.Count;
        _root = leaves.Count == 0 ? null : Build(leaves);
    }

    public int Count { get; }

    public BoundingBox Bounds => _root?.Bounds ?? BoundingBox.Empty;

    public IEnumerable<T> Query(BoundingBox area)
    {
        if (_root is null || !_root.Bounds.Intersects(area))
        {
            yield break;
        }

        var stack = new Stack<Node>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Children is null)
            {
                yield return node.Item!;
                continue;
            }

            foreach (var child in node.Children)
            {
                if (child.Bounds.Intersects(area))
                {
                    stack.Push(child);
                }
            }
        }
    }

    private static Node Build(List<Node> nodes)
    {
        while (nodes.Count > 1)
        {
            nodes = Pack(nodes);
        }

        return nodes[0];
    }

    /// <summary>Raggruppa i nodi in fasce verticali ordinate per X, poi per Y dentro ogni fascia.</summary>
    private static List<Node> Pack(List<Node> nodes)
    {
        var parentCount = (int)Math.Ceiling(nodes.Count / (double)NodeCapacity);
        var sliceCount = (int)Math.Ceiling(Math.Sqrt(parentCount));
        var sliceSize = sliceCount * NodeCapacity;

        // Ordinamenti su array con le chiavi già calcolate: con centomila entità l'indice si ricostruisce a ogni modifica.
        var sorted = nodes.ToArray();
        var keys = new double[sorted.Length];
        for (var i = 0; i < sorted.Length; i++)
        {
            keys[i] = sorted[i].Bounds.Min.X + sorted[i].Bounds.Max.X;
        }

        Array.Sort(keys, sorted);
        var parents = new List<Node>(parentCount);
        for (var s = 0; s < sorted.Length; s += sliceSize)
        {
            var length = Math.Min(sliceSize, sorted.Length - s);
            for (var i = 0; i < length; i++)
            {
                keys[s + i] = sorted[s + i].Bounds.Min.Y + sorted[s + i].Bounds.Max.Y;
            }

            Array.Sort(keys, sorted, s, length);
            for (var i = 0; i < length; i += NodeCapacity)
            {
                var children = new Node[Math.Min(NodeCapacity, length - i)];
                Array.Copy(sorted, s + i, children, 0, children.Length);
                var bounds = children[0].Bounds;
                for (var c = 1; c < children.Length; c++)
                {
                    bounds = bounds.Union(children[c].Bounds);
                }

                parents.Add(new Node(bounds, default, children));
            }
        }

        return parents;
    }

    private sealed record Node(BoundingBox Bounds, T? Item, Node[]? Children);
}

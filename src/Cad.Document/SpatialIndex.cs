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

        var byX = nodes.OrderBy(n => n.Bounds.Center.X).ToList();
        var parents = new List<Node>(parentCount);
        for (var s = 0; s < byX.Count; s += sliceSize)
        {
            var slice = byX.Skip(s).Take(sliceSize).OrderBy(n => n.Bounds.Center.Y).ToList();
            for (var i = 0; i < slice.Count; i += NodeCapacity)
            {
                var children = slice.Skip(i).Take(NodeCapacity).ToArray();
                var bounds = children.Aggregate(BoundingBox.Empty, (b, c) => b.Union(c.Bounds));
                parents.Add(new Node(bounds, default, children));
            }
        }

        return parents;
    }

    private sealed record Node(BoundingBox Bounds, T? Item, Node[]? Children);
}

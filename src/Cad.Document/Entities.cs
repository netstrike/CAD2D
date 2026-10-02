using Cad.Geometry;

namespace Cad.Document;

public abstract class Entity
{
    protected Entity(Layer layer) => Layer = layer;

    public Layer Layer { get; set; }
    public EntityColor Color { get; set; } = EntityColor.ByLayer;

    /// <summary>Ingombro in coordinate del contenitore (modello o blocco).</summary>
    public abstract BoundingBox Bounds { get; }
}

public sealed class LineEntity(Layer layer, Vector2 start, Vector2 end) : Entity(layer)
{
    public Vector2 Start { get; set; } = start;
    public Vector2 End { get; set; } = end;
    public Segment2D Segment => new(Start, End);
    public override BoundingBox Bounds => Segment.Bounds;
}

public sealed class CircleEntity(Layer layer, Vector2 center, double radius) : Entity(layer)
{
    public Vector2 Center { get; set; } = center;
    public double Radius { get; set; } = radius;
    public override BoundingBox Bounds => new Circle2D(Center, Radius).Bounds;
}

/// <summary>Arco percorso in senso antiorario da <see cref="StartAngle"/> a <see cref="EndAngle"/> (radianti).</summary>
public sealed class ArcEntity(Layer layer, Vector2 center, double radius, double startAngle, double endAngle) : Entity(layer)
{
    public Vector2 Center { get; set; } = center;
    public double Radius { get; set; } = radius;
    public double StartAngle { get; set; } = startAngle;
    public double EndAngle { get; set; } = endAngle;
    public Arc2D Arc => new(Center, Radius, StartAngle, EndAngle);
    public override BoundingBox Bounds => Arc.Bounds;
}

/// <summary>
/// Ellisse o arco di ellisse: punto = centro + asse maggiore·cos t + asse minore·sin t, con t da
/// <see cref="StartParameter"/> a <see cref="EndParameter"/>.
/// </summary>
public sealed class EllipseEntity(Layer layer, Vector2 center, Vector2 majorAxis, Vector2 minorAxis, double startParameter, double endParameter)
    : Entity(layer)
{
    public Vector2 Center { get; set; } = center;
    public Vector2 MajorAxis { get; set; } = majorAxis;
    public Vector2 MinorAxis { get; set; } = minorAxis;
    public double StartParameter { get; set; } = startParameter;
    public double EndParameter { get; set; } = endParameter;

    public Vector2 PointAt(double t) => Center + MajorAxis * Math.Cos(t) + MinorAxis * Math.Sin(t);

    public double Sweep => Arc2D.NormalizeSweep(EndParameter - StartParameter);

    public override BoundingBox Bounds
    {
        get
        {
            // Estremi esatti di un'ellisse ruotata: semiassi proiettati sugli assi X e Y.
            if (Sweep >= Math.Tau - Tolerance.Default)
            {
                var hx = Math.Sqrt(MajorAxis.X * MajorAxis.X + MinorAxis.X * MinorAxis.X);
                var hy = Math.Sqrt(MajorAxis.Y * MajorAxis.Y + MinorAxis.Y * MinorAxis.Y);
                return new BoundingBox(Center - new Vector2(hx, hy), Center + new Vector2(hx, hy));
            }

            const int steps = 64;
            var box = BoundingBox.Empty;
            for (var i = 0; i <= steps; i++)
            {
                box = box.Union(PointAt(StartParameter + Sweep * i / steps));
            }

            return box;
        }
    }
}

/// <summary>Vertice di polilinea: il bulge è tan(angolo/4) dell'arco verso il vertice successivo (0 = tratto dritto).</summary>
public readonly record struct PolylineVertex(Vector2 Position, double Bulge = 0);

public sealed class PolylineEntity(Layer layer, IEnumerable<PolylineVertex> vertices, bool isClosed) : Entity(layer)
{
    public List<PolylineVertex> Vertices { get; } = [.. vertices];
    public bool IsClosed { get; set; } = isClosed;

    public int SegmentCount => Vertices.Count < 2 ? 0 : IsClosed ? Vertices.Count : Vertices.Count - 1;

    /// <summary>Restituisce il tratto i-esimo: segmento se il bulge è zero, altrimenti arco.</summary>
    public (Segment2D Segment, Arc2D? Arc) GetSegment(int index)
    {
        var a = Vertices[index];
        var b = Vertices[(index + 1) % Vertices.Count];
        var segment = new Segment2D(a.Position, b.Position);
        return (segment, Arc2D.FromBulge(a.Position, b.Position, a.Bulge));
    }

    public override BoundingBox Bounds
    {
        get
        {
            var box = BoundingBox.Empty;
            foreach (var v in Vertices)
            {
                box = box.Union(v.Position);
            }

            for (var i = 0; i < SegmentCount; i++)
            {
                if (GetSegment(i).Arc is { } arc)
                {
                    box = box.Union(arc.Bounds);
                }
            }

            return box;
        }
    }
}

/// <summary>Curva già approssimata da una sequenza di punti (spline e simili).</summary>
public sealed class PolylinePathEntity(Layer layer, IEnumerable<Vector2> points, bool isClosed) : Entity(layer)
{
    public List<Vector2> Points { get; } = [.. points];
    public bool IsClosed { get; set; } = isClosed;
    public override BoundingBox Bounds => BoundingBox.FromPoints(Points);
}

public sealed class PointEntity(Layer layer, Vector2 position) : Entity(layer)
{
    public Vector2 Position { get; set; } = position;
    public override BoundingBox Bounds => new(Position, Position);
}

public enum TextHorizontalAlignment
{
    Left,
    Center,
    Right,
}

public enum TextVerticalAlignment
{
    Baseline,
    Bottom,
    Middle,
    Top,
}

/// <summary>Testo su una o più righe. <see cref="Height"/> è l'altezza delle maiuscole in unità di disegno.</summary>
public sealed class TextEntity(Layer layer, Vector2 position, double height, string value) : Entity(layer)
{
    /// <summary>Rapporto tipico tra larghezza media di un carattere e altezza, usato per stimare l'ingombro.</summary>
    public const double AverageCharacterWidth = 0.7;

    public Vector2 Position { get; set; } = position;
    public double Height { get; set; } = height;
    public string Value { get; set; } = value;
    public double Rotation { get; set; }
    public double WidthFactor { get; set; } = 1;
    public double LineSpacing { get; set; } = 5.0 / 3.0;
    public TextHorizontalAlignment HorizontalAlignment { get; set; }
    public TextVerticalAlignment VerticalAlignment { get; set; }

    public IReadOnlyList<string> Lines => Value.Split('\n');

    public override BoundingBox Bounds
    {
        get
        {
            var lines = Lines;
            var width = lines.Max(l => l.Length) * Height * AverageCharacterWidth * WidthFactor;
            var totalHeight = Height + (lines.Count - 1) * Height * LineSpacing;
            var x0 = HorizontalAlignment switch
            {
                TextHorizontalAlignment.Center => -width / 2,
                TextHorizontalAlignment.Right => -width,
                _ => 0,
            };
            var yTop = VerticalAlignment switch
            {
                TextVerticalAlignment.Top => 0,
                TextVerticalAlignment.Middle => totalHeight / 2,
                _ => Height,
            };
            var rotation = Matrix2D.Rotation(Rotation) * Matrix2D.Translation(Position);
            return BoundingBox.FromPoints(
            [
                rotation.Transform(new Vector2(x0, yTop)),
                rotation.Transform(new Vector2(x0 + width, yTop)),
                rotation.Transform(new Vector2(x0, yTop - totalHeight)),
                rotation.Transform(new Vector2(x0 + width, yTop - totalHeight)),
            ]);
        }
    }
}

/// <summary>Definizione di blocco: entità in coordinate locali rispetto a <see cref="BasePoint"/>.</summary>
public sealed class BlockDefinition(string name)
{
    public string Name { get; } = name;
    public Vector2 BasePoint { get; set; }
    public List<Entity> Entities { get; } = [];

    public BoundingBox Bounds => Entities.Aggregate(BoundingBox.Empty, (box, e) => box.Union(e.Bounds));

    public override string ToString() => Name;
}

/// <summary>
/// Riferimento a un blocco. Oltre al blocco porta entità proprie già posizionate (attributi), disegnate senza trasformazione.
/// </summary>
public sealed class InsertEntity(Layer layer, BlockDefinition block) : Entity(layer)
{
    public BlockDefinition Block { get; } = block;

    /// <summary>Trasformazione dalle coordinate del blocco a quelle del contenitore.</summary>
    public Matrix2D Transform { get; set; } = Matrix2D.Identity;

    public List<Entity> Attributes { get; } = [];

    public static Matrix2D BuildTransform(Vector2 basePoint, Vector2 position, double scaleX, double scaleY, double rotation) =>
        Matrix2D.Translation(-basePoint) * Matrix2D.Scaling(scaleX, scaleY) * Matrix2D.Rotation(rotation) * Matrix2D.Translation(position);

    public override BoundingBox Bounds
    {
        get
        {
            var local = Block.Bounds;
            var box = local.IsEmpty
                ? BoundingBox.Empty
                : BoundingBox.FromPoints(
                [
                    Transform.Transform(local.Min),
                    Transform.Transform(local.Max),
                    Transform.Transform(new Vector2(local.Min.X, local.Max.Y)),
                    Transform.Transform(new Vector2(local.Max.X, local.Min.Y)),
                ]);
            return Attributes.Aggregate(box, (b, a) => b.Union(a.Bounds));
        }
    }
}

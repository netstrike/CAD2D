using Cad.Geometry;

namespace Cad.Document;

public abstract class Entity
{
    protected Entity(Layer layer) => Layer = layer;

    public Layer Layer { get; set; }
    public EntityColor Color { get; set; } = EntityColor.ByLayer;

    /// <summary>
    /// Oggetto da cui l'entità è stata letta (per esempio l'entità ACadSharp). Serve al salvataggio per riscrivere
    /// intatto ciò che non è stato modificato. Le copie e le entità trasformate non lo ereditano.
    /// </summary>
    public object? SourceTag { get; set; }

    /// <summary>
    /// Se l'entità è un originale letto da file spostato, ruotato o specchiato: l'originale e la trasformazione.
    /// Il salvataggio può così trasformare l'oggetto originale e conservarne tutte le proprietà.
    /// </summary>
    public SourceTransform? DerivedFrom { get; private set; }

    /// <summary>Ingombro in coordinate del contenitore (modello o blocco).</summary>
    public abstract BoundingBox Bounds { get; }

    /// <summary>Punti di presa (grip) mostrati quando l'entità è selezionata.</summary>
    public abstract IReadOnlyList<Vector2> Grips { get; }

    /// <summary>Nuova entità trasformata; l'originale resta invariata. Pensata per trasformazioni conformi (sposta, ruota, scala, specchia).</summary>
    public Entity Transformed(Matrix2D m)
    {
        var result = TransformCore(m);
        result.Layer = Layer;
        result.Color = Color;
        result.DerivedFrom = SourceTag is not null
            ? new SourceTransform(SourceTag, m)
            : DerivedFrom is { } d ? d with { Transform = d.Transform * m } : null;
        return result;
    }

    /// <summary>Nuova entità con il grip <paramref name="index"/> spostato in <paramref name="position"/>.</summary>
    public Entity WithGripMoved(int index, Vector2 position)
    {
        var result = MoveGripCore(index, position);
        result.Layer = Layer;
        result.Color = Color;
        return result;
    }

    protected abstract Entity TransformCore(Matrix2D m);

    /// <summary>Di default un grip sposta l'intera entità.</summary>
    protected virtual Entity MoveGripCore(int index, Vector2 position) =>
        TransformCore(Matrix2D.Translation(position - Grips[index]));

    /// <summary>Fattore di scala lineare di una trasformazione conforme.</summary>
    protected static double LinearScale(Matrix2D m) => Math.Sqrt(Math.Abs(m.Determinant));

    /// <summary>Nuova direzione di un angolo dopo la trasformazione.</summary>
    protected static double TransformAngle(Matrix2D m, double angle) => m.TransformVector(Vector2.FromPolar(1, angle)).Angle;
}

/// <summary>Oggetto originale e trasformazione da applicargli per ottenere l'entità attuale.</summary>
public readonly record struct SourceTransform(object Source, Matrix2D Transform);

public sealed class LineEntity(Layer layer, Vector2 start, Vector2 end) : Entity(layer)
{
    public Vector2 Start { get; set; } = start;
    public Vector2 End { get; set; } = end;
    public Segment2D Segment => new(Start, End);
    public override BoundingBox Bounds => Segment.Bounds;
    public override IReadOnlyList<Vector2> Grips => [Start, Segment.Midpoint, End];

    protected override Entity TransformCore(Matrix2D m) => new LineEntity(Layer, m.Transform(Start), m.Transform(End));

    protected override Entity MoveGripCore(int index, Vector2 position) => index switch
    {
        0 => new LineEntity(Layer, position, End),
        2 => new LineEntity(Layer, Start, position),
        _ => base.MoveGripCore(index, position),
    };
}

public sealed class CircleEntity(Layer layer, Vector2 center, double radius) : Entity(layer)
{
    public Vector2 Center { get; set; } = center;
    public double Radius { get; set; } = radius;
    public override BoundingBox Bounds => new Circle2D(Center, Radius).Bounds;
    public override IReadOnlyList<Vector2> Grips =>
        [Center, Center + new Vector2(Radius, 0), Center + new Vector2(0, Radius), Center - new Vector2(Radius, 0), Center - new Vector2(0, Radius)];

    protected override Entity TransformCore(Matrix2D m) => new CircleEntity(Layer, m.Transform(Center), Radius * LinearScale(m));

    /// <summary>Il grip al centro sposta il cerchio, quelli sui quadranti cambiano il raggio.</summary>
    protected override Entity MoveGripCore(int index, Vector2 position) => index == 0
        ? base.MoveGripCore(index, position)
        : new CircleEntity(Layer, Center, Vector2.Distance(Center, position));
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
    public override IReadOnlyList<Vector2> Grips => [Arc.StartPoint, Arc.Midpoint, Arc.EndPoint, Center];

    protected override Entity TransformCore(Matrix2D m)
    {
        var start = TransformAngle(m, StartAngle);
        var end = TransformAngle(m, EndAngle);
        var radius = Radius * LinearScale(m);
        // Una simmetria inverte il verso di percorrenza: si scambiano inizio e fine.
        return m.Determinant < 0
            ? new ArcEntity(Layer, m.Transform(Center), radius, end, start)
            : new ArcEntity(Layer, m.Transform(Center), radius, start, end);
    }

    /// <summary>I grip su estremi e punto medio ridisegnano l'arco per tre punti; quello al centro lo sposta.</summary>
    protected override Entity MoveGripCore(int index, Vector2 position)
    {
        if (index == 3)
        {
            return base.MoveGripCore(index, position);
        }

        var points = new[] { Arc.StartPoint, Arc.Midpoint, Arc.EndPoint };
        points[index] = position;
        return Arc2D.FromThreePoints(points[0], points[1], points[2]) is { } arc
            ? new ArcEntity(Layer, arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle)
            : new ArcEntity(Layer, Center, Radius, StartAngle, EndAngle);
    }
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

    public override IReadOnlyList<Vector2> Grips => [Center, Center + MajorAxis, Center + MinorAxis, Center - MajorAxis, Center - MinorAxis];

    protected override Entity TransformCore(Matrix2D m) => new EllipseEntity(
        Layer, m.Transform(Center), m.TransformVector(MajorAxis), m.TransformVector(MinorAxis), StartParameter, EndParameter);

    /// <summary>I grip sugli assi cambiano la lunghezza del semiasse corrispondente, mantenendo la direzione.</summary>
    protected override Entity MoveGripCore(int index, Vector2 position)
    {
        if (index == 0)
        {
            return base.MoveGripCore(index, position);
        }

        var axis = index % 2 == 1 ? MajorAxis : MinorAxis;
        var length = Math.Abs(Vector2.Dot(position - Center, axis.Normalized()));
        var resized = axis.Normalized() * length;
        return index % 2 == 1
            ? new EllipseEntity(Layer, Center, resized, MinorAxis, StartParameter, EndParameter)
            : new EllipseEntity(Layer, Center, MajorAxis, resized, StartParameter, EndParameter);
    }

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

    public override IReadOnlyList<Vector2> Grips => [.. Vertices.Select(v => v.Position)];

    protected override Entity TransformCore(Matrix2D m)
    {
        var flip = m.Determinant < 0 ? -1 : 1;
        return new PolylineEntity(Layer, Vertices.Select(v => new PolylineVertex(m.Transform(v.Position), v.Bulge * flip)), IsClosed);
    }

    protected override Entity MoveGripCore(int index, Vector2 position) =>
        new PolylineEntity(Layer, Vertices.Select((v, i) => i == index ? v with { Position = position } : v), IsClosed);

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
    public override IReadOnlyList<Vector2> Grips => Points.Count > 0 ? [Points[0]] : [];

    protected override Entity TransformCore(Matrix2D m) => new PolylinePathEntity(Layer, Points.Select(m.Transform), IsClosed);
}

public sealed class PointEntity(Layer layer, Vector2 position) : Entity(layer)
{
    public Vector2 Position { get; set; } = position;
    public override BoundingBox Bounds => new(Position, Position);
    public override IReadOnlyList<Vector2> Grips => [Position];

    protected override Entity TransformCore(Matrix2D m) => new PointEntity(Layer, m.Transform(Position));
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

    public override IReadOnlyList<Vector2> Grips => [Position];

    protected override Entity TransformCore(Matrix2D m)
    {
        var up = m.TransformVector(Vector2.FromPolar(1, Rotation).Perpendicular());
        return new TextEntity(Layer, m.Transform(Position), Height * up.Length, Value)
        {
            Rotation = TransformAngle(m, Rotation),
            WidthFactor = WidthFactor,
            LineSpacing = LineSpacing,
            HorizontalAlignment = HorizontalAlignment,
            VerticalAlignment = VerticalAlignment,
        };
    }

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

    /// <summary>Punto di inserimento: dove finisce il punto base del blocco.</summary>
    public Vector2 Position => Transform.Transform(Block.BasePoint);

    public override IReadOnlyList<Vector2> Grips => [Position];

    protected override Entity TransformCore(Matrix2D m)
    {
        var result = new InsertEntity(Layer, Block) { Transform = Transform * m };
        result.Attributes.AddRange(Attributes.Select(a => a.Transformed(m)));
        return result;
    }

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

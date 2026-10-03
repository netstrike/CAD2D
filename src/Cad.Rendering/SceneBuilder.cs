using Cad.Document;
using Cad.Geometry;

namespace Cad.Rendering;

/// <summary>
/// Converte un <see cref="CadDocument"/> in una <see cref="Scene"/>: esplode i blocchi applicando la loro trasformazione,
/// risolve i colori DaLayer/DaBlocco e approssima archi, cerchi ed ellissi con spezzate.
/// </summary>
public static class SceneBuilder
{
    /// <summary>Passo angolare massimo nell'approssimazione delle curve: 2° danno meno di un pixel di errore fino a raggi di circa 5000 px.</summary>
    public const double MaxAngleStep = Math.PI / 90;

    private const int MaxDepth = 32;

    public static Scene Build(CadDocument document)
    {
        var builder = new Builder { LinetypeScale = document.LinetypeScale };
        var root = new Context(Matrix2D.Identity, null, CadColor.White, Linetype.Continuous, 0, LineWeight.DefaultValue);
        foreach (var entity in document.ModelSpace)
        {
            builder.Add(entity, root);
        }

        return builder.ToScene();
    }

    /// <summary>
    /// Scena per un gruppo di entità sciolte (anteprime dei comandi, evidenziazione della selezione).
    /// Con <paramref name="color"/> tutte le entità prendono quel colore.
    /// </summary>
    public static Scene BuildEntities(IEnumerable<Entity> entities, CadColor? color = null)
    {
        // Anteprime ed evidenziazione: linee continue, i tratteggi bastano come contorno.
        var builder = new Builder { ColorOverride = color, IgnoreVisibility = true, IgnoreLinetypes = true, HatchOutlinesOnly = color is not null };
        var root = new Context(Matrix2D.Identity, null, CadColor.White, Linetype.Continuous, 0, LineWeight.DefaultValue);
        foreach (var entity in entities)
        {
            builder.Add(entity, root);
        }

        return builder.ToScene();
    }

    /// <summary>Stato ereditato da un inserimento di blocco verso le entità che contiene.</summary>
    private readonly record struct Context(Matrix2D Transform, Layer? InsertLayer, CadColor InsertColor, Linetype InsertLinetype, int Depth, int InsertWeight);

    private sealed class Builder
    {
        public Dictionary<(CadColor, int), RenderBatch> Batches { get; } = [];

        /// <summary>Spessore (centesimi di mm, già risolto) dell'entità che si sta aggiungendo.</summary>
        private int _weight = LineWeight.DefaultValue;
        public List<RenderText> Texts { get; } = [];
        public List<RenderPoint> Points { get; } = [];
        public int EntityCount { get; private set; }
        public Dictionary<CadColor, FillBatch> Fills { get; } = [];
        public CadColor? ColorOverride { get; init; }
        public bool IgnoreVisibility { get; init; }
        public bool IgnoreLinetypes { get; init; }
        public bool HatchOutlinesOnly { get; init; }
        public double LinetypeScale { get; init; } = 1;

        public Scene ToScene() => new([.. Batches.Values], Texts, Points, EntityCount, [.. Fills.Values]);

        public void Add(Entity entity, Context context)
        {
            // Nei blocchi il layer "0" è un segnaposto: l'entità prende il layer dell'inserimento.
            var layer = context.InsertLayer is not null && entity.Layer.Name == Layer.DefaultName
                ? context.InsertLayer
                : entity.Layer;
            if (!layer.IsVisible && !IgnoreVisibility)
            {
                return;
            }

            var color = ColorOverride ?? entity.Color.Source switch
            {
                ColorSource.Explicit => entity.Color.Value,
                ColorSource.ByBlock => context.InsertColor,
                _ => layer.Color,
            };

            var linetype = entity.Linetype is null ? layer.Linetype
                : ReferenceEquals(entity.Linetype, Linetype.ByBlock) ? context.InsertLinetype
                : entity.Linetype;

            var weight = entity.LineWeight switch
            {
                LineWeight.ByLayer => layer.LineWeight,
                LineWeight.ByBlock => context.InsertWeight,
                var w => w,
            };
            _weight = weight < 0 ? LineWeight.DefaultValue : weight;

            if (entity is InsertEntity insert)
            {
                AddInsert(insert, context, layer, color, linetype);
                return;
            }

            if (entity is DimensionEntity dimension)
            {
                AddDimension(dimension, context, layer, color);
                return;
            }

            if (entity is ICompositeEntity composite)
            {
                if (context.Depth < MaxDepth)
                {
                    var inner = new Context(context.Transform, layer, color, linetype, context.Depth + 1, _weight);
                    foreach (var part in composite.Explode())
                    {
                        Add(part, inner);
                    }
                }

                return;
            }

            EntityCount++;
            var m = context.Transform;
            var dash = IgnoreLinetypes || linetype.IsContinuous
                ? null
                : linetype.Pattern;
            var dashScale = LinetypeScale * entity.LinetypeScale * Math.Sqrt(Math.Abs(m.Determinant));
            switch (entity)
            {
                case LineEntity line:
                    AddCurve(color, [m.Transform(line.Start), m.Transform(line.End)], dash, dashScale);
                    break;
                case CircleEntity circle:
                    AddCurve(color, Tessellate(new Arc2D(circle.Center, circle.Radius, 0, Math.Tau), m), dash, dashScale);
                    break;
                case ArcEntity arc:
                    AddCurve(color, Tessellate(arc.Arc, m), dash, dashScale);
                    break;
                case EllipseEntity ellipse:
                    AddCurve(color, Tessellate(ellipse, m), dash, dashScale);
                    break;
                case PolylineEntity polyline:
                    AddCurve(color, Tessellate(polyline, m), dash, dashScale);
                    break;
                case PolylinePathEntity path:
                    AddCurve(color, [.. path.Points.Select(m.Transform), .. path.IsClosed && path.Points.Count > 0 ? [m.Transform(path.Points[0])] : Array.Empty<Vector2>()], dash, dashScale);
                    break;
                case SolidEntity solid:
                    AddFill(color, [[.. solid.Corners.Select(m.Transform)]]);
                    break;
                case HatchEntity hatch:
                    AddHatch(hatch, m, color);
                    break;
                case PointEntity point:
                    Points.Add(new RenderPoint(m.Transform(point.Position), color));
                    break;
                case TextEntity text:
                    AddText(text, m, color);
                    break;
            }
        }

        private void AddInsert(InsertEntity insert, Context context, Layer layer, CadColor color, Linetype linetype)
        {
            if (context.Depth >= MaxDepth)
            {
                return;
            }

            var weight = _weight;
            var inner = new Context(insert.Transform * context.Transform, layer, color, linetype, context.Depth + 1, weight);
            foreach (var child in insert.Block.Entities)
            {
                Add(child, inner);
            }

            // Gli attributi sono già in coordinate del contenitore dell'inserimento.
            var attributes = context with { InsertLayer = layer, InsertColor = color, InsertLinetype = linetype, Depth = context.Depth + 1, InsertWeight = weight };
            foreach (var attribute in insert.Attributes)
            {
                Add(attribute, attributes);
            }
        }

        /// <summary>Quota: la grafica letta dal file se c'è, altrimenti quella generata dai punti di definizione.</summary>
        private void AddDimension(DimensionEntity dimension, Context context, Layer layer, CadColor color)
        {
            if (context.Depth >= MaxDepth)
            {
                return;
            }

            if (dimension.Graphics is { } graphics)
            {
                var inner = new Context(dimension.GraphicsTransform * context.Transform, layer, color, Linetype.Continuous, context.Depth + 1, _weight);
                foreach (var child in graphics.Entities)
                {
                    Add(child, inner);
                }

                return;
            }

            var generated = new Context(context.Transform, layer, color, Linetype.Continuous, context.Depth + 1, _weight);
            foreach (var part in dimension.Explode())
            {
                Add(part, generated);
            }
        }

        private void AddCurve(CadColor color, Vector2[] points, IReadOnlyList<double>? dash, double dashScale)
        {
            if (dash is null)
            {
                AddPolyline(color, points);
                return;
            }

            foreach (var piece in Patterns.Dash(points, dash, dashScale))
            {
                AddPolyline(color, piece);
            }
        }

        private void AddFill(CadColor color, IReadOnlyList<Vector2[]> rings)
        {
            if (!Fills.TryGetValue(color, out var batch))
            {
                batch = new FillBatch(color);
                Fills.Add(color, batch);
            }

            batch.Rings.AddRange(Patterns.OrientForWinding(rings));
        }

        private void AddHatch(HatchEntity hatch, Matrix2D m, CadColor color)
        {
            var rings = hatch.Loops
                .Select(loop => Tessellate(new PolylineEntity(hatch.Layer, loop, isClosed: true), m))
                .Where(r => r.Length >= 3)
                .ToList();
            if (HatchOutlinesOnly)
            {
                foreach (var ring in rings)
                {
                    AddPolyline(color, ring);
                }

                return;
            }

            if (hatch.IsSolid)
            {
                AddFill(color, rings);
                return;
            }

            var budget = new Ref<int>(Patterns.MaxHatchSegments);
            foreach (var line in hatch.PatternLines)
            {
                var transformed = line.Transformed(m);
                foreach (var (from, to) in Patterns.HatchLines(rings, transformed, budget))
                {
                    AddPolyline(color, [from, to]);
                }
            }
        }

        private void AddPolyline(CadColor color, Vector2[] points)
        {
            if (points.Length < 2)
            {
                return;
            }

            if (!Batches.TryGetValue((color, _weight), out var batch))
            {
                batch = new RenderBatch(color, _weight / 100.0);
                Batches.Add((color, _weight), batch);
            }

            batch.Polylines.Add(points);
        }

        private void AddText(TextEntity text, Matrix2D m, CadColor color)
        {
            var lines = text.Lines;
            var direction = Vector2.FromPolar(1, text.Rotation);
            var up = direction.Perpendicular();
            var lineStep = text.Height * text.LineSpacing;
            var totalHeight = text.Height + (lines.Count - 1) * lineStep;

            // Distanza lungo "up" dal punto di inserimento alla linea di base della prima riga.
            var firstBaseline = text.VerticalAlignment switch
            {
                TextVerticalAlignment.Top => -text.Height,
                TextVerticalAlignment.Middle => totalHeight / 2 - text.Height,
                TextVerticalAlignment.Bottom => (lines.Count - 1) * lineStep,
                _ => 0,
            };

            var worldDirection = m.TransformVector(direction);
            var worldUp = m.TransformVector(up);
            var heightScale = worldUp.Length;
            if (Tolerance.IsZero(heightScale))
            {
                return;
            }

            for (var i = 0; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var local = new TextEntity(text.Layer, text.Position + up * (firstBaseline - i * lineStep), text.Height, lines[i])
                {
                    Rotation = text.Rotation,
                    WidthFactor = text.WidthFactor,
                    HorizontalAlignment = text.HorizontalAlignment,
                };
                var corners = local.Bounds;
                Texts.Add(new RenderText(
                    lines[i],
                    m.Transform(local.Position),
                    text.Height * heightScale,
                    worldDirection.Angle,
                    text.WidthFactor * worldDirection.Length / heightScale,
                    text.HorizontalAlignment,
                    color,
                    BoundingBox.FromPoints(
                    [
                        m.Transform(corners.Min),
                        m.Transform(corners.Max),
                        m.Transform(new Vector2(corners.Min.X, corners.Max.Y)),
                        m.Transform(new Vector2(corners.Max.X, corners.Min.Y)),
                    ]),
                    text.Style?.FontFamily ?? TextStyle.DefaultFamily,
                    text.Style?.ObliqueAngle ?? 0));
            }
        }
    }

    public static Vector2[] Tessellate(Arc2D arc, Matrix2D m)
    {
        var steps = StepsFor(arc.Sweep);
        var points = new Vector2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            points[i] = m.Transform(arc.PointAt((double)i / steps));
        }

        return points;
    }

    public static Vector2[] Tessellate(EllipseEntity ellipse, Matrix2D m)
    {
        var sweep = ellipse.Sweep;
        var steps = StepsFor(sweep);
        var points = new Vector2[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            points[i] = m.Transform(ellipse.PointAt(ellipse.StartParameter + sweep * i / steps));
        }

        return points;
    }

    public static Vector2[] Tessellate(PolylineEntity polyline, Matrix2D m)
    {
        if (polyline.Vertices.Count == 0)
        {
            return [];
        }

        var points = new List<Vector2> { m.Transform(polyline.Vertices[0].Position) };
        for (var i = 0; i < polyline.SegmentCount; i++)
        {
            var (segment, arc) = polyline.GetSegment(i);
            if (arc is not { } a)
            {
                points.Add(m.Transform(segment.End));
                continue;
            }

            // L'arco è sempre antiorario: per un bulge negativo va percorso al contrario.
            var arcPoints = Tessellate(a, m);
            if (polyline.Vertices[i].Bulge < 0)
            {
                Array.Reverse(arcPoints);
            }

            points.AddRange(arcPoints.Skip(1));
        }

        return [.. points];
    }

    private static int StepsFor(double sweep) => Math.Max(4, (int)Math.Ceiling(sweep / MaxAngleStep));
}

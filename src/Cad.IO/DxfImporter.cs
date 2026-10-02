using ACadSharp.IO;
using Cad.Document;
using Cad.Geometry;
using Acad = ACadSharp;
using AcadEntities = ACadSharp.Entities;

namespace Cad.IO;

/// <summary>
/// Il documento ACadSharp letto dal file e l'elenco delle sue entità di modello convertite.
/// Le entità non convertite (tipi non gestiti) restano nel documento e vengono riscritte intatte al salvataggio.
/// </summary>
public sealed class DxfSource(Acad.CadDocument document)
{
    public Acad.CadDocument Document { get; } = document;

    public HashSet<object> ConvertedEntities { get; } = new(ReferenceEqualityComparer.Instance);
}

/// <summary>Risultato di un'importazione: il documento e gli errori segnalati dal lettore (parti del file saltate).</summary>
public sealed record ImportResult(CadDocument Document, IReadOnlyList<string> Errors);

/// <summary>
/// Legge un file DXF (ASCII o binario) con ACadSharp e lo converte nel modello interno.
/// </summary>
public static class DxfImporter
{
    /// <summary>Profondità massima di blocchi annidati: oltre si assume un riferimento circolare.</summary>
    private const int MaxBlockDepth = 32;

    /// <summary>Punti per approssimare spline: ACadSharp ne genera tanti quanti richiesti.</summary>
    private const int SplinePrecision = 64;

    private const uint SplineFitIterations = 1000;

    public static ImportResult Load(string path)
    {
        using var stream = File.OpenRead(path);
        var result = Load(stream);
        result.Document.FilePath = path;
        return result;
    }

    public static ImportResult Load(Stream stream)
    {
        var errors = new List<string>();
        var source = DxfReader.Read(stream, (_, e) =>
        {
            // Gli avvisi di ACadSharp riguardano quasi sempre dizionari irrilevanti per il 2D: si tengono solo gli errori.
            if (e.NotificationType is NotificationType.Error)
            {
                errors.Add(e.Message);
            }
        });

        return new ImportResult(Convert(source), errors);
    }

    public static CadDocument Convert(Acad.CadDocument source)
    {
        var dxfSource = new DxfSource(source);
        var converter = new Converter(new CadDocument { Source = dxfSource });
        converter.ConvertLayers(source);
        foreach (var entity in source.Entities)
        {
            if (converter.Add(entity, converter.Document.ModelSpace, depth: 0) is { } converted)
            {
                converted.SourceTag = entity;
                dxfSource.ConvertedEntities.Add(entity);
            }
        }

        return converter.Document;
    }

    private sealed class Converter(CadDocument document)
    {
        private readonly HashSet<string> _convertedBlocks = new(StringComparer.OrdinalIgnoreCase);

        public CadDocument Document { get; } = document;

        public void ConvertLayers(Acad.CadDocument source)
        {
            foreach (var layer in source.Layers)
            {
                var target = Document.GetOrAddLayer(layer.Name);
                target.Color = ToRgb(layer.Color) ?? CadColor.White;
                target.IsOn = layer.IsOn;
                target.IsFrozen = layer.Flags.HasFlag(Acad.Tables.LayerFlags.Frozen);
                target.IsLocked = layer.Flags.HasFlag(Acad.Tables.LayerFlags.Locked);
            }
        }

        public Entity? Add(AcadEntities.Entity source, List<Entity> target, int depth)
        {
            if (source.IsInvisible)
            {
                return null;
            }

            var converted = ConvertEntity(source, depth);
            if (converted is null)
            {
                Document.AddUnsupported(source.ObjectName);
                return null;
            }

            converted.Color = ToEntityColor(source.Color);
            target.Add(converted);
            return converted;
        }

        private Entity? ConvertEntity(AcadEntities.Entity source, int depth)
        {
            var layer = Document.GetOrAddLayer(source.Layer?.Name ?? Layer.DefaultName);
            switch (source)
            {
                case AcadEntities.Line line:
                    return new LineEntity(layer, ToVector(line.StartPoint), ToVector(line.EndPoint));

                // Arc deriva da Circle: va controllato prima.
                case AcadEntities.Arc arc:
                {
                    var ocs = Ocs.From(arc.Normal);
                    var start = ocs.Angle(arc.StartAngle);
                    var end = ocs.Angle(arc.EndAngle);
                    return ocs.Mirrored
                        ? new ArcEntity(layer, ocs.Point(arc.Center), arc.Radius, end, start)
                        : new ArcEntity(layer, ocs.Point(arc.Center), arc.Radius, start, end);
                }

                case AcadEntities.Circle circle:
                    return new CircleEntity(layer, Ocs.From(circle.Normal).Point(circle.Center), circle.Radius);

                case AcadEntities.Ellipse ellipse:
                {
                    var major = ToVector(ellipse.MajorAxisEndPoint);
                    // Asse minore = normale × asse maggiore, ridotto del rapporto tra gli assi.
                    var minor = (ellipse.Normal.Z < 0 ? -major.Perpendicular() : major.Perpendicular()) * ellipse.RadiusRatio;
                    return new EllipseEntity(layer, ToVector(ellipse.Center), major, minor, ellipse.StartParameter, ellipse.EndParameter);
                }

                case AcadEntities.LwPolyline lw:
                {
                    var ocs = Ocs.From(lw.Normal);
                    return new PolylineEntity(
                        layer,
                        lw.Vertices.Select(v => new PolylineVertex(ocs.Point(v.Location.X, v.Location.Y), ocs.Bulge(v.Bulge))),
                        lw.IsClosed);
                }

                case AcadEntities.IPolyline polyline when source is not AcadEntities.PolyfaceMesh and not AcadEntities.PolygonMesh:
                {
                    var ocs = source is AcadEntities.Polyline2D p2 ? Ocs.From(p2.Normal) : Ocs.World;
                    return new PolylineEntity(
                        layer,
                        polyline.Vertices.Select(v => new PolylineVertex(
                            ocs.Point(v.Location[0], v.Location[1]),
                            ocs.Bulge(v.Bulge))),
                        polyline.IsClosed);
                }

                case AcadEntities.Spline spline:
                    return ConvertSpline(spline, layer);

                case AcadEntities.Point point:
                    return new PointEntity(layer, ToVector(point.Location));

                case AcadEntities.TextEntity text:
                    return ConvertText(text, layer);

                case AcadEntities.MText mtext:
                    return ConvertMText(mtext, layer);

                case AcadEntities.Insert insert:
                    return ConvertInsert(insert, layer, depth);

                // Le quote hanno la loro grafica già pronta in un blocco anonimo, in coordinate mondo.
                case AcadEntities.Dimension { Block: { } block }:
                {
                    var definition = ConvertBlock(block, depth);
                    return definition is null ? null : new InsertEntity(layer, definition);
                }

                default:
                    return null;
            }
        }

        private InsertEntity? ConvertInsert(AcadEntities.Insert insert, Layer layer, int depth)
        {
            if (insert.Block is null)
            {
                return null;
            }

            var definition = ConvertBlock(insert.Block, depth);
            if (definition is null)
            {
                return null;
            }

            var ocs = Ocs.From(insert.Normal);
            var result = new InsertEntity(layer, definition)
            {
                Transform = InsertEntity.BuildTransform(
                    definition.BasePoint,
                    new Vector2(insert.InsertPoint.X, insert.InsertPoint.Y),
                    insert.XScale,
                    insert.YScale,
                    insert.Rotation) * ocs.Matrix,
            };

            // MINSERT: righe e colonne di copie, convertite in un blocco che le contiene tutte.
            if (insert.RowCount > 1 || insert.ColumnCount > 1)
            {
                var grid = Document.GetOrAddBlock($"*MINSERT-{Guid.NewGuid():N}");
                for (var row = 0; row < Math.Max((int)insert.RowCount, 1); row++)
                {
                    for (var column = 0; column < Math.Max((int)insert.ColumnCount, 1); column++)
                    {
                        var offset = new Vector2(column * insert.ColumnSpacing, row * insert.RowSpacing);
                        grid.Entities.Add(new InsertEntity(layer, definition)
                        {
                            Transform = Matrix2D.Translation(offset) * result.Transform,
                        });
                    }
                }

                result = new InsertEntity(layer, grid);
            }

            foreach (var attribute in insert.Attributes)
            {
                Add(attribute, result.Attributes, depth);
            }

            return result;
        }

        private BlockDefinition? ConvertBlock(Acad.Tables.BlockRecord record, int depth)
        {
            if (depth >= MaxBlockDepth)
            {
                return null;
            }

            var definition = Document.GetOrAddBlock(record.Name);
            if (_convertedBlocks.Add(record.Name))
            {
                if (record.BlockEntity is { } block)
                {
                    definition.BasePoint = new Vector2(block.BasePoint.X, block.BasePoint.Y);
                }

                foreach (var entity in record.Entities)
                {
                    Add(entity, definition.Entities, depth + 1);
                }
            }

            return definition;
        }

        private static PolylinePathEntity? ConvertSpline(AcadEntities.Spline spline, Layer layer)
        {
            // Le spline definite solo da punti di passaggio vanno prima convertite in punti di controllo.
            if (spline.ControlPoints.Count == 0 && spline.FitPoints.Count > 1)
            {
                spline.UpdateFromFitPoints(SplineFitIterations);
            }

            if (spline.TryPolygonalVertexes(SplinePrecision, out var points) && points.Count > 1)
            {
                return new PolylinePathEntity(layer, points.Select(ToVector), spline.IsClosed);
            }

            // Ultima risorsa: la spezzata dei punti noti, meglio di niente a schermo.
            var fallback = spline.FitPoints.Count > 1 ? spline.FitPoints : spline.ControlPoints;
            return fallback.Count > 1 ? new PolylinePathEntity(layer, fallback.Select(ToVector), spline.IsClosed) : null;
        }

        private static TextEntity ConvertText(AcadEntities.TextEntity text, Layer layer)
        {
            var aligned = text.HorizontalAlignment != AcadEntities.TextHorizontalAlignment.Left ||
                          text.VerticalAlignment != AcadEntities.TextVerticalAlignmentType.Baseline;
            var ocs = Ocs.From(text.Normal);
            var anchor = aligned ? text.AlignmentPoint : text.InsertPoint;
            return new TextEntity(layer, ocs.Point(anchor), text.Height, TextCodes.DecodeSpecialCharacters(text.Value ?? string.Empty))
            {
                Rotation = ocs.Angle(text.Rotation),
                WidthFactor = text.WidthFactor > 0 ? text.WidthFactor : 1,
                HorizontalAlignment = text.HorizontalAlignment switch
                {
                    AcadEntities.TextHorizontalAlignment.Center or AcadEntities.TextHorizontalAlignment.Middle => TextHorizontalAlignment.Center,
                    AcadEntities.TextHorizontalAlignment.Right => TextHorizontalAlignment.Right,
                    _ => TextHorizontalAlignment.Left,
                },
                VerticalAlignment = text.HorizontalAlignment == AcadEntities.TextHorizontalAlignment.Middle
                    ? TextVerticalAlignment.Middle
                    : text.VerticalAlignment switch
                    {
                        AcadEntities.TextVerticalAlignmentType.Bottom => TextVerticalAlignment.Bottom,
                        AcadEntities.TextVerticalAlignmentType.Middle => TextVerticalAlignment.Middle,
                        AcadEntities.TextVerticalAlignmentType.Top => TextVerticalAlignment.Top,
                        _ => TextVerticalAlignment.Baseline,
                    },
            };
        }

        private static TextEntity ConvertMText(AcadEntities.MText mtext, Layer layer)
        {
            var value = TextCodes.MTextToPlain(mtext.Value ?? string.Empty);
            var attachment = (int)mtext.AttachmentPoint;
            // AttachmentPoint: 1..9 = alto/medio/basso × sinistra/centro/destra.
            var row = attachment is >= 1 and <= 9 ? (attachment - 1) / 3 : 0;
            var column = attachment is >= 1 and <= 9 ? (attachment - 1) % 3 : 0;
            return new TextEntity(layer, ToVector(mtext.InsertPoint), mtext.Height, value)
            {
                Rotation = mtext.Rotation,
                LineSpacing = 5.0 / 3.0 * (mtext.LineSpacing > 0 ? mtext.LineSpacing : 1),
                HorizontalAlignment = (TextHorizontalAlignment)column,
                VerticalAlignment = row switch
                {
                    0 => TextVerticalAlignment.Top,
                    1 => TextVerticalAlignment.Middle,
                    _ => TextVerticalAlignment.Bottom,
                },
            };
        }

        private static EntityColor ToEntityColor(Acad.Color color)
        {
            if (color.IsByLayer)
            {
                return EntityColor.ByLayer;
            }

            if (color.IsByBlock)
            {
                return EntityColor.ByBlock;
            }

            return ToRgb(color) is { } rgb ? EntityColor.Explicit(rgb) : EntityColor.ByLayer;
        }

        private static CadColor? ToRgb(Acad.Color color)
        {
            if (color.IsByLayer || color.IsByBlock)
            {
                return null;
            }

            var rgb = color.GetRgb();
            return rgb.Length >= 3 ? new CadColor(rgb[0], rgb[1], rgb[2]) : null;
        }

        private static Vector2 ToVector(CSMath.XYZ p) => new(p.X, p.Y);
    }

    /// <summary>
    /// Sistema di coordinate dell'oggetto (OCS) per entità piane. Gestisce solo la normale +Z o -Z:
    /// con -Z il disegno è specchiato rispetto all'asse Y.
    /// </summary>
    private readonly record struct Ocs(bool Mirrored)
    {
        public static readonly Ocs World = new(false);

        public static Ocs From(CSMath.XYZ normal) => new(normal.Z < 0);

        public Matrix2D Matrix => Mirrored ? Matrix2D.Scaling(-1, 1) : Matrix2D.Identity;

        public Vector2 Point(double x, double y) => Mirrored ? new Vector2(-x, y) : new Vector2(x, y);

        public Vector2 Point(CSMath.XYZ p) => Point(p.X, p.Y);

        public double Angle(double angle) => Mirrored ? Math.PI - angle : angle;

        public double Bulge(double bulge) => Mirrored ? -bulge : bulge;
    }
}

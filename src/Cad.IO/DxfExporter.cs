using ACadSharp.IO;
using Cad.Document;
using Cad.Geometry;
using CSMath;
using Acad = ACadSharp;
using AcadEntities = ACadSharp.Entities;

namespace Cad.IO;

/// <summary>
/// Salva un documento in DXF. Se il disegno è stato letto da file, si riparte dal documento ACadSharp originale:
/// le entità non toccate e quelle che CAD2D non sa ancora gestire vengono riscritte così come sono;
/// quelle spostate o ruotate sono copie trasformate dell'originale; solo le entità nuove o modificate nella forma
/// vengono convertite da zero.
/// </summary>
public static class DxfExporter
{
    public static void Save(CadDocument document, string path)
    {
        var target = Prepare(document);
        using (var writer = new DxfWriter(path, target, binary: false))
        {
            writer.Write();
        }

        document.FilePath = path;
        document.MarkSaved();
    }

    public static void Save(CadDocument document, Stream stream)
    {
        var target = Prepare(document);
        using var writer = new DxfWriter(stream, target, binary: false);
        writer.Write();
    }

    /// <summary>Aggiorna (o crea) il documento ACadSharp in modo che rispecchi il modello.</summary>
    public static Acad.CadDocument Prepare(CadDocument document)
    {
        if (document.Source is not DxfSource source)
        {
            source = new DxfSource(new Acad.CadDocument());
            document.Source = source;
        }

        var target = source.Document;
        var layers = SyncLayers(document, target);

        // Entità convertite all'apertura (o al salvataggio precedente) che oggi non sono più nel modello così come erano.
        var kept = new HashSet<object>(document.ModelSpace.Where(e => e.SourceTag is not null).Select(e => e.SourceTag!), ReferenceEqualityComparer.Instance);
        foreach (var stale in target.Entities.Where(e => source.ConvertedEntities.Contains(e) && !kept.Contains(e)).ToList())
        {
            target.Entities.Remove(stale);
            source.ConvertedEntities.Remove(stale);
        }

        var blocks = new BlockWriter(target, layers);
        foreach (var entity in document.ModelSpace.Where(e => e.SourceTag is null))
        {
            var written = entity.DerivedFrom is { Source: AcadEntities.Entity original } derived
                ? CopyTransformed(target, original, derived.Transform)
                : null;
            if (written is not null)
            {
                // La copia trasformata tiene colore e proprietà dell'originale; il layer può essere cambiato.
                written.Layer = layers[entity.Layer.Name];
            }
            else
            {
                written = blocks.Convert(entity);
            }

            if (written is null)
            {
                continue;
            }

            target.Entities.Add(written);
            entity.SourceTag = written;
            source.ConvertedEntities.Add(written);
        }

        return target;
    }

    private static Dictionary<string, Acad.Tables.Layer> SyncLayers(CadDocument document, Acad.CadDocument target)
    {
        var result = new Dictionary<string, Acad.Tables.Layer>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in document.Layers)
        {
            if (!target.Layers.TryGetValue(layer.Name, out var acadLayer))
            {
                acadLayer = new Acad.Tables.Layer(layer.Name) { Color = new Acad.Color(layer.Color.R, layer.Color.G, layer.Color.B) };
                target.Layers.Add(acadLayer);
            }

            acadLayer.IsOn = layer.IsOn;
            result[layer.Name] = acadLayer;
        }

        return result;
    }

    /// <summary>
    /// Copia l'entità originale e ne trasforma la geometria: conserva tipo di linea, spessore, attributi, stile di quota.
    /// Restituisce null per i casi non gestiti, che vengono riconvertiti dal modello.
    /// </summary>
    private static AcadEntities.Entity? CopyTransformed(Acad.CadDocument target, AcadEntities.Entity original, Matrix2D m)
    {
        var copy = (AcadEntities.Entity)original.Clone();
        if (!TransformInPlace(original, copy, m))
        {
            return null;
        }

        // La grafica di una quota sta in un blocco anonimo che il clone condivide con l'originale:
        // serve un blocco nuovo con la grafica trasformata.
        if (copy is AcadEntities.Dimension { Block: { } graphics } dimension)
        {
            var record = new Acad.Tables.BlockRecord(UniqueAnonymousName(target, "*D"));
            foreach (var child in graphics.Entities)
            {
                var childCopy = (AcadEntities.Entity)child.Clone();
                if (TransformInPlace(child, childCopy, m))
                {
                    record.Entities.Add(childCopy);
                }
            }

            target.BlockRecords.Add(record);
            dimension.Block = record;
        }

        return copy;
    }

    /// <summary>
    /// Trasforma la geometria di <paramref name="copy"/> partendo da <paramref name="original"/>.
    /// Scritto a mano perché ApplyTransform di ACadSharp 3.8 sbaglia su polilinee e testi.
    /// </summary>
    private static bool TransformInPlace(AcadEntities.Entity original, AcadEntities.Entity copy, Matrix2D m)
    {
        var mirrored = m.Determinant < 0;
        double Angle(double angle) => m.TransformVector(Vector2.FromPolar(1, angle)).Angle;
        double Scale() => Math.Sqrt(Math.Abs(m.Determinant));

        switch (original, copy)
        {
            case (AcadEntities.Line from, AcadEntities.Line to):
                to.StartPoint = Apply(m, from.StartPoint);
                to.EndPoint = Apply(m, from.EndPoint);
                return true;

            case (AcadEntities.Arc from, AcadEntities.Arc to) when !IsFlipped(from.Normal):
                to.Center = Apply(m, from.Center);
                to.Radius = from.Radius * Scale();
                to.StartAngle = mirrored ? Angle(from.EndAngle) : Angle(from.StartAngle);
                to.EndAngle = mirrored ? Angle(from.StartAngle) : Angle(from.EndAngle);
                return true;

            case (AcadEntities.Circle from, AcadEntities.Circle to) when !IsFlipped(from.Normal):
                to.Center = Apply(m, from.Center);
                to.Radius = from.Radius * Scale();
                return true;

            case (AcadEntities.LwPolyline from, AcadEntities.LwPolyline to) when !IsFlipped(from.Normal):
                for (var i = 0; i < from.Vertices.Count; i++)
                {
                    var p = m.Transform(new Vector2(from.Vertices[i].Location.X, from.Vertices[i].Location.Y));
                    to.Vertices[i].Location = new XY(p.X, p.Y);
                    to.Vertices[i].Bulge = mirrored ? -from.Vertices[i].Bulge : from.Vertices[i].Bulge;
                }

                return true;

            case (AcadEntities.Ellipse from, AcadEntities.Ellipse to) when !IsFlipped(from.Normal) && !mirrored:
                to.Center = Apply(m, from.Center);
                var major = m.TransformVector(new Vector2(from.MajorAxisEndPoint.X, from.MajorAxisEndPoint.Y));
                to.MajorAxisEndPoint = new XYZ(major.X, major.Y, 0);
                return true;

            case (AcadEntities.Point from, AcadEntities.Point to):
                to.Location = Apply(m, from.Location);
                return true;

            case (AcadEntities.Spline from, AcadEntities.Spline to):
                for (var i = 0; i < from.ControlPoints.Count; i++)
                {
                    to.ControlPoints[i] = Apply(m, from.ControlPoints[i]);
                }

                for (var i = 0; i < from.FitPoints.Count; i++)
                {
                    to.FitPoints[i] = Apply(m, from.FitPoints[i]);
                }

                return true;

            case (AcadEntities.TextEntity from, AcadEntities.TextEntity to) when !IsFlipped(from.Normal):
                to.InsertPoint = Apply(m, from.InsertPoint);
                to.AlignmentPoint = Apply(m, from.AlignmentPoint);
                to.Rotation = Angle(from.Rotation);
                to.Height = from.Height * m.TransformVector(Vector2.FromPolar(1, from.Rotation).Perpendicular()).Length;
                return true;

            case (AcadEntities.MText from, AcadEntities.MText to):
                to.InsertPoint = Apply(m, from.InsertPoint);
                var direction = m.TransformVector(new Vector2(from.AlignmentPoint.X, from.AlignmentPoint.Y));
                to.AlignmentPoint = direction.Length > 0 ? new XYZ(direction.X, direction.Y, 0) : new XYZ(1, 0, 0);
                to.Height = from.Height * Scale();
                return true;

            case (AcadEntities.Insert from, AcadEntities.Insert to) when !IsFlipped(from.Normal):
            {
                // Matrice completa del blocco: scala e rotazione dell'inserimento, poi la trasformazione nuova.
                var local = Matrix2D.Scaling(from.XScale, from.YScale) * Matrix2D.Rotation(from.Rotation) * m;
                var xAxis = local.TransformVector(Vector2.UnitX);
                var yAxis = local.TransformVector(Vector2.UnitY);
                to.InsertPoint = Apply(m, from.InsertPoint);
                to.Rotation = xAxis.Angle;
                to.XScale = xAxis.Length;
                to.YScale = local.Determinant < 0 ? -yAxis.Length : yAxis.Length;
                foreach (var (attributeFrom, attributeTo) in from.Attributes.Zip(to.Attributes))
                {
                    TransformInPlace(attributeFrom, attributeTo, m);
                }

                return true;
            }

            case (AcadEntities.Dimension from, AcadEntities.Dimension to) when !IsFlipped(from.Normal):
                // Basta spostare i punti di definizione: la grafica è rigenerata a parte.
                to.ApplyTransform(new Transform(ToMatrix4(m)));
                return true;

            default:
                return false;
        }
    }

    private static bool IsFlipped(XYZ normal) => normal.Z < 0;

    private static XYZ Apply(Matrix2D m, XYZ p)
    {
        var result = m.Transform(new Vector2(p.X, p.Y));
        return new XYZ(result.X, result.Y, p.Z);
    }

    private static string UniqueAnonymousName(Acad.CadDocument target, string prefix)
    {
        var index = 1;
        while (target.BlockRecords.Contains($"{prefix}{index}"))
        {
            index++;
        }

        return $"{prefix}{index}";
    }

    internal static Matrix4 ToMatrix4(Matrix2D m) => new(
        m.M11, m.M21, 0, m.OffsetX,
        m.M12, m.M22, 0, m.OffsetY,
        0, 0, 1, 0,
        0, 0, 0, 1);

    private static Acad.Color ToAcadColor(EntityColor color) => color.Source switch
    {
        ColorSource.ByBlock => Acad.Color.ByBlock,
        ColorSource.Explicit => ToAcadColor(color.Value),
        _ => Acad.Color.ByLayer,
    };

    /// <summary>Usa l'indice ACI se il colore ne ha uno identico, altrimenti il colore RGB.</summary>
    private static Acad.Color ToAcadColor(CadColor rgb)
    {
        var index = Acad.Color.ApproxIndex(rgb.R, rgb.G, rgb.B);
        var candidate = new Acad.Color(index);
        var values = candidate.GetRgb();
        return values.Length >= 3 && values[0] == rgb.R && values[1] == rgb.G && values[2] == rgb.B
            ? candidate
            : new Acad.Color(rgb.R, rgb.G, rgb.B);
    }

    /// <summary>Converte entità del modello in entità ACadSharp, creando i blocchi che mancano nel file.</summary>
    private sealed class BlockWriter(Acad.CadDocument target, Dictionary<string, Acad.Tables.Layer> layers)
    {
        public AcadEntities.Entity? Convert(Entity entity)
        {
            AcadEntities.Entity? result = entity switch
            {
                LineEntity line => new AcadEntities.Line { StartPoint = ToXyz(line.Start), EndPoint = ToXyz(line.End) },
                CircleEntity circle => new AcadEntities.Circle { Center = ToXyz(circle.Center), Radius = circle.Radius },
                ArcEntity arc => new AcadEntities.Arc { Center = ToXyz(arc.Center), Radius = arc.Radius, StartAngle = arc.StartAngle, EndAngle = arc.EndAngle },
                EllipseEntity ellipse => ConvertEllipse(ellipse),
                PolylineEntity polyline => ConvertPolyline(polyline),
                PolylinePathEntity path => ConvertPolyline(new PolylineEntity(path.Layer, path.Points.Select(p => new PolylineVertex(p)), path.IsClosed)),
                PointEntity point => new AcadEntities.Point { Location = ToXyz(point.Position) },
                TextEntity text => ConvertText(text),
                InsertEntity insert => ConvertInsert(insert),
                _ => null,
            };

            if (result is not null)
            {
                result.Layer = layers.TryGetValue(entity.Layer.Name, out var layer) ? layer : target.Layers[Layer.DefaultName];
                result.Color = ToAcadColor(entity.Color);
            }

            return result;
        }

        private static AcadEntities.Ellipse ConvertEllipse(EllipseEntity ellipse)
        {
            var major = ellipse.MajorAxis;
            var minor = ellipse.MinorAxis;
            // Se l'asse minore sta a destra del maggiore, l'ellisse è vista dal basso: normale -Z.
            var normalZ = Vector2.Cross(major, minor) < 0 ? -1 : 1;
            return new AcadEntities.Ellipse
            {
                Center = ToXyz(ellipse.Center),
                MajorAxisEndPoint = ToXyz(major),
                RadiusRatio = major.Length > 0 ? minor.Length / major.Length : 1,
                StartParameter = ellipse.StartParameter,
                EndParameter = ellipse.EndParameter,
                Normal = new XYZ(0, 0, normalZ),
            };
        }

        private static AcadEntities.LwPolyline ConvertPolyline(PolylineEntity polyline)
        {
            var result = new AcadEntities.LwPolyline { IsClosed = polyline.IsClosed };
            foreach (var vertex in polyline.Vertices)
            {
                result.Vertices.Add(new AcadEntities.LwPolyline.Vertex(vertex.Position.X, vertex.Position.Y) { Bulge = vertex.Bulge });
            }

            return result;
        }

        private static AcadEntities.Entity ConvertText(TextEntity text)
        {
            if (text.Lines.Count > 1)
            {
                var row = text.VerticalAlignment switch
                {
                    TextVerticalAlignment.Top => 0,
                    TextVerticalAlignment.Middle => 1,
                    _ => 2,
                };
                return new AcadEntities.MText
                {
                    InsertPoint = ToXyz(text.Position),
                    Height = text.Height,
                    // La rotazione di un MTEXT si scrive come direzione dell'asse X del testo.
                    AlignmentPoint = new XYZ(Math.Cos(text.Rotation), Math.Sin(text.Rotation), 0),
                    Value = string.Join("\\P", text.Lines),
                    AttachmentPoint = (AcadEntities.AttachmentPointType)(row * 3 + (int)text.HorizontalAlignment + 1),
                };
            }

            var aligned = text.HorizontalAlignment != TextHorizontalAlignment.Left || text.VerticalAlignment != TextVerticalAlignment.Baseline;
            return new AcadEntities.TextEntity
            {
                InsertPoint = ToXyz(text.Position),
                AlignmentPoint = aligned ? ToXyz(text.Position) : XYZ.Zero,
                Height = text.Height,
                Rotation = text.Rotation,
                WidthFactor = text.WidthFactor,
                Value = text.Value,
                HorizontalAlignment = text.HorizontalAlignment switch
                {
                    TextHorizontalAlignment.Center => AcadEntities.TextHorizontalAlignment.Center,
                    TextHorizontalAlignment.Right => AcadEntities.TextHorizontalAlignment.Right,
                    _ => AcadEntities.TextHorizontalAlignment.Left,
                },
                VerticalAlignment = text.VerticalAlignment switch
                {
                    TextVerticalAlignment.Bottom => AcadEntities.TextVerticalAlignmentType.Bottom,
                    TextVerticalAlignment.Middle => AcadEntities.TextVerticalAlignmentType.Middle,
                    TextVerticalAlignment.Top => AcadEntities.TextVerticalAlignmentType.Top,
                    _ => AcadEntities.TextVerticalAlignmentType.Baseline,
                },
            };
        }

        private AcadEntities.Insert ConvertInsert(InsertEntity insert)
        {
            var record = GetOrCreateBlock(insert.Block);
            var m = insert.Transform;
            var xAxis = m.TransformVector(Vector2.UnitX);
            var yAxis = m.TransformVector(Vector2.UnitY);
            var result = new AcadEntities.Insert(record)
            {
                InsertPoint = ToXyz(insert.Position),
                Rotation = xAxis.Angle,
                XScale = xAxis.Length,
                // Una simmetria si scrive come scala Y negativa.
                YScale = m.Determinant < 0 ? -yAxis.Length : yAxis.Length,
                ZScale = 1,
            };
            return result;
        }

        private Acad.Tables.BlockRecord GetOrCreateBlock(BlockDefinition block)
        {
            if (target.BlockRecords.TryGetValue(block.Name, out var existing))
            {
                return existing;
            }

            var record = new Acad.Tables.BlockRecord(block.Name);
            record.BlockEntity.BasePoint = ToXyz(block.BasePoint);
            target.BlockRecords.Add(record);
            foreach (var entity in block.Entities)
            {
                if (Convert(entity) is { } converted)
                {
                    record.Entities.Add(converted);
                }
            }

            return record;
        }

        private static XYZ ToXyz(Vector2 v) => new(v.X, v.Y, 0);
    }
}

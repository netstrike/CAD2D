using System.Globalization;
using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>Come si modifica un valore nella palette Proprietà.</summary>
public enum PropertyKind
{
    /// <summary>Solo lettura (misure calcolate).</summary>
    ReadOnly,

    /// <summary>Testo libero.</summary>
    Text,

    /// <summary>Numero (lunghezze, coordinate, angoli in gradi).</summary>
    Number,

    /// <summary>Scelta da un elenco.</summary>
    Choice,
}

/// <summary>Una riga della palette Proprietà: valore comune alle entità, oppure null se diverso (Vari).</summary>
public sealed record PropertyRow(string Category, string Name, PropertyKind Kind, string? Value, IReadOnlyList<string> Choices, PropertyDefinition Definition)
{
    public bool IsMixed => Value is null;
}

/// <summary>
/// Definizione di una proprietà. <see cref="Set"/> riceve l'entità originale e il valore scritto e restituisce
/// l'entità che la sostituisce, oppure null se il valore non è valido.
/// </summary>
public sealed record PropertyDefinition(
    string Category,
    string Name,
    PropertyKind Kind,
    Func<Entity, string> Get,
    Func<Entity, string, Entity?>? Set = null,
    Func<CadDocument, IReadOnlyList<string>>? Choices = null);

/// <summary>Le proprietà mostrate nella palette per una selezione e la loro modifica annullabile.</summary>
public static class PropertySheet
{
    public const string General = "Generale";
    public const string Geometry = "Geometria";
    public const string Content = "Testo";
    public const string Mixed = "*Vari*";
    private const string Yes = "Sì";
    private const string No = "No";

    /// <summary>Righe per le entità date: le proprietà generali sempre, quelle geometriche se sono tutte dello stesso tipo.</summary>
    public static IReadOnlyList<PropertyRow> For(CadDocument document, IReadOnlyList<Entity> entities)
    {
        if (entities.Count == 0)
        {
            return [];
        }

        var definitions = new List<PropertyDefinition>(GeneralDefinitions(document));
        var type = entities[0].GetType();
        if (entities.All(e => e.GetType() == type))
        {
            definitions.AddRange(SpecificDefinitions(entities[0], document));
        }

        return definitions.Select(d =>
        {
            var first = d.Get(entities[0]);
            var common = entities.All(e => d.Get(e) == first) ? first : null;
            var kind = d.Set is null ? PropertyKind.ReadOnly : d.Kind;
            return new PropertyRow(d.Category, d.Name, kind, common, d.Choices?.Invoke(document) ?? [], d);
        }).ToList();
    }

    /// <summary>Descrizione breve della selezione per l'intestazione della palette.</summary>
    public static string Describe(IReadOnlyList<Entity> entities) => entities.Count switch
    {
        0 => "Nessuna selezione",
        1 => TypeName(entities[0]),
        _ when entities.All(e => e.GetType() == entities[0].GetType()) => $"{TypeName(entities[0])} ({entities.Count})",
        _ => $"Tutti ({entities.Count})",
    };

    public static string TypeName(Entity entity) => entity switch
    {
        LineEntity => "Linea",
        CircleEntity => "Cerchio",
        ArcEntity => "Arco",
        PolylineEntity => "Polilinea",
        EllipseEntity => "Ellisse",
        TextEntity => "Testo",
        DimensionEntity => "Quota",
        LeaderEntity => "Direttrice",
        InsertEntity => "Blocco",
        HatchEntity => "Tratteggio",
        SolidEntity => "Solido",
        PointEntity => "Punto",
        ImageEntity => "Immagine",
        _ => entity.GetType().Name.Replace("Entity", "", StringComparison.Ordinal),
    };

    /// <summary>Applica un valore a tutte le entità in un'unica operazione annullabile; falso se il valore non è valido.</summary>
    public static bool Apply(Editor editor, IReadOnlyList<Entity> entities, PropertyDefinition definition, string value)
    {
        if (definition.Set is null || entities.Count == 0)
        {
            return false;
        }

        var replacements = new List<(Entity Old, Entity New)>();
        foreach (var entity in entities)
        {
            if (definition.Set(entity, value.Trim()) is not { } changed)
            {
                editor.Write($"Valore non valido per {definition.Name}: {value}");
                return false;
            }

            replacements.Add((entity, changed));
        }

        editor.Document.Edit("PROPRIETÀ", e =>
        {
            foreach (var (old, changed) in replacements)
            {
                e.Replace(old, changed);
            }
        });
        editor.Selection.Clear();
        editor.Selection.Add(replacements.Select(r => r.New));
        return true;
    }

    // ---------- Valori ----------

    public static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string FormatAngle(double radians) => Format(Arc2D.NormalizeAngle(radians) * 180 / Math.PI);

    /// <summary>Numero scritto con il punto o con la virgola decimale.</summary>
    public static bool TryParse(string text, out double value) =>
        InputParser.TryParseNumber(text.Replace(',', '.'), out value);

    private static Func<Entity, string, Entity?> Number<T>(Func<T, double, Entity?> apply, Func<double, bool>? valid = null) where T : Entity =>
        (entity, text) => TryParse(text, out var v) && (valid?.Invoke(v) ?? true) ? apply((T)entity, v) : null;

    private static Func<Entity, string, Entity?> Detached<T>(Action<T> change) where T : Entity => (entity, _) =>
    {
        var copy = (T)entity.CopyDetached();
        change(copy);
        return copy;
    };

    /// <summary>Le proprietà di stile non cambiano la forma: la copia resta legata all'originale letto dal file.</summary>
    private static Entity Restyled(Entity entity, Action<Entity> change)
    {
        var copy = entity.Transformed(Matrix2D.Identity);
        change(copy);
        return copy;
    }

    // ---------- Proprietà generali ----------

    private static readonly string[] ColorNames =
        [.. StandardColors.All.Select(c => c.Name).Prepend("DaBlocco").Prepend("DaLayer")];

    private static IEnumerable<PropertyDefinition> GeneralDefinitions(CadDocument document)
    {
        yield return new(General, "Layer", PropertyKind.Choice, e => e.Layer.Name,
            (e, v) => document.FindLayer(v) is { } layer ? Restyled(e, c => c.Layer = layer) : null,
            d => [.. d.Layers.Select(l => l.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)]);
        yield return new(General, "Colore", PropertyKind.Choice, e => StandardColors.Describe(e.Color),
            (e, v) => ParseColor(v) is { } color ? Restyled(e, c => c.Color = color) : null,
            _ => ColorNames);
        yield return new(General, "Tipo di linea", PropertyKind.Choice, e => e.Linetype?.Name ?? "DaLayer",
            (e, v) => ParseLinetype(document, v) is { } result ? Restyled(e, c => c.Linetype = result.Linetype) : null,
            d => [.. d.Linetypes.Select(l => l.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Prepend("DaBlocco").Prepend("DaLayer")]);
        yield return new(General, "Scala tipo di linea", PropertyKind.Number, e => Format(e.LinetypeScale),
            Number<Entity>((e, v) => Restyled(e, c => c.LinetypeScale = v), v => v > 0));
        yield return new(General, "Spessore linea", PropertyKind.Choice, e => LineWeight.Format(e.LineWeight),
            (e, v) => LineWeight.TryParse(v, out var weight) ? Restyled(e, c => c.LineWeight = weight) : null,
            _ => LineWeightNames);
    }

    /// <summary>DaLayer, DaBlocco, Predefinito e gli spessori del formato.</summary>
    public static IReadOnlyList<string> LineWeightNames { get; } =
        [.. new[] { LineWeight.ByLayer, LineWeight.ByBlock, LineWeight.Default }.Concat(LineWeight.Standard).Select(LineWeight.Format)];

    private static (Linetype? Linetype, bool Ok)? ParseLinetype(CadDocument document, string name)
    {
        if (name.Equals("DaLayer", StringComparison.OrdinalIgnoreCase))
        {
            return (null, true);
        }

        if (name.Equals("DaBlocco", StringComparison.OrdinalIgnoreCase))
        {
            return (Linetype.ByBlock, true);
        }

        return document.FindLinetype(name) is { } found && !ReferenceEquals(found, Linetype.ByBlock) ? (found, true) : null;
    }

    private static EntityColor? ParseColor(string text)
    {
        if (text.Equals("DaLayer", StringComparison.OrdinalIgnoreCase))
        {
            return EntityColor.ByLayer;
        }

        if (text.Equals("DaBlocco", StringComparison.OrdinalIgnoreCase))
        {
            return EntityColor.ByBlock;
        }

        return StandardColors.Parse(text) is { } color ? EntityColor.Explicit(color) : null;
    }

    public static bool Apply(Editor editor, IReadOnlyList<Entity> entities, PropertyRow row, string value) =>
        Apply(editor, entities, row.Definition, value);

    // ---------- Proprietà per tipo ----------

    private static IEnumerable<PropertyDefinition> SpecificDefinitions(Entity sample, CadDocument document) => sample switch
    {
        LineEntity => LineDefinitions(),
        CircleEntity => CircleDefinitions(),
        ArcEntity => ArcDefinitions(),
        PolylineEntity => PolylineDefinitions(),
        TextEntity => TextDefinitions(document),
        DimensionEntity => DimensionDefinitions(),
        LeaderEntity => LeaderDefinitions(),
        InsertEntity => InsertDefinitions(),
        HatchEntity => HatchDefinitions(),
        ImageEntity => ImageDefinitions(),
        _ => [],
    };

    private static IEnumerable<PropertyDefinition> Point<T>(string name, Func<T, Vector2> get, Func<T, Vector2, Entity> set) where T : Entity
    {
        yield return new(Geometry, $"{name} X", PropertyKind.Number, e => Format(get((T)e).X),
            Number<T>((e, v) => set(e, new Vector2(v, get(e).Y))));
        yield return new(Geometry, $"{name} Y", PropertyKind.Number, e => Format(get((T)e).Y),
            Number<T>((e, v) => set(e, new Vector2(get(e).X, v))));
    }

    /// <summary>Spostamento: una trasformazione, quindi l'entità resta legata all'originale.</summary>
    private static Entity MoveTo(Entity entity, Vector2 from, Vector2 to) => entity.Transformed(Matrix2D.Translation(to - from));

    private static IEnumerable<PropertyDefinition> LineDefinitions()
    {
        foreach (var d in Point<LineEntity>("Inizio", l => l.Start, (l, p) => WithLine(l, p, l.End)))
        {
            yield return d;
        }

        foreach (var d in Point<LineEntity>("Fine", l => l.End, (l, p) => WithLine(l, l.Start, p)))
        {
            yield return d;
        }

        yield return new(Geometry, "Lunghezza", PropertyKind.Number, e => Format(((LineEntity)e).Segment.Length),
            Number<LineEntity>((l, v) =>
            {
                var direction = (l.End - l.Start).Normalized();
                return WithLine(l, l.Start, l.Start + direction * v);
            }, v => v > Tolerance.Default));
        yield return new(Geometry, "Angolo", PropertyKind.Number, e => FormatAngle((((LineEntity)e).End - ((LineEntity)e).Start).Angle),
            Number<LineEntity>((l, v) => WithLine(l, l.Start, l.Start + Vector2.FromPolar(l.Segment.Length, v * Math.PI / 180))));
    }

    private static Entity WithLine(LineEntity line, Vector2 start, Vector2 end)
    {
        var copy = (LineEntity)line.CopyDetached();
        copy.Start = start;
        copy.End = end;
        return copy;
    }

    private static IEnumerable<PropertyDefinition> CircleDefinitions()
    {
        foreach (var d in Point<CircleEntity>("Centro", c => c.Center, (c, p) => MoveTo(c, c.Center, p)))
        {
            yield return d;
        }

        // Cambiare il raggio è una scala attorno al centro: anche questa una trasformazione.
        yield return new(Geometry, "Raggio", PropertyKind.Number, e => Format(((CircleEntity)e).Radius),
            Number<CircleEntity>((c, v) => c.Transformed(Matrix2D.Scaling(v / c.Radius, c.Center)), v => v > Tolerance.Default));
        yield return new(Geometry, "Diametro", PropertyKind.Number, e => Format(((CircleEntity)e).Radius * 2),
            Number<CircleEntity>((c, v) => c.Transformed(Matrix2D.Scaling(v / 2 / c.Radius, c.Center)), v => v > Tolerance.Default));
        yield return new(Geometry, "Circonferenza", PropertyKind.ReadOnly, e => Format(Math.Tau * ((CircleEntity)e).Radius));
        yield return new(Geometry, "Area", PropertyKind.ReadOnly, e => Format(Math.PI * ((CircleEntity)e).Radius * ((CircleEntity)e).Radius));
    }

    private static IEnumerable<PropertyDefinition> ArcDefinitions()
    {
        foreach (var d in Point<ArcEntity>("Centro", a => a.Center, (a, p) => MoveTo(a, a.Center, p)))
        {
            yield return d;
        }

        yield return new(Geometry, "Raggio", PropertyKind.Number, e => Format(((ArcEntity)e).Radius),
            Number<ArcEntity>((a, v) => a.Transformed(Matrix2D.Scaling(v / a.Radius, a.Center)), v => v > Tolerance.Default));
        yield return new(Geometry, "Angolo iniziale", PropertyKind.Number, e => FormatAngle(((ArcEntity)e).StartAngle),
            Number<ArcEntity>((a, v) => Detached<ArcEntity>(c => c.StartAngle = Arc2D.NormalizeAngle(v * Math.PI / 180))(a, "")));
        yield return new(Geometry, "Angolo finale", PropertyKind.Number, e => FormatAngle(((ArcEntity)e).EndAngle),
            Number<ArcEntity>((a, v) => Detached<ArcEntity>(c => c.EndAngle = Arc2D.NormalizeAngle(v * Math.PI / 180))(a, "")));
        yield return new(Geometry, "Lunghezza", PropertyKind.ReadOnly, e => Format(((ArcEntity)e).Arc.Sweep * ((ArcEntity)e).Radius));
    }

    private static IEnumerable<PropertyDefinition> PolylineDefinitions()
    {
        yield return new(Geometry, "Chiusa", PropertyKind.Choice, e => ((PolylineEntity)e).IsClosed ? Yes : No,
            (e, v) => v == Yes || v == No ? Detached<PolylineEntity>(c => c.IsClosed = v == Yes)(e, v) : null,
            _ => [Yes, No]);
        yield return new(Geometry, "Vertici", PropertyKind.ReadOnly, e => ((PolylineEntity)e).Vertices.Count.ToString(CultureInfo.InvariantCulture));
        yield return new(Geometry, "Lunghezza", PropertyKind.ReadOnly, e => Format(Length(e)));
        yield return new(Geometry, "Area", PropertyKind.ReadOnly, e => ((PolylineEntity)e).IsClosed ? Format(PolylineArea(((PolylineEntity)e).Vertices)) : "-");
    }

    private static IEnumerable<PropertyDefinition> TextDefinitions(CadDocument document)
    {
        yield return new(Content, "Contenuto", PropertyKind.Text, e => ((TextEntity)e).Value.Replace("\n", "\\P", StringComparison.Ordinal),
            (e, v) => v.Length == 0 ? null : Detached<TextEntity>(t => t.Value = v.Replace("\\P", "\n", StringComparison.Ordinal))(e, v));
        // Altezza e rotazione sono scala e rotazione attorno al punto di inserimento.
        yield return new(Content, "Altezza", PropertyKind.Number, e => Format(((TextEntity)e).Height),
            Number<TextEntity>((t, v) => t.Transformed(Matrix2D.Scaling(v / t.Height, t.Position)), v => v > Tolerance.Default));
        yield return new(Content, "Rotazione", PropertyKind.Number, e => FormatAngle(((TextEntity)e).Rotation),
            Number<TextEntity>((t, v) => t.Transformed(Matrix2D.Rotation(v * Math.PI / 180 - t.Rotation, t.Position))));
        yield return new(Content, "Stile", PropertyKind.Choice, e => ((TextEntity)e).Style?.Name ?? TextStyle.DefaultName,
            (e, v) => document.FindTextStyle(v) is { } style ? Detached<TextEntity>(t => t.Style = style)(e, v) : null,
            d => [.. d.TextStyles.Select(s => s.Name).Order(StringComparer.OrdinalIgnoreCase)]);
        yield return new(Content, "Fattore di larghezza", PropertyKind.Number, e => Format(((TextEntity)e).WidthFactor),
            Number<TextEntity>((t, v) => Detached<TextEntity>(c => c.WidthFactor = v)(t, ""), v => v > Tolerance.Default));
        foreach (var d in Point<TextEntity>("Posizione", t => t.Position, (t, p) => MoveTo(t, t.Position, p)))
        {
            yield return d;
        }
    }

    private static IEnumerable<PropertyDefinition> DimensionDefinitions()
    {
        yield return new(Geometry, "Tipo", PropertyKind.ReadOnly, e => ((DimensionEntity)e).Kind switch
        {
            DimensionKind.Linear => "Lineare",
            DimensionKind.Aligned => "Allineata",
            DimensionKind.Radius => "Raggio",
            DimensionKind.Diameter => "Diametro",
            DimensionKind.Angular => "Angolare",
            DimensionKind.Ordinate => ((DimensionEntity)e).OrdinateX ? "Coordinata X" : "Coordinata Y",
            DimensionKind.ArcLength => "Lunghezza d'arco",
            var k => k.ToString(),
        });
        yield return new(Geometry, "Misura", PropertyKind.ReadOnly, e => ((DimensionEntity)e).Kind == DimensionKind.Angular
            ? Format(((DimensionEntity)e).Measurement) + "°"
            : Format(((DimensionEntity)e).Measurement * ((DimensionEntity)e).Style.LinearFactor));
        yield return new(Content, "Testo", PropertyKind.Text, e => ((DimensionEntity)e).TextOverride ?? "<>",
            (e, v) =>
            {
                var dimension = (DimensionEntity)e;
                // Come in MODIFICATESTO: la quota si rigenera, la grafica letta dal file non varrebbe più.
                var copy = (DimensionEntity)dimension.WithGripMoved(dimension.Grips.Count - 1, dimension.Location);
                copy.TextOverride = v.Length == 0 || v == "<>" ? null : v;
                return copy;
            });
        yield return new(Geometry, "Stile", PropertyKind.ReadOnly, e => ((DimensionEntity)e).Style.Name);
    }

    private static IEnumerable<PropertyDefinition> LeaderDefinitions()
    {
        yield return new(Geometry, "Vertici", PropertyKind.ReadOnly, e => ((LeaderEntity)e).Vertices.Count.ToString(CultureInfo.InvariantCulture));
        yield return new(Geometry, "Freccia", PropertyKind.Choice, e => ((LeaderEntity)e).HasArrow ? Yes : No,
            (e, v) => v == Yes || v == No ? Detached<LeaderEntity>(c => c.HasArrow = v == Yes)(e, v) : null,
            _ => [Yes, No]);
        yield return new(Geometry, "Stile di quota", PropertyKind.ReadOnly, e => ((LeaderEntity)e).Style.Name);
    }

    private static IEnumerable<PropertyDefinition> ImageDefinitions()
    {
        yield return new(Geometry, "File", PropertyKind.ReadOnly, e => ((ImageEntity)e).Path);
        foreach (var d in Point<ImageEntity>("Angolo", i => i.Corner, (i, p) => MoveTo(i, i.Corner, p)))
        {
            yield return d;
        }

        yield return new(Geometry, "Larghezza", PropertyKind.Number, e => Format(((ImageEntity)e).U.Length),
            Number<ImageEntity>((i, v) => i.Transformed(Matrix2D.Scaling(v / i.U.Length, i.Corner)), v => v > 0));
        yield return new(Geometry, "Altezza", PropertyKind.ReadOnly, e => Format(((ImageEntity)e).V.Length));
        yield return new(Geometry, "Pixel", PropertyKind.ReadOnly, e => $"{((ImageEntity)e).PixelWidth} × {((ImageEntity)e).PixelHeight}");
        yield return new(Geometry, "Opacità %", PropertyKind.Number, e => Format(Math.Round(((ImageEntity)e).Opacity * 100)),
            Number<ImageEntity>((i, v) => Detached<ImageEntity>(c => c.Opacity = v / 100)(i, ""), v => v is >= 10 and <= 100));
    }

    private static IEnumerable<PropertyDefinition> InsertDefinitions()
    {
        yield return new(Geometry, "Nome", PropertyKind.ReadOnly, e => ((InsertEntity)e).Block.Name);
        foreach (var d in Point<InsertEntity>("Posizione", i => i.Position, (i, p) => MoveTo(i, i.Position, p)))
        {
            yield return d;
        }

        yield return new(Geometry, "Scala", PropertyKind.Number, e => Format(InsertScale((InsertEntity)e)),
            Number<InsertEntity>((i, v) => i.Transformed(Matrix2D.Scaling(v / InsertScale(i), i.Position)), v => v > Tolerance.Default));
        yield return new(Geometry, "Rotazione", PropertyKind.Number, e => FormatAngle(InsertRotation((InsertEntity)e)),
            Number<InsertEntity>((i, v) => i.Transformed(Matrix2D.Rotation(v * Math.PI / 180 - InsertRotation(i), i.Position))));
    }

    private static double InsertScale(InsertEntity insert) => insert.Transform.TransformVector(Vector2.UnitX).Length;

    private static double InsertRotation(InsertEntity insert) => insert.Transform.TransformVector(Vector2.UnitX).Angle;

    private static IEnumerable<PropertyDefinition> HatchDefinitions()
    {
        yield return new(Geometry, "Motivo", PropertyKind.ReadOnly, e => ((HatchEntity)e).PatternName);
        yield return new(Geometry, "Scala del motivo", PropertyKind.ReadOnly, e => Format(((HatchEntity)e).PatternScale));
        yield return new(Geometry, "Angolo del motivo", PropertyKind.ReadOnly, e => Format(((HatchEntity)e).PatternAngle * 180 / Math.PI));
        yield return new(Geometry, "Area", PropertyKind.ReadOnly, e =>
        {
            var hatch = (HatchEntity)e;
            if (hatch.Loops.Count == 0)
            {
                return "0";
            }

            // Il contorno più grande meno le isole.
            var areas = hatch.Loops.Select(l => Math.Abs(PolylineArea(l))).OrderByDescending(a => a).ToList();
            return Format(areas[0] - areas.Skip(1).Sum());
        });
    }

    // ---------- Misure ----------

    /// <summary>Lunghezza di linee, archi, cerchi e polilinee (con i tratti curvi).</summary>
    public static double Length(Entity entity) =>
        Curve.From(entity)?.Pieces.Sum(p => p.Length) ?? 0;

    /// <summary>Area esatta di una polilinea chiusa, compresi i tratti ad arco.</summary>
    public static double PolylineArea(IReadOnlyList<PolylineVertex> vertices)
    {
        var area = 0.0;
        for (var i = 0; i < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % vertices.Count];
            area += Vector2.Cross(a.Position, b.Position) / 2;
            if (Math.Abs(a.Bulge) > 1e-12)
            {
                // Segmento circolare tra la corda e l'arco: positivo se l'arco gira in senso antiorario.
                var theta = 4 * Math.Atan(Math.Abs(a.Bulge));
                var chord = Vector2.Distance(a.Position, b.Position);
                var radius = chord / (2 * Math.Sin(theta / 2));
                area += Math.Sign(a.Bulge) * radius * radius / 2 * (theta - Math.Sin(theta));
            }
        }

        return Math.Abs(area);
    }
}

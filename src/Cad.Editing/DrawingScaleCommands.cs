using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>
/// SCALADISEGNO: scala del disegno (1:2, 1:50, 2:1...). Due modi:
/// <list type="bullet">
/// <item>Assegna: la geometria resta in misura reale e le quote non cambiano; testi, frecce, tabelle e tipi di linea
/// si ingrandiscono o rimpiccioliscono perché sulla carta abbiano sempre la stessa misura.</item>
/// <item>Ridimensiona: il disegno si scala dalla scala corrente alla nuova attorno a un punto base; le quote continuano a
/// mostrare le misure reali (fattore delle misure, DIMLFAC) e testi e annotazioni restano della stessa misura.</item>
/// </list>
/// </summary>
public static class DrawingScaleCommands
{
    internal static void Register(Editor editor)
    {
        editor.RegisterCommand("SCALADISEGNO", DrawingScale, "SD", "DRAWINGSCALE");
    }

    private static async Task DrawingScale(Editor ed)
    {
        var document = ed.Document;
        ed.Write($"Scala del disegno: {CadDocument.FormatScale(document.DrawingScale)}.");
        var mode = await ed.GetKeywordAsync("Modo [Assegna/Ridimensiona] <Assegna>:", "Assegna", "Ridimensiona");
        if (mode.Status == PromptStatus.Cancel)
        {
            return;
        }

        var resize = mode.Keyword == "Ridimensiona";
        var text = await ed.GetStringAsync("Nuova scala (per esempio 1:2, 1:50, 2:1):");
        if (!text.IsOk)
        {
            return;
        }

        if (CadDocument.ParseScale(text.Text!) is not { } scale)
        {
            ed.Write("Scala non valida: scrivi per esempio 1:2 oppure 2:1.");
            return;
        }

        if (Math.Abs(scale / document.DrawingScale - 1) < 1e-12)
        {
            ed.Write("Il disegno è già in quella scala.");
            return;
        }

        if (!resize)
        {
            Assign(ed, scale);
        }
        else
        {
            var basePoint = await ed.GetPointAsync("Punto base <0,0>:");
            if (basePoint.Status == PromptStatus.Cancel)
            {
                return;
            }

            Resize(ed, scale, basePoint.IsOk ? basePoint.Point : Vector2.Zero);
        }

        ed.Write($"Scala del disegno: {CadDocument.FormatScale(document.DrawingScale)}.");
    }

    /// <summary>Assegna la scala: annotazioni ridimensionate, geometria e quote invariate. Si annulla con un solo ANNULLA.</summary>
    public static void Assign(Editor ed, double scale)
    {
        var document = ed.Document;
        var ratio = scale / document.DrawingScale;
        document.Edit("SCALADISEGNO", e =>
        {
            foreach (var style in document.DimensionStyles.ToList())
            {
                var old = style.Scale;
                e.Setting(() => style.Scale = old * ratio, () => style.Scale = old);
            }

            foreach (var style in document.TextStyles.Where(s => s.Height > 0).ToList())
            {
                var old = style.Height;
                e.Setting(() => style.Height = old * ratio, () => style.Height = old);
            }

            var linetypeScale = document.LinetypeScale;
            e.Setting(() => document.LinetypeScale = linetypeScale * ratio, () => document.LinetypeScale = linetypeScale);

            foreach (var (entity, anchor) in Annotations(document).ToList())
            {
                e.Replace(entity, entity.Transformed(Matrix2D.Scaling(ratio, anchor)));
            }
        });
        ed.Settings.TextHeight *= ratio;
    }

    /// <summary>
    /// Ridimensiona il disegno dalla scala corrente alla nuova attorno a <paramref name="basePoint"/>. Le quote mostrano
    /// ancora le misure reali; testi e annotazioni si spostano con il disegno ma restano della stessa misura.
    /// </summary>
    public static void Resize(Editor ed, double scale, Vector2 basePoint)
    {
        var document = ed.Document;
        var ratio = scale / document.DrawingScale;
        var m = Matrix2D.Scaling(1 / ratio, basePoint);
        var annotations = Annotations(document).ToDictionary(a => a.Entity, a => a.Anchor);
        document.Edit("SCALADISEGNO", e =>
        {
            foreach (var style in document.DimensionStyles.ToList())
            {
                var old = style.LinearFactor;
                e.Setting(() => style.LinearFactor = old * ratio, () => style.LinearFactor = old);
            }

            foreach (var entity in document.ModelSpace.ToList())
            {
                Entity moved;
                if (annotations.TryGetValue(entity, out var anchor))
                {
                    // Le annotazioni seguono il disegno senza cambiare misura.
                    moved = entity.Transformed(Matrix2D.Translation(m.Transform(anchor) - anchor));
                }
                else
                {
                    moved = entity.Transformed(m);
                    if (moved is DimensionEntity { Graphics: not null } dimension)
                    {
                        // La grafica letta dal file avrebbe testi e frecce scalati: la quota si rigenera.
                        moved = dimension.WithGripMoved(dimension.Grips.Count - 1, dimension.Grips[^1]);
                    }
                }

                e.Replace(entity, moved);
            }
        });
        ed.Settings.OrdinateOrigin = m.Transform(ed.Settings.OrdinateOrigin);
    }

    /// <summary>
    /// Entità che devono avere sempre la stessa misura sulla carta, con il punto attorno a cui si scalano: i testi
    /// (attorno al punto di inserimento) e le parti di tabelle e tolleranze (attorno all'angolo in alto a sinistra e
    /// al punto a metà del lato sinistro).
    /// </summary>
    private static IEnumerable<(Entity Entity, Vector2 Anchor)> Annotations(CadDocument document)
    {
        var anchors = new Dictionary<CadGroup, Vector2>();
        foreach (var group in document.Groups.Where(g => g.Description is AnnotationGroups.Table or AnnotationGroups.Tolerance))
        {
            var box = document.Members(group).Aggregate(BoundingBox.Empty, (b, x) => b.Union(x.Bounds));
            if (!box.IsEmpty)
            {
                anchors[group] = group.Description == AnnotationGroups.Table
                    ? new Vector2(box.Min.X, box.Max.Y)
                    : new Vector2(box.Min.X, (box.Min.Y + box.Max.Y) / 2);
            }
        }

        foreach (var entity in document.ModelSpace)
        {
            if (entity.Group is { } group && anchors.TryGetValue(group, out var anchor))
            {
                yield return (entity, anchor);
            }
            else if (entity is TextEntity text)
            {
                yield return (entity, text.Position);
            }
        }
    }
}

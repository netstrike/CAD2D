using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>
/// Appunti del CAD: COPIACLIP (Ctrl+C), TAGLIACLIP (Ctrl+X), COPIABASE (Ctrl+Maiusc+C) e INCOLLACLIP (Ctrl+V).
/// Gli appunti sono condivisi tra i disegni aperti uno dopo l'altro: incollando in un altro disegno layer, tipi di
/// linea, blocchi e stili di quota mancanti vengono creati.
/// </summary>
public static class ClipboardCommands
{
    private static Clip? _clip;

    private sealed record Clip(CadDocument Source, IReadOnlyList<Entity> Entities, Vector2 BasePoint);

    /// <summary>Numero di oggetti negli appunti.</summary>
    public static int Count => _clip?.Entities.Count ?? 0;

    internal static void Register(Editor editor)
    {
        editor.RegisterCommand("COPIACLIP", ed => CopyAsync(ed, cut: false, askBase: false), "COPYCLIP");
        editor.RegisterCommand("TAGLIACLIP", ed => CopyAsync(ed, cut: true, askBase: false), "CUTCLIP");
        editor.RegisterCommand("COPIABASE", ed => CopyAsync(ed, cut: false, askBase: true), "COPYBASE");
        editor.RegisterCommand("INCOLLACLIP", PasteAsync, "PASTECLIP", "INCOLLA");
    }

    /// <summary>Svuota gli appunti (per i test).</summary>
    public static void Clear() => _clip = null;

    private static async Task CopyAsync(Editor ed, bool cut, bool askBase)
    {
        var entities = await ed.GetSelectionAsync("Seleziona gli oggetti da copiare:");
        if (entities is null || entities.Count == 0)
        {
            return;
        }

        // Senza punto base esplicito vale l'angolo in basso a sinistra degli oggetti.
        var bounds = entities.Aggregate(BoundingBox.Empty, (box, e) => box.Union(e.Bounds));
        var basePoint = bounds.IsEmpty ? Vector2.Zero : bounds.Min;
        if (askBase)
        {
            var picked = await ed.GetPointAsync("Punto base:");
            if (!picked.IsOk)
            {
                return;
            }

            basePoint = picked.Point;
        }

        _clip = new Clip(ed.Document, [.. entities], basePoint);
        if (cut)
        {
            ed.Document.Edit("TAGLIACLIP", e =>
            {
                foreach (var entity in entities)
                {
                    e.Remove(entity);
                }
            });
        }

        ed.Selection.Clear();
        ed.Write($"{entities.Count} oggetti {(cut ? "tagliati" : "copiati")} negli appunti.");
    }

    private static async Task PasteAsync(Editor ed)
    {
        if (_clip is not { } clip)
        {
            ed.Write("Gli appunti sono vuoti.");
            return;
        }

        var sameDocument = ReferenceEquals(clip.Source, ed.Document);
        var prepared = sameDocument ? clip.Entities : [.. clip.Entities.Select(e => Import(e, ed.Document))];
        var target = await ed.GetPointAsync(
            "Punto di inserimento:", null, p => prepared.Select(e => e.Transformed(Matrix2D.Translation(p - clip.BasePoint))));
        if (!target.IsOk)
        {
            return;
        }

        var move = Matrix2D.Translation(target.Point - clip.BasePoint);
        var pasted = sameDocument
            ? GroupCommands.Copies(ed.Document, prepared, e => e.Transformed(move))
            : prepared.Select(e => e.Transformed(move)).ToList();
        ed.Document.Edit("INCOLLACLIP", e =>
        {
            foreach (var entity in pasted)
            {
                e.Add(entity);
            }
        });
        ed.Selection.Clear();
        ed.Selection.Add(pasted);
        ed.Write($"{pasted.Count} oggetti incollati.");
    }

    /// <summary>Copia un'entità di un altro disegno, rifacendo i riferimenti a layer, tipi di linea, blocchi e stili.</summary>
    public static Entity Import(Entity entity, CadDocument target)
    {
        var copy = entity switch
        {
            InsertEntity insert => ImportInsert(insert, target),
            _ => entity.CopyDetached(),
        };

        copy.Layer = ImportLayer(entity.Layer, target);
        copy.Group = null;
        copy.Linetype = ImportLinetype(entity.Linetype, target);
        if (copy is DimensionEntity dimension && entity is DimensionEntity source)
        {
            // La grafica letta dal file appartiene all'altro disegno: si ricostruisce dai punti della quota.
            dimension.Graphics = null;
            dimension.GraphicsTransform = Matrix2D.Identity;
            dimension.Style = ImportDimensionStyle(source.Style, target);
        }

        return copy;
    }

    private static InsertEntity ImportInsert(InsertEntity insert, CadDocument target)
    {
        var block = target.Blocks.FirstOrDefault(b => string.Equals(b.Name, insert.Block.Name, StringComparison.OrdinalIgnoreCase));
        if (block is null)
        {
            block = target.GetOrAddBlock(insert.Block.Name);
            block.BasePoint = insert.Block.BasePoint;
            block.Entities.AddRange(insert.Block.Entities.Select(e => Import(e, target)));
        }

        var result = new InsertEntity(insert.Layer, block) { Transform = insert.Transform };
        result.CopyStyleFrom<Entity>(insert);
        result.Attributes.AddRange(insert.Attributes.Select(a => Import(a, target)));
        return result;
    }

    private static Layer ImportLayer(Layer layer, CadDocument target)
    {
        if (target.FindLayer(layer.Name) is { } existing)
        {
            return existing;
        }

        var created = target.GetOrAddLayer(layer.Name);
        created.Color = layer.Color;
        created.Linetype = ImportLinetype(layer.Linetype, target) ?? Linetype.Continuous;
        return created;
    }

    private static Linetype? ImportLinetype(Linetype? linetype, CadDocument target)
    {
        if (linetype is null || ReferenceEquals(linetype, Linetype.ByBlock) || ReferenceEquals(linetype, Linetype.Continuous))
        {
            return linetype;
        }

        return target.FindLinetype(linetype.Name) ?? target.AddLinetype(linetype);
    }

    private static DimensionStyle ImportDimensionStyle(DimensionStyle style, CadDocument target)
    {
        var exists = target.DimensionStyles.Any(s => string.Equals(s.Name, style.Name, StringComparison.OrdinalIgnoreCase));
        var result = target.GetOrAddDimensionStyle(style.Name);
        if (!exists)
        {
            result.TextHeight = style.TextHeight;
            result.ArrowSize = style.ArrowSize;
            result.ExtensionOffset = style.ExtensionOffset;
            result.ExtensionExtend = style.ExtensionExtend;
            result.TextGap = style.TextGap;
            result.Decimals = style.Decimals;
            result.DecimalSeparator = style.DecimalSeparator;
            result.Scale = style.Scale;
        }

        return result;
    }
}

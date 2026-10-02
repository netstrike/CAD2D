using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>Blocchi: BLOCCO (crea) e INSERISCI. ESPLODI li riporta a entità semplici.</summary>
internal static class BlockCommands
{
    public static void Register(Editor editor)
    {
        editor.RegisterCommand("BLOCCO", CreateBlock, "B", "BLOCK", "BMAKE");
        editor.RegisterCommand("INSERISCI", InsertBlock, "I", "INSERT", "DDINSERT");
    }

    /// <summary>Blocchi che si possono inserire: esclusi quelli anonimi (quote, tratteggi associativi) e gli spazi carta.</summary>
    public static IEnumerable<BlockDefinition> InsertableBlocks(CadDocument document) =>
        document.Blocks
            .Where(b => !b.Name.StartsWith('*') && b.Entities.Count > 0)
            .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase);

    private static async Task CreateBlock(Editor ed)
    {
        string name;
        while (true)
        {
            var answer = await ed.GetStringAsync("Nome del blocco:");
            if (!answer.IsOk)
            {
                return;
            }

            name = answer.Text!.Trim();
            if (!CadDocument.IsValidName(name))
            {
                ed.Write("Nome non valido.");
                continue;
            }

            if (ed.Document.Blocks.Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                ed.Write($"Il blocco {name} esiste già: scegli un altro nome.");
                continue;
            }

            break;
        }

        var basePoint = await ed.GetPointAsync("Punto base:");
        if (!basePoint.IsOk)
        {
            return;
        }

        var entities = await ed.GetSelectionAsync("Seleziona gli oggetti del blocco:");
        if (entities is null || entities.Count == 0)
        {
            return;
        }

        var keep = await ed.GetKeywordAsync("Gli oggetti diventano un'istanza del blocco [Sì/Lascia/Cancella] <Sì>:", "Sì", "Lascia", "Cancella");
        if (keep.Status == PromptStatus.Cancel)
        {
            return;
        }

        // Il blocco tiene le entità in coordinate relative al punto base, come negli altri CAD.
        var block = ed.Document.GetOrAddBlock(name);
        block.BasePoint = Vector2.Zero;
        var toLocal = Matrix2D.Translation(-basePoint.Point);
        block.Entities.AddRange(entities.Select(e => e.Transformed(toLocal)));

        var mode = keep.Status == PromptStatus.Keyword ? keep.Keyword : "Sì";
        ed.Selection.Clear();
        ed.Document.Edit("BLOCCO", e =>
        {
            if (mode == "Lascia")
            {
                return;
            }

            foreach (var entity in entities)
            {
                e.Remove(entity);
            }

            if (mode == "Sì")
            {
                e.Add(ed.Styled(new InsertEntity(ed.CurrentLayer, block) { Transform = Matrix2D.Translation(basePoint.Point) }));
            }
        });
        ed.Write($"Blocco {name} creato con {entities.Count} oggetti.");
    }

    private static async Task InsertBlock(Editor ed)
    {
        var blocks = InsertableBlocks(ed.Document).ToList();
        if (blocks.Count == 0)
        {
            ed.Write("Il disegno non contiene blocchi: creane uno con BLOCCO.");
            return;
        }

        var last = ed.Settings.LastBlock is { } remembered && blocks.Any(b => b.Name.Equals(remembered, StringComparison.OrdinalIgnoreCase))
            ? remembered
            : blocks[0].Name;
        BlockDefinition block;
        while (true)
        {
            var answer = await ed.GetStringAsync($"Nome del blocco (? per l'elenco) <{last}>:");
            if (answer.Status == PromptStatus.Cancel)
            {
                return;
            }

            var name = answer.IsOk ? answer.Text!.Trim() : last;
            if (name == "?")
            {
                ed.Write("Blocchi: " + string.Join(", ", blocks.Select(b => b.Name)));
                continue;
            }

            if (blocks.FirstOrDefault(b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is not { } found)
            {
                ed.Write($"Il blocco {name} non esiste.");
                continue;
            }

            block = found;
            break;
        }

        ed.Settings.LastBlock = block.Name;
        var scale = 1.0;
        var rotation = 0.0;
        InsertEntity Build(Vector2 position) => ed.Styled(new InsertEntity(ed.CurrentLayer, block)
        {
            Transform = InsertEntity.BuildTransform(block.BasePoint, position, scale, scale, rotation),
        });

        Vector2 point;
        while (true)
        {
            var position = await ed.GetPointAsync("Punto di inserimento:", null, p => [Build(p)], "Scala", "Rotazione");
            if (position.IsOk)
            {
                point = position.Point;
                break;
            }

            if (position.Status != PromptStatus.Keyword)
            {
                return;
            }

            if (position.Keyword == "Scala")
            {
                var value = await ed.GetNumberAsync($"Fattore di scala <{Editor.Format(scale)}>:");
                if (value.IsOk && Math.Abs(value.Value) > Tolerance.Default)
                {
                    scale = value.Value;
                }
            }
            else
            {
                var value = await ed.GetNumberAsync($"Rotazione in gradi <{Editor.Format(rotation * 180 / Math.PI)}>:");
                if (value.IsOk)
                {
                    rotation = value.Value * Math.PI / 180;
                }
            }
        }

        var insert = Build(point);
        ed.Document.Edit("INSERISCI", e => e.Add(insert));
    }
}

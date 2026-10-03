using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>Comandi di modifica del disegno tecnico: specchia, scala, offset, taglia, estendi, raccorda, cima, serie, esplodi.</summary>
internal static class ModifyCommands
{
    public static void Register(Editor editor)
    {
        editor.RegisterCommand("SPECCHIA", Mirror, "MI", "MIRROR");
        editor.RegisterCommand("SCALA", Scale, "SC", "SCALE");
        editor.RegisterCommand("OFFSET", Offset, "O");
        editor.RegisterCommand("TAGLIA", Trim, "TR", "TRIM");
        editor.RegisterCommand("ESTENDI", Extend, "EX", "EXTEND");
        editor.RegisterCommand("RACCORDA", Fillet, "F", "FILLET");
        editor.RegisterCommand("CIMA", Chamfer, "CHA", "CHAMFER");
        editor.RegisterCommand("SERIE", Array, "AR", "ARRAY");
        editor.RegisterCommand("ESPLODI", Explode, "X", "EXPLODE");
    }

    private static async Task Mirror(Editor ed)
    {
        var entities = await ed.GetSelectionAsync();
        if (entities is null)
        {
            return;
        }

        var first = await ed.GetPointAsync("Primo punto dell'asse di simmetria:");
        if (!first.IsOk)
        {
            return;
        }

        var a = first.Point;
        var second = await ed.GetPointAsync(
            "Secondo punto dell'asse di simmetria:",
            a,
            p => p.IsAlmostEqual(a) ? [] : entities.Select(e => e.Transformed(Matrix2D.Mirror(a, p))));
        if (!second.IsOk || second.Point.IsAlmostEqual(a))
        {
            return;
        }

        var mirror = Matrix2D.Mirror(a, second.Point);
        var erase = await ed.GetKeywordAsync("Cancellare gli oggetti originali? [Sì/No] <No>:", "Sì", "No");
        if (erase.Status == PromptStatus.Cancel)
        {
            return;
        }

        var replace = erase.Keyword == "Sì";
        var copies = replace ? [] : GroupCommands.Copies(ed.Document, entities, entity => entity.Transformed(mirror));
        ed.Document.Edit("SPECCHIA", e =>
        {
            if (!replace)
            {
                foreach (var copy in copies)
                {
                    e.Add(copy);
                }

                return;
            }

            foreach (var entity in entities)
            {
                e.Replace(entity, entity.Transformed(mirror));
            }
        });
        ed.Selection.Clear();
    }

    private static async Task Scale(Editor ed)
    {
        var entities = await ed.GetSelectionAsync();
        if (entities is null)
        {
            return;
        }

        var basePoint = await ed.GetPointAsync("Punto base:");
        if (!basePoint.IsOk)
        {
            return;
        }

        var c = basePoint.Point;
        var factor = await ed.GetDistanceAsync(
            "Fattore di scala:",
            c,
            p => Vector2.Distance(c, p) is var f && f > Tolerance.Default ? entities.Select(e => e.Transformed(Matrix2D.Scaling(f, c))) : [],
            "Riferimento");
        var value = factor.Value;
        if (factor.Status == PromptStatus.Keyword)
        {
            var reference = await ed.GetNumberAsync("Lunghezza di riferimento:");
            if (!reference.IsOk || reference.Value <= Tolerance.Default)
            {
                return;
            }

            var length = await ed.GetNumberAsync("Nuova lunghezza:");
            if (!length.IsOk)
            {
                return;
            }

            value = length.Value / reference.Value;
        }
        else if (!factor.IsOk)
        {
            return;
        }

        if (value <= Tolerance.Default)
        {
            ed.Write("Il fattore di scala deve essere maggiore di zero.");
            return;
        }

        var scaling = Matrix2D.Scaling(value, c);
        ed.Document.Edit("SCALA", e =>
        {
            foreach (var entity in entities)
            {
                e.Replace(entity, entity.Transformed(scaling));
            }
        });
        ed.Selection.Clear();
    }

    private static async Task Offset(Editor ed)
    {
        var settings = ed.Settings;
        var distance = await ed.GetNumberAsync($"Distanza di offset <{Editor.Format(settings.OffsetDistance)}>:", "Punto");
        var throughPoint = false;
        switch (distance.Status)
        {
            case PromptStatus.Ok when distance.Value > Tolerance.Default:
                settings.OffsetDistance = distance.Value;
                break;
            case PromptStatus.Ok:
                ed.Write("La distanza deve essere maggiore di zero.");
                return;
            case PromptStatus.Keyword:
                throughPoint = true;
                break;
            case PromptStatus.Cancel:
                return;
        }

        ed.Selection.Clear();
        while (true)
        {
            var picked = await ed.GetEntityAsync("Seleziona l'oggetto da copiare in parallelo:");
            if (picked.Entity is not { } entity)
            {
                return;
            }

            ed.Selection.Add([entity]);
            Func<Vector2, Entity?> build = throughPoint
                ? p => Modify.Offset(entity, EntityGeometry.DistanceTo(entity, p), p)
                : p => Modify.Offset(entity, settings.OffsetDistance, p);
            var side = await ed.GetPointAsync(
                throughPoint ? "Punto di passaggio:" : "Punto dalla parte dell'offset:",
                null,
                p => build(p) is { } preview ? [preview] : []);
            ed.Selection.Clear();
            if (!side.IsOk)
            {
                return;
            }

            if (build(side.Point) is { } result)
            {
                result.Group = null;
                ed.Document.Edit("OFFSET", e => e.Add(result));
            }
            else
            {
                ed.Write("Impossibile copiare questo oggetto in parallelo a quella distanza.");
            }
        }
    }

    private static async Task Trim(Editor ed)
    {
        ed.Selection.Clear();
        ed.Write("Tutte le entità fanno da bordo di taglio.");
        while (true)
        {
            var picked = await ed.GetEntityAsync("Seleziona la parte da tagliare:");
            if (picked.Entity is not { } entity)
            {
                return;
            }

            var boundaries = Modify.Boundaries(ed.Locator.Overlapping(entity.Bounds.Inflate(Tolerance.Default)), entity);
            var pieces = Modify.Trim(entity, picked.Point, boundaries);
            if (pieces is null)
            {
                ed.Write("Questo oggetto non si può tagliare qui.");
                continue;
            }

            ed.Document.Edit("TAGLIA", e =>
            {
                e.Remove(entity);
                foreach (var piece in pieces)
                {
                    e.Add(piece);
                }
            });
        }
    }

    private static async Task Extend(Editor ed)
    {
        ed.Selection.Clear();
        ed.Write("Tutte le entità fanno da bordo.");
        while (true)
        {
            var picked = await ed.GetEntityAsync("Seleziona l'oggetto da estendere vicino all'estremo:");
            if (picked.Entity is not { } entity)
            {
                return;
            }

            var everything = ed.Locator.Overlapping(ed.Document.Bounds.Inflate(1));
            if (Modify.Extend(entity, picked.Point, Modify.Boundaries(everything, entity)) is { } extended)
            {
                ed.Document.Edit("ESTENDI", e => e.Replace(entity, extended));
            }
            else
            {
                ed.Write("Nessun bordo da raggiungere in quella direzione.");
            }
        }
    }

    private static async Task Fillet(Editor ed)
    {
        var settings = ed.Settings;
        ed.Selection.Clear();
        while (true)
        {
            var first = await ed.GetEntityAsync($"Seleziona la prima linea <raggio {Editor.Format(settings.FilletRadius)}>:", "Raggio", "Polilinea");
            if (first.Status == PromptStatus.Keyword && first.Keyword == "Raggio")
            {
                var radius = await ed.GetNumberAsync($"Raggio di raccordo <{Editor.Format(settings.FilletRadius)}>:");
                if (radius.IsOk && radius.Value >= 0)
                {
                    settings.FilletRadius = radius.Value;
                }

                continue;
            }

            if (first.Status == PromptStatus.Keyword)
            {
                var picked = await ed.GetEntityAsync("Seleziona la polilinea:");
                if (picked.Entity is PolylineEntity polyline)
                {
                    var (result, filleted, skipped) = Modify.FilletPolyline(polyline, settings.FilletRadius);
                    if (filleted > 0)
                    {
                        ed.Document.Edit("RACCORDA", e => e.Replace(polyline, result));
                    }

                    ed.Write(skipped > 0 ? $"{filleted} spigoli raccordati, {skipped} troppo corti per il raggio." : $"{filleted} spigoli raccordati.");
                }
                else if (picked.Entity is not null)
                {
                    ed.Write("L'oggetto non è una polilinea.");
                }

                return;
            }

            if (first.Entity is not LineEntity line1)
            {
                if (first.Entity is not null)
                {
                    ed.Write("Per ora si raccordano linee, oppure gli spigoli di una polilinea (opzione Polilinea).");
                    continue;
                }

                return;
            }

            ed.Selection.Add([line1]);
            var second = await ed.GetEntityAsync("Seleziona la seconda linea:");
            ed.Selection.Clear();
            if (second.Entity is not LineEntity line2)
            {
                if (second.Entity is not null)
                {
                    ed.Write("Il secondo oggetto deve essere una linea.");
                }

                return;
            }

            var (corner, error) = Modify.Fillet(line1, first.Point, line2, second.Point, settings.FilletRadius);
            if (ApplyCorner(ed, "RACCORDA", line1, line2, corner, error))
            {
                return;
            }
        }
    }

    private static async Task Chamfer(Editor ed)
    {
        var settings = ed.Settings;
        ed.Selection.Clear();
        while (true)
        {
            var first = await ed.GetEntityAsync(
                $"Seleziona la prima linea <distanze {Editor.Format(settings.ChamferDistance1)}, {Editor.Format(settings.ChamferDistance2)}>:",
                "Distanza");
            if (first.Status == PromptStatus.Keyword)
            {
                var d1 = await ed.GetNumberAsync($"Prima distanza <{Editor.Format(settings.ChamferDistance1)}>:");
                if (d1.Status == PromptStatus.Cancel)
                {
                    return;
                }

                if (d1.IsOk && d1.Value >= 0)
                {
                    settings.ChamferDistance1 = d1.Value;
                    settings.ChamferDistance2 = d1.Value;
                }

                var d2 = await ed.GetNumberAsync($"Seconda distanza <{Editor.Format(settings.ChamferDistance2)}>:");
                if (d2.IsOk && d2.Value >= 0)
                {
                    settings.ChamferDistance2 = d2.Value;
                }

                continue;
            }

            if (first.Entity is not LineEntity line1)
            {
                if (first.Entity is not null)
                {
                    ed.Write("La cima si applica tra due linee.");
                    continue;
                }

                return;
            }

            ed.Selection.Add([line1]);
            var second = await ed.GetEntityAsync("Seleziona la seconda linea:");
            ed.Selection.Clear();
            if (second.Entity is not LineEntity line2)
            {
                if (second.Entity is not null)
                {
                    ed.Write("Il secondo oggetto deve essere una linea.");
                }

                return;
            }

            var (corner, error) = Modify.Chamfer(line1, first.Point, line2, second.Point, settings.ChamferDistance1, settings.ChamferDistance2);
            if (ApplyCorner(ed, "CIMA", line1, line2, corner, error))
            {
                return;
            }
        }
    }

    /// <summary>Applica raccordo o cima. Restituisce true se riuscito (il comando finisce), false per riprovare.</summary>
    private static bool ApplyCorner(Editor ed, string name, LineEntity line1, LineEntity line2, Modify.CornerResult? corner, string? error)
    {
        if (corner is null)
        {
            ed.Write(error ?? "Operazione non riuscita.");
            return false;
        }

        ed.Document.Edit(name, e =>
        {
            e.Replace(line1, corner.First);
            e.Replace(line2, corner.Second);
            if (corner.Joint is { } joint)
            {
                e.Add(joint);
            }
        });
        return true;
    }

    private static async Task Array(Editor ed)
    {
        var entities = await ed.GetSelectionAsync();
        if (entities is null)
        {
            return;
        }

        var type = await ed.GetKeywordAsync("Tipo di serie [Rettangolare/Polare] <Rettangolare>:", "Rettangolare", "Polare");
        if (type.Status == PromptStatus.Cancel)
        {
            return;
        }

        var transforms = type.Keyword == "Polare" ? await PolarArray(ed) : await RectangularArray(ed);
        if (transforms is null)
        {
            return;
        }

        var copies = transforms.SelectMany(m => GroupCommands.Copies(ed.Document, entities, entity => entity.Transformed(m))).ToList();
        ed.Document.Edit("SERIE", e =>
        {
            foreach (var copy in copies)
            {
                e.Add(copy);
            }
        });
        ed.Write($"{transforms.Count * entities.Count} entità create.");
        ed.Selection.Clear();
    }

    private static async Task<List<Matrix2D>?> RectangularArray(Editor ed)
    {
        var rows = await AskCount(ed, "Numero di righe <1>:");
        var columns = rows is null ? null : await AskCount(ed, "Numero di colonne <1>:");
        if (rows is null || columns is null)
        {
            return null;
        }

        var rowSpacing = 0.0;
        var columnSpacing = 0.0;
        if (rows > 1)
        {
            var r = await ed.GetNumberAsync("Distanza tra le righe (verso l'alto se positiva):");
            if (!r.IsOk)
            {
                return null;
            }

            rowSpacing = r.Value;
        }

        if (columns > 1)
        {
            var c = await ed.GetNumberAsync("Distanza tra le colonne (verso destra se positiva):");
            if (!c.IsOk)
            {
                return null;
            }

            columnSpacing = c.Value;
        }

        var result = new List<Matrix2D>();
        for (var i = 0; i < rows; i++)
        {
            for (var j = 0; j < columns; j++)
            {
                if (i != 0 || j != 0)
                {
                    result.Add(Matrix2D.Translation(new Vector2(j * columnSpacing, i * rowSpacing)));
                }
            }
        }

        return result;
    }

    private static async Task<List<Matrix2D>?> PolarArray(Editor ed)
    {
        var center = await ed.GetPointAsync("Centro della serie:");
        if (!center.IsOk)
        {
            return null;
        }

        var count = await AskCount(ed, "Numero di elementi, compreso l'originale:", defaultValue: null);
        if (count is null or < 2)
        {
            if (count is not null)
            {
                ed.Write("Servono almeno due elementi.");
            }

            return null;
        }

        var fill = await ed.GetNumberAsync("Angolo da riempire, in gradi (negativo = orario) <360>:");
        if (fill.Status == PromptStatus.Cancel)
        {
            return null;
        }

        var degrees = fill.IsOk ? fill.Value : 360;
        // Su un giro completo l'ultimo elemento coinciderebbe con il primo.
        var step = Math.Abs(Math.Abs(degrees) - 360) < 1e-9 ? degrees / count.Value : degrees / (count.Value - 1);
        return [.. Enumerable.Range(1, count.Value - 1).Select(i => Matrix2D.Rotation(InputParser.DegreesToRadians(step * i), center.Point))];
    }

    /// <summary>Numero intero positivo; Invio a vuoto dà <paramref name="defaultValue"/>. Null se annullato.</summary>
    private static async Task<int?> AskCount(Editor ed, string prompt, int? defaultValue = 1)
    {
        while (true)
        {
            var result = await ed.GetNumberAsync(prompt);
            switch (result.Status)
            {
                case PromptStatus.None when defaultValue is not null:
                    return defaultValue;
                case PromptStatus.Ok when result.Value >= 1 && result.Value <= 10000 && Math.Abs(result.Value - Math.Round(result.Value)) < 1e-9:
                    return (int)Math.Round(result.Value);
                case PromptStatus.Ok:
                    ed.Write("Serve un numero intero tra 1 e 10000.");
                    continue;
                case PromptStatus.None:
                    continue;
                default:
                    return null;
            }
        }
    }

    private static async Task Explode(Editor ed)
    {
        var entities = await ed.GetSelectionAsync();
        if (entities is null)
        {
            return;
        }

        var exploded = 0;
        ed.Document.Edit("ESPLODI", e =>
        {
            foreach (var entity in entities)
            {
                if (Modify.Explode(entity) is { } parts)
                {
                    e.Remove(entity);
                    foreach (var part in parts)
                    {
                        part.Group = null;
                        e.Add(part);
                    }

                    exploded++;
                }
            }
        });
        ed.Selection.Clear();
        ed.Write(exploded == entities.Count ? $"{exploded} oggetti esplosi." : $"{exploded} oggetti esplosi, {entities.Count - exploded} non scomponibili.");
    }
}

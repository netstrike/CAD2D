using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>Tratteggio e proprietà: TRATTEGGIO, COLORE, TIPOLINEA, SCALATL.</summary>
internal static class DraftingCommands
{
    public static void Register(Editor editor)
    {
        editor.RegisterCommand("TRATTEGGIO", Hatch, "H", "BH", "HATCH", "BHATCH", "TRATT");
        editor.RegisterCommand("COLORE", Color, "COL", "COLOR");
        editor.RegisterCommand("TIPOLINEA", LinetypeCommand, "LT", "LINETYPE", "TL");
        editor.RegisterCommand("SCALATL", LinetypeScale, "LTS", "LTSCALE");
    }

    private static async Task Hatch(Editor ed)
    {
        var settings = ed.Settings;
        ed.Selection.Clear();
        var created = 0;
        while (true)
        {
            var prompt = $"Punto interno <{settings.HatchPattern}, scala {Editor.Format(settings.HatchScale)}, angolo {Editor.Format(settings.HatchAngle)}°>:";
            var result = await ed.GetPointAsync(prompt, null, null, "Motivo", "Scala", "Angolo", "Seleziona");
            switch (result.Status)
            {
                case PromptStatus.Ok:
                {
                    if (Hatching.FindBoundary(result.Point, ed.Locator.Overlapping) is { } loops)
                    {
                        AddHatch(ed, loops);
                        created++;
                    }
                    else
                    {
                        ed.Write("Nessun contorno chiuso intorno al punto. Servono cerchi, polilinee chiuse o ellissi; altrimenti usa l'opzione Seleziona.");
                    }

                    continue;
                }

                case PromptStatus.Keyword when result.Keyword == "Motivo":
                {
                    ed.Write("Motivi: " + string.Join(", ", HatchPatterns.Names.Select(n => $"{n} ({HatchPatterns.Describe(n)})")));
                    var name = await ed.GetStringAsync($"Nome del motivo <{settings.HatchPattern}>:");
                    if (name.IsOk)
                    {
                        var value = name.Text!.Trim();
                        if (HatchPatterns.Exists(value))
                        {
                            settings.HatchPattern = value.ToUpperInvariant();
                        }
                        else
                        {
                            ed.Write($"Motivo sconosciuto: {value}");
                        }
                    }

                    continue;
                }

                case PromptStatus.Keyword when result.Keyword == "Scala":
                {
                    var scale = await ed.GetNumberAsync($"Scala del motivo <{Editor.Format(settings.HatchScale)}>:");
                    if (scale.IsOk && scale.Value > 0)
                    {
                        settings.HatchScale = scale.Value;
                    }

                    continue;
                }

                case PromptStatus.Keyword when result.Keyword == "Angolo":
                {
                    var angle = await ed.GetNumberAsync($"Angolo del motivo in gradi <{Editor.Format(settings.HatchAngle)}>:");
                    if (angle.IsOk)
                    {
                        settings.HatchAngle = angle.Value;
                    }

                    continue;
                }

                case PromptStatus.Keyword:
                {
                    var entities = await ed.GetSelectionAsync("Seleziona i contorni chiusi:");
                    if (entities is null)
                    {
                        continue;
                    }

                    var loops = entities.Select(Hatching.ClosedLoop).Where(l => l is not null).Select(l => l!).ToList();
                    ed.Selection.Clear();
                    if (loops.Count == 0)
                    {
                        ed.Write("Nessuno degli oggetti selezionati è chiuso.");
                        continue;
                    }

                    AddHatch(ed, loops);
                    created++;
                    continue;
                }

                default:
                    if (created > 0)
                    {
                        ed.Write($"{created} tratteggi creati.");
                    }

                    return;
            }
        }
    }

    private static void AddHatch(Editor ed, IEnumerable<IReadOnlyList<PolylineVertex>> loops)
    {
        var settings = ed.Settings;
        var hatch = Hatching.Create(ed.CurrentLayer, loops, settings.HatchPattern, settings.HatchScale, InputParser.DegreesToRadians(settings.HatchAngle));
        ed.Document.Edit("TRATTEGGIO", e => e.Add(ed.Styled(hatch)));
    }

    private static async Task Color(Editor ed)
    {
        var names = StandardColors.All.Select(c => c.Name.Replace(" ", "", StringComparison.Ordinal)).Prepend("DaBlocco").Prepend("DaLayer").ToArray();
        var targets = ed.Selection.Items.ToList();
        var subject = targets.Count > 0 ? $"Colore per {targets.Count} entità selezionate" : "Colore per le nuove entità";
        var result = await ed.GetKeywordAsync($"{subject} <{StandardColors.Describe(ed.CurrentColor)}>:", names);
        if (result.Status != PromptStatus.Keyword)
        {
            return;
        }

        var color = result.Keyword switch
        {
            "DaLayer" => EntityColor.ByLayer,
            "DaBlocco" => EntityColor.ByBlock,
            var name => EntityColor.Explicit(StandardColors.All.First(c => c.Name.Replace(" ", "", StringComparison.Ordinal) == name).Color),
        };

        if (targets.Count > 0)
        {
            ed.ChangeProperties(targets, e => e.Color = color);
        }
        else
        {
            ed.CurrentColor = color;
        }
    }

    private static async Task LinetypeCommand(Editor ed)
    {
        var linetypes = ed.Document.Linetypes.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var names = linetypes.Select(l => l.Name).Prepend("DaBlocco").Prepend("DaLayer").ToArray();
        ed.Write("Tipi di linea: " + string.Join(", ", linetypes.Select(l => $"{l.Name} ({l.Description})")));
        var targets = ed.Selection.Items.ToList();
        var current = ed.CurrentLinetype?.Name ?? "DaLayer";
        var subject = targets.Count > 0 ? $"Tipo di linea per {targets.Count} entità selezionate" : "Tipo di linea per le nuove entità";
        var result = await ed.GetKeywordAsync($"{subject} <{current}>:", names);
        if (result.Status != PromptStatus.Keyword)
        {
            return;
        }

        var linetype = result.Keyword switch
        {
            "DaLayer" => null,
            "DaBlocco" => Linetype.ByBlock,
            var name => linetypes.First(l => l.Name == name),
        };

        if (targets.Count > 0)
        {
            ed.ChangeProperties(targets, e => e.Linetype = linetype);
        }
        else
        {
            ed.CurrentLinetype = linetype;
        }
    }

    private static async Task LinetypeScale(Editor ed)
    {
        var result = await ed.GetNumberAsync($"Scala globale dei tipi di linea <{Editor.Format(ed.Document.LinetypeScale)}>:");
        if (result.IsOk && result.Value > 0)
        {
            ed.Document.LinetypeScale = result.Value;
            ed.Document.MarkModified();
        }
    }
}

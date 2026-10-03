using Cad.Document;

namespace Cad.Editing;

/// <summary>LAYER dalla riga di comando: crea, rende corrente, cambia colore e tipo di linea, accende, blocca, congela, rinomina, elimina.</summary>
internal static class LayerCommands
{
    private static readonly string[] Options =
        ["Nuovo", "Corrente", "Colore", "Tipolinea", "Accendi", "Spegni", "Blocca", "Sblocca", "Congela", "Scongela", "Rinomina", "Elimina", "Elenco"];

    public static void Register(Editor editor) => editor.RegisterCommand("LAYER", Layer, "LA", "-LAYER", "LAYMGR");

    private static async Task Layer(Editor ed)
    {
        var document = ed.Document;
        while (true)
        {
            var option = await ed.GetKeywordAsync($"Layer corrente {ed.CurrentLayer.Name}. Opzione [{string.Join('/', Options)}] <fine>:", Options);
            if (option.Status != PromptStatus.Keyword)
            {
                return;
            }

            switch (option.Keyword)
            {
                case "Elenco":
                    foreach (var layer in document.Layers.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase))
                    {
                        ed.Write(Describe(layer, ed.CurrentLayer));
                    }

                    break;

                case "Nuovo":
                {
                    var names = await AskNames(ed, "Nomi dei nuovi layer (separati da virgola):");
                    foreach (var name in names)
                    {
                        if (document.FindLayer(name) is not null)
                        {
                            ed.Write($"Il layer {name} esiste già.");
                        }
                        else if (!CadDocument.IsValidName(name))
                        {
                            ed.Write($"Nome non valido: {name}");
                        }
                        else
                        {
                            document.GetOrAddLayer(name);
                            document.MarkModified();
                        }
                    }

                    break;
                }

                case "Corrente":
                {
                    if (await AskLayers(ed, "Layer da rendere corrente:", single: true) is [var layer])
                    {
                        if (layer.IsFrozen)
                        {
                            ed.Write("Un layer congelato non può essere corrente.");
                        }
                        else
                        {
                            ed.CurrentLayer = layer;
                            document.RaiseChanged();
                        }
                    }

                    break;
                }

                case "Colore":
                {
                    var color = await ed.GetStringAsync("Colore (nome, 1-9 oppure r,g,b):");
                    if (!color.IsOk)
                    {
                        break;
                    }

                    if (StandardColors.Parse(color.Text!) is not { } value)
                    {
                        ed.Write("Colore non riconosciuto.");
                        break;
                    }

                    Apply(ed, await AskLayers(ed, "Layer a cui dare il colore:"), l => l.Color = value);
                    break;
                }

                case "Tipolinea":
                {
                    var name = await ed.GetStringAsync("Tipo di linea:");
                    if (!name.IsOk)
                    {
                        break;
                    }

                    if (document.FindLinetype(name.Text!.Trim()) is not { } linetype || ReferenceEquals(linetype, Linetype.ByBlock))
                    {
                        ed.Write("Tipi di linea: " + string.Join(", ", document.Linetypes.Select(l => l.Name)));
                        break;
                    }

                    Apply(ed, await AskLayers(ed, "Layer a cui dare il tipo di linea:"), l => l.Linetype = linetype);
                    break;
                }

                case "Accendi":
                    Apply(ed, await AskLayers(ed, "Layer da accendere:"), l => l.IsOn = true);
                    break;
                case "Spegni":
                    Apply(ed, await AskLayers(ed, "Layer da spegnere:"), l => l.IsOn = false);
                    break;
                case "Blocca":
                    Apply(ed, await AskLayers(ed, "Layer da bloccare:"), l => l.IsLocked = true);
                    ed.Selection.Remove(ed.Selection.Items.Where(e => !EntityLocator.IsSelectable(e)).ToList());
                    break;
                case "Sblocca":
                    Apply(ed, await AskLayers(ed, "Layer da sbloccare:"), l => l.IsLocked = false);
                    break;
                case "Congela":
                {
                    var layers = (await AskLayers(ed, "Layer da congelare:")).Where(l => l != ed.CurrentLayer || Refuse(ed)).ToList();
                    Apply(ed, layers, l => l.IsFrozen = true);
                    ed.Selection.Remove(ed.Selection.Items.Where(e => !EntityLocator.IsSelectable(e)).ToList());
                    break;
                }

                case "Scongela":
                    Apply(ed, await AskLayers(ed, "Layer da scongelare:"), l => l.IsFrozen = false);
                    break;

                case "Rinomina":
                {
                    if (await AskLayers(ed, "Layer da rinominare:", single: true) is not [var layer])
                    {
                        break;
                    }

                    var name = await ed.GetStringAsync("Nuovo nome:", layer.Name);
                    if (name.IsOk && name.Text!.Trim() != layer.Name)
                    {
                        if (document.RenameLayer(layer, name.Text!.Trim()))
                        {
                            document.MarkModified();
                        }
                        else
                        {
                            ed.Write("Impossibile rinominare: il layer 0 non si rinomina e il nome deve essere valido e libero.");
                        }
                    }

                    break;
                }

                case "Elimina":
                {
                    foreach (var layer in await AskLayers(ed, "Layer da eliminare:"))
                    {
                        if (layer == ed.CurrentLayer)
                        {
                            ed.Write($"{layer.Name}: è il layer corrente.");
                        }
                        else if (document.RemoveLayer(layer))
                        {
                            document.MarkModified();
                        }
                        else
                        {
                            ed.Write($"{layer.Name}: non si può eliminare (layer 0 o contiene oggetti).");
                        }
                    }

                    break;
                }
            }
        }
    }

    private static bool Refuse(Editor ed)
    {
        ed.Write("Il layer corrente non si può congelare.");
        return false;
    }

    public static string Describe(Layer layer, Layer current) =>
        $"{(layer == current ? "* " : "  ")}{layer.Name}: {StandardColors.Describe(EntityColor.Explicit(layer.Color))}, {layer.Linetype.Name}" +
        (layer.IsOn ? "" : ", spento") + (layer.IsFrozen ? ", congelato" : "") + (layer.IsLocked ? ", bloccato" : "");

    private static void Apply(Editor ed, IReadOnlyList<Layer> layers, Action<Layer> change)
    {
        if (layers.Count == 0)
        {
            return;
        }

        foreach (var layer in layers)
        {
            change(layer);
        }

        ed.Document.MarkModified();
    }

    private static async Task<List<string>> AskNames(Editor ed, string prompt)
    {
        var answer = await ed.GetStringAsync(prompt);
        return answer.IsOk
            ? answer.Text!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
    }

    /// <summary>Layer esistenti indicati per nome (separati da virgola, * = tutti).</summary>
    private static async Task<IReadOnlyList<Layer>> AskLayers(Editor ed, string prompt, bool single = false)
    {
        var names = await AskNames(ed, single ? prompt : prompt.TrimEnd(':') + " (separati da virgola, * = tutti):");
        if (names is ["*"] && !single)
        {
            return ed.Document.Layers.ToList();
        }

        var result = new List<Layer>();
        foreach (var name in names)
        {
            if (ed.Document.FindLayer(name) is { } layer)
            {
                result.Add(layer);
            }
            else
            {
                ed.Write($"Il layer {name} non esiste.");
            }
        }

        return single && result.Count > 1 ? result.Take(1).ToList() : result;
    }
}

using Cad.Document;

namespace Cad.Editing;

/// <summary>Gruppi: GRUPPO (crea, aggiunge, toglie, rinomina, elenca) e SEPARA.</summary>
public static class GroupCommands
{
    internal static void Register(Editor editor)
    {
        editor.RegisterCommand("GRUPPO", Group, "G", "GROUP");
        editor.RegisterCommand("SEPARA", Ungroup, "UNGROUP", "SG");
    }

    /// <summary>
    /// Copie di più oggetti (COPIA, SERIE, INCOLLA...): se tra gli originali c'è un gruppo intero, le sue copie formano
    /// un nuovo gruppo senza nome, come negli altri CAD; le copie di parti di un gruppo restano fuori dai gruppi.
    /// </summary>
    public static List<Entity> Copies(CadDocument document, IReadOnlyList<Entity> originals, Func<Entity, Entity> copy)
    {
        var copies = originals.Select(copy).ToList();
        var whole = originals
            .Where(e => e.Group is not null)
            .GroupBy(e => e.Group!)
            .Where(g => g.Count() == document.Members(g.Key).Count())
            .ToDictionary(g => g.Key, g => (CadGroup?)null);
        for (var i = 0; i < copies.Count; i++)
        {
            if (originals[i].Group is { } group && whole.ContainsKey(group))
            {
                copies[i].Group = whole[group] ??= NewUnnamedLike(document, group);
            }
            else
            {
                copies[i].Group = null;
            }
        }

        return copies;
    }

    /// <summary>Gruppo senza nome per le copie: tiene la descrizione (per esempio "Tabella", che serve a CELLA).</summary>
    private static CadGroup NewUnnamedLike(CadDocument document, CadGroup original)
    {
        var group = document.AddGroup()!;
        group.Description = original.Description;
        group.Selectable = original.Selectable;
        return group;
    }

    /// <summary>Tutti i membri dei gruppi selezionabili degli oggetti dati (per la selezione a gruppi).</summary>
    public static IReadOnlyList<Entity> ExpandToGroups(CadDocument document, IReadOnlyList<Entity> entities)
    {
        var groups = entities.Select(e => e.Group).Where(g => g is { Selectable: true }).Distinct().ToList();
        if (groups.Count == 0)
        {
            return entities;
        }

        return [.. entities.Concat(document.ModelSpace.Where(e => e.Group is { } g && groups.Contains(g) && EntityLocator.IsSelectable(e))).Distinct()];
    }

    /// <summary>Mette o toglie un gruppo agli oggetti con una modifica annullabile.</summary>
    public static void Assign(Editor ed, string name, IEnumerable<Entity> entities, CadGroup? group) =>
        ed.Document.Edit(name, e =>
        {
            foreach (var entity in entities.Where(x => x.Group != group))
            {
                var copy = entity.Transformed(Geometry.Matrix2D.Identity);
                copy.Group = group;
                e.Replace(entity, copy);
            }
        });

    private static async Task Group(Editor ed)
    {
        var option = await ed.GetKeywordAsync("Gruppo [Crea/Aggiungi/Togli/Rinomina/Elenco] <Crea>:", "Crea", "Aggiungi", "Togli", "Rinomina", "Elenco");
        switch (option.Status)
        {
            case PromptStatus.None:
            case PromptStatus.Keyword when option.Keyword == "Crea":
                await Create(ed);
                return;
            case PromptStatus.Keyword when option.Keyword == "Elenco":
                List(ed);
                return;
            case PromptStatus.Keyword when option.Keyword == "Rinomina":
                await Rename(ed);
                return;
            case PromptStatus.Keyword:
                await AddOrRemove(ed, option.Keyword == "Aggiungi");
                return;
        }
    }

    private static async Task Create(Editor ed)
    {
        CadGroup? group;
        while (true)
        {
            var name = await ed.GetStringAsync("Nome del gruppo <senza nome>:");
            if (name.Status == PromptStatus.Cancel)
            {
                return;
            }

            var text = name.Text?.Trim();
            group = ed.Document.AddGroup(text);
            if (group is not null)
            {
                break;
            }

            ed.Write($"Nome non valido o già usato: {text}");
        }

        var entities = await ed.GetSelectionAsync("Seleziona gli oggetti del gruppo:");
        if (entities is null || entities.Count == 0)
        {
            ed.Document.RemoveGroup(group);
            return;
        }

        Assign(ed, "GRUPPO", entities, group);
        ed.Selection.Clear();
        ed.Write($"Gruppo {group.Name} con {entities.Count} oggetti.");
    }

    private static async Task AddOrRemove(Editor ed, bool add)
    {
        var picked = await ed.GetEntityAsync("Seleziona un oggetto del gruppo:");
        if (!picked.IsOk || picked.Entity?.Group is not { } group)
        {
            if (picked.IsOk)
            {
                ed.Write("L'oggetto non fa parte di un gruppo.");
            }

            return;
        }

        // Senza selezione a gruppi, altrimenti il clic prenderebbe di nuovo tutto il gruppo.
        var previous = ed.GroupSelectionEnabled;
        ed.GroupSelectionEnabled = false;
        ed.Selection.Clear();
        var entities = await ed.GetSelectionAsync(add ? "Oggetti da aggiungere:" : "Oggetti da togliere:");
        ed.GroupSelectionEnabled = previous;
        if (entities is null || entities.Count == 0)
        {
            return;
        }

        Assign(ed, "GRUPPO", add ? entities : entities.Where(e => e.Group == group), add ? group : null);
        ed.Selection.Clear();
        ed.Write($"Gruppo {group.Name}: {ed.Document.Members(group).Count()} oggetti.");
    }

    private static async Task Rename(Editor ed)
    {
        var picked = await ed.GetEntityAsync("Seleziona un oggetto del gruppo:");
        if (!picked.IsOk || picked.Entity?.Group is not { } group)
        {
            return;
        }

        var name = await ed.GetStringAsync($"Nuovo nome per {group.Name}:");
        if (name.IsOk && name.Text!.Trim() is { Length: > 0 } text)
        {
            ed.Write(ed.Document.RenameGroup(group, text) ? $"Gruppo rinominato in {text}." : $"Nome non valido o già usato: {text}");
            ed.Document.MarkModified();
        }
    }

    private static void List(Editor ed)
    {
        var groups = ed.Document.Groups.Select(g => (Group: g, Count: ed.Document.Members(g).Count())).Where(x => x.Count > 0).ToList();
        if (groups.Count == 0)
        {
            ed.Write("Nessun gruppo nel disegno.");
            return;
        }

        foreach (var (group, count) in groups)
        {
            ed.Write($"{group.Name}: {count} oggetti{(group.Selectable ? "" : ", non selezionabile in blocco")}");
        }
    }

    private static async Task Ungroup(Editor ed)
    {
        var entities = await ed.GetSelectionAsync("Seleziona i gruppi da separare:");
        if (entities is null)
        {
            return;
        }

        var groups = entities.Select(e => e.Group).OfType<CadGroup>().Distinct().ToList();
        if (groups.Count == 0)
        {
            ed.Write("Nessun gruppo tra gli oggetti selezionati.");
            return;
        }

        var members = ed.Document.ModelSpace.Where(e => e.Group is { } g && groups.Contains(g)).ToList();
        Assign(ed, "SEPARA", members, null);
        foreach (var group in groups)
        {
            ed.Document.RemoveGroup(group);
        }

        ed.Selection.Clear();
        ed.Write($"{groups.Count} gruppi separati.");
    }
}

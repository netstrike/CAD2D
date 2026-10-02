namespace Cad.Document;

/// <summary>
/// Raccoglie le modifiche di un comando. Tutte le modifiche fatte in un <see cref="CadDocument.Edit"/> si annullano insieme.
/// </summary>
public sealed class DocumentEditor
{
    private readonly CadDocument _document;
    private readonly List<Change> _changes;

    internal DocumentEditor(CadDocument document, List<Change> changes)
    {
        _document = document;
        _changes = changes;
    }

    public void Add(Entity entity)
    {
        _document.ModelSpace.Add(entity);
        _changes.Add(new Change(ChangeKind.Added, entity, null, _document.ModelSpace.Count - 1));
    }

    public void Remove(Entity entity)
    {
        var index = _document.ModelSpace.IndexOf(entity);
        if (index < 0)
        {
            return;
        }

        _document.ModelSpace.RemoveAt(index);
        _changes.Add(new Change(ChangeKind.Removed, entity, null, index));
    }

    /// <summary>Sostituisce un'entità con la sua versione modificata, nella stessa posizione dell'elenco.</summary>
    public void Replace(Entity original, Entity replacement)
    {
        var index = _document.ModelSpace.IndexOf(original);
        if (index < 0)
        {
            return;
        }

        _document.ModelSpace[index] = replacement;
        _changes.Add(new Change(ChangeKind.Replaced, original, replacement, index));
    }
}

internal enum ChangeKind
{
    Added,
    Removed,
    Replaced,
}

internal sealed record Change(ChangeKind Kind, Entity Entity, Entity? Replacement, int Index);

/// <summary>Operazione annullabile: il nome del comando e le modifiche nell'ordine in cui sono avvenute.</summary>
internal sealed record UndoUnit(string Name, IReadOnlyList<Change> Changes);

/// <summary>Pila di annulla/ripeti senza limite di passi.</summary>
public sealed class UndoHistory
{
    private readonly CadDocument _document;
    private readonly Stack<UndoUnit> _undo = new();
    private readonly Stack<UndoUnit> _redo = new();

    internal UndoHistory(CadDocument document) => _document = document;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.TryPeek(out var unit) ? unit.Name : null;
    public string? RedoName => _redo.TryPeek(out var unit) ? unit.Name : null;

    /// <summary>Numero di operazioni applicate, usato per sapere se il documento differisce dall'ultimo salvataggio.</summary>
    internal int Depth => _undo.Count;

    internal void Push(UndoUnit unit)
    {
        _undo.Push(unit);
        _redo.Clear();
    }

    /// <summary>Annulla l'ultima operazione e ne restituisce il nome, o null se non c'è nulla da annullare.</summary>
    public string? Undo()
    {
        if (!_undo.TryPop(out var unit))
        {
            return null;
        }

        var model = _document.ModelSpace;
        for (var i = unit.Changes.Count - 1; i >= 0; i--)
        {
            var change = unit.Changes[i];
            switch (change.Kind)
            {
                case ChangeKind.Added:
                    model.RemoveAt(change.Index);
                    break;
                case ChangeKind.Removed:
                    model.Insert(change.Index, change.Entity);
                    break;
                case ChangeKind.Replaced:
                    model[change.Index] = change.Entity;
                    break;
            }
        }

        _redo.Push(unit);
        _document.RaiseChanged();
        return unit.Name;
    }

    /// <summary>Ripete l'ultima operazione annullata e ne restituisce il nome, o null se non c'è nulla da ripetere.</summary>
    public string? Redo()
    {
        if (!_redo.TryPop(out var unit))
        {
            return null;
        }

        var model = _document.ModelSpace;
        foreach (var change in unit.Changes)
        {
            switch (change.Kind)
            {
                case ChangeKind.Added:
                    model.Insert(change.Index, change.Entity);
                    break;
                case ChangeKind.Removed:
                    model.RemoveAt(change.Index);
                    break;
                case ChangeKind.Replaced:
                    model[change.Index] = change.Replacement!;
                    break;
            }
        }

        _undo.Push(unit);
        _document.RaiseChanged();
        return unit.Name;
    }
}

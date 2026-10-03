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

    /// <summary>Sotto questa soglia si cerca l'entità scorrendo l'elenco; sopra si usa una mappa delle posizioni.</summary>
    private const int LinearSearchLimit = 256;

    private PositionMap? _positions;

    public void Add(Entity entity)
    {
        _positions?.Append(entity);
        _document.ModelSpace.Add(entity);
        _changes.Add(new Change(ChangeKind.Added, entity, null, _document.ModelSpace.Count - 1));
    }

    public void Remove(Entity entity)
    {
        var index = IndexOf(entity);
        if (index < 0)
        {
            return;
        }

        _positions?.Remove(entity);
        _document.ModelSpace.RemoveAt(index);
        _changes.Add(new Change(ChangeKind.Removed, entity, null, index));
    }

    /// <summary>Sostituisce un'entità con la sua versione modificata, nella stessa posizione dell'elenco.</summary>
    public void Replace(Entity original, Entity replacement)
    {
        var index = IndexOf(original);
        if (index < 0)
        {
            return;
        }

        _positions?.Replace(original, replacement);

        _document.ModelSpace[index] = replacement;
        _changes.Add(new Change(ChangeKind.Replaced, original, replacement, index));
    }

    /// <summary>
    /// Posizione di un'entità nel modello. Con migliaia di entità (SPOSTA o CANCELLA di tutto un disegno grande) la
    /// ricerca lineare a ogni entità diventerebbe quadratica: si usa la mappa delle posizioni.
    /// </summary>
    private int IndexOf(Entity entity)
    {
        var model = _document.ModelSpace;
        if (_positions is null && model.Count < LinearSearchLimit)
        {
            return model.IndexOf(entity);
        }

        _positions ??= new PositionMap(model);
        return _positions.IndexOf(entity);
    }

    /// <summary>
    /// Modifica di un'impostazione del disegno (stili, scala dei tipi di linea...) che si annulla insieme alle entità:
    /// <paramref name="apply"/> si esegue subito e a ogni ripeti, <paramref name="revert"/> a ogni annulla.
    /// </summary>
    public void Setting(Action apply, Action revert)
    {
        apply();
        _changes.Add(new Change(ChangeKind.Setting, null!, null, -1) { Apply = apply, Revert = revert });
    }
}

internal enum ChangeKind
{
    Added,
    Removed,
    Replaced,
    Setting,
}

internal sealed record Change(ChangeKind Kind, Entity Entity, Entity? Replacement, int Index)
{
    public Action? Apply { get; init; }
    public Action? Revert { get; init; }
}

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

        Revert(unit.Changes);
        _redo.Push(unit);
        _document.RaiseChanged();
        return unit.Name;
    }

    /// <summary>Riporta il modello a prima delle modifiche, dall'ultima alla prima.</summary>
    internal void Revert(IReadOnlyList<Change> changes)
    {
        var model = _document.ModelSpace;
        for (var i = changes.Count - 1; i >= 0; i--)
        {
            var change = changes[i];
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
                case ChangeKind.Setting:
                    change.Revert!();
                    break;
            }
        }

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
                case ChangeKind.Setting:
                    change.Apply!();
                    break;
            }
        }

        _undo.Push(unit);
        _document.RaiseChanged();
        return unit.Name;
    }
}

/// <summary>
/// Posizioni delle entità durante una modifica: ogni entità ha una posizione "virtuale" fissa e un albero di Fenwick
/// conta le rimozioni prima di ciascuna, così la posizione reale si trova in tempo logaritmico anche dopo molte rimozioni.
/// </summary>
internal sealed class PositionMap
{
    private readonly Dictionary<Entity, int> _virtual = new(ReferenceEqualityComparer.Instance);
    private int[] _removed;
    private int _next;

    public PositionMap(List<Entity> model)
    {
        for (var i = 0; i < model.Count; i++)
        {
            _virtual.TryAdd(model[i], i);
        }

        _next = model.Count;
        _removed = new int[Math.Max(16, model.Count * 2) + 1];
    }

    public int IndexOf(Entity entity) =>
        _virtual.TryGetValue(entity, out var position) ? position - RemovedBefore(position) : -1;

    public void Append(Entity entity)
    {
        if (_next + 1 >= _removed.Length)
        {
            Grow();
        }

        _virtual.TryAdd(entity, _next++);
    }

    public void Remove(Entity entity)
    {
        if (_virtual.Remove(entity, out var position))
        {
            for (var i = position + 1; i < _removed.Length; i += i & -i)
            {
                _removed[i]++;
            }
        }
    }

    public void Replace(Entity original, Entity replacement)
    {
        if (_virtual.Remove(original, out var position))
        {
            _virtual[replacement] = position;
        }
    }

    /// <summary>Rimozioni nelle posizioni virtuali da 0 a position - 1.</summary>
    private int RemovedBefore(int position)
    {
        var sum = 0;
        for (var i = position; i > 0; i -= i & -i)
        {
            sum += _removed[i];
        }

        return sum;
    }

    private void Grow()
    {
        // Si ricostruisce l'albero più grande a partire dai conteggi puntuali.
        var counts = new int[_next];
        for (var p = 0; p < _next; p++)
        {
            counts[p] = RemovedBefore(p + 1) - RemovedBefore(p);
        }

        _removed = new int[_removed.Length * 2];
        for (var p = 0; p < counts.Length; p++)
        {
            if (counts[p] == 0)
            {
                continue;
            }

            for (var i = p + 1; i < _removed.Length; i += i & -i)
            {
                _removed[i] += counts[p];
            }
        }
    }
}

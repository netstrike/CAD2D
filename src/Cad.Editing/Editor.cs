using System.Globalization;
using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>
/// Cuore dell'interazione: esegue i comandi, riceve punti e testo dalla finestra, applica snap e ortho,
/// gestisce la selezione e fornisce le anteprime da disegnare. Non dipende dall'interfaccia, quindi è testabile.
/// </summary>
public sealed class Editor
{
    public const string IdlePrompt = "Comando:";

    private readonly Dictionary<string, CommandInfo> _commands = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource<PromptResult>? _pending;
    private PromptKind _pendingKind;
    private IReadOnlyList<string> _keywords = [];
    private Func<Vector2, IEnumerable<Entity>>? _preview;
    private string? _lastCommand;
    private int _runId;
    private double _aperture = 1;

    public Editor(CadDocument document)
    {
        Document = document;
        Locator = new EntityLocator(document);
        CurrentLayer = document.FindLayer(Layer.DefaultName) ?? document.GetOrAddLayer(Layer.DefaultName);
        document.Changed += (_, _) => Selection.Prune(document);
        BuiltInCommands.Register(this);
    }

    public CadDocument Document { get; }
    public SelectionSet Selection { get; } = new();
    public EntityLocator Locator { get; }
    public Layer CurrentLayer { get; set; }

    /// <summary>Colore delle entità nuove (DaLayer se non scelto).</summary>
    public EntityColor CurrentColor { get; set; } = EntityColor.ByLayer;

    /// <summary>Tipo di linea delle entità nuove; null = DaLayer.</summary>
    public Linetype? CurrentLinetype { get; set; }

    /// <summary>
    /// Cambia proprietà (layer, colore, tipo di linea) delle entità date in un'unica operazione annullabile.
    /// Le entità modificate restano selezionate.
    /// </summary>
    public void ChangeProperties(IReadOnlyList<Entity> entities, Action<Entity> change)
    {
        var copies = new List<Entity>(entities.Count);
        Document.Edit("PROPRIETÀ", e =>
        {
            foreach (var entity in entities)
            {
                // Una copia "trasformata" con l'identità resta legata all'originale letto dal file, che il salvataggio conserva.
                var copy = entity.Transformed(Matrix2D.Identity);
                change(copy);
                e.Replace(entity, copy);
                copies.Add(copy);
            }
        });
        Selection.Clear();
        Selection.Add(copies);
    }

    /// <summary>Applica layer, colore e tipo di linea correnti a un'entità appena disegnata.</summary>
    public T Styled<T>(T entity) where T : Entity
    {
        entity.Layer = CurrentLayer;
        entity.Color = CurrentColor;
        entity.Linetype = CurrentLinetype;
        return entity;
    }

    /// <summary>Valori ricordati tra un comando e l'altro (distanza di offset, raggio di raccordo...).</summary>
    public EditorSettings Settings { get; } = new();

    public SnapModes SnapModes { get; set; } = SnapModes.Default;
    public bool SnapEnabled { get; set; } = true;
    public bool OrthoEnabled { get; set; }

    /// <summary>Cresce a ogni nuova richiesta: la finestra lo usa per sapere quando proporre <see cref="SuggestedInput"/>.</summary>
    public int PromptId { get; private set; }

    /// <summary>Testo da precompilare nella riga di comando per la richiesta in corso (per esempio il testo da modificare).</summary>
    public string? SuggestedInput { get; private set; }

    /// <summary>Testo da mostrare accanto alla riga di comando.</summary>
    public string Prompt { get; private set; } = IdlePrompt;

    public bool IsCommandActive => ActiveCommand is not null;
    public string? ActiveCommand { get; private set; }

    /// <summary>Punto da cui partono elastico, ortho, distanze e snap perpendicolare.</summary>
    public Vector2? BasePoint { get; private set; }

    /// <summary>Ultima posizione del cursore dopo snap e ortho.</summary>
    public Vector2 Cursor { get; private set; }

    public SnapResult? CurrentSnap { get; private set; }

    /// <summary>Primo angolo di una finestra di selezione in corso.</summary>
    public Vector2? WindowStart { get; private set; }

    /// <summary>Vero quando un clic deve selezionare entità (nessun comando, o comando che chiede oggetti).</summary>
    public bool IsSelecting => _pending is null || _pendingKind == PromptKind.Selection;

    /// <summary>Vero quando si sta scrivendo un testo libero: la barra spaziatrice inserisce uno spazio invece di confermare.</summary>
    public bool AcceptsSpaces => _pending is not null && _pendingKind == PromptKind.Text;

    /// <summary>Vero quando un clic deve indicare un'entità (il cursore non usa snap né ortho).</summary>
    public bool IsPickingEntity => _pending is not null && _pendingKind == PromptKind.Entity;

    /// <summary>Opzioni della richiesta in corso (per esempio Raggio e Polilinea in RACCORDA).</summary>
    public IReadOnlyList<string> Keywords => _keywords;

    /// <summary>Ultimo comando eseguito, quello che Invio a vuoto ripete.</summary>
    public string? LastCommand => _lastCommand;

    /// <summary>Entità sotto il cursore quando un clic la selezionerebbe: si evidenzia prima del clic.</summary>
    public Entity? HoverEntity { get; private set; }

    /// <summary>Raggio di cattura usato per l'ultimo movimento del mouse, in unità di disegno.</summary>
    public double Aperture => _aperture;

    /// <summary>Righe da aggiungere allo storico della riga di comando.</summary>
    public event Action<string>? Message;

    /// <summary>Lo stato da disegnare (prompt, anteprime, snap) è cambiato.</summary>
    public event EventHandler? StateChanged;

    public IEnumerable<string> CommandNames => _commands.Values.Select(c => c.Name).Distinct();

    /// <summary>Registra un comando con il suo nome e gli alias (per esempio LINEA, L, LINE).</summary>
    public void RegisterCommand(string name, Func<Editor, Task> run, params string[] aliases)
    {
        var info = new CommandInfo(name, run);
        _commands[name] = info;
        foreach (var alias in aliases)
        {
            _commands[alias] = info;
        }
    }

    public void Write(string message) => Message?.Invoke(message);

    // ---------- Input dalla finestra ----------

    /// <summary>Movimento del mouse: aggiorna snap, ortho e anteprime. <paramref name="aperture"/> è il raggio di cattura in unità di disegno.</summary>
    public void Hover(Vector2 world, double aperture)
    {
        _aperture = aperture;
        Cursor = ResolvePoint(world, aperture, out var snap);
        CurrentSnap = snap;
        HoverEntity = (IsSelecting || IsPickingEntity) && WindowStart is null ? Locator.Pick(world, aperture) : null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clic sinistro: un punto se il comando ne chiede uno, altrimenti selezione.</summary>
    public void Click(Vector2 world, double aperture, bool removeFromSelection = false)
    {
        _aperture = aperture;
        if (IsPickingEntity)
        {
            if (Locator.Pick(world, aperture) is { } picked)
            {
                CompletePending(new PromptResult(PromptStatus.Ok, world, Entity: picked));
            }
            else
            {
                Write("Nessun oggetto in quel punto.");
            }

            return;
        }

        if (_pendingKind is PromptKind.Number or PromptKind.Text or PromptKind.Keyword && _pending is not null)
        {
            return;
        }

        if (!IsSelecting)
        {
            var point = ResolvePoint(world, aperture, out _);
            CompletePending(_pendingKind switch
            {
                PromptKind.Distance when BasePoint is { } b => new PromptResult(PromptStatus.Ok, point, Vector2.Distance(b, point)),
                PromptKind.Angle when BasePoint is { } b => new PromptResult(PromptStatus.Ok, point, (point - b).Angle),
                _ => new PromptResult(PromptStatus.Ok, point),
            });
            return;
        }

        if (WindowStart is { } start)
        {
            // Da sinistra a destra: finestra (solo entità interne). Da destra a sinistra: interseca.
            var crossing = world.X < start.X;
            var found = Locator.InWindow(BoundingBox.FromPoints(start, world), crossing).ToList();
            WindowStart = null;
            ApplySelection(found, removeFromSelection);
            return;
        }

        if (Locator.Pick(world, aperture) is { } entity)
        {
            ApplySelection([entity], removeFromSelection);
            return;
        }

        WindowStart = world;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Invio sulla riga di comando (anche vuoto).</summary>
    public void SubmitText(string text)
    {
        var trimmed = text.Trim();
        if (_pending is null)
        {
            if (trimmed.Length == 0)
            {
                if (_lastCommand is not null)
                {
                    RunCommand(_lastCommand);
                }

                return;
            }

            RunCommand(trimmed);
            return;
        }

        if (_pendingKind == PromptKind.Text)
        {
            CompletePending(text.Length == 0 ? PromptResult.Empty : new PromptResult(PromptStatus.Ok, Text: text));
            return;
        }

        if (trimmed.Length == 0)
        {
            CompletePending(PromptResult.Empty);
            return;
        }

        if (InputParser.MatchKeyword(trimmed, _keywords) is { } keyword)
        {
            CompletePending(new PromptResult(PromptStatus.Keyword, Keyword: keyword));
            return;
        }

        if (TryInterpret(trimmed, out var result))
        {
            CompletePending(result);
            return;
        }

        Write($"Valore non valido: {trimmed}");
    }

    /// <summary>Esc: interrompe il comando, oppure annulla finestra e selezione.</summary>
    public void Cancel()
    {
        if (_pending is not null && _pendingKind != PromptKind.Selection)
        {
            CompletePending(PromptResult.Cancelled);
            return;
        }

        if (_pending is not null)
        {
            Selection.Clear();
            WindowStart = null;
            CompletePending(PromptResult.Cancelled);
            return;
        }

        WindowStart = null;
        Selection.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Entità di anteprima da disegnare per la posizione attuale del cursore.</summary>
    public IReadOnlyList<Entity> GetPreview() => _preview is null ? [] : [.. _preview(Cursor)];

    // ---------- Esecuzione dei comandi ----------

    public void RunCommand(string name)
    {
        var key = name.Trim().TrimStart('_');
        if (!_commands.TryGetValue(key, out var command))
        {
            Write($"Comando sconosciuto: {name.Trim().ToUpperInvariant()}");
            return;
        }

        if (_pending is not null)
        {
            CompletePending(PromptResult.Cancelled);
        }

        _lastCommand = command.Name;
        _ = RunAsync(command);
    }

    private async Task RunAsync(CommandInfo command)
    {
        var id = ++_runId;
        ActiveCommand = command.Name;
        WindowStart = null;
        Write($"{IdlePrompt} {command.Name}");
        try
        {
            await command.Run(this);
        }
        catch (Exception ex)
        {
            Write($"{command.Name}: errore: {ex.Message}");
        }
        finally
        {
            // Un comando avviato mentre questo terminava ha già preso il controllo.
            if (id == _runId)
            {
                ActiveCommand = null;
                Prompt = IdlePrompt;
                BasePoint = null;
                _preview = null;
                _keywords = [];
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // ---------- Richieste usate dai comandi ----------

    public Task<PromptResult> GetPointAsync(string prompt, Vector2? basePoint = null, Func<Vector2, IEnumerable<Entity>>? preview = null, params string[] keywords) =>
        Ask(PromptKind.Point, prompt, basePoint, preview, keywords);

    /// <summary>Distanza scritta come numero, oppure indicata con un punto (distanza dal punto base).</summary>
    public Task<PromptResult> GetDistanceAsync(string prompt, Vector2 basePoint, Func<Vector2, IEnumerable<Entity>>? preview = null, params string[] keywords) =>
        Ask(PromptKind.Distance, prompt, basePoint, preview, keywords);

    /// <summary>Angolo scritto in gradi, oppure indicato con un punto (direzione dal punto base). Il risultato è in radianti.</summary>
    public Task<PromptResult> GetAngleAsync(string prompt, Vector2 basePoint, Func<Vector2, IEnumerable<Entity>>? preview = null, params string[] keywords) =>
        Ask(PromptKind.Angle, prompt, basePoint, preview, keywords);

    /// <summary>Un'entità indicata con un clic. Il risultato porta l'entità e il punto cliccato.</summary>
    public Task<PromptResult> GetEntityAsync(string prompt, params string[] keywords) =>
        Ask(PromptKind.Entity, prompt, null, null, keywords);

    /// <summary>Una delle opzioni elencate; Invio a vuoto restituisce <see cref="PromptStatus.None"/> (l'opzione predefinita).</summary>
    public Task<PromptResult> GetKeywordAsync(string prompt, params string[] keywords) =>
        Ask(PromptKind.Keyword, prompt, null, null, keywords);

    /// <summary>Un numero scritto sulla riga di comando.</summary>
    public Task<PromptResult> GetNumberAsync(string prompt, params string[] keywords) =>
        Ask(PromptKind.Number, prompt, null, null, keywords);

    /// <summary>Testo libero, spazi compresi. Invio a vuoto restituisce <see cref="PromptStatus.None"/>.</summary>
    public Task<PromptResult> GetStringAsync(string prompt, string? initialText = null, Vector2? basePoint = null, Func<Vector2, IEnumerable<Entity>>? preview = null) =>
        Ask(PromptKind.Text, prompt, basePoint, preview, [], initialText);

    /// <summary>
    /// Entità su cui operare: se c'è già una selezione si usa quella, altrimenti si selezionano con clic e finestre
    /// e si conferma con Invio. Restituisce null se annullato o se non è stato selezionato nulla.
    /// </summary>
    public async Task<IReadOnlyList<Entity>?> GetSelectionAsync(string prompt = "Seleziona entità:")
    {
        if (Selection.Count == 0)
        {
            var result = await Ask(PromptKind.Selection, prompt, null, null, []);
            if (result.Status == PromptStatus.Cancel)
            {
                return null;
            }
        }

        if (Selection.Count == 0)
        {
            Write("Nessuna entità selezionata.");
            return null;
        }

        var items = Selection.Items.ToList();
        Write($"{items.Count} entità selezionate.");
        return items;
    }

    private Task<PromptResult> Ask(
        PromptKind kind, string prompt, Vector2? basePoint, Func<Vector2, IEnumerable<Entity>>? preview, IReadOnlyList<string> keywords, string? initialText = null)
    {
        PromptId++;
        SuggestedInput = initialText;
        _pending = new TaskCompletionSource<PromptResult>();
        _pendingKind = kind;
        _keywords = keywords;
        _preview = preview;
        BasePoint = basePoint;
        Prompt = keywords.Count == 0 ? prompt : $"{prompt} [{string.Join("/", keywords)}]";
        StateChanged?.Invoke(this, EventArgs.Empty);
        return _pending.Task;
    }

    private void CompletePending(PromptResult result)
    {
        var pending = _pending;
        if (pending is null)
        {
            return;
        }

        _pending = null;
        _preview = null;
        WindowStart = null;
        pending.SetResult(result);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Converte il testo scritto nel valore atteso dalla richiesta in corso.</summary>
    private bool TryInterpret(string text, out PromptResult result)
    {
        result = default;
        switch (_pendingKind)
        {
            case PromptKind.Point:
                if (InputParser.TryParsePoint(text, BasePoint, out var point))
                {
                    result = new PromptResult(PromptStatus.Ok, point);
                    return true;
                }

                // Distanza diretta: un numero sposta il punto lungo la direzione del cursore.
                if (BasePoint is { } from && InputParser.TryParseNumber(text, out var length))
                {
                    var direction = (Cursor - from).Normalized();
                    if (direction == Vector2.Zero)
                    {
                        direction = Vector2.UnitX;
                    }

                    result = new PromptResult(PromptStatus.Ok, from + direction * length);
                    return true;
                }

                return false;

            case PromptKind.Distance:
                if (InputParser.TryParseNumber(text, out var distance))
                {
                    result = new PromptResult(PromptStatus.Ok, Value: distance);
                    return distance >= 0;
                }

                if (BasePoint is { } b1 && InputParser.TryParsePoint(text, b1, out var p1))
                {
                    result = new PromptResult(PromptStatus.Ok, p1, Vector2.Distance(b1, p1));
                    return true;
                }

                return false;

            case PromptKind.Angle:
                if (InputParser.TryParseNumber(text, out var degrees))
                {
                    result = new PromptResult(PromptStatus.Ok, Value: InputParser.DegreesToRadians(degrees));
                    return true;
                }

                if (BasePoint is { } b2 && InputParser.TryParsePoint(text, b2, out var p2))
                {
                    result = new PromptResult(PromptStatus.Ok, p2, (p2 - b2).Angle);
                    return true;
                }

                return false;

            case PromptKind.Number:
                if (InputParser.TryParseNumber(text, out var number))
                {
                    result = new PromptResult(PromptStatus.Ok, Value: number);
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    /// <summary>Applica snap (che ha la precedenza) e ortho a una posizione del cursore.</summary>
    public Vector2 ResolvePoint(Vector2 world, double aperture, out SnapResult? snap)
    {
        snap = null;
        if (_pending is null || _pendingKind is PromptKind.Selection or PromptKind.Entity)
        {
            return world;
        }

        if (SnapEnabled)
        {
            snap = SnapEngine.Find(Locator.Near(world, aperture), world, aperture, SnapModes, BasePoint);
            if (snap is { } s)
            {
                return s.Point;
            }
        }

        if (OrthoEnabled && BasePoint is { } b)
        {
            var d = world - b;
            return Math.Abs(d.X) >= Math.Abs(d.Y) ? new Vector2(world.X, b.Y) : new Vector2(b.X, world.Y);
        }

        return world;
    }

    private void ApplySelection(IReadOnlyList<Entity> entities, bool remove)
    {
        if (remove)
        {
            Selection.Remove(entities);
        }
        else
        {
            Selection.Add(entities);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Modifica di un grip: stira il punto <paramref name="gripIndex"/> dell'entità.</summary>
    public void StartGripEdit(Entity entity, int gripIndex)
    {
        if (_pending is not null && _pendingKind != PromptKind.Selection)
        {
            return;
        }

        if (_pending is not null)
        {
            CompletePending(PromptResult.Cancelled);
        }

        _ = RunAsync(new CommandInfo("STIRA", async editor =>
        {
            var grip = entity.Grips[gripIndex];
            var target = await editor.GetPointAsync("Punto di stiramento:", grip, p => [entity.WithGripMoved(gripIndex, p)]);
            if (target.IsOk)
            {
                editor.Document.Edit("STIRA", e => e.Replace(entity, entity.WithGripMoved(gripIndex, target.Point)));
                editor.Selection.Clear();
            }
        }));
    }

    /// <summary>Formatta un numero per i messaggi.</summary>
    internal static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private sealed record CommandInfo(string Name, Func<Editor, Task> Run);
}

/// <summary>Valori dei comandi che restano tra un uso e l'altro, come in DraftSight.</summary>
public sealed class EditorSettings
{
    public double OffsetDistance { get; set; } = 10;
    public double FilletRadius { get; set; }
    public double ChamferDistance1 { get; set; }
    public double ChamferDistance2 { get; set; }
    public double TextHeight { get; set; } = 2.5;

    /// <summary>Ultimo blocco inserito, proposto da INSERISCI.</summary>
    public string? LastBlock { get; set; }

    public string HatchPattern { get; set; } = "ANSI31";
    public double HatchScale { get; set; } = 1;

    /// <summary>Angolo del tratteggio in gradi.</summary>
    public double HatchAngle { get; set; }
}

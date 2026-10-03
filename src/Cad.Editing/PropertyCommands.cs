using System.Globalization;
using System.Text.Json;
using Cad.Document;
using Cad.Geometry;

namespace Cad.Editing;

/// <summary>Gruppi di proprietà che si copiano da un oggetto all'altro.</summary>
[Flags]
public enum PropertyMask
{
    None = 0,
    Layer = 1,
    Color = 2,
    Linetype = 4,
    LinetypeScale = 8,
    LineWeight = 16,

    /// <summary>Stile e altezza dei testi.</summary>
    Text = 32,

    /// <summary>Stile di quote e direttrici.</summary>
    Dimension = 64,

    /// <summary>Motivo, scala e angolo dei tratteggi.</summary>
    Hatch = 128,

    General = Layer | Color | Linetype | LinetypeScale | LineWeight,
    All = General | Text | Dimension | Hatch,
}

/// <summary>
/// Proprietà di un oggetto o quelle correnti, per nome e non per riferimento: si incollano anche in un altro disegno
/// e si salvano su file. Un valore null vuol dire "non definita" e non viene applicato.
/// </summary>
public sealed class PropertySet
{
    public string Name { get; set; } = "";
    public string? Layer { get; set; }

    /// <summary>Colore del layer ("r,g,b"), per crearlo uguale nei disegni che non lo hanno.</summary>
    public string? LayerColor { get; set; }

    /// <summary>"DaLayer", "DaBlocco" o "r,g,b".</summary>
    public string? Color { get; set; }

    /// <summary>Nome del tipo di linea, "DaLayer" o "DaBlocco".</summary>
    public string? Linetype { get; set; }
    public double? LinetypeScale { get; set; }
    public int? LineWeight { get; set; }
    public string? TextStyle { get; set; }
    public double? TextHeight { get; set; }
    public string? DimensionStyle { get; set; }
    public string? HatchPattern { get; set; }
    public double? HatchScale { get; set; }

    /// <summary>Angolo del motivo in gradi.</summary>
    public double? HatchAngle { get; set; }

    /// <summary>Riassunto leggibile, per la riga di comando e le finestre.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        void Add(string label, string? value)
        {
            if (value is not null)
            {
                parts.Add($"{label} {value}");
            }
        }

        Add("layer", Layer);
        Add("colore", Color is null ? null : StandardColors.Describe(PropertyTools.ParseColor(Color) ?? EntityColor.ByLayer));
        Add("tipo linea", Linetype);
        Add("scala TL", LinetypeScale is { } s ? PropertySheet.Format(s) : null);
        Add("spessore", LineWeight is { } w ? Document.LineWeight.Format(w) : null);
        Add("stile testo", TextStyle);
        Add("altezza", TextHeight is { } h ? PropertySheet.Format(h) : null);
        Add("stile quota", DimensionStyle);
        Add("tratteggio", HatchPattern is null ? null : $"{HatchPattern} {PropertySheet.Format(HatchScale ?? 1)} {PropertySheet.Format(HatchAngle ?? 0)}°");
        return string.Join(", ", parts);
    }

    public PropertySet Clone() => (PropertySet)MemberwiseClone();
}

/// <summary>Lettura e applicazione delle proprietà: da un oggetto, a una selezione o come proprietà correnti.</summary>
public static class PropertyTools
{
    public const string ByLayer = "DaLayer";
    public const string ByBlock = "DaBlocco";

    /// <summary>Le proprietà di un oggetto: quelle generali sempre, quelle di testo, quota o tratteggio se le ha.</summary>
    public static PropertySet Capture(Entity entity)
    {
        var set = new PropertySet
        {
            Layer = entity.Layer.Name,
            LayerColor = FormatRgb(entity.Layer.Color),
            Color = FormatColor(entity.Color),
            Linetype = FormatLinetype(entity.Linetype),
            LinetypeScale = entity.LinetypeScale,
            LineWeight = entity.LineWeight,
        };
        switch (entity)
        {
            case TextEntity text:
                set.TextStyle = text.Style?.Name ?? Document.TextStyle.DefaultName;
                set.TextHeight = text.Height;
                break;
            case DimensionEntity dimension:
                set.DimensionStyle = dimension.Style.Name;
                break;
            case LeaderEntity leader:
                set.DimensionStyle = leader.Style.Name;
                break;
            case HatchEntity hatch:
                set.HatchPattern = hatch.PatternName;
                set.HatchScale = hatch.PatternScale;
                set.HatchAngle = hatch.PatternAngle * 180 / Math.PI;
                break;
        }

        return set;
    }

    /// <summary>Le proprietà con cui si disegnano gli oggetti nuovi.</summary>
    public static PropertySet CaptureCurrent(Editor editor) => new()
    {
        Layer = editor.CurrentLayer.Name,
        LayerColor = FormatRgb(editor.CurrentLayer.Color),
        Color = FormatColor(editor.CurrentColor),
        Linetype = FormatLinetype(editor.CurrentLinetype),
        LineWeight = editor.CurrentLineWeight,
        TextStyle = editor.Document.CurrentTextStyle.Name,
        TextHeight = editor.Settings.TextHeight,
        DimensionStyle = editor.Document.CurrentDimensionStyle.Name,
        HatchPattern = editor.Settings.HatchPattern,
        HatchScale = editor.Settings.HatchScale,
        HatchAngle = editor.Settings.HatchAngle,
    };

    /// <summary>
    /// Applica le proprietà scelte da <paramref name="mask"/> agli oggetti, in un'unica operazione annullabile.
    /// Restituisce quanti oggetti sono cambiati. Un layer che manca viene creato; stili e tipi di linea che mancano
    /// nel disegno vengono saltati.
    /// </summary>
    public static int Apply(Editor editor, IReadOnlyList<Entity> entities, PropertySet set, PropertyMask mask)
    {
        var document = editor.Document;
        var layer = (mask & PropertyMask.Layer) != 0 && set.Layer is { } layerName ? ResolveLayer(document, layerName, set.LayerColor) : null;
        var color = (mask & PropertyMask.Color) != 0 && set.Color is { } colorText ? ParseColor(colorText) : null;
        var linetype = (mask & PropertyMask.Linetype) != 0 && set.Linetype is { } linetypeName ? ResolveLinetype(document, linetypeName) : null;
        var linetypeScale = (mask & PropertyMask.LinetypeScale) != 0 ? set.LinetypeScale : null;
        var weight = (mask & PropertyMask.LineWeight) != 0 ? set.LineWeight : null;
        var textStyle = (mask & PropertyMask.Text) != 0 && set.TextStyle is { } textStyleName ? document.FindTextStyle(textStyleName) : null;
        var textHeight = (mask & PropertyMask.Text) != 0 ? set.TextHeight : null;
        var dimensionStyle = (mask & PropertyMask.Dimension) != 0 && set.DimensionStyle is { } dimensionName
            ? document.DimensionStyles.FirstOrDefault(s => s.Name.Equals(dimensionName, StringComparison.OrdinalIgnoreCase))
            : null;
        var hatch = (mask & PropertyMask.Hatch) != 0 && set.HatchPattern is { } pattern && HatchPatterns.Exists(pattern) ? pattern : null;

        var replacements = new List<(Entity Old, Entity New)>();
        foreach (var entity in entities)
        {
            var current = entity;
            var changed = false;

            // Prima ciò che rigenera l'oggetto (altezza, stile, motivo), poi le proprietà generali sulla copia.
            if (current is TextEntity text)
            {
                if (textHeight is { } height && height > Tolerance.Default && Math.Abs(height - text.Height) > Tolerance.Default)
                {
                    current = text.Transformed(Matrix2D.Scaling(height / text.Height, text.Position));
                    changed = true;
                }

                if (textStyle is not null && !ReferenceEquals((current as TextEntity)!.Style ?? document.FindTextStyle(Document.TextStyle.DefaultName), textStyle))
                {
                    var copy = (TextEntity)current.CopyDetached();
                    copy.Style = textStyle;
                    copy.WidthFactor = textStyle.WidthFactor;
                    current = copy;
                    changed = true;
                }
            }
            else if (current is DimensionEntity dimension && dimensionStyle is not null && !ReferenceEquals(dimension.Style, dimensionStyle))
            {
                // La quota si rigenera con il nuovo stile: la grafica letta dal file non varrebbe più.
                var copy = (DimensionEntity)dimension.WithGripMoved(dimension.Grips.Count - 1, dimension.Location);
                copy.Style = dimensionStyle;
                current = copy;
                changed = true;
            }
            else if (current is LeaderEntity leader && dimensionStyle is not null && !ReferenceEquals(leader.Style, dimensionStyle))
            {
                var copy = (LeaderEntity)leader.CopyDetached();
                copy.Style = dimensionStyle;
                current = copy;
                changed = true;
            }
            else if (current is HatchEntity existing && hatch is not null)
            {
                var scale = set.HatchScale is > 0 ? set.HatchScale.Value : existing.PatternScale;
                var angle = set.HatchAngle is { } degrees ? degrees * Math.PI / 180 : existing.PatternAngle;
                if (!hatch.Equals(existing.PatternName, StringComparison.OrdinalIgnoreCase) ||
                    Math.Abs(scale - existing.PatternScale) > Tolerance.Default || Math.Abs(angle - existing.PatternAngle) > Tolerance.Default)
                {
                    current = Hatching.Create(existing.Layer, existing.Loops, hatch, scale, angle).CopyStyleFrom<HatchEntity>(existing);
                    changed = true;
                }
            }

            var general = (layer is not null && !ReferenceEquals(current.Layer, layer))
                          || (color is { } c && current.Color != c)
                          || (linetype is { } lt && !ReferenceEquals(current.Linetype, lt.Linetype))
                          || (linetypeScale is { } ls && Math.Abs(current.LinetypeScale - ls) > Tolerance.Default)
                          || (weight is { } w && current.LineWeight != w);
            if (general)
            {
                // Le sole proprietà di stile non cambiano la forma: la copia resta legata all'originale letto dal file.
                if (ReferenceEquals(current, entity))
                {
                    current = entity.Transformed(Matrix2D.Identity);
                }

                current.Layer = layer ?? current.Layer;
                current.Color = color ?? current.Color;
                if (linetype is { } resolved)
                {
                    current.Linetype = resolved.Linetype;
                }

                current.LinetypeScale = linetypeScale ?? current.LinetypeScale;
                current.LineWeight = weight ?? current.LineWeight;
                changed = true;
            }

            if (changed)
            {
                replacements.Add((entity, current));
            }
        }

        if (replacements.Count > 0)
        {
            document.Edit("PROPRIETÀ", e =>
            {
                foreach (var (old, replacement) in replacements)
                {
                    e.Replace(old, replacement);
                }
            });
        }

        return replacements.Count;
    }

    /// <summary>Rende le proprietà correnti per gli oggetti nuovi.</summary>
    public static void ApplyToCurrent(Editor editor, PropertySet set)
    {
        var document = editor.Document;
        if (set.Layer is { } layerName)
        {
            editor.CurrentLayer = ResolveLayer(document, layerName, set.LayerColor);
        }

        if (set.Color is { } colorText && ParseColor(colorText) is { } color)
        {
            editor.CurrentColor = color;
        }

        if (set.Linetype is { } linetypeName && ResolveLinetype(document, linetypeName) is { } linetype)
        {
            editor.CurrentLinetype = linetype.Linetype;
        }

        if (set.LineWeight is { } weight)
        {
            editor.CurrentLineWeight = weight;
        }

        if (set.TextStyle is { } textStyleName && document.FindTextStyle(textStyleName) is { } textStyle)
        {
            document.CurrentTextStyle = textStyle;
        }

        if (set.TextHeight is > 0)
        {
            editor.Settings.TextHeight = set.TextHeight.Value;
        }

        if (set.DimensionStyle is { } dimensionName &&
            document.DimensionStyles.FirstOrDefault(s => s.Name.Equals(dimensionName, StringComparison.OrdinalIgnoreCase)) is { } dimensionStyle)
        {
            document.CurrentDimensionStyle = dimensionStyle;
        }

        if (set.HatchPattern is { } pattern && HatchPatterns.Exists(pattern))
        {
            editor.Settings.HatchPattern = pattern.ToUpperInvariant();
            editor.Settings.HatchScale = set.HatchScale is > 0 ? set.HatchScale.Value : editor.Settings.HatchScale;
            editor.Settings.HatchAngle = set.HatchAngle ?? editor.Settings.HatchAngle;
        }
    }

    internal static EntityColor? ParseColor(string text)
    {
        if (text.Equals(ByLayer, StringComparison.OrdinalIgnoreCase))
        {
            return EntityColor.ByLayer;
        }

        if (text.Equals(ByBlock, StringComparison.OrdinalIgnoreCase))
        {
            return EntityColor.ByBlock;
        }

        return StandardColors.Parse(text) is { } color ? EntityColor.Explicit(color) : null;
    }

    private static string FormatRgb(CadColor color) => string.Create(CultureInfo.InvariantCulture, $"{color.R},{color.G},{color.B}");

    private static string FormatColor(EntityColor color) => color.Source switch
    {
        ColorSource.ByLayer => ByLayer,
        ColorSource.ByBlock => ByBlock,
        _ => FormatRgb(color.Value),
    };

    private static string FormatLinetype(Linetype? linetype) => linetype switch
    {
        null => ByLayer,
        _ when ReferenceEquals(linetype, Document.Linetype.ByBlock) => ByBlock,
        _ => linetype.Name,
    };

    private static Layer ResolveLayer(CadDocument document, string name, string? color)
    {
        if (document.FindLayer(name) is { } found)
        {
            return found;
        }

        var layer = document.GetOrAddLayer(name);
        if (color is not null && StandardColors.Parse(color) is { } rgb)
        {
            layer.Color = rgb;
        }

        return layer;
    }

    /// <summary>Il tipo di linea per nome (null = DaLayer); null se il disegno non lo ha.</summary>
    private static (Linetype? Linetype, bool Found)? ResolveLinetype(CadDocument document, string name)
    {
        if (name.Equals(ByLayer, StringComparison.OrdinalIgnoreCase))
        {
            return (null, true);
        }

        if (name.Equals(ByBlock, StringComparison.OrdinalIgnoreCase))
        {
            return (Document.Linetype.ByBlock, true);
        }

        return document.FindLinetype(name) is { } found ? (found, true) : null;
    }
}

/// <summary>Proprietà salvate con un nome, in un file JSON comune a tutti i disegni.</summary>
public sealed class PropertyLibrary(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private List<PropertySet>? _sets;

    /// <summary>File delle proprietà salvate: %AppData%\CAD2D\proprieta.json.</summary>
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CAD2D", "proprieta.json");

    public string FilePath { get; } = path;

    public IReadOnlyList<PropertySet> Sets => _sets ??= Load();

    public PropertySet? Find(string name) => Sets.FirstOrDefault(s => s.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Salva con il nome dato, sostituendo quello con lo stesso nome. Restituisce true se lo ha sostituito.</summary>
    public bool Save(string name, PropertySet set)
    {
        var copy = set.Clone();
        copy.Name = name.Trim();
        var sets = (List<PropertySet>)Sets;
        var index = sets.FindIndex(s => s.Name.Equals(copy.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            sets[index] = copy;
        }
        else
        {
            sets.Add(copy);
            sets.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        Write();
        return index >= 0;
    }

    public bool Remove(string name)
    {
        var removed = ((List<PropertySet>)Sets).RemoveAll(s => s.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            Write();
        }

        return removed;
    }

    private List<PropertySet> Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<PropertySet>>(File.ReadAllText(FilePath), Options) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Un file rovinato o illeggibile non deve impedire di lavorare: si riparte da vuoto.
            return [];
        }
    }

    private void Write()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_sets, Options));
    }
}

/// <summary>
/// CORRISPONDENZA copia le proprietà di un oggetto su altri; COPIAPROP e INCOLLAPROP le passano attraverso degli
/// appunti propri (anche tra disegni); SALVAPROP e APPLICAPROP le conservano con un nome.
/// </summary>
public static class PropertyCommands
{
    /// <summary>Proprietà copiate con COPIAPROP, comuni a tutti i disegni aperti.</summary>
    public static PropertySet? Clipboard { get; set; }

    /// <summary>Proprietà salvate. L'applicazione usa <see cref="PropertyLibrary.DefaultPath"/>; i test un file temporaneo.</summary>
    public static PropertyLibrary Library { get; set; } = new(PropertyLibrary.DefaultPath);

    /// <summary>Quali proprietà copia CORRISPONDENZA (tutte all'inizio, come negli altri CAD).</summary>
    public static PropertyMask MatchMask { get; set; } = PropertyMask.All;

    /// <summary>Finestra delle impostazioni di CORRISPONDENZA, se l'interfaccia ne ha una; altrimenti si chiede in riga di comando.</summary>
    public static Func<Task>? SettingsDialog { get; set; }

    private static readonly (string Name, PropertyMask Mask)[] MaskNames =
    [
        ("Layer", PropertyMask.Layer),
        ("Colore", PropertyMask.Color),
        ("Tipolinea", PropertyMask.Linetype),
        ("ScalaTL", PropertyMask.LinetypeScale),
        ("Spessore", PropertyMask.LineWeight),
        ("Testo", PropertyMask.Text),
        ("Quota", PropertyMask.Dimension),
        ("Tratteggio", PropertyMask.Hatch),
    ];

    internal static void Register(Editor editor)
    {
        editor.RegisterCommand("CORRISPONDENZA", Match, "MATCHPROP", "MA", "PROPCORR");
        editor.RegisterCommand("COPIAPROP", Copy, "COPYPROP");
        editor.RegisterCommand("INCOLLAPROP", Paste, "PASTEPROP");
        editor.RegisterCommand("SALVAPROP", Save, "SAVEPROP");
        editor.RegisterCommand("APPLICAPROP", ApplySaved, "APPLYPROP");
    }

    /// <summary>Descrive quali proprietà copia una maschera ("Layer, Colore, ...").</summary>
    public static string DescribeMask(PropertyMask mask) =>
        mask == PropertyMask.All ? "tutte" : string.Join(", ", MaskNames.Where(m => (mask & m.Mask) != 0).Select(m => m.Name));

    /// <summary>Legge una lista come "Layer, Colore" o "Tutte"; null se contiene nomi sconosciuti.</summary>
    public static PropertyMask? ParseMask(string text)
    {
        var mask = PropertyMask.None;
        foreach (var part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("Tutte", StringComparison.OrdinalIgnoreCase))
            {
                mask = PropertyMask.All;
                continue;
            }

            var match = MaskNames.FirstOrDefault(m => m.Name.StartsWith(part, StringComparison.OrdinalIgnoreCase));
            if (match.Name is null)
            {
                return null;
            }

            mask |= match.Mask;
        }

        return mask;
    }

    private static async Task Match(Editor ed)
    {
        Entity? source = ed.Selection.Count == 1 ? ed.Selection.Items.First() : null;
        while (source is null)
        {
            var pick = await ed.GetEntityAsync($"Seleziona l'oggetto sorgente o [Impostazioni] <copia: {DescribeMask(MatchMask)}>:", "Impostazioni");
            if (pick.Status == PromptStatus.Keyword)
            {
                await EditSettings(ed);
                continue;
            }

            if (pick.Status != PromptStatus.Ok)
            {
                return;
            }

            source = pick.Entity;
        }

        var set = PropertyTools.Capture(source!);
        ed.Write($"Sorgente: {PropertySheet.TypeName(source!)} ({set.Describe()}).");
        var total = 0;
        while (true)
        {
            ed.Selection.Clear();
            var targets = await ed.GetSelectionAsync("Seleziona gli oggetti di destinazione (Invio per finire):", reportEmpty: false);
            if (targets is null)
            {
                break;
            }

            total += PropertyTools.Apply(ed, targets.Where(t => !ReferenceEquals(t, source)).ToList(), set, MatchMask);
            ed.Selection.Clear();
        }

        ed.Write(total == 1 ? "Proprietà copiate su 1 oggetto." : $"Proprietà copiate su {total} oggetti.");
    }

    private static async Task EditSettings(Editor ed)
    {
        if (SettingsDialog is { } dialog)
        {
            await dialog();
            return;
        }

        ed.Write("Proprietà: " + string.Join(", ", MaskNames.Select(m => m.Name)) + ", Tutte.");
        var answer = await ed.GetStringAsync($"Proprietà da copiare, separate da virgole <{DescribeMask(MatchMask)}>:");
        if (answer.IsOk && ParseMask(answer.Text!) is { } mask && mask != PropertyMask.None)
        {
            MatchMask = mask;
        }
        else if (answer.IsOk)
        {
            ed.Write("Elenco non valido: impostazioni invariate.");
        }
    }

    /// <summary>Un oggetto: quello selezionato se è uno solo, altrimenti si indica. Null se si preme Invio o Esc.</summary>
    private static async Task<(Entity? Entity, bool Cancelled)> PickOne(Editor ed, string prompt, params string[] keywords)
    {
        if (ed.Selection.Count == 1)
        {
            return (ed.Selection.Items.First(), false);
        }

        var pick = await ed.GetEntityAsync(prompt, keywords);
        return pick.Status switch
        {
            PromptStatus.Ok => (pick.Entity, false),
            PromptStatus.Cancel => (null, true),
            _ => (null, false),
        };
    }

    private static async Task Copy(Editor ed)
    {
        var (entity, _) = await PickOne(ed, "Seleziona l'oggetto di cui copiare le proprietà:");
        if (entity is null)
        {
            return;
        }

        Clipboard = PropertyTools.Capture(entity);
        ed.Write($"Proprietà copiate: {Clipboard.Describe()}. INCOLLAPROP le applica.");
    }

    private static async Task Paste(Editor ed)
    {
        if (Clipboard is not { } set)
        {
            ed.Write("Nessuna proprietà copiata: usa prima COPIAPROP.");
            return;
        }

        var targets = await ed.GetSelectionAsync("Seleziona gli oggetti su cui incollare le proprietà:");
        if (targets is null)
        {
            return;
        }

        var count = PropertyTools.Apply(ed, targets, set, PropertyMask.All);
        ed.Selection.Clear();
        ed.Write(count == 1 ? "Proprietà incollate su 1 oggetto." : $"Proprietà incollate su {count} oggetti.");
    }

    private static async Task Save(Editor ed)
    {
        var (entity, cancelled) = await PickOne(ed, "Oggetto da cui prendere le proprietà o <Correnti>:", "Correnti");
        if (cancelled)
        {
            return;
        }

        var set = entity is null ? PropertyTools.CaptureCurrent(ed) : PropertyTools.Capture(entity);
        var name = await ed.GetStringAsync("Nome delle proprietà:");
        if (!name.IsOk || string.IsNullOrWhiteSpace(name.Text))
        {
            return;
        }

        var replaced = Library.Save(name.Text!, set);
        ed.Write($"{(replaced ? "Sostituite" : "Salvate")} le proprietà \"{name.Text!.Trim()}\": {set.Describe()}.");
    }

    private static async Task ApplySaved(Editor ed)
    {
        if (Library.Sets.Count == 0)
        {
            ed.Write("Nessuna proprietà salvata: usa prima SALVAPROP.");
            return;
        }

        ed.Write("Proprietà salvate: " + string.Join(", ", Library.Sets.Select(s => s.Name)) + ".");
        var name = await ed.GetStringAsync("Nome delle proprietà da applicare:");
        if (!name.IsOk || string.IsNullOrWhiteSpace(name.Text))
        {
            return;
        }

        if (Library.Find(name.Text!) is not { } set)
        {
            ed.Write($"Non ci sono proprietà salvate con il nome \"{name.Text!.Trim()}\".");
            return;
        }

        ApplySaved(ed, set);
    }

    /// <summary>Alla selezione, se c'è; altrimenti diventano le proprietà correnti per i nuovi oggetti.</summary>
    public static void ApplySaved(Editor ed, PropertySet set)
    {
        var targets = ed.Selection.Items.ToList();
        if (targets.Count == 0)
        {
            PropertyTools.ApplyToCurrent(ed, set);
            ed.Write($"Proprietà \"{set.Name}\" rese correnti per i nuovi oggetti.");
            return;
        }

        var count = PropertyTools.Apply(ed, targets, set, PropertyMask.All);
        ed.Selection.Clear();
        ed.Write($"Proprietà \"{set.Name}\" applicate a {count} {(count == 1 ? "oggetto" : "oggetti")}.");
    }
}

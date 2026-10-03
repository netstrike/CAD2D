using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Cad.Document;
using Cad.Editing;
using Cad.IO;

namespace Cad.App;

public partial class MainWindow : Window
{
    private const int MaxHistoryLines = 300;

    private static readonly FilePickerFileType DxfFiles = new("Disegni DXF") { Patterns = ["*.dxf"] };

    private readonly List<string> _history = [];
    private Editor? _editor;
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        Canvas.CursorWorldPositionChanged += (_, p) =>
            CoordinatesText.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.0000}, {1:0.0000}", p.X, p.Y);
        Canvas.ViewChanged += (_, _) =>
            ScaleText.Text = string.Format(CultureInfo.CurrentCulture, "{0:N0} entità · zoom {1:0.###}", Canvas.Scene.EntityCount, Canvas.View.Scale);

        // Il fuoco resta sulla riga di comando: si può scrivere un comando in qualunque momento, come nei CAD.
        Canvas.AddHandler(PointerPressedEvent, (_, _) => CommandBox.Focus(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        CommandBox.AddHandler(KeyDownEvent, OnCommandBoxKeyDown, RoutingStrategies.Tunnel);

        InitializeDraftingToggles();
        InitializeQuickInput();
        InitializeCompletion();
        InitializeLayerControls();

        AddHandler(DragDrop.DropEvent, OnDrop);
        BuildSnapMenu();
        BuildRibbon();
        BuildPalettes();
        Canvas.ContextMenuRequested += (_, at) => ShowCanvasMenu(at);
    }

    // ---------- Barra multifunzione, accesso rapido e palette ----------

    private void BuildRibbon()
    {
        RibbonBar.Build(Controls.Ribbon.Default(BuildPropertyCombos));
        RibbonBar.CommandRequested += (_, command) => RunUiCommand(command);

        (string Command, string Tip)[] quick =
        [
            ("NUOVO", "Nuovo (Ctrl+N)"),
            ("APRI", "Apri (Ctrl+O)"),
            ("SALVA", "Salva (Ctrl+S)"),
            ("SALVACOME", "Salva con nome (Ctrl+Maiusc+S)"),
            ("ANNULLA", "Annulla (Ctrl+Z)"),
            ("RIPETI", "Ripeti (Ctrl+Y)"),
        ];
        foreach (var (command, tip) in quick)
        {
            var button = new Button
            {
                Content = Controls.Icons.Create(command, 16),
                Padding = new Thickness(5, 3),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Focusable = false,
            };
            ToolTip.SetTip(button, tip);
            button.Click += (_, _) => RunUiCommand(command);
            QuickAccess.Children.Add(button);
        }
    }

    private void BuildPalettes()
    {
        PaletteTabs.SelectedIndex = 0;
        PaletteTabs.SelectionChanged += (_, _) =>
        {
            PropertiesPanel.IsVisible = PaletteTabs.SelectedIndex == 0;
            LayerPalette.IsVisible = PaletteTabs.SelectedIndex == 1;
        };
        PropertiesPanel.Committed += (_, _) => CommandBox.Focus();

        // Tasto destro su ESNAP: gli stessi tipi di snap del menu Strumenti.
        var snapItems = new List<MenuItem>();
        foreach (var (mode, label) in SnapItems)
        {
            var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.CheckBox };
            item.Click += (_, _) => ToggleSnapMode(mode);
            snapItems.Add(item);
        }

        var menu = new ContextMenu { ItemsSource = snapItems };
        menu.Opening += (_, _) =>
        {
            for (var i = 0; i < SnapItems.Length; i++)
            {
                snapItems[i].IsChecked = _snapModes.HasFlag(SnapItems[i].Mode);
            }
        };
        SnapToggle.ContextMenu = menu;
    }

    /// <summary>Comando chiesto da barra, menu o accesso rapido: quelli di sola interfaccia qui, gli altri all'editor.</summary>
    private void RunUiCommand(string command)
    {
        switch (command)
        {
            case "ZOOMESTENSIONI":
                Canvas.ZoomExtents();
                break;
            case "PROPRIETA":
                ShowPalette(0);
                break;
            case "PALETTELAYER":
                ShowPalette(1);
                break;
            case "GESTORELAYER":
                if (_editor is { } editor)
                {
                    _ = LayerManagerDialog.ShowAsync(this, editor, () =>
                    {
                        editor.Locator.Invalidate();
                        OnLayerStateChanged();
                        RefreshLayers();
                        Canvas.RefreshScene();
                    }, AppendHistory);
                }

                break;
            case "SELEZIONATUTTO":
                _editor?.SelectAll();
                break;
            case "DLGTRATTEGGIO" or "DLGSTILEQUOTA" or "OPZIONI":
                ShowDialogCommand(command);
                return;
            case "TEMA":
                SetTheme(!AppTheme.IsLight);
                break;
            default:
                _editor?.RunCommand(command);
                break;
        }

        CommandBox.Focus();
    }

    /// <summary>
    /// Menu del tasto destro tenuto premuto, secondo il momento: durante un comando Invio, Annulla e le sue opzioni;
    /// con oggetti selezionati le modifiche più usate; altrimenti ripetizione e vista.
    /// </summary>
    private void ShowCanvasMenu(Point at)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        var items = new List<Control>();
        void Add(string header, Action action, string? icon = null)
        {
            var item = new MenuItem { Header = header, Icon = icon is null ? null : Controls.Icons.Create(icon, 16) };
            item.Click += (_, _) =>
            {
                action();
                CommandBox.Focus();
            };
            items.Add(item);
        }

        if (editor.IsCommandActive)
        {
            Add("Invio", () => editor.SubmitText(""));
            Add("Annulla", () => editor.Cancel());
            if (editor.Keywords.Count > 0)
            {
                items.Add(new Separator());
                foreach (var keyword in editor.Keywords)
                {
                    Add(keyword, () => editor.SubmitText(keyword));
                }
            }
        }
        else
        {
            if (editor.LastCommand is { } last)
            {
                Add($"Ripeti {last}", () => editor.RunCommand(last), last);
                items.Add(new Separator());
            }

            if (editor.Selection.Count > 0)
            {
                foreach (var (command, label) in new[] { ("SPOSTA", "Sposta"), ("COPIA", "Copia"), ("RUOTA", "Ruota"), ("SCALA", "Scala"), ("SPECCHIA", "Specchia"), ("CANCELLA", "Cancella") })
                {
                    Add(label, () => editor.RunCommand(command), command);
                }

                items.Add(new Separator());
                Add("Proprietà", () => ShowPalette(0), "PROPRIETA");
                Add("Deseleziona tutto", () => editor.Selection.Clear());
            }
            else
            {
                Add("Annulla", () => editor.RunCommand("ANNULLA"), "ANNULLA");
                Add("Ripeti", () => editor.RunCommand("RIPETI"), "RIPETI");
                items.Add(new Separator());
                Add("Zoom estensioni", Canvas.ZoomExtents, "ZOOMESTENSIONI");
                Add("Zoom finestra", () => editor.RunCommand("ZOOM"), "ZOOM");
                Add("Proprietà", () => ShowPalette(0), "PROPRIETA");
            }
        }

        var menu = new ContextMenu { ItemsSource = items, Placement = PlacementMode.AnchorAndGravity, PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft, PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight, PlacementRect = new Rect(at, new Size(1, 1)) };
        menu.Open(Canvas);
    }

    /// <summary>Finestre di dialogo: tratteggio, stile di quota e opzioni.</summary>
    private async void ShowDialogCommand(string command)
    {
        if (_editor is not { } editor)
        {
            return;
        }

        switch (command)
        {
            case "DLGTRATTEGGIO":
            {
                var dialog = new HatchDialog(editor.Settings);
                await dialog.ShowDialog(this);
                if (dialog.Accepted)
                {
                    editor.RunCommand("TRATTEGGIO");
                    if (dialog.SelectObjects)
                    {
                        editor.SubmitText("Seleziona");
                    }
                }

                break;
            }

            case "DLGSTILEQUOTA":
            {
                var dialog = new DimensionStyleDialog(editor.Document);
                await dialog.ShowDialog(this);
                if (dialog.Accepted)
                {
                    // Le quote già disegnate si aggiornano con lo stile.
                    editor.Document.MarkModified();
                    AppendHistory($"Stile di quota {editor.Document.CurrentDimensionStyle.Name} aggiornato.");
                }

                break;
            }

            case "OPZIONI":
            {
                var dialog = new OptionsDialog(editor, () => _snapModes, SetSnapModes, AppTheme.IsLight, SetTheme);
                await dialog.ShowDialog(this);
                if (dialog.Accepted)
                {
                    _polarIncrement = editor.PolarIncrementDegrees;
                    Canvas.InvalidateOverlay();
                }

                break;
            }
        }

        CommandBox.Focus();
    }

    private void SetSnapModes(SnapModes modes)
    {
        _snapModes = modes;
        if (SnapMenu.ItemsSource is IEnumerable<MenuItem> items)
        {
            foreach (var (item, (mode, _)) in items.Zip(SnapItems))
            {
                item.IsChecked = modes.HasFlag(mode);
            }
        }

        if (_editor is not null)
        {
            _editor.SnapModes = modes;
        }
    }

    /// <summary>Tema chiaro o scuro: interfaccia e sfondo del disegno insieme.</summary>
    private void SetTheme(bool light)
    {
        AppTheme.Apply(light);
        Canvas.LightBackground = light;
        Canvas.RefreshScene();
        foreach (var palette in new Control[] { RibbonBar, PropertiesPanel })
        {
            palette.InvalidateVisual();
        }

        PropertiesPanel.Refresh();
    }

    private void ShowPalette(int tab)
    {
        PalettePanel.IsVisible = true;
        PaletteSplitter.IsVisible = true;
        WorkArea.ColumnDefinitions[2].Width = new GridLength(_paletteWidth);
        PaletteTabs.SelectedIndex = tab;
    }

    private double _paletteWidth = 300;

    private void OnClosePalettes(object? sender, RoutedEventArgs e)
    {
        _paletteWidth = Math.Max(200, PalettePanel.Bounds.Width);
        PalettePanel.IsVisible = false;
        PaletteSplitter.IsVisible = false;
        WorkArea.ColumnDefinitions[2].Width = new GridLength(0);
        CommandBox.Focus();
    }

    private void ToggleSnapMode(SnapModes mode)
    {
        // Lo stato si ricava dalla maschera: non dipende da quando il menu aggiorna la spunta.
        _snapModes ^= mode;
        if (SnapMenu.ItemsSource is IEnumerable<MenuItem> items)
        {
            foreach (var (item, (m, _)) in items.Zip(SnapItems))
            {
                item.IsChecked = _snapModes.HasFlag(m);
            }
        }

        if (_editor is not null)
        {
            _editor.SnapModes = _snapModes;
        }

        CommandBox.Focus();
    }

    private static readonly (SnapModes Mode, string Label)[] SnapItems =
    [
        (SnapModes.Endpoint, "_Estremo"),
        (SnapModes.Midpoint, "_Medio"),
        (SnapModes.Center, "_Centro"),
        (SnapModes.Quadrant, "_Quadrante"),
        (SnapModes.Intersection, "_Intersezione"),
        (SnapModes.Perpendicular, "_Perpendicolare"),
        (SnapModes.Tangent, "_Tangente"),
        (SnapModes.Node, "_Nodo"),
        (SnapModes.Nearest, "_Vicino"),
    ];

    private SnapModes _snapModes = SnapModes.Default;

    /// <summary>Una voce spuntabile per ogni tipo di snap; la scelta vale anche per i disegni aperti dopo.</summary>
    private void BuildSnapMenu()
    {
        var items = new List<MenuItem>();
        foreach (var (mode, label) in SnapItems)
        {
            var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = _snapModes.HasFlag(mode) };
            item.Click += (_, _) => ToggleSnapMode(mode);
            items.Add(item);
        }

        SnapMenu.ItemsSource = items;
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        CommandBox.Focus();

        // Primo argomento della riga di comando, altrimenti il disegno di esempio.
        var path = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault()
                   ?? Path.Combine(AppContext.BaseDirectory, "samples", "demo.dxf");
        if (File.Exists(path))
        {
            await OpenAsync(path);
        }
        else
        {
            SetDocument(new CadDocument());
            StatusText.Text = "File > Apri (Ctrl+O) oppure trascina qui un file DXF";
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || _editor?.Document.IsModified != true)
        {
            return;
        }

        e.Cancel = true;
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // ---------- Documento ----------

    private void SetDocument(CadDocument document)
    {
        if (_editor is not null)
        {
            _editor.Message -= AppendHistory;
            _editor.StateChanged -= OnEditorStateChanged;
            _editor.Document.Changed -= OnDocumentChanged;
            _editor.Cancel();
        }

        var editor = new Editor(document)
        {
            SnapModes = _snapModes,
        };
        ApplyDraftingToggles(editor);
        RegisterUiCommands(editor);
        editor.Message += AppendHistory;
        editor.StateChanged += OnEditorStateChanged;
        document.Changed += OnDocumentChanged;
        _editor = editor;

        Canvas.Editor = editor;
        PropertiesPanel.Editor = editor;
        editor.Selection.Changed += (_, _) => RefreshProperties();
        RefreshLayers(force: true);
        UpdateTitle();
        _shownKeywords = [];
        UpdateKeywordButtons();
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        UpdateTitle();
        // Dopo il giro corrente: il cambio può arrivare da una casella della lista stessa.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshLayers());
    }

    private void UpdateTitle()
    {
        var document = _editor?.Document;
        var name = document?.FilePath is { } path ? Path.GetFileName(path) : "Senza nome";
        Title = $"CAD2D - {name}{(document?.IsModified == true ? " *" : "")}";
    }

    private void OnEditorStateChanged(object? sender, EventArgs e)
    {
        if (_editor is not null)
        {
            UpdateKeywordButtons();
            UpdateQuickInput();
            if (_editor.PromptId != _shownPromptId)
            {
                // Testo proposto (es. MODIFICATESTO): si parte da quello, pronto da correggere.
                _shownPromptId = _editor.PromptId;
                if (_editor.SuggestedInput is { } suggested)
                {
                    CommandBox.Text = suggested;
                    CommandBox.CaretIndex = suggested.Length;
                }
            }
        }
    }

    private int _shownPromptId;

    private void AppendHistory(string line)
    {
        _history.Add(line);
        if (_history.Count > MaxHistoryLines)
        {
            _history.RemoveRange(0, _history.Count - MaxHistoryLines);
        }

        HistoryText.Text = string.Join(Environment.NewLine, _history);
        Dispatcher.UIThread.Post(HistoryScroll.ScrollToEnd, DispatcherPriority.Background);
    }

    // ---------- Comandi dell'interfaccia (file e vista) ----------

    private void RegisterUiCommands(Editor editor)
    {
        editor.RegisterCommand("SALVA", _ => SaveAsync(saveAs: false), "SAVE", "QSAVE");
        editor.RegisterCommand("SALVACOME", _ => SaveAsync(saveAs: true), "SAVEAS");
        editor.RegisterCommand("APRI", _ => OpenWithPickerAsync(), "OPEN");
        editor.RegisterCommand("NUOVO", _ => NewAsync(), "NEW");
        editor.RegisterCommand("ZOOM", ZoomAsync, "Z");
    }

    private async Task ZoomAsync(Editor ed)
    {
        var first = await ed.GetPointAsync("Primo angolo della finestra o <Estensioni>:", null, null, "Estensioni");
        if (first.Status is PromptStatus.None or PromptStatus.Keyword)
        {
            Canvas.ZoomExtents();
            return;
        }

        if (!first.IsOk)
        {
            return;
        }

        var second = await ed.GetPointAsync("Angolo opposto:", first.Point);
        if (second.IsOk)
        {
            Canvas.ZoomWindow(first.Point, second.Point);
        }
    }

    /// <summary>Salva il disegno. Restituisce false se l'utente ha rinunciato o il salvataggio non è riuscito.</summary>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_editor is null)
        {
            return false;
        }

        var document = _editor.Document;
        var path = document.FilePath;
        if (saveAs || path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salva disegno",
                SuggestedFileName = path is null ? "Disegno1.dxf" : Path.GetFileName(path),
                DefaultExtension = "dxf",
                FileTypeChoices = [DxfFiles],
                ShowOverwritePrompt = true,
            });
            path = file?.TryGetLocalPath();
            if (path is null)
            {
                return false;
            }
        }

        try
        {
            DxfExporter.Save(document, path);
            AppendHistory($"Salvato in {path}");
            StatusText.Text = $"Salvato: {Path.GetFileName(path)}";
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            AppendHistory($"Impossibile salvare {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Se ci sono modifiche non salvate chiede cosa fare. Restituisce true se si può procedere.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (_editor?.Document is not { IsModified: true } document)
        {
            return true;
        }

        var name = document.FilePath is { } path ? Path.GetFileName(path) : "Senza nome";
        return await ConfirmDialog.AskAsync(this, $"Salvare le modifiche a {name}?") switch
        {
            SaveChoice.Save => await SaveAsync(saveAs: false),
            SaveChoice.Discard => true,
            _ => false,
        };
    }

    private async Task NewAsync()
    {
        if (await ConfirmDiscardAsync())
        {
            SetDocument(new CadDocument());
            StatusText.Text = "Nuovo disegno";
        }
    }

    private async Task OpenAsync(string path)
    {
        StatusText.Text = $"Apertura di {Path.GetFileName(path)}...";
        try
        {
            var result = await Task.Run(() => DxfImporter.Load(path));
            SetDocument(result.Document);
            StatusText.Text = DescribeImport(path, result);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Impossibile aprire {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private static string DescribeImport(string path, ImportResult result)
    {
        var parts = new List<string> { Path.GetFileName(path) };
        var unsupported = result.Document.UnsupportedEntities;
        if (unsupported.Count > 0)
        {
            parts.Add("non ancora gestite (conservate al salvataggio): " + string.Join(", ", unsupported.OrderByDescending(u => u.Value).Select(u => $"{u.Key} {u.Value}")));
        }

        if (result.Errors.Count > 0)
        {
            parts.Add($"{result.Errors.Count} errori di lettura, parti del file saltate");
        }

        return string.Join(" · ", parts);
    }

    private async Task OpenWithPickerAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Apri disegno",
            AllowMultiple = false,
            FileTypeFilter = [DxfFiles, FilePickerFileTypes.All],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            await OpenAsync(path);
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618 // API di drag and drop ancora valida in Avalonia 11.
        var file = e.Data.GetFiles()?.FirstOrDefault();
#pragma warning restore CS0618
        if (file?.TryGetLocalPath() is { } path && await ConfirmDiscardAsync())
        {
            await OpenAsync(path);
        }
    }

    // ---------- Tastiera ----------

    /// <summary>Scorciatoie globali, intercettate prima della casella di testo (che altrimenti userebbe Ctrl+Z per sé).</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_editor is null || e.Source is Visual visual && visual.FindAncestorOfType<ConfirmDialog>(includeSelf: true) is not null)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        string? command = (e.Key, ctrl, shift) switch
        {
            (Key.Z, true, false) => "ANNULLA",
            (Key.Y, true, false) => "RIPETI",
            (Key.S, true, false) => "SALVA",
            (Key.S, true, true) => "SALVACOME",
            (Key.O, true, false) => "APRI",
            (Key.N, true, false) => "NUOVO",
            _ => null,
        };

        command ??= ClipboardShortcut(e, ctrl, shift);
        if (command is not null)
        {
            RunUiCommand(command);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers == KeyModifiers.None && HandleFunctionKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape when CompletionPopup.IsOpen:
                CompletionPopup.IsOpen = false;
                e.Handled = true;
                break;
            case Key.Escape when e.Source is Visual source && IsInPalette(source):
                // Esc in una casella della palette annulla solo la modifica in corso.
                break;
            case Key.Escape:
                CommandBox.Text = "";
                _editor.Cancel();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Invio e Spazio confermano; Canc a riga vuota cancella la selezione.</summary>
    private void OnCommandBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_editor is null || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (HandleCommandBoxNavigation(e))
        {
            e.Handled = true;
            return;
        }

        var text = CommandBox.Text ?? "";
        if (e.Key == Key.Enter || (e.Key == Key.Space && !_editor.AcceptsSpaces))
        {
            text = AcceptCompletion(text);
            RememberInput(text);
            CommandBox.Text = "";
            if (!_editor.IsCommandActive && UiOnlyCommands.Contains(text.Trim().ToUpperInvariant()))
            {
                RunUiCommand(text.Trim().ToUpperInvariant());
            }
            else
            {
                _editor.SubmitText(text);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Delete && text.Length == 0 && !_editor.IsCommandActive && _editor.Selection.Count > 0)
        {
            _editor.RunCommand("CANCELLA");
            e.Handled = true;
        }
    }

    private bool IsInPalette(Visual visual) => visual == PalettePanel || visual.GetVisualAncestors().Contains(PalettePanel);

    private void OnMenuCommand(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string command })
        {
            RunUiCommand(command);
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

}

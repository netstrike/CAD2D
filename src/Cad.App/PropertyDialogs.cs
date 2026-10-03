using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Cad.Editing;

namespace Cad.App;

/// <summary>Quali proprietà copia CORRISPONDENZA, come le impostazioni di "Proprietà corrispondenti" di DraftSight.</summary>
public sealed class MatchSettingsDialog : FormDialog
{
    public MatchSettingsDialog() : base("Impostazioni di corrispondenza", 380)
    {
        var mask = PropertyCommands.MatchMask;
        var chosen = PropertyMask.None;
        void Option(string label, PropertyMask flag) => Check(label, (mask & flag) != 0, on => chosen |= on ? flag : PropertyMask.None);

        Section("Proprietà generali");
        Option("Layer", PropertyMask.Layer);
        Option("Colore", PropertyMask.Color);
        Option("Tipo di linea", PropertyMask.Linetype);
        Option("Scala del tipo di linea", PropertyMask.LinetypeScale);
        Option("Spessore di linea", PropertyMask.LineWeight);
        Section("Proprietà speciali");
        Option("Testo: stile e altezza", PropertyMask.Text);
        Option("Quote e direttrici: stile di quota", PropertyMask.Dimension);
        Option("Tratteggio: motivo, scala e angolo", PropertyMask.Hatch);
        OnAccept(() => true, () => PropertyCommands.MatchMask = chosen);
    }
}

/// <summary>
/// Proprietà salvate con un nome, comuni a tutti i disegni: si salvano dall'oggetto selezionato o da quelle correnti,
/// si applicano alla selezione o diventano le proprietà dei nuovi oggetti.
/// </summary>
public sealed class PropertySetsDialog : Window
{
    private readonly Editor _editor;
    private readonly Action<string> _log;
    private readonly ListBox _list = new() { Height = 220, SelectionMode = SelectionMode.Single };
    private readonly TextBlock _details = new() { FontSize = 11.5, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, MinHeight = 34 };
    private readonly TextBox _name = new() { Watermark = "Nome", MinHeight = 26 };

    public PropertySetsDialog(Editor editor, Action<string> log)
    {
        _editor = editor;
        _log = log;
        Title = "Proprietà salvate";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var selected = editor.Selection.Items.ToList();
        var sourceText = selected.Count == 1
            ? $"Salva le proprietà dell'oggetto selezionato ({PropertySheet.TypeName(selected[0])})"
            : "Salva le proprietà correnti (layer, colore, stili dei nuovi oggetti)";

        var save = Button("Salva", SaveCurrent);
        var saveRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        saveRow.Children.Add(_name);
        Grid.SetColumn(save, 1);
        save.Margin = new Thickness(6, 0, 0, 0);
        saveRow.Children.Add(save);

        var apply = Button(selected.Count > 0 ? $"Applica alla selezione ({selected.Count})" : "Applica alla selezione", () => Use(toSelection: true));
        apply.IsEnabled = false;
        var current = Button("Rendi correnti", () => Use(toSelection: false));
        var delete = Button("Elimina", Delete);
        var close = Button("Chiudi", Close);
        close.IsCancel = true;

        _list.SelectionChanged += (_, _) =>
        {
            var set = Selected;
            _details.Text = set?.Describe() ?? "";
            apply.IsEnabled = set is not null && selected.Count > 0;
            current.IsEnabled = delete.IsEnabled = set is not null;
            if (set is not null)
            {
                _name.Text = set.Name;
            }
        };
        _list.DoubleTapped += (_, _) => Use(toSelection: selected.Count > 0);
        current.IsEnabled = delete.IsEnabled = false;

        Content = new StackPanel
        {
            Margin = new Thickness(18, 14, 18, 16),
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Proprietà salvate", FontWeight = FontWeight.SemiBold },
                _list,
                _details,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, current, delete } },
                new TextBlock { Text = sourceText, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) },
                saveRow,
                new TextBlock
                {
                    Text = $"Valgono per tutti i disegni e restano salvate in {PropertyCommands.Library.FilePath}.",
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { close } },
            },
        };

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += (_, _) => _name.Focus();
        Refresh();
    }

    private PropertySet? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < PropertyCommands.Library.Sets.Count
        ? PropertyCommands.Library.Sets[_list.SelectedIndex]
        : null;

    private static Button Button(string text, Action onClick)
    {
        var button = new Button { Content = text, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void Refresh(string? select = null)
    {
        var sets = PropertyCommands.Library.Sets;
        _list.ItemsSource = sets.Select(s => s.Name).ToList();
        _list.SelectedIndex = select is null ? (sets.Count > 0 ? 0 : -1) : sets.ToList().FindIndex(s => s.Name.Equals(select, StringComparison.OrdinalIgnoreCase));
        if (sets.Count == 0)
        {
            _details.Text = "Nessuna proprietà salvata: scrivi un nome qui sotto e premi Salva.";
        }
    }

    private void SaveCurrent()
    {
        var name = (_name.Text ?? "").Trim();
        if (name.Length == 0)
        {
            _name.BorderBrush = Brushes.IndianRed;
            return;
        }

        _name.ClearValue(BorderBrushProperty);
        var selected = _editor.Selection.Items.ToList();
        var set = selected.Count == 1 ? PropertyTools.Capture(selected[0]) : PropertyTools.CaptureCurrent(_editor);
        var replaced = PropertyCommands.Library.Save(name, set);
        _log($"{(replaced ? "Sostituite" : "Salvate")} le proprietà \"{name}\": {set.Describe()}.");
        Refresh(name);
    }

    private void Use(bool toSelection)
    {
        if (Selected is not { } set)
        {
            return;
        }

        if (!toSelection)
        {
            _editor.Selection.Clear();
        }

        PropertyCommands.ApplySaved(_editor, set);
        Close();
    }

    private void Delete()
    {
        if (Selected is { } set && PropertyCommands.Library.Remove(set.Name))
        {
            _log($"Proprietà \"{set.Name}\" eliminate.");
            Refresh();
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Cad.Document;
using Cad.Editing;

namespace Cad.App;

/// <summary>
/// Gestore dei layer come in DraftSight: una tabella con stato, colore, tipo e spessore di linea di ogni layer,
/// modificabili sul posto. Le modifiche valgono subito; il layer corrente si sceglie con il pulsante o il doppio clic.
/// </summary>
public sealed class LayerManagerDialog : Window
{
    private static readonly string[] Columns = ["", "Nome", "Acceso", "Congelato", "Bloccato", "Colore", "Tipo di linea", "Spessore", "Oggetti"];
    private const string ColumnWidths = "26,*,64,76,70,120,140,130,64";

    private IBrush? HeaderBrush => this.TryFindResource("Cad.Category", ActualThemeVariant, out var b) ? b as IBrush : null;
    private IBrush? SelectedBrush => this.TryFindResource("Cad.Selected", ActualThemeVariant, out var b) ? b as IBrush : null;
    private static readonly IBrush RowBrush = Brushes.Transparent;

    private readonly Editor _editor;
    private readonly Action _changed;
    private readonly Action<string> _message;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
    private Layer? _selected;

    private LayerManagerDialog(Editor editor, Action changed, Action<string> message)
    {
        _editor = editor;
        _changed = changed;
        _message = text =>
        {
            message(text);
            _status.Text = text;
        };
        Title = "Gestore layer";
        Width = 960;
        Height = 520;
        MinWidth = 700;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(10, 10, 10, 6) };
        toolbar.Children.Add(ToolButton("Nuovo", "Nuovo layer con le proprietà di quello evidenziato", NewLayer));
        toolbar.Children.Add(ToolButton("Elimina", "Elimina il layer evidenziato (se vuoto)", DeleteLayer));
        toolbar.Children.Add(ToolButton("Corrente", "Rende corrente il layer evidenziato", MakeCurrent));
        toolbar.Children.Add(_status);

        var header = BuildGrid();
        header.Background = HeaderBrush;
        for (var i = 0; i < Columns.Length; i++)
        {
            var text = new TextBlock { Text = Columns[i], FontWeight = FontWeight.SemiBold, FontSize = 12, Margin = new Thickness(6, 5), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, i);
            header.Children.Add(text);
        }

        var close = new Button { Content = "Chiudi", IsDefault = true, IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
        close.Click += (_, _) => Close();

        var layout = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        DockPanel.SetDock(close, Dock.Bottom);
        layout.Children.Add(close);
        var headerBorder = new Border { Child = header, Margin = new Thickness(10, 0, 10, 0) };
        DockPanel.SetDock(headerBorder, Dock.Top);
        layout.Children.Add(headerBorder);
        layout.Children.Add(new Border { BorderBrush = HeaderBrush, BorderThickness = new Thickness(1), Margin = new Thickness(10, 0, 10, 0), Child = new ScrollViewer { Content = _rows } });
        Content = layout;

        // Esc chiude anche quando il fuoco è in una casella di testo.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _selected = editor.CurrentLayer;
        Refresh();
    }

    /// <summary>Apre il gestore; <paramref name="changed"/> viene chiamato dopo ogni modifica.</summary>
    public static Task ShowAsync(Window owner, Editor editor, Action changed, Action<string> message) =>
        new LayerManagerDialog(editor, changed, message).ShowDialog(owner);

    private static Grid BuildGrid() => new() { ColumnDefinitions = new ColumnDefinitions(ColumnWidths), MinHeight = 30 };

    private static Button ToolButton(string text, string tip, Action action)
    {
        var button = new Button { Content = text, MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private void Refresh()
    {
        _rows.Children.Clear();
        var counts = _editor.Document.ModelSpace.GroupBy(e => e.Layer).ToDictionary(g => g.Key, g => g.Count());
        foreach (var layer in _editor.Document.Layers.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            _rows.Children.Add(BuildRow(layer, counts.GetValueOrDefault(layer)));
        }

        _status.Text = $"Layer corrente: {_editor.CurrentLayer.Name} · {_editor.Document.Layers.Count()} layer";
    }

    private Control BuildRow(Layer layer, int count)
    {
        var grid = BuildGrid();
        var row = new Border { Background = layer == _selected ? SelectedBrush : RowBrush, Child = grid };
        row.PointerPressed += (_, _) =>
        {
            _selected = layer;
            foreach (var other in _rows.Children.OfType<Border>())
            {
                other.Background = other == row ? SelectedBrush : RowBrush;
            }
        };
        row.DoubleTapped += (_, _) =>
        {
            _selected = layer;
            MakeCurrent();
        };

        void Put(Control control, int column)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(4, 1);
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }

        Put(new TextBlock { Text = layer == _editor.CurrentLayer ? "✓" : "", Foreground = Brushes.LightGreen, HorizontalAlignment = HorizontalAlignment.Center }, 0);

        // Nome: si rinomina scrivendo e premendo Invio (o lasciando la casella).
        var name = new TextBox { Text = layer.Name, IsReadOnly = layer.Name == Layer.DefaultName, FontWeight = layer == _editor.CurrentLayer ? FontWeight.Bold : FontWeight.Normal, MinHeight = 24, Padding = new Thickness(4, 2) };
        void Rename()
        {
            var text = name.Text?.Trim() ?? string.Empty;
            if (text == layer.Name || text.Length == 0)
            {
                name.Text = layer.Name;
                return;
            }

            if (_editor.Document.RenameLayer(layer, text))
            {
                Changed();
            }
            else
            {
                _message($"Nome di layer non valido o già usato: {text}");
                name.Text = layer.Name;
            }
        }

        name.LostFocus += (_, _) => Rename();
        name.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                Rename();
                e.Handled = true;
            }
        };
        Put(name, 1);

        Put(Check(layer.IsOn, v => layer.IsOn = v), 2);
        Put(Check(layer.IsFrozen, v =>
        {
            // Il layer corrente non si congela, come negli altri CAD.
            if (v && layer == _editor.CurrentLayer)
            {
                _message("Il layer corrente non si può congelare.");
                Refresh();
                return;
            }

            layer.IsFrozen = v;
        }), 3);
        Put(Check(layer.IsLocked, v => layer.IsLocked = v), 4);
        Put(ColorButton(layer), 5);

        var linetypes = _editor.Document.Linetypes.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var linetype = new ComboBox { ItemsSource = linetypes.Select(l => l.Name).ToList(), SelectedItem = layer.Linetype.Name, MinHeight = 24, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        linetype.SelectionChanged += (_, _) =>
        {
            if (linetypes.FirstOrDefault(l => l.Name == linetype.SelectedItem as string) is { } chosen && chosen != layer.Linetype)
            {
                layer.Linetype = chosen;
                Changed();
            }
        };
        Put(linetype, 6);

        var weights = new[] { LineWeight.Default }.Concat(LineWeight.Standard).ToList();
        var weight = new ComboBox { ItemsSource = weights.Select(LineWeight.Format).ToList(), SelectedItem = LineWeight.Format(layer.LineWeight), MinHeight = 24, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        weight.SelectionChanged += (_, _) =>
        {
            if (weight.SelectedItem is string text && LineWeight.TryParse(text, out var value) && value != layer.LineWeight)
            {
                layer.LineWeight = value;
                Changed();
            }
        };
        Put(weight, 7);

        Put(new TextBlock { Text = count.ToString(), HorizontalAlignment = HorizontalAlignment.Right, Foreground = Brushes.Gray }, 8);
        return row;
    }

    private CheckBox Check(bool value, Action<bool> apply)
    {
        var box = new CheckBox { IsChecked = value, HorizontalAlignment = HorizontalAlignment.Center };
        box.IsCheckedChanged += (_, _) =>
        {
            apply(box.IsChecked == true);
            Changed();
        };
        return box;
    }

    private Button ColorButton(Layer layer)
    {
        var swatch = new Border
        {
            Width = 14,
            Height = 14,
            Background = new SolidColorBrush(Color.FromRgb(layer.Color.R, layer.Color.G, layer.Color.B)),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
        };
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { swatch, new TextBlock { Text = StandardColors.Describe(EntityColor.Explicit(layer.Color)), FontSize = 12 } } },
            Padding = new Thickness(6, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += (_, _) =>
        {
            var flyout = new MenuFlyout();
            foreach (var (label, color) in StandardColors.All)
            {
                var entry = new MenuItem { Header = label };
                entry.Click += (_, _) =>
                {
                    layer.Color = color;
                    Changed();
                };
                flyout.Items.Add(entry);
            }

            var other = new MenuItem { Header = "Altro (r,g,b)..." };
            other.Click += async (_, _) =>
            {
                var c = layer.Color;
                var text = await InputDialog.AskAsync(this, "Colore del layer", "Colore come r,g,b (0-255):", $"{c.R},{c.G},{c.B}");
                if (text is not null && StandardColors.Parse(text) is { } parsed)
                {
                    layer.Color = parsed;
                    Changed();
                }
            };
            flyout.Items.Add(other);
            flyout.ShowAt(button);
        };
        return button;
    }

    private async void NewLayer()
    {
        var document = _editor.Document;
        var index = 1;
        while (document.FindLayer($"Layer{index}") is not null)
        {
            index++;
        }

        var name = await InputDialog.AskAsync(this, "Nuovo layer", "Nome del nuovo layer:", $"Layer{index}");
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (!CadDocument.IsValidName(name) || document.FindLayer(name) is not null)
        {
            _message($"Nome di layer non valido o già usato: {name}");
            return;
        }

        var layer = document.GetOrAddLayer(name);
        if (_selected is { } template)
        {
            layer.Color = template.Color;
            layer.Linetype = template.Linetype;
            layer.LineWeight = template.LineWeight;
        }

        _selected = layer;
        Changed();
    }

    private void DeleteLayer()
    {
        if (_selected is not { } layer)
        {
            return;
        }

        if (layer == _editor.CurrentLayer)
        {
            _message($"{layer.Name} è il layer corrente: rendine corrente un altro prima di eliminarlo.");
        }
        else if (_editor.Document.RemoveLayer(layer))
        {
            _selected = _editor.CurrentLayer;
            Changed();
        }
        else
        {
            _message($"{layer.Name}: non si può eliminare (layer 0 o contiene oggetti).");
        }
    }

    private void MakeCurrent()
    {
        if (_selected is not { } layer)
        {
            return;
        }

        if (layer.IsFrozen)
        {
            _message("Un layer congelato non può essere corrente.");
            return;
        }

        _editor.CurrentLayer = layer;
        Changed();
    }

    private void Changed()
    {
        _changed();
        Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }
}

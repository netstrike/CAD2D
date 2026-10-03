using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Cad.Document;

namespace Cad.App;

/// <summary>Gestore layer e barra delle proprietà.</summary>
public partial class MainWindow
{
    /// <summary>Firma dello stato dei layer: la lista si ricostruisce solo quando cambia davvero.</summary>
    private string _layerSignature = "";

    private bool _updatingProperties;

    // Caselle layer, colore e tipo di linea: stanno nella barra multifunzione (Home > Layer e proprietà).
    private readonly ComboBox LayerCombo = PropertyCombo();
    private readonly ComboBox ColorCombo = PropertyCombo();
    private readonly ComboBox LinetypeCombo = PropertyCombo();

    private static ComboBox PropertyCombo() => new()
    {
        Width = 190,
        Height = 24,
        MinHeight = 24,
        FontSize = 11.5,
        Padding = new Thickness(6, 0),
        Margin = new Thickness(0, 1),
        Focusable = false,
    };

    /// <summary>Le tre caselle in colonna, con una piccola icona davanti, per il gruppo della barra multifunzione.</summary>
    private Control BuildPropertyCombos()
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(6, 0, 4, 0) };
        (string Icon, ComboBox Combo)[] rows = [("LAYER", LayerCombo), ("COLORE", ColorCombo), ("TIPOLINEA", LinetypeCombo)];
        for (var i = 0; i < rows.Length; i++)
        {
            var icon = Controls.Icons.Create(rows[i].Icon, 16);
            icon.Margin = new Thickness(0, 0, 6, 0);
            icon.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Grid.SetRow(icon, i);
            grid.Children.Add(icon);
            (rows[i].Combo.Parent as Panel)?.Children.Remove(rows[i].Combo);
            Grid.SetRow(rows[i].Combo, i);
            Grid.SetColumn(rows[i].Combo, 1);
            grid.Children.Add(rows[i].Combo);
        }

        return grid;
    }

    private sealed record ColorChoice(string Label, EntityColor Value)
    {
        public override string ToString() => Label;
    }

    private sealed record LinetypeChoice(string Label, Linetype? Value)
    {
        public override string ToString() => Label;
    }

    private void InitializeLayerControls()
    {
        LayerCombo.SelectionChanged += (_, _) => OnPropertyPicked(LayerCombo.SelectedItem as Layer, e => e.Layer = (Layer)LayerCombo.SelectedItem!, l =>
        {
            if (l.IsFrozen)
            {
                AppendHistory("Un layer congelato non può essere corrente.");
                return false;
            }

            _editor!.CurrentLayer = l;
            return true;
        });
        ColorCombo.SelectionChanged += (_, _) => OnPropertyPicked(ColorCombo.SelectedItem as ColorChoice, e => e.Color = ((ColorChoice)ColorCombo.SelectedItem!).Value, c =>
        {
            _editor!.CurrentColor = c.Value;
            return true;
        });
        LinetypeCombo.SelectionChanged += (_, _) => OnPropertyPicked(LinetypeCombo.SelectedItem as LinetypeChoice, e => e.Linetype = ((LinetypeChoice)LinetypeCombo.SelectedItem!).Value, c =>
        {
            _editor!.CurrentLinetype = c.Value;
            return true;
        });

        ColorCombo.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ColorChoice>((choice, _) => ColorRow(choice));
    }

    private static Control ColorRow(ColorChoice? choice)
    {
        var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        if (choice is null)
        {
            return panel;
        }

        if (choice.Value.Source == ColorSource.Explicit)
        {
            var c = choice.Value.Value;
            panel.Children.Add(new Avalonia.Controls.Shapes.Rectangle
            {
                Width = 12, Height = 12, Stroke = Brushes.Gray, StrokeThickness = 1,
                Fill = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
        }

        panel.Children.Add(new TextBlock { Text = choice.Label });
        return panel;
    }

    /// <summary>Scelta in una casella della barra: sulla selezione, oppure come valore corrente.</summary>
    private void OnPropertyPicked<T>(T? value, Action<Entity> change, Func<T, bool> setCurrent) where T : class
    {
        if (_updatingProperties || _editor is null || value is null)
        {
            return;
        }

        var selected = _editor.Selection.Items.ToList();
        if (selected.Count > 0)
        {
            _editor.ChangeProperties(selected, change);
        }
        else
        {
            setCurrent(value);
        }

        RefreshLayers();
        CommandBox.Focus();
    }

    private void RefreshLayers(bool force = false)
    {
        if (_editor is null)
        {
            return;
        }

        var document = _editor.Document;
        var signature = string.Join("|", document.Layers.Select(l => $"{l.Name}/{l.Color}/{l.Linetype.Name}/{l.IsOn}/{l.IsFrozen}/{l.IsLocked}")) +
                        "#" + _editor.CurrentLayer.Name;
        if (force || signature != _layerSignature)
        {
            _layerSignature = signature;
            var items = document.Layers
                .OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(l => new LayerItem(l, l == _editor.CurrentLayer, OnLayerStateChanged))
                .ToList();
            var highlighted = (LayerList.SelectedItem as LayerItem)?.Layer ?? _editor.CurrentLayer;
            LayerList.ItemsSource = items;
            LayerList.SelectedItem = items.FirstOrDefault(i => i.Layer == highlighted) ?? items.FirstOrDefault(i => i.IsCurrent);
        }

        RefreshProperties();
    }

    private void OnLayerStateChanged()
    {
        if (_editor is null)
        {
            return;
        }

        // Gli oggetti di layer bloccati o nascosti escono dalla selezione.
        var stale = _editor.Selection.Items.Where(e => !Cad.Editing.EntityLocator.IsSelectable(e)).ToList();
        if (stale.Count > 0)
        {
            _editor.Selection.Remove(stale);
        }

        _editor.Document.MarkModified();
    }

    /// <summary>Le caselle mostrano il valore comune agli oggetti selezionati (vuote se diversi), o quelli correnti.</summary>
    private void RefreshProperties()
    {
        if (_editor is null)
        {
            return;
        }

        var document = _editor.Document;
        var selected = _editor.Selection.Items;
        _updatingProperties = true;
        try
        {
            SelectionText.Text = selected.Count switch
            {
                0 => "",
                1 => "1 oggetto selezionato",
                var n => $"{n} oggetti selezionati",
            };
            var subject = selected.Count > 0 ? "degli oggetti selezionati" : "dei nuovi oggetti";
            ToolTip.SetTip(LayerCombo, $"Layer {subject}");
            ToolTip.SetTip(ColorCombo, $"Colore {subject}");
            ToolTip.SetTip(LinetypeCombo, $"Tipo di linea {subject}");

            var layers = document.Layers.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            LayerCombo.ItemsSource = layers;
            LayerCombo.SelectedItem = selected.Count > 0 ? Common(selected, e => e.Layer) : _editor.CurrentLayer;

            var colors = StandardColors.All.Select(c => new ColorChoice(c.Name, EntityColor.Explicit(c.Color)))
                .Prepend(new ColorChoice("DaBlocco", EntityColor.ByBlock))
                .Prepend(new ColorChoice("DaLayer", EntityColor.ByLayer))
                .ToList();
            var color = selected.Count > 0 ? Common(selected, e => e.Color) : _editor.CurrentColor;
            if (color is { } c && colors.All(x => x.Value != c))
            {
                colors.Add(new ColorChoice(StandardColors.Describe(c), c));
            }

            ColorCombo.ItemsSource = colors;
            ColorCombo.SelectedItem = color is { } chosen ? colors.First(x => x.Value == chosen) : null;

            var linetypes = document.Linetypes.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .Select(l => new LinetypeChoice(l.Name, l))
                .Prepend(new LinetypeChoice("DaBlocco", Linetype.ByBlock))
                .Prepend(new LinetypeChoice("DaLayer", null))
                .ToList();
            var hasLinetype = selected.Count == 0 || selected.Select(e => e.Linetype).Distinct().Count() == 1;
            var linetype = selected.Count > 0 ? selected[0].Linetype : _editor.CurrentLinetype;
            LinetypeCombo.ItemsSource = linetypes;
            LinetypeCombo.SelectedItem = hasLinetype ? linetypes.FirstOrDefault(x => ReferenceEquals(x.Value, linetype) || (x.Value is not null && linetype is not null && x.Value.Name == linetype.Name)) : null;
        }
        finally
        {
            _updatingProperties = false;
        }
    }

    private static T? Common<T>(IReadOnlyList<Entity> entities, Func<Entity, T> property)
    {
        var first = property(entities[0]);
        return entities.All(e => EqualityComparer<T>.Default.Equals(property(e), first)) ? first : default;
    }

    // ---------- Pannello layer ----------

    private Layer? HighlightedLayer => (LayerList.SelectedItem as LayerItem)?.Layer;

    private void OnLayerDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_editor is not null && (sender as Control)?.DataContext is LayerItem item)
        {
            if (item.Layer.IsFrozen)
            {
                AppendHistory("Un layer congelato non può essere corrente.");
                return;
            }

            _editor.CurrentLayer = item.Layer;
            StatusText.Text = $"Layer corrente: {item.Name}";
            RefreshLayers();
        }
    }

    private async void OnNewLayerClick(object? sender, RoutedEventArgs e)
    {
        if (_editor is null)
        {
            return;
        }

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
            AppendHistory($"Nome di layer non valido o già usato: {name}");
            return;
        }

        var layer = document.GetOrAddLayer(name);
        // Il nuovo layer eredita colore e tipo di linea di quello evidenziato.
        if (HighlightedLayer is { } template)
        {
            layer.Color = template.Color;
            layer.Linetype = template.Linetype;
        }

        _editor.CurrentLayer = layer;
        document.MarkModified();
        RefreshLayers();
    }

    private async void OnRenameLayerClick(object? sender, RoutedEventArgs e)
    {
        if (_editor is null || HighlightedLayer is not { } layer)
        {
            return;
        }

        if (layer.Name == Layer.DefaultName)
        {
            AppendHistory("Il layer 0 non si può rinominare.");
            return;
        }

        var name = await InputDialog.AskAsync(this, "Rinomina layer", $"Nuovo nome per {layer.Name}:", layer.Name);
        if (string.IsNullOrEmpty(name) || name == layer.Name)
        {
            return;
        }

        if (_editor.Document.RenameLayer(layer, name))
        {
            _editor.Document.MarkModified();
            RefreshLayers();
        }
        else
        {
            AppendHistory($"Nome di layer non valido o già usato: {name}");
        }
    }

    private void OnDeleteLayerClick(object? sender, RoutedEventArgs e)
    {
        if (_editor is null || HighlightedLayer is not { } layer)
        {
            return;
        }

        if (layer == _editor.CurrentLayer)
        {
            AppendHistory($"{layer.Name} è il layer corrente: rendine corrente un altro prima di eliminarlo.");
        }
        else if (_editor.Document.RemoveLayer(layer))
        {
            _editor.Document.MarkModified();
            RefreshLayers();
        }
        else
        {
            AppendHistory($"{layer.Name}: non si può eliminare (layer 0 o contiene oggetti).");
        }
    }

    private void OnLayerColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LayerItem item } button)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var (name, color) in StandardColors.All)
        {
            var entry = new MenuItem { Header = ColorRow(new ColorChoice(name, EntityColor.Explicit(color))) };
            entry.Click += (_, _) => ChangeLayer(item.Layer, l => l.Color = color);
            flyout.Items.Add(entry);
        }

        var other = new MenuItem { Header = "Altro (r,g,b)..." };
        other.Click += async (_, _) =>
        {
            var c = item.Layer.Color;
            var text = await InputDialog.AskAsync(this, "Colore del layer", "Colore come r,g,b (0-255):", $"{c.R},{c.G},{c.B}");
            if (text is not null && StandardColors.Parse(text) is { } parsed)
            {
                ChangeLayer(item.Layer, l => l.Color = parsed);
            }
        };
        flyout.Items.Add(other);
        flyout.ShowAt(button);
    }

    private void OnLayerLinetypeClick(object? sender, RoutedEventArgs e)
    {
        if (_editor is null || sender is not Button { Tag: LayerItem item } button)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var linetype in _editor.Document.Linetypes.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
        {
            var entry = new MenuItem { Header = string.IsNullOrEmpty(linetype.Description) ? linetype.Name : $"{linetype.Name}  {linetype.Description}" };
            entry.Click += (_, _) => ChangeLayer(item.Layer, l => l.Linetype = linetype);
            flyout.Items.Add(entry);
        }

        flyout.ShowAt(button);
    }

    private void ChangeLayer(Layer layer, Action<Layer> change)
    {
        change(layer);
        _editor?.Document.MarkModified();
        RefreshLayers();
    }
}

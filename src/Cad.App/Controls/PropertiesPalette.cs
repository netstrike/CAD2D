using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Cad.Document;
using Cad.Editing;

namespace Cad.App.Controls;

/// <summary>
/// Palette Proprietà, come in DraftSight: tutti i dati degli oggetti selezionati, raggruppati per categoria e
/// modificabili. I valori diversi tra più oggetti appaiono come *Vari*; ogni modifica si annulla con ANNULLA.
/// </summary>
public sealed class PropertiesPalette : UserControl
{
    private static readonly IBrush CategoryBrush = new SolidColorBrush(Color.FromRgb(0x36, 0x36, 0x36));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.FromRgb(0xB4, 0xB4, 0xB4));
    private static readonly IBrush ReadOnlyBrush = new SolidColorBrush(Color.FromRgb(0x96, 0x96, 0x96));

    private readonly TextBlock _header = new() { FontWeight = FontWeight.SemiBold, Margin = new Thickness(10, 8, 10, 6) };
    private readonly StackPanel _rows = new();
    private Editor? _editor;
    private bool _refreshQueued;

    public PropertiesPalette()
    {
        var hint = new TextBlock
        {
            Text = "Seleziona oggetti nel disegno per vederne e cambiarne le proprietà.",
            Foreground = ReadOnlyBrush,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10, 0, 10, 8),
        };
        var panel = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        panel.Children.Add(_header);
        DockPanel.SetDock(hint, Dock.Top);
        panel.Children.Add(hint);
        panel.Children.Add(new ScrollViewer { Content = _rows });
        Content = panel;
        _hint = hint;
    }

    private readonly TextBlock _hint;

    /// <summary>Sollevato dopo ogni modifica, per restituire il fuoco alla riga di comando.</summary>
    public event EventHandler? Committed;

    public Editor? Editor
    {
        get => _editor;
        set
        {
            if (_editor is not null)
            {
                _editor.Selection.Changed -= OnChanged;
                _editor.Document.Changed -= OnChanged;
            }

            _editor = value;
            if (_editor is not null)
            {
                _editor.Selection.Changed += OnChanged;
                _editor.Document.Changed += OnChanged;
            }

            Refresh();
        }
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        // Più cambi di fila (una modifica che riseleziona) diventano un solo aggiornamento.
        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            Refresh();
        });
    }

    public void Refresh()
    {
        _rows.Children.Clear();
        var selected = _editor?.Selection.Items ?? [];
        _header.Text = PropertySheet.Describe(selected);
        _hint.IsVisible = selected.Count == 0;
        if (_editor is null || selected.Count == 0)
        {
            return;
        }

        var entities = selected.ToList();
        foreach (var group in PropertySheet.For(_editor.Document, entities).GroupBy(r => r.Category))
        {
            _rows.Children.Add(new Border
            {
                Background = CategoryBrush,
                Padding = new Thickness(10, 3),
                Child = new TextBlock { Text = group.Key, FontWeight = FontWeight.SemiBold, FontSize = 11.5 },
            });
            foreach (var row in group)
            {
                _rows.Children.Add(BuildRow(row, entities));
            }
        }
    }

    private Control BuildRow(PropertyRow row, IReadOnlyList<Entity> entities)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), Margin = new Thickness(10, 1, 6, 1), MinHeight = 26 };
        grid.Children.Add(new TextBlock { Text = row.Name, Foreground = LabelBrush, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var editor = BuildEditor(row, entities);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private Control BuildEditor(PropertyRow row, IReadOnlyList<Entity> entities)
    {
        switch (row.Kind)
        {
            case PropertyKind.ReadOnly:
                return new SelectableTextBlock
                {
                    Text = row.Value ?? PropertySheet.Mixed,
                    Foreground = ReadOnlyBrush,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0),
                };

            case PropertyKind.Choice:
            {
                var choices = row.Choices.ToList();
                if (row.Value is { } current && !choices.Contains(current))
                {
                    choices.Add(current);
                }

                var combo = new ComboBox
                {
                    ItemsSource = choices,
                    SelectedItem = row.Value,
                    PlaceholderText = PropertySheet.Mixed,
                    FontSize = 11.5,
                    MinHeight = 24,
                    Padding = new Thickness(6, 1),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is string value && value != row.Value)
                    {
                        Commit(row, entities, value);
                    }
                };
                return combo;
            }

            default:
            {
                var box = new TextBox
                {
                    Text = row.Value ?? string.Empty,
                    Watermark = row.IsMixed ? PropertySheet.Mixed : null,
                    FontSize = 11.5,
                    MinHeight = 24,
                    Padding = new Thickness(6, 2),
                };
                var original = box.Text;
                void TryCommit()
                {
                    var text = box.Text ?? string.Empty;
                    if (text != original && (text.Length > 0 || row.Kind == PropertyKind.Text))
                    {
                        original = text;
                        if (!Commit(row, entities, text))
                        {
                            box.Text = row.Value ?? string.Empty;
                            original = box.Text;
                        }
                    }
                }

                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        TryCommit();
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        box.Text = row.Value ?? string.Empty;
                        Committed?.Invoke(this, EventArgs.Empty);
                        e.Handled = true;
                    }
                };
                box.LostFocus += (_, _) => TryCommit();
                return box;
            }
        }
    }

    private bool Commit(PropertyRow row, IReadOnlyList<Entity> entities, string value)
    {
        if (_editor is null)
        {
            return false;
        }

        // Solo le entità ancora nel disegno: la palette può essere rimasta indietro di un aggiornamento.
        var current = entities.Where(e => _editor.Document.ModelSpace.Contains(e)).ToList();
        var applied = current.Count > 0 && PropertySheet.Apply(_editor, current, row, value);
        Committed?.Invoke(this, EventArgs.Empty);
        return applied;
    }
}

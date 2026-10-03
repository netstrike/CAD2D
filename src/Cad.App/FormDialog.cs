using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Cad.App;

/// <summary>
/// Finestra di impostazioni a righe "etichetta: valore", raggruppate in sezioni, con OK e Annulla. I valori si
/// applicano solo con OK, e solo se sono tutti validi: un valore sbagliato si colora di rosso e la finestra resta aperta.
/// </summary>
public class FormDialog : Window
{
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x50, 0x50));

    private readonly StackPanel _body = new() { Spacing = 4 };
    private readonly List<Func<bool>> _validators = [];
    private readonly List<Action> _appliers = [];
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };

    protected FormDialog(string title, double width = 420)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Annulla", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close();
        _buttons.Children.Add(ok);
        _buttons.Children.Add(cancel);

        // Esc chiude anche quando il fuoco è in una casella di testo.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Content = new StackPanel
        {
            Margin = new Thickness(18, 14, 18, 16),
            Spacing = 14,
            Children = { _body, _buttons },
        };
    }

    /// <summary>True se la finestra è stata chiusa con OK.</summary>
    public bool Accepted { get; private set; }

    /// <summary>Pulsante in più accanto a OK (per esempio "Seleziona oggetti" nel tratteggio).</summary>
    protected void AddButton(string text, Action onClick)
    {
        var button = new Button { Content = text, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) => onClick();
        _buttons.Children.Insert(0, button);
    }

    protected void Section(string title) => _body.Children.Add(new TextBlock
    {
        Text = title,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, _body.Children.Count == 0 ? 0 : 10, 0, 2),
    });

    protected void Row(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*"), MinHeight = 30 };
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Silver });
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        _body.Children.Add(grid);
    }

    protected void Add(Control control) => _body.Children.Add(control);

    /// <summary>Controllo e applicazione propri, eseguiti con OK insieme a quelli delle righe.</summary>
    protected void OnAccept(Func<bool> validate, Action apply)
    {
        _validators.Add(validate);
        _appliers.Add(apply);
    }

    /// <summary>Bordo rosso sulle caselle con un valore non valido.</summary>
    protected static void MarkError(TextBox box, bool ok) => box.BorderBrush = ok ? null : ErrorBrush;

    /// <summary>Casella numerica (accetta punto o virgola) con controllo del valore.</summary>
    protected TextBox Number(string label, double value, Func<double, bool> isValid, Action<double> apply)
    {
        var box = new TextBox { Text = value.ToString("0.####", CultureInfo.InvariantCulture), MinHeight = 26 };
        Row(label, box);
        double parsed = value;
        _validators.Add(() =>
        {
            var ok = double.TryParse((box.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                     && double.IsFinite(parsed) && isValid(parsed);
            box.BorderBrush = ok ? null : ErrorBrush;
            return ok;
        });
        _appliers.Add(() => apply(parsed));
        return box;
    }

    protected ComboBox Choice(string label, IReadOnlyList<string> items, string selected, Action<string> apply)
    {
        var combo = new ComboBox { ItemsSource = items, SelectedItem = selected, MinHeight = 26, HorizontalAlignment = HorizontalAlignment.Stretch };
        Row(label, combo);
        _appliers.Add(() =>
        {
            if (combo.SelectedItem is string value)
            {
                apply(value);
            }
        });
        return combo;
    }

    protected CheckBox Check(string label, bool value, Action<bool> apply)
    {
        var box = new CheckBox { Content = label, IsChecked = value };
        _body.Children.Add(box);
        _appliers.Add(() => apply(box.IsChecked == true));
        return box;
    }

    /// <summary>Controlla e applica tutti i valori; false se qualcuno non è valido.</summary>
    protected bool Apply()
    {
        if (!_validators.Select(v => v()).ToList().All(ok => ok))
        {
            return false;
        }

        foreach (var apply in _appliers)
        {
            apply();
        }

        return true;
    }

    protected void Accept()
    {
        if (Apply())
        {
            Accepted = true;
            Close();
        }
    }
}

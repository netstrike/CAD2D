using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Cad.Document;
using Cad.Geometry;
using Cad.Plot;
using Cad.Rendering;
using SkiaSharp;

namespace Cad.App;

/// <summary>
/// Finestra di stampa: dove (PDF, SVG, PNG o una stampante), foglio, area, scala e aspetto, con l'anteprima del foglio
/// che si aggiorna a ogni cambio. "Finestra &lt;" chiude la finestra per indicare l'area nel disegno; il comando la riapre.
/// </summary>
public sealed class PlotDialog : Window
{
    public const string Pdf = "File PDF";
    public const string Svg = "File SVG";
    public const string Png = "Immagine PNG";
    private const string PrinterPrefix = "Stampante: ";
    private const double PreviewSize = 360;
    private static readonly string[] CommonScales = ["1:1", "1:2", "1:5", "1:10", "1:20", "1:25", "1:50", "1:100", "1:200", "1:500", "2:1", "5:1", "10:1"];
    private static readonly string[] Areas = ["Estensioni", "Vista corrente", "Finestra"];

    private readonly Scene _scene;
    private readonly BoundingBox _display;
    private readonly PlotSettings _settings;
    private readonly ComboBox _destination;
    private readonly ComboBox _paper;
    private readonly RadioButton _landscape = new() { Content = "Orizzontale", GroupName = "orientamento" };
    private readonly RadioButton _portrait = new() { Content = "Verticale", GroupName = "orientamento", Margin = new Thickness(12, 0, 0, 0) };
    private readonly ComboBox _area;
    private readonly CheckBox _fit = new() { Content = "Adatta al foglio" };
    private readonly AutoCompleteBox _scale;
    private readonly CheckBox _center = new() { Content = "Centra sul foglio" };
    private readonly CheckBox _monochrome = new() { Content = "Bianco e nero" };
    private readonly CheckBox _lineweights = new() { Content = "Spessori di linea" };
    private readonly TextBlock _result = new() { Foreground = Brushes.Gray, FontSize = 11.5 };
    private readonly TextBlock _warning = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x90, 0x40)), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, Width = PreviewSize, Height = PreviewSize };
    private BoundingBox _window;
    private bool _loading = true;

    public PlotDialog(Scene scene, PlotSettings settings, BoundingBox display, string destination, IReadOnlyList<string> printers)
    {
        Title = "Stampa";
        Width = 820;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _scene = scene;
        _display = display;
        _settings = settings.Clone();
        _window = settings.Area == PlotArea.Window ? settings.Window : BoundingBox.Empty;

        var destinations = new List<string> { Pdf, Svg, Png };
        destinations.AddRange(printers.Select(p => PrinterPrefix + p));
        _destination = new ComboBox { ItemsSource = destinations, SelectedItem = destinations.Contains(destination) ? destination : Pdf, HorizontalAlignment = HorizontalAlignment.Stretch };
        _paper = new ComboBox { ItemsSource = PaperSize.Standard, SelectedItem = _settings.Paper, HorizontalAlignment = HorizontalAlignment.Stretch };
        _area = new ComboBox { ItemsSource = Areas, SelectedIndex = (int)_settings.Area, HorizontalAlignment = HorizontalAlignment.Stretch };
        _scale = new AutoCompleteBox
        {
            Text = CadDocument.FormatScale(_settings.Scale),
            ItemsSource = CommonScales,
            FilterMode = AutoCompleteFilterMode.StartsWith,
            MinimumPrefixLength = 0,
            MinHeight = 26,
        };
        (_settings.Landscape ? _landscape : _portrait).IsChecked = true;
        _fit.IsChecked = _settings.Fit;
        _center.IsChecked = _settings.Center;
        _monochrome.IsChecked = _settings.Monochrome;
        _lineweights.IsChecked = _settings.Lineweights;

        var pick = new Button { Content = "Finestra <", Margin = new Thickness(6, 0, 0, 0) };
        ToolTip.SetTip(pick, "Indica nel disegno i due angoli dell'area da stampare");
        pick.Click += (_, _) =>
        {
            Read();
            PickWindow = true;
            Close();
        };
        var areaRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        areaRow.Children.Add(_area);
        Grid.SetColumn(pick, 1);
        areaRow.Children.Add(pick);

        var form = new StackPanel { Spacing = 4 };
        void Section(string title) => form.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, form.Children.Count == 0 ? 0 : 10, 0, 2),
        });
        void Row(string label, Control control)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), MinHeight = 30 };
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Silver });
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            form.Children.Add(grid);
        }

        Section("Destinazione");
        Row("Stampa su", _destination);
        Section("Foglio");
        Row("Formato", _paper);
        Row("Orientamento", new StackPanel { Orientation = Orientation.Horizontal, Children = { _landscape, _portrait } });
        Section("Area e scala");
        Row("Area", areaRow);
        form.Children.Add(_fit);
        Row("Scala", _scale);
        form.Children.Add(_result);
        form.Children.Add(_center);
        Section("Aspetto");
        form.Children.Add(_monochrome);
        form.Children.Add(_lineweights);
        form.Children.Add(_warning);

        var ok = new Button { Content = "Stampa", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) =>
        {
            if (Read())
            {
                Accepted = true;
                Close();
            }
        };
        var cancel = new Button { Content = "Annulla", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close();

        var frame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A)),
            Padding = new Thickness(10),
            Child = _preview,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var right = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(18, 0, 0, 0),
            Children =
            {
                new TextBlock { Text = "Anteprima", FontWeight = FontWeight.SemiBold },
                frame,
            },
        };
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        columns.Children.Add(form);
        Grid.SetColumn(right, 1);
        columns.Children.Add(right);

        Content = new StackPanel
        {
            Margin = new Thickness(18, 14, 18, 16),
            Spacing = 14,
            Children =
            {
                columns,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
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

        foreach (var combo in new[] { _destination, _paper, _area })
        {
            combo.SelectionChanged += (_, _) => Update();
        }

        foreach (var box in new ToggleButton[] { _landscape, _portrait, _fit, _center, _monochrome, _lineweights })
        {
            box.IsCheckedChanged += (_, _) => Update();
        }

        _scale.TextChanged += (_, _) => Update();
        Opened += (_, _) =>
        {
            _loading = false;
            Update();
        };
    }

    /// <summary>Le impostazioni scelte (valide anche dopo Annulla, per non perdere le modifiche se si riapre).</summary>
    public PlotSettings Settings => _settings;

    /// <summary>"File PDF", "File SVG", "Immagine PNG" o il nome della stampante.</summary>
    public string Destination { get; private set; } = Pdf;

    public string? Printer => Destination.StartsWith(PrinterPrefix, StringComparison.Ordinal) ? Destination[PrinterPrefix.Length..] : null;

    public bool Accepted { get; private set; }

    /// <summary>True se si vuole indicare l'area nel disegno: il comando chiede i due angoli e riapre la finestra.</summary>
    public bool PickWindow { get; private set; }

    /// <summary>Copia i controlli nelle impostazioni; false se la scala non è valida.</summary>
    private bool Read()
    {
        Destination = _destination.SelectedItem as string ?? Pdf;
        _settings.Paper = _paper.SelectedItem as PaperSize ?? PaperSize.A4;
        _settings.Landscape = _landscape.IsChecked == true;
        _settings.Area = (PlotArea)Math.Max(0, _area.SelectedIndex);
        _settings.Window = _settings.Area switch
        {
            PlotArea.Display => _display,
            PlotArea.Window => _window,
            _ => BoundingBox.Empty,
        };
        _settings.Fit = _fit.IsChecked == true;
        _settings.Center = _center.IsChecked == true;
        _settings.Monochrome = _monochrome.IsChecked == true;
        _settings.Lineweights = _lineweights.IsChecked == true;
        var scale = CadDocument.ParseScale(_scale.Text ?? "");
        if (scale is { } value)
        {
            _settings.Scale = value;
        }

        var ok = scale is not null || _settings.Fit;
        if (ok)
        {
            _scale.ClearValue(BorderBrushProperty);
        }
        else
        {
            _scale.BorderBrush = Brushes.IndianRed;
        }
        return ok;
    }

    private void Update()
    {
        if (_loading)
        {
            return;
        }

        Read();
        _scale.IsEnabled = _fit.IsChecked != true;
        var layout = PlotLayout.Compute(_settings, PlotExporter.AreaOf(_settings, _scene));
        _result.Text = _settings.Fit
            ? $"Scala risultante: {CadDocument.FormatScale(layout.Scale)} (unità del disegno per mm di carta)"
            : $"Il disegno occupa {layout.Area.Width * layout.PaperPerUnit:0} × {layout.Area.Height * layout.PaperPerUnit:0} mm sul foglio";
        _warning.Text = _settings.Area == PlotArea.Window && _window.IsEmpty
            ? "Indica la finestra con il pulsante \"Finestra <\": per ora si stampa tutto il disegno."
            : layout.Fits ? "" : "Alla scala scelta il disegno esce dal foglio: la parte fuori viene tagliata.";

        // Anteprima con il lato lungo di circa 360 pixel: abbastanza per vedere scala e margini.
        var dpi = PreviewSize / (Math.Max(_settings.PaperWidth, _settings.PaperHeight) / 25.4);
        using var bitmap = PlotExporter.Bitmap(_scene, layout, dpi);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        var old = _preview.Source as IDisposable;
        _preview.Source = new Bitmap(stream);
        old?.Dispose();
    }
}

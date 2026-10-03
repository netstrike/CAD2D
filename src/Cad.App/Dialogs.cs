using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Cad.Document;
using Cad.Editing;
using Cad.Geometry;
using Cad.Rendering;

namespace Cad.App;

/// <summary>Tratteggio: motivo con anteprima, scala e angolo; poi si indicano i punti interni o gli oggetti.</summary>
public sealed class HatchDialog : FormDialog
{
    private const double PreviewSize = 96;
    private readonly Canvas _preview = new() { Width = PreviewSize, Height = PreviewSize, ClipToBounds = true, Background = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21)) };
    private readonly ListBox _patterns;
    private readonly TextBox _scale;
    private readonly TextBox _angle;

    public HatchDialog(EditorSettings settings) : base("Tratteggio", 460)
    {
        Section("Motivo");
        var names = HatchPatterns.Names.ToList();
        _patterns = new ListBox
        {
            ItemsSource = names.Select(n => $"{n}  ·  {HatchPatterns.Describe(n)}").ToList(),
            SelectedIndex = Math.Max(0, names.FindIndex(n => n.Equals(settings.HatchPattern, StringComparison.OrdinalIgnoreCase))),
            Height = 150,
            FontSize = 12,
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(_patterns);
        var frame = new Border { Child = _preview, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(frame, 1);
        row.Children.Add(frame);
        Add(row);

        Section("Proprietà");
        _scale = Number("Scala", settings.HatchScale, v => v > 0, v => settings.HatchScale = v);
        _angle = Number("Angolo (gradi)", settings.HatchAngle, _ => true, v => settings.HatchAngle = v);

        _patterns.SelectionChanged += (_, _) => DrawPreview();
        _scale.TextChanged += (_, _) => DrawPreview();
        _angle.TextChanged += (_, _) => DrawPreview();
        Opened += (_, _) => DrawPreview();

        AddButton("Seleziona oggetti", () =>
        {
            SelectObjects = true;
            Accept();
        });
        Closing += (_, _) =>
        {
            if (Accepted)
            {
                settings.HatchPattern = names[Math.Max(0, _patterns.SelectedIndex)];
            }
        };
    }

    /// <summary>True se si vuole tratteggiare oggetti chiusi invece di indicare punti interni.</summary>
    public bool SelectObjects { get; private set; }

    /// <summary>Anteprima del motivo in un quadrato di 10 unità per lato (scala e angolo come impostati).</summary>
    private void DrawPreview()
    {
        _preview.Children.Clear();
        var names = HatchPatterns.Names.ToList();
        var name = names[Math.Max(0, _patterns.SelectedIndex)];
        var scale = Parse(_scale.Text, 1);
        var angle = Parse(_angle.Text, 0);
        if (scale <= 0)
        {
            return;
        }

        var brush = new SolidColorBrush(Color.FromRgb(0x4F, 0xA3, 0xFF));
        if (name.Equals(HatchPatterns.Solid, StringComparison.OrdinalIgnoreCase))
        {
            _preview.Children.Add(new Rectangle { Width = PreviewSize, Height = PreviewSize, Fill = brush });
            return;
        }

        const double side = 10;
        var ring = new[] { new Vector2(0, 0), new Vector2(side, 0), new Vector2(side, side), new Vector2(0, side), new Vector2(0, 0) };
        var budget = new Ref<int>(4000);
        var factor = PreviewSize / side;
        foreach (var line in HatchPatterns.Build(name, scale, InputParser.DegreesToRadians(angle)))
        {
            foreach (var (from, to) in Patterns.HatchLines([ring], line, budget))
            {
                _preview.Children.Add(new Line
                {
                    StartPoint = new Point(from.X * factor, PreviewSize - from.Y * factor),
                    EndPoint = new Point(to.X * factor, PreviewSize - to.Y * factor),
                    Stroke = brush,
                    StrokeThickness = 1,
                });
            }
        }
    }

    private static double Parse(string? text, double fallback) =>
        double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}

/// <summary>Stile di quota: scelta dello stile corrente e delle sue grandezze, come nella finestra di DraftSight.</summary>
public sealed class DimensionStyleDialog : FormDialog
{
    public DimensionStyleDialog(CadDocument document) : base("Stile di quota", 480)
    {
        var style = document.CurrentDimensionStyle;
        Section($"Stile corrente: {style.Name}");
        var names = document.DimensionStyles.Select(s => s.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count > 1)
        {
            Choice("Rendi corrente", names, style.Name, name =>
            {
                if (document.DimensionStyles.FirstOrDefault(s => s.Name == name) is { } chosen)
                {
                    document.CurrentDimensionStyle = chosen;
                }
            });
        }

        Section("Testo");
        Number("Altezza del testo", style.TextHeight, v => v > 0, v => style.TextHeight = v);
        Number("Distanza dalla linea", style.TextGap, v => v >= 0, v => style.TextGap = v);
        Number("Decimali (0-8)", style.Decimals, v => v is >= 0 and <= 8 && v == Math.Floor(v), v => style.Decimals = (int)v);
        Choice("Separatore decimale", [",", "."], style.DecimalSeparator.ToString(), v => style.DecimalSeparator = v[0]);

        Section("Linee e frecce");
        Number("Lunghezza frecce", style.ArrowSize, v => v > 0, v => style.ArrowSize = v);
        Number("Scostamento dall'oggetto", style.ExtensionOffset, v => v >= 0, v => style.ExtensionOffset = v);
        Number("Prolungamento oltre la quota", style.ExtensionExtend, v => v >= 0, v => style.ExtensionExtend = v);

        Section("Scala");
        Number("Scala globale", style.Scale, v => v > 0, v => style.Scale = v);
    }
}

/// <summary>Opzioni del programma: aiuti al disegno, snap, tipi di linea e tema.</summary>
public sealed class OptionsDialog : FormDialog
{
    public OptionsDialog(Editor editor, Func<SnapModes> getSnapModes, Action<SnapModes> setSnapModes, bool lightTheme, Action<bool> setTheme)
        : base("Opzioni", 480)
    {
        Section("Aiuti al disegno");
        Number("Passo della griglia", editor.GridSpacing, v => v > 0, v => editor.GridSpacing = v);
        Number("Incremento polare (gradi)", editor.PolarIncrementDegrees, v => v is > 0 and <= 180, v => editor.PolarIncrementDegrees = v);

        Section("Snap agli oggetti");
        var modes = getSnapModes();
        var selected = modes;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        var index = 0;
        foreach (var (mode, label) in SnapLabels)
        {
            var box = new CheckBox { Content = label, IsChecked = modes.HasFlag(mode) };
            box.IsCheckedChanged += (_, _) => selected = box.IsChecked == true ? selected | mode : selected & ~mode;
            Grid.SetColumn(box, index % 3);
            Grid.SetRow(box, index / 3);
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Children.Add(box);
            index++;
        }

        Add(grid);
        Closing += (_, _) =>
        {
            if (Accepted)
            {
                setSnapModes(selected);
            }
        };

        Section("Disegno");
        Number("Scala globale tipi di linea", editor.Document.LinetypeScale, v => v > 0, v =>
        {
            if (Math.Abs(v - editor.Document.LinetypeScale) > 1e-12)
            {
                editor.Document.LinetypeScale = v;
                editor.Document.MarkModified();
            }
        });
        Number("Altezza testo predefinita", editor.Settings.TextHeight, v => v > 0, v => editor.Settings.TextHeight = v);

        Section("Aspetto");
        Choice("Tema", ["Scuro", "Chiaro"], lightTheme ? "Chiaro" : "Scuro", v => setTheme(v == "Chiaro"));
    }

    private static readonly (SnapModes Mode, string Label)[] SnapLabels =
    [
        (SnapModes.Endpoint, "Estremo"), (SnapModes.Midpoint, "Medio"), (SnapModes.Center, "Centro"),
        (SnapModes.Quadrant, "Quadrante"), (SnapModes.Intersection, "Intersezione"), (SnapModes.Perpendicular, "Perpendicolare"),
        (SnapModes.Tangent, "Tangente"), (SnapModes.Node, "Nodo"), (SnapModes.Nearest, "Vicino"),
    ];
}

/// <summary>
/// Stili di testo: si sceglie lo stile (o se ne crea uno), si cambiano carattere, altezza fissa, larghezza e
/// inclinazione con l'anteprima, e con OK lo stile scelto diventa quello corrente.
/// </summary>
public sealed class TextStyleDialog : FormDialog
{
    private sealed class Draft(TextStyle style)
    {
        public TextStyle Style { get; } = style;
        public string Family { get; set; } = style.FontFamily;
        public string Height { get; set; } = Format(style.Height);
        public string Width { get; set; } = Format(style.WidthFactor);
        public string Oblique { get; set; } = Format(style.ObliqueAngle * 180 / Math.PI);
    }

    private readonly CadDocument _document;
    private readonly List<Draft> _drafts;
    private readonly ComboBox _styles = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 26 };
    private readonly ComboBox _family = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 26, MaxDropDownHeight = 320 };
    private readonly TextBox _height = new() { MinHeight = 26 };
    private readonly TextBox _width = new() { MinHeight = 26 };
    private readonly TextBox _oblique = new() { MinHeight = 26 };
    private readonly TextBlock _preview = new() { Text = "AaBbCc 0123 Ø±°", FontSize = 26, Margin = new Thickness(8, 4) };
    private Draft? _current;
    private bool _loading;

    public TextStyleDialog(CadDocument document) : base("Stile di testo", 480)
    {
        _document = document;
        _drafts = [.. document.TextStyles.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(s => new Draft(s))];

        Section("Stile");
        var newName = new TextBox { Watermark = "Nome del nuovo stile", MinHeight = 26 };
        var create = new Button { Content = "Nuovo", Margin = new Thickness(6, 0, 0, 0) };
        create.Click += (_, _) =>
        {
            var name = (newName.Text ?? string.Empty).Trim();
            var ok = CadDocument.IsValidName(name) && _drafts.All(d => !d.Style.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            MarkError(newName, ok);
            if (!ok)
            {
                return;
            }

            // Lo stile nuovo parte dai valori di quello mostrato; si crea davvero solo con OK.
            var draft = new Draft(new TextStyle(name))
            {
                Family = _family.SelectedItem as string ?? TextStyle.DefaultFamily,
                Height = _height.Text ?? "0",
                Width = _width.Text ?? "1",
                Oblique = _oblique.Text ?? "0",
            };
            _drafts.Add(draft);
            RefreshStyles(draft);
            newName.Text = string.Empty;
        };
        Row("Stile corrente", _styles);
        var createRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        createRow.Children.Add(newName);
        Grid.SetColumn(create, 1);
        createRow.Children.Add(create);
        Row("Crea uno stile", createRow);

        Section("Carattere");
        var families = FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var draft in _drafts.Where(d => !families.Contains(d.Family, StringComparer.OrdinalIgnoreCase)))
        {
            families.Insert(0, draft.Family);
        }

        _family.ItemsSource = families;
        Row("Famiglia", _family);
        Row("Altezza fissa (0 = libera)", _height);
        Row("Fattore di larghezza", _width);
        Row("Inclinazione (gradi)", _oblique);

        Section("Anteprima");
        Add(new Border { Child = _preview, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), ClipToBounds = true, Height = 56 });

        _styles.SelectionChanged += (_, _) => Load(_drafts.ElementAtOrDefault(_styles.SelectedIndex));
        _family.SelectionChanged += (_, _) => Store();
        _height.TextChanged += (_, _) => Store();
        _width.TextChanged += (_, _) => Store();
        _oblique.TextChanged += (_, _) => Store();
        RefreshStyles(_drafts.FirstOrDefault(d => d.Style == document.CurrentTextStyle) ?? _drafts[0]);

        OnAccept(Validate, Save);
    }

    private void RefreshStyles(Draft selected)
    {
        _styles.ItemsSource = _drafts.Select(d => d.Style.Name).ToList();
        _styles.SelectedIndex = _drafts.IndexOf(selected);
    }

    private void Load(Draft? draft)
    {
        if (draft is null)
        {
            return;
        }

        _loading = true;
        _current = draft;
        _family.SelectedItem = (_family.ItemsSource as List<string>)?.FirstOrDefault(f => f.Equals(draft.Family, StringComparison.OrdinalIgnoreCase)) ?? draft.Family;
        _height.Text = draft.Height;
        _width.Text = draft.Width;
        _oblique.Text = draft.Oblique;
        _loading = false;
        UpdatePreview();
    }

    private void Store()
    {
        if (_loading || _current is null)
        {
            return;
        }

        _current.Family = _family.SelectedItem as string ?? _current.Family;
        _current.Height = _height.Text ?? string.Empty;
        _current.Width = _width.Text ?? string.Empty;
        _current.Oblique = _oblique.Text ?? string.Empty;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_current is null)
        {
            return;
        }

        _preview.FontFamily = new FontFamily(_current.Family);
        var width = TryParse(_current.Width, out var w) && w > 0 ? w : 1;
        var oblique = TryParse(_current.Oblique, out var o) && Math.Abs(o) <= 85 ? o : 0;
        _preview.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
        _preview.RenderTransform = new MatrixTransform(new Matrix(width, 0, -Math.Tan(oblique * Math.PI / 180), 1, 0, 0));
    }

    /// <summary>Tutti gli stili devono avere valori validi; il primo sbagliato viene mostrato.</summary>
    private bool Validate()
    {
        foreach (var draft in _drafts)
        {
            var heightOk = TryParse(draft.Height, out var h) && h >= 0;
            var widthOk = TryParse(draft.Width, out var w) && w > 0;
            var obliqueOk = TryParse(draft.Oblique, out var o) && Math.Abs(o) <= 85;
            if (!(heightOk && widthOk && obliqueOk))
            {
                RefreshStyles(draft);
                MarkError(_height, heightOk);
                MarkError(_width, widthOk);
                MarkError(_oblique, obliqueOk);
                return false;
            }
        }

        return true;
    }

    private void Save()
    {
        foreach (var draft in _drafts)
        {
            var style = _document.FindTextStyle(draft.Style.Name) ?? _document.GetOrAddTextStyle(draft.Style.Name);
            if (!style.FontFamily.Equals(draft.Family, StringComparison.OrdinalIgnoreCase))
            {
                style.FontFamily = draft.Family;
                style.FontFile = null;
            }

            TryParse(draft.Height, out var height);
            TryParse(draft.Width, out var width);
            TryParse(draft.Oblique, out var oblique);
            style.Height = height;
            style.WidthFactor = width;
            style.ObliqueAngle = oblique * Math.PI / 180;
        }

        if (_drafts.ElementAtOrDefault(_styles.SelectedIndex) is { } selected)
        {
            _document.CurrentTextStyle = _document.FindTextStyle(selected.Style.Name)!;
        }
    }

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static bool TryParse(string? text, out double value) =>
        double.TryParse((text ?? string.Empty).Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}

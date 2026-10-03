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

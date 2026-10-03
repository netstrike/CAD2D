using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

/// <summary>CORRISPONDENZA, COPIAPROP/INCOLLAPROP e le proprietà salvate.</summary>
public sealed class PropertyCommandTests : IDisposable
{
    private const double Aperture = 0.5;
    private readonly string _folder = Directory.CreateTempSubdirectory("cad2d-prop").FullName;
    private readonly CadDocument _document = new();
    private readonly Editor _editor;
    private readonly Layer _red;
    private readonly Layer _plain;

    public PropertyCommandTests()
    {
        _editor = new Editor(_document) { SnapEnabled = false, PolarEnabled = false, TrackingEnabled = false };
        _red = _document.GetOrAddLayer("ROSSO");
        _red.Color = new CadColor(255, 0, 0);
        _plain = _document.GetOrAddLayer("0");
        PropertyCommands.Clipboard = null;
        PropertyCommands.MatchMask = PropertyMask.All;
        PropertyCommands.SettingsDialog = null;
        PropertyCommands.Library = new PropertyLibrary(Path.Combine(_folder, "proprieta.json"));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private T Add<T>(T entity) where T : Entity
    {
        _document.Edit("test", e => e.Add(entity));
        return entity;
    }

    private void Type(params string[] inputs)
    {
        foreach (var input in inputs)
        {
            _editor.SubmitText(input);
        }
    }

    private void Click(double x, double y) => _editor.Click(new Vector2(x, y), Aperture);

    private static LineEntity Styled(LineEntity line)
    {
        line.Color = EntityColor.Explicit(new CadColor(0, 0, 255));
        line.LineWeight = 50;
        line.LinetypeScale = 2;
        return line;
    }

    [Fact]
    public void Match_copies_layer_colour_linetype_and_weight_in_one_undo_step()
    {
        var source = Add(Styled(new LineEntity(_red, new Vector2(0, 0), new Vector2(10, 0))));
        source.Linetype = _document.FindLinetype("CENTER");
        Add(new CircleEntity(_plain, new Vector2(30, 0), 5));
        Add(new LineEntity(_plain, new Vector2(0, 20), new Vector2(10, 20)));

        Type("CORRISPONDENZA");
        Click(5, 0);
        Click(35, 0);
        Click(5, 20);
        Type("", "");

        var targets = _document.ModelSpace.Where(e => !ReferenceEquals(e, source)).ToList();
        Assert.Equal(2, targets.Count);
        Assert.All(targets, t =>
        {
            Assert.Same(_red, t.Layer);
            Assert.Equal(source.Color, t.Color);
            Assert.Same(source.Linetype, t.Linetype);
            Assert.Equal(50, t.LineWeight);
            Assert.Equal(2, t.LinetypeScale);
        });
        // La forma non cambia.
        Assert.Contains(_document.ModelSpace.OfType<CircleEntity>(), c => c.Center == new Vector2(30, 0) && c.Radius == 5);

        _editor.RunCommand("ANNULLA");
        Assert.All(_document.ModelSpace.Where(e => !ReferenceEquals(e, source)), t => Assert.Same(_plain, t.Layer));
    }

    [Fact]
    public void Match_settings_limit_what_is_copied()
    {
        Add(Styled(new LineEntity(_red, new Vector2(0, 0), new Vector2(10, 0))));
        var target = Add(new LineEntity(_plain, new Vector2(0, 20), new Vector2(10, 20)));

        Type("MA", "Impostazioni", "Colore, Spessore");
        Assert.Equal(PropertyMask.Color | PropertyMask.LineWeight, PropertyCommands.MatchMask);
        Click(5, 0);
        Click(5, 20);
        Type("", "");

        var changed = _document.ModelSpace.OfType<LineEntity>().Single(l => l.Start.Y == 20);
        Assert.NotSame(target, changed);
        Assert.Same(_plain, changed.Layer);
        Assert.Equal(EntityColor.Explicit(new CadColor(0, 0, 255)), changed.Color);
        Assert.Equal(50, changed.LineWeight);
        Assert.Equal(1, changed.LinetypeScale);
    }

    [Fact]
    public void Match_copies_text_style_and_height_between_texts()
    {
        var titles = _document.GetOrAddTextStyle("TITOLI");
        titles.FontFamily = "Times New Roman";
        Add(new TextEntity(_plain, new Vector2(0, 0), 5, "TITOLO") { Style = titles });
        Add(new TextEntity(_plain, new Vector2(0, 20), 2.5, "nota"));

        Type("CORRISPONDENZA");
        Click(1, 1);
        Click(0.5, 20.5);
        Type("", "");

        var note = _document.ModelSpace.OfType<TextEntity>().Single(t => t.Value == "nota");
        Assert.Same(titles, note.Style);
        Assert.Equal(5, note.Height, 9);
        Assert.Equal(new Vector2(0, 20), note.Position);
    }

    [Fact]
    public void Match_copies_hatch_pattern_and_dimension_style()
    {
        var square = new List<PolylineVertex> { new(new Vector2(0, 0)), new(new Vector2(10, 0)), new(new Vector2(10, 10)), new(new Vector2(0, 10)) };
        var other = new List<PolylineVertex> { new(new Vector2(20, 0)), new(new Vector2(30, 0)), new(new Vector2(30, 10)), new(new Vector2(20, 10)) };
        var source = Add(Hatching.Create(_plain, [square], "ANSI37", 2, Math.PI / 4));
        Add(Hatching.Create(_plain, [other], "ANSI31", 1, 0));
        var properties = PropertyTools.Capture(source);
        var target = _document.ModelSpace.OfType<HatchEntity>().Single(h => h.PatternName == "ANSI31");
        Assert.Equal(1, PropertyTools.Apply(_editor, [target], properties, PropertyMask.All));
        var changed = _document.ModelSpace.OfType<HatchEntity>().Single(h => !ReferenceEquals(h, source));
        Assert.Equal("ANSI37", changed.PatternName);
        Assert.Equal(2, changed.PatternScale, 9);
        Assert.Equal(Math.PI / 4, changed.PatternAngle, 9);
        Assert.Equal(20, changed.Loops[0][0].Position.X);

        var big = _document.GetOrAddDimensionStyle("GRANDE");
        big.TextHeight = 7;
        Type("QLINEARE", "0,-20", "50,-20", "25,-30");
        var dimension = _document.ModelSpace.OfType<DimensionEntity>().Single();
        Assert.Equal(1, PropertyTools.Apply(_editor, [dimension], new PropertySet { DimensionStyle = "GRANDE" }, PropertyMask.All));
        Assert.Same(big, _document.ModelSpace.OfType<DimensionEntity>().Single().Style);
    }

    [Fact]
    public void Copied_properties_paste_into_another_drawing_and_create_the_layer()
    {
        var source = Add(Styled(new LineEntity(_red, new Vector2(0, 0), new Vector2(10, 0))));
        _editor.Selection.Add([source]);
        _editor.RunCommand("COPIAPROP");
        Assert.NotNull(PropertyCommands.Clipboard);
        Assert.Equal("ROSSO", PropertyCommands.Clipboard!.Layer);

        var other = new CadDocument();
        var otherEditor = new Editor(other) { SnapEnabled = false };
        var circle = new CircleEntity(other.GetOrAddLayer("0"), Vector2.Zero, 3);
        other.Edit("test", e => e.Add(circle));
        otherEditor.Selection.Add([circle]);
        otherEditor.RunCommand("INCOLLAPROP");

        var pasted = Assert.Single(other.ModelSpace);
        Assert.Equal("ROSSO", pasted.Layer.Name);
        Assert.Same(other.FindLayer("ROSSO"), pasted.Layer);
        Assert.Equal(new CadColor(255, 0, 0), pasted.Layer.Color);
        Assert.Equal(50, pasted.LineWeight);
    }

    [Fact]
    public void Paste_without_copied_properties_explains_what_to_do()
    {
        var messages = new List<string>();
        _editor.Message += messages.Add;
        _editor.RunCommand("INCOLLAPROP");
        Assert.Contains(messages, m => m.Contains("COPIAPROP", StringComparison.Ordinal));
    }

    [Fact]
    public void Saved_properties_survive_on_file_and_apply_to_selection_or_become_current()
    {
        var source = Add(Styled(new LineEntity(_red, new Vector2(0, 0), new Vector2(10, 0))));
        _editor.Selection.Add([source]);
        Type("SALVAPROP", "Assi rossi");
        _editor.Selection.Clear();

        // Un'altra sessione rilegge il file.
        PropertyCommands.Library = new PropertyLibrary(Path.Combine(_folder, "proprieta.json"));
        var saved = Assert.Single(PropertyCommands.Library.Sets);
        Assert.Equal("Assi rossi", saved.Name);
        Assert.Equal("ROSSO", saved.Layer);

        var target = Add(new LineEntity(_plain, new Vector2(0, 20), new Vector2(10, 20)));
        _editor.Selection.Add([target]);
        Type("APPLICAPROP", "assi rossi");
        var changed = _document.ModelSpace.OfType<LineEntity>().Single(l => l.Start.Y == 20);
        Assert.Same(_red, changed.Layer);
        Assert.Equal(50, changed.LineWeight);

        // Senza selezione diventano le proprietà dei nuovi oggetti.
        Type("APPLICAPROP", "Assi rossi");
        Assert.Same(_red, _editor.CurrentLayer);
        Assert.Equal(EntityColor.Explicit(new CadColor(0, 0, 255)), _editor.CurrentColor);
        Type("LINEA", "0,50", "10,50", "");
        Assert.Same(_red, _document.ModelSpace.OfType<LineEntity>().Single(l => l.Start.Y == 50).Layer);
    }

    [Fact]
    public void Current_properties_can_be_saved_and_replaced_by_name()
    {
        _editor.CurrentLayer = _red;
        _editor.CurrentLineWeight = 35;
        Type("SALVAPROP", "", "Base");
        Assert.Equal(35, PropertyCommands.Library.Find("base")!.LineWeight);

        _editor.CurrentLineWeight = 70;
        Type("SALVAPROP", "", "BASE");
        Assert.Equal(70, Assert.Single(PropertyCommands.Library.Sets).LineWeight);

        Assert.True(PropertyCommands.Library.Remove("Base"));
        Assert.Empty(new PropertyLibrary(Path.Combine(_folder, "proprieta.json")).Sets);
    }

    [Theory]
    [InlineData("Tutte", PropertyMask.All)]
    [InlineData("layer, col", PropertyMask.Layer | PropertyMask.Color)]
    [InlineData("Testo;Quota", PropertyMask.Text | PropertyMask.Dimension)]
    public void Mask_lists_are_read_by_prefix(string text, PropertyMask expected) => Assert.Equal(expected, PropertyCommands.ParseMask(text));
}

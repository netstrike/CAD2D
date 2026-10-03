using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public class AnnotationToolsTests
{
    private readonly CadDocument _document = new();
    private readonly Editor _editor;
    private readonly Layer _layer;

    public AnnotationToolsTests()
    {
        _editor = new Editor(_document) { SnapEnabled = false, PolarEnabled = false, TrackingEnabled = false };
        _layer = _document.GetOrAddLayer("0");
    }

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

    [Fact]
    public void Leader_with_text_forms_a_group_with_a_landing_and_moves_together()
    {
        Type("DIRETTRICE", "0,0", "10,10", "", "Foro M8", "");

        var leader = Assert.Single(_document.ModelSpace.OfType<LeaderEntity>());
        var text = Assert.Single(_document.ModelSpace.OfType<TextEntity>());
        Assert.Equal("Foro M8", text.Value);
        Assert.NotNull(leader.Group);
        Assert.Same(leader.Group, text.Group);
        Assert.Equal(AnnotationGroups.Leader, leader.Group!.Description);

        // L'ultimo tratto è inclinato: si aggiunge l'approdo orizzontale e il testo parte da lì.
        Assert.Equal(3, leader.Vertices.Count);
        Assert.Equal(10, leader.Vertices[2].Y, 9);
        Assert.True(leader.Vertices[2].X > 10);
        Assert.True(text.Position.X > leader.Vertices[2].X);
        Assert.Contains(leader.Explode(), p => p is SolidEntity);

        // Un clic sulla direttrice seleziona anche il testo.
        _editor.Click(new Vector2(5, 5), 0.5);
        Assert.Equal(2, _editor.Selection.Count);
    }

    [Fact]
    public void Continued_dimensions_share_the_line_and_baseline_dimensions_step_outwards()
    {
        Type("QLINEARE", "0,0", "10,0", "5,10");
        Type("QCONTINUA", "25,0", "40,0", "");
        var dimensions = _document.ModelSpace.OfType<DimensionEntity>().ToList();
        Assert.Equal(3, dimensions.Count);
        Assert.Equal(new Vector2(10, 0), dimensions[1].First);
        Assert.Equal(15, dimensions[1].Measurement, 9);
        Assert.Equal(new Vector2(25, 0), dimensions[2].First);
        Assert.All(dimensions, d => Assert.Equal(10, d.Location.Y, 9));

        Type("QBASE", "Seleziona");
        _editor.Click(new Vector2(0, 5), 0.5);
        Type("60,0", "");
        var baseline = _document.ModelSpace.OfType<DimensionEntity>().Last();
        Assert.Equal(Vector2.Zero, baseline.First);
        Assert.Equal(60, baseline.Measurement, 9);
        Assert.Equal(10 + _document.CurrentDimensionStyle.BaselineSpacing, baseline.Location.Y, 9);
    }

    [Fact]
    public void Ordinate_dimension_measures_from_the_origin_and_chooses_the_axis_from_the_leader()
    {
        Type("QCOORDINATA", "Origine", "10,10", "30,25", "30,40");
        var x = Assert.Single(_document.ModelSpace.OfType<DimensionEntity>());
        Assert.Equal(DimensionKind.Ordinate, x.Kind);
        Assert.True(x.OrdinateX);
        Assert.Equal(20, x.Measurement, 9);
        Assert.Equal("20", x.Text);

        Type("QCOORDINATA", "30,25", "50,27");
        var y = _document.ModelSpace.OfType<DimensionEntity>().Last();
        Assert.False(y.OrdinateX);
        Assert.Equal(15, y.Measurement, 9);
        Assert.Contains(y.Explode(), p => p is TextEntity { Value: "15" });
    }

    [Fact]
    public void Arc_length_dimension_measures_the_arc()
    {
        Add(new ArcEntity(_layer, Vector2.Zero, 10, 0, Math.PI / 2));
        Type("QARCO");
        _editor.Click(Vector2.FromPolar(10, Math.PI / 4), 0.5);
        Type("15,15");
        var dimension = Assert.Single(_document.ModelSpace.OfType<DimensionEntity>());
        Assert.Equal(DimensionKind.ArcLength, dimension.Kind);
        Assert.Equal(5 * Math.PI, dimension.Measurement, 9);
        Assert.StartsWith("⌒15,71", dimension.Text);
        Assert.Contains(dimension.Explode(), p => p is ArcEntity { Radius: > 20 });
    }

    [Fact]
    public void Center_mark_draws_two_center_lines_with_the_short_dash_in_the_middle()
    {
        Add(new CircleEntity(_layer, new Vector2(50, 50), 10));
        Type("SEGNOCENTRO");
        _editor.Click(new Vector2(60, 50), 0.5);
        Type("");

        var lines = _document.ModelSpace.OfType<LineEntity>().ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal("CENTER", l.Linetype?.Name));
        Assert.All(lines, l => Assert.Equal(new Vector2(50, 50), l.Segment.Midpoint));
        Assert.Same(lines[0].Group, lines[1].Group);
        // A metà della linea cade il centro del tratto corto del motivo.
        var half = lines[0].Segment.Length / 2;
        Assert.Equal(41.275, half / lines[0].LinetypeScale, 6);
    }

    [Fact]
    public void Center_line_runs_between_two_parallel_lines()
    {
        Add(new LineEntity(_layer, new Vector2(0, 0), new Vector2(100, 0)));
        Add(new LineEntity(_layer, new Vector2(110, 20), new Vector2(10, 20)));
        Type("ASSE");
        _editor.Click(new Vector2(50, 0), 0.5);
        _editor.Click(new Vector2(50, 20), 0.5);
        var axis = _document.ModelSpace.OfType<LineEntity>().Last();
        Assert.Equal(10, axis.Start.Y, 9);
        Assert.Equal(10, axis.End.Y, 9);
        Assert.Equal(10 - 2.5, Math.Min(axis.Start.X, axis.End.X), 9);
        Assert.Equal(100 + 2.5, Math.Max(axis.Start.X, axis.End.X), 9);
    }

    [Fact]
    public void Table_is_a_grid_of_lines_with_texts_and_cells_can_be_edited_later()
    {
        Type("TABELLA", "Colonne", "2", "Righe", "2", "0,100", "Pos.", "Descrizione", "1", "Fine");
        var lines = _document.ModelSpace.OfType<LineEntity>().ToList();
        Assert.Equal(6, lines.Count);
        var texts = _document.ModelSpace.OfType<TextEntity>().ToList();
        Assert.Equal(["Pos.", "Descrizione", "1"], texts.Select(t => t.Value));
        Assert.Equal(new Vector2(15, 100 - 3.75), texts[0].Position);
        var group = lines[0].Group!;
        Assert.Equal(AnnotationGroups.Table, group.Description);

        // Cella vuota in basso a destra: CELLA ci scrive un testo nuovo; poi si cambia una cella piena.
        Type("CELLA", "45,90", "Vite M8", "15,97", "Posizione", "");
        texts = _document.ModelSpace.OfType<TextEntity>().ToList();
        Assert.Contains(texts, t => t.Value == "Vite M8" && t.Position == new Vector2(45, 100 - 7.5 - 3.75) && t.Group == group);
        Assert.Contains(texts, t => t.Value == "Posizione");
        Assert.DoesNotContain(texts, t => t.Value == "Pos.");

        // Anche dopo uno spostamento della tabella le celle si ritrovano.
        Assert.NotNull(AnnotationTools.FindCell(_document, new Vector2(10, 95)));
        Assert.Null(AnnotationTools.FindCell(_document, new Vector2(80, 95)));
    }

    [Fact]
    public void Tolerance_frame_has_a_box_for_symbol_value_and_each_datum()
    {
        Type("TOLLERANZA", "Perp", "%%c0,05", "A B", "0,0");
        var texts = _document.ModelSpace.OfType<TextEntity>().Select(t => t.Value).ToList();
        Assert.Equal(["Ø0,05", "A", "B"], texts);
        var lines = _document.ModelSpace.OfType<LineEntity>().ToList();
        // Lati sopra, sotto e sinistro, quattro separatori, più le due linee del simbolo.
        Assert.Equal(3 + 4 + 2, lines.Count);
        Assert.All(_document.ModelSpace, e => Assert.Equal(AnnotationGroups.Tolerance, e.Group?.Description));
    }

    [Fact]
    public void Revision_cloud_is_a_closed_polyline_of_outward_arcs()
    {
        Type("NUVOLA", "0,0", "40,20");
        var cloud = Assert.Single(_document.ModelSpace.OfType<PolylineEntity>());
        Assert.True(cloud.IsClosed);
        Assert.True(cloud.Vertices.Count >= 10);
        Assert.All(cloud.Vertices, v => Assert.True(v.Bulge > 0));
        // Gli archi sporgono verso l'esterno: l'ingombro supera il rettangolo.
        Assert.True(cloud.Bounds.Max.X > 40 && cloud.Bounds.Min.Y < 0);

        var circle = Add(new CircleEntity(_layer, new Vector2(100, 0), 10));
        Type("NUVOLA", "Oggetto");
        _editor.Click(new Vector2(110, 0), 0.5);
        Assert.DoesNotContain(circle, _document.ModelSpace);
        Assert.Equal(2, _document.ModelSpace.OfType<PolylineEntity>().Count());
    }

    [Fact]
    public void Text_styles_are_used_by_new_texts_and_a_fixed_height_is_not_asked()
    {
        Type("STILETESTO", "Nuovo", "Titoli", "Carattere", "Times New Roman", "Altezza", "5", "Inclinazione", "15", "");
        var style = _document.CurrentTextStyle;
        Assert.Equal("Titoli", style.Name);
        Assert.Equal(5, style.Height);

        Type("TESTO", "0,0", "", "Tavola 1", "");
        var text = Assert.Single(_document.ModelSpace.OfType<TextEntity>());
        Assert.Same(style, text.Style);
        Assert.Equal(5, text.Height);

        var rows = PropertySheet.For(_document, [text]);
        var row = rows.Single(r => r.Name == "Stile");
        Assert.Contains(TextStyle.DefaultName, row.Choices);
        Assert.True(PropertySheet.Apply(_editor, [text], row.Definition, TextStyle.DefaultName));
        Assert.Equal(TextStyle.DefaultName, _document.ModelSpace.OfType<TextEntity>().Single().Style!.Name);
    }

    [Fact]
    public void Aliases_resolve_to_their_command_and_symbol_codes_are_decoded()
    {
        Assert.Equal("CERCHIO", _editor.ResolveCommand("c"));
        Assert.Equal("ASSE", _editor.ResolveCommand("CL"));
        Assert.Null(_editor.ResolveCommand("CERC"));

        Type("TESTO", "0,0", "2.5", "0", "%%c8 %%p0,1 45%%d", "");
        Assert.Equal("Ø8 ±0,1 45°", Assert.Single(_document.ModelSpace.OfType<TextEntity>()).Value);
    }
}

using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public class CommandTests
{
    private const double Aperture = 0.5;

    private readonly CadDocument _document = new();
    private readonly Editor _editor;

    public CommandTests()
    {
        _editor = new Editor(_document) { SnapEnabled = false };
    }

    private void Type(params string[] inputs)
    {
        foreach (var input in inputs)
        {
            _editor.SubmitText(input);
        }
    }

    [Fact]
    public void Line_with_typed_coordinates_creates_connected_segments()
    {
        Type("L", "0,0", "@10,0", "@0,5", "");

        var lines = _document.ModelSpace.OfType<LineEntity>().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(new Vector2(10, 0), lines[0].End);
        Assert.Equal(new Vector2(10, 5), lines[1].End);
        Assert.False(_editor.IsCommandActive);
    }

    [Fact]
    public void Line_close_option_returns_to_first_point()
    {
        Type("LINEA", "0,0", "10,0", "10,10", "c");

        var lines = _document.ModelSpace.OfType<LineEntity>().ToList();
        Assert.Equal(3, lines.Count);
        Assert.Equal(Vector2.Zero, lines[^1].End);
    }

    [Fact]
    public void Line_undo_option_removes_last_segment_only()
    {
        Type("L", "0,0", "10,0", "20,0", "a", "");
        Assert.Single(_document.ModelSpace);
    }

    [Fact]
    public void Polar_input_uses_degrees()
    {
        Type("L", "0,0", "@10<90", "");
        Assert.True(_document.ModelSpace.OfType<LineEntity>().Single().End.IsAlmostEqual(new Vector2(0, 10)));
    }

    [Fact]
    public void Direct_distance_follows_the_cursor_direction()
    {
        Type("L", "0,0");
        _editor.Hover(new Vector2(0, -3), Aperture);
        Type("7", "");
        Assert.True(_document.ModelSpace.OfType<LineEntity>().Single().End.IsAlmostEqual(new Vector2(0, -7)));
    }

    [Fact]
    public void Circle_by_radius_and_by_diameter()
    {
        Type("C", "5,5", "2");
        Type("C", "0,0", "d", "10");

        var circles = _document.ModelSpace.OfType<CircleEntity>().ToList();
        Assert.Equal(2, circles[0].Radius);
        Assert.Equal(5, circles[1].Radius);
    }

    [Fact]
    public void Circle_radius_can_be_picked_with_the_mouse()
    {
        Type("C", "0,0");
        _editor.Click(new Vector2(3, 4), Aperture);
        Assert.Equal(5, _document.ModelSpace.OfType<CircleEntity>().Single().Radius, 9);
    }

    [Fact]
    public void Arc_through_three_points()
    {
        Type("A", "10,0", "0,10", "-10,0");
        var arc = _document.ModelSpace.OfType<ArcEntity>().Single();
        Assert.True(arc.Center.IsAlmostEqual(Vector2.Zero, 1e-9));
        Assert.True(arc.Arc.Midpoint.IsAlmostEqual(new Vector2(0, 10), 1e-9));
    }

    [Fact]
    public void Rectangle_is_a_closed_polyline()
    {
        Type("REC", "0,0", "20,10");
        var rectangle = _document.ModelSpace.OfType<PolylineEntity>().Single();
        Assert.True(rectangle.IsClosed);
        Assert.Equal(new Vector2(20, 10), rectangle.Bounds.Max);
    }

    [Fact]
    public void Polyline_close_option()
    {
        Type("PL", "0,0", "10,0", "10,10", "C");
        var polyline = _document.ModelSpace.OfType<PolylineEntity>().Single();
        Assert.True(polyline.IsClosed);
        Assert.Equal(3, polyline.Vertices.Count);
    }

    [Fact]
    public void Move_with_window_selection_then_undo_and_redo()
    {
        Type("L", "0,0", "10,0", "");
        Type("M");
        // Finestra da sinistra a destra che contiene la linea, poi Invio per confermare.
        _editor.Click(new Vector2(-1, -1), Aperture);
        _editor.Click(new Vector2(11, 1), Aperture);
        Type("", "0,0", "5,5");

        var moved = _document.ModelSpace.OfType<LineEntity>().Single();
        Assert.Equal(new Vector2(5, 5), moved.Start);

        _document.History.Undo();
        Assert.Equal(Vector2.Zero, _document.ModelSpace.OfType<LineEntity>().Single().Start);
        _document.History.Redo();
        Assert.Equal(new Vector2(5, 5), _document.ModelSpace.OfType<LineEntity>().Single().Start);
    }

    [Fact]
    public void Window_selection_ignores_partially_inside_entities_but_crossing_takes_them()
    {
        Type("L", "0,0", "10,0", "");
        _editor.Click(new Vector2(-1, -1), Aperture);
        _editor.Click(new Vector2(5, 1), Aperture);
        Assert.Equal(0, _editor.Selection.Count);

        _editor.Click(new Vector2(5, 1), Aperture);
        _editor.Click(new Vector2(-1, -1), Aperture);
        Assert.Equal(1, _editor.Selection.Count);
    }

    [Fact]
    public void Copy_creates_multiple_copies_until_enter()
    {
        Type("C", "0,0", "1");
        _editor.Click(new Vector2(1, 0), Aperture);
        Type("CO", "0,0", "10,0", "20,0", "");
        Assert.Equal(3, _document.ModelSpace.OfType<CircleEntity>().Count());
    }

    [Fact]
    public void Rotate_by_typed_angle()
    {
        Type("L", "0,0", "10,0", "");
        _editor.Selection.Add(_document.ModelSpace);
        Type("RO", "0,0", "90");
        Assert.True(_document.ModelSpace.OfType<LineEntity>().Single().End.IsAlmostEqual(new Vector2(0, 10), 1e-9));
    }

    [Fact]
    public void Erase_uses_existing_selection()
    {
        Type("L", "0,0", "10,0", "");
        _editor.Click(new Vector2(5, 0.1), Aperture);
        Type("E");
        Assert.Empty(_document.ModelSpace);
        Assert.Equal(0, _editor.Selection.Count);
    }

    [Fact]
    public void Escape_cancels_without_changes()
    {
        Type("L", "0,0");
        _editor.Cancel();
        Assert.False(_editor.IsCommandActive);
        Assert.Empty(_document.ModelSpace);
    }

    [Fact]
    public void Enter_repeats_the_last_command()
    {
        Type("C", "0,0", "1", "", "5,5", "2");
        Assert.Equal(2, _document.ModelSpace.Count);
    }

    [Fact]
    public void Starting_a_command_cancels_the_running_one()
    {
        Type("L", "0,0");
        _editor.RunCommand("C");
        Type("5,5", "1");
        Assert.Single(_document.ModelSpace.OfType<CircleEntity>());
        Assert.Empty(_document.ModelSpace.OfType<LineEntity>());
    }

    [Fact]
    public void Command_names_typed_during_a_prompt_are_rejected()
    {
        Type("L", "0,0", "C");
        Assert.True(_editor.IsCommandActive);
        Assert.Empty(_document.ModelSpace);
    }

    [Fact]
    public void Unknown_command_reports_an_error()
    {
        var messages = new List<string>();
        _editor.Message += messages.Add;
        Type("PIPPO");
        Assert.Contains(messages, m => m.Contains("sconosciuto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Clicks_snap_to_endpoints_when_snap_is_on()
    {
        Type("L", "0,0", "10,0", "");
        _editor.SnapEnabled = true;
        Type("L");
        _editor.Click(new Vector2(9.8, 0.2), Aperture);
        Type("10,10", "");
        Assert.Equal(new Vector2(10, 0), _document.ModelSpace.OfType<LineEntity>().Last().Start);
    }

    [Fact]
    public void Ortho_constrains_clicks_to_horizontal_or_vertical()
    {
        _editor.OrthoEnabled = true;
        Type("L", "0,0");
        _editor.Click(new Vector2(10, 2), Aperture);
        Type("");
        Assert.Equal(new Vector2(10, 0), _document.ModelSpace.OfType<LineEntity>().Single().End);
    }

    [Fact]
    public void Grip_edit_moves_a_line_endpoint()
    {
        Type("L", "0,0", "10,0", "");
        var line = _document.ModelSpace.OfType<LineEntity>().Single();
        _editor.StartGripEdit(line, 2);
        Type("10,5");
        Assert.Equal(new Vector2(10, 5), _document.ModelSpace.OfType<LineEntity>().Single().End);
    }

    [Fact]
    public void Preview_follows_the_cursor()
    {
        Type("L", "0,0");
        _editor.Hover(new Vector2(3, 3), Aperture);
        var preview = Assert.IsType<LineEntity>(Assert.Single(_editor.GetPreview()));
        Assert.Equal(new Vector2(3, 3), preview.End);
    }

    [Fact]
    public void Document_tracks_unsaved_changes()
    {
        Assert.False(_document.IsModified);
        Type("L", "0,0", "1,0", "");
        Assert.True(_document.IsModified);
        _document.MarkSaved();
        Assert.False(_document.IsModified);
        _document.History.Undo();
        Assert.True(_document.IsModified);
        _document.History.Redo();
        Assert.False(_document.IsModified);
    }
}

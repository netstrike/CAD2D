using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public class ModifyTests
{
    private const double Aperture = 0.5;

    private readonly CadDocument _document = new();
    private readonly Editor _editor;
    private readonly Layer _layer;

    public ModifyTests()
    {
        _editor = new Editor(_document) { SnapEnabled = false };
        _layer = _document.GetOrAddLayer("0");
    }

    private void Type(params string[] inputs)
    {
        foreach (var input in inputs)
        {
            _editor.SubmitText(input);
        }
    }

    private T Add<T>(T entity) where T : Entity
    {
        _document.Edit("test", e => e.Add(entity));
        return entity;
    }

    private void Click(double x, double y) => _editor.Click(new Vector2(x, y), Aperture);

    private static void AssertNear(Vector2 expected, Vector2 actual, double tolerance = 1e-6) =>
        Assert.True(expected.IsAlmostEqual(actual, tolerance), $"atteso {expected}, ottenuto {actual}");

    [Fact]
    public void Trim_removes_the_middle_of_a_line_between_two_crossings()
    {
        Add(new LineEntity(_layer, new Vector2(0, 0), new Vector2(10, 0)));
        Add(new LineEntity(_layer, new Vector2(3, -5), new Vector2(3, 5)));
        Add(new LineEntity(_layer, new Vector2(7, -5), new Vector2(7, 5)));

        Type("TR");
        Click(5, 0);
        Type("");

        var horizontal = _document.ModelSpace.OfType<LineEntity>().Where(l => Tolerance.IsZero(l.Start.Y) && Tolerance.IsZero(l.End.Y)).ToList();
        Assert.Equal(2, horizontal.Count);
        Assert.Contains(horizontal, l => l.End.IsAlmostEqual(new Vector2(3, 0)));
        Assert.Contains(horizontal, l => l.Start.IsAlmostEqual(new Vector2(7, 0)));
    }

    [Fact]
    public void Trim_end_of_line_keeps_the_other_side()
    {
        var line = Add(new LineEntity(_layer, new Vector2(0, 0), new Vector2(10, 0)));
        Add(new LineEntity(_layer, new Vector2(4, -5), new Vector2(4, 5)));

        var result = Modify.Trim(line, new Vector2(8, 0), Modify.Boundaries(_document.ModelSpace, line))!;

        var piece = Assert.IsType<LineEntity>(Assert.Single(result));
        AssertNear(new Vector2(0, 0), piece.Start);
        AssertNear(new Vector2(4, 0), piece.End);
    }

    [Fact]
    public void Trim_circle_between_two_lines_leaves_an_arc()
    {
        var circle = Add(new CircleEntity(_layer, Vector2.Zero, 5));
        Add(new LineEntity(_layer, new Vector2(-10, 3), new Vector2(10, 3)));

        // Clic in alto: si toglie la calotta sopra la retta y = 3.
        var result = Modify.Trim(circle, new Vector2(0, 5), Modify.Boundaries(_document.ModelSpace, circle))!;

        var arc = Assert.IsType<ArcEntity>(Assert.Single(result));
        Assert.Equal(5, arc.Radius, 9);
        AssertNear(new Vector2(-4, 3), arc.Arc.StartPoint);
        AssertNear(new Vector2(4, 3), arc.Arc.EndPoint);
        AssertNear(new Vector2(0, -5), arc.Arc.Midpoint);
    }

    [Fact]
    public void Trim_closed_polyline_opens_it()
    {
        var square = Add(new PolylineEntity(_layer,
            [new(new Vector2(0, 0)), new(new Vector2(10, 0)), new(new Vector2(10, 10)), new(new Vector2(0, 10))], isClosed: true));
        Add(new LineEntity(_layer, new Vector2(5, -5), new Vector2(5, 5)));

        // La linea taglia solo il lato in basso in un punto: servono due tagli, quindi niente da fare.
        Assert.Null(Modify.Trim(square, new Vector2(8, 0), Modify.Boundaries(_document.ModelSpace, square)));

        Add(new LineEntity(_layer, new Vector2(-5, 5), new Vector2(15, 5)));
        var result = Modify.Trim(square, new Vector2(10, 2), Modify.Boundaries(_document.ModelSpace, square))!;

        var open = Assert.IsType<PolylineEntity>(Assert.Single(result));
        Assert.False(open.IsClosed);
        AssertNear(new Vector2(10, 5), open.Vertices[0].Position);
        AssertNear(new Vector2(5, 0), open.Vertices[^1].Position);
        Assert.Equal(5, open.Vertices.Count);
    }

    [Fact]
    public void Trim_without_boundaries_deletes_the_entity()
    {
        var line = Add(new LineEntity(_layer, new Vector2(0, 0), new Vector2(10, 0)));
        Assert.Empty(Modify.Trim(line, new Vector2(5, 0), [])!);
    }

    [Fact]
    public void Extend_line_reaches_the_nearest_boundary()
    {
        Add(new LineEntity(_layer, new Vector2(0, 0), new Vector2(5, 0)));
        Add(new LineEntity(_layer, new Vector2(8, -5), new Vector2(8, 5)));
        Add(new LineEntity(_layer, new Vector2(12, -5), new Vector2(12, 5)));

        Type("EX");
        Click(4.8, 0);
        Type("");

        var extended = _document.ModelSpace.OfType<LineEntity>().Single(l => Tolerance.IsZero(l.Start.Y) && Tolerance.IsZero(l.End.Y));
        AssertNear(new Vector2(8, 0), extended.End);
        AssertNear(new Vector2(0, 0), extended.Start);
    }

    [Fact]
    public void Extend_start_of_arc_to_a_line()
    {
        // Quarto di cerchio da 0° a 90°, esteso all'indietro fino alla retta y = -3.
        var arc = Add(new ArcEntity(_layer, Vector2.Zero, 5, 0, Math.PI / 2));
        Add(new LineEntity(_layer, new Vector2(-10, -3), new Vector2(10, -3)));

        var result = Assert.IsType<ArcEntity>(Modify.Extend(arc, new Vector2(5, 0.5), Modify.Boundaries(_document.ModelSpace, arc)));

        AssertNear(new Vector2(4, -3), result.Arc.StartPoint);
        AssertNear(new Vector2(0, 5), result.Arc.EndPoint);
    }

    [Fact]
    public void Offset_line_circle_and_closed_polyline()
    {
        var line = new LineEntity(_layer, new Vector2(0, 0), new Vector2(10, 0));
        var up = Assert.IsType<LineEntity>(Modify.Offset(line, 2, new Vector2(3, 1)));
        AssertNear(new Vector2(0, 2), up.Start);
        AssertNear(new Vector2(10, 2), up.End);

        var circle = new CircleEntity(_layer, Vector2.Zero, 5);
        Assert.Equal(3, Assert.IsType<CircleEntity>(Modify.Offset(circle, 2, new Vector2(1, 0))).Radius, 9);
        Assert.Equal(7, Assert.IsType<CircleEntity>(Modify.Offset(circle, 2, new Vector2(9, 0))).Radius, 9);
        Assert.Null(Modify.Offset(circle, 6, new Vector2(1, 0)));

        var square = new PolylineEntity(_layer,
            [new(new Vector2(0, 0)), new(new Vector2(10, 0)), new(new Vector2(10, 10)), new(new Vector2(0, 10))], isClosed: true);
        var outside = Assert.IsType<PolylineEntity>(Modify.Offset(square, 1, new Vector2(5, -3)));
        Assert.True(outside.IsClosed);
        Assert.Equal(4, outside.Vertices.Count);
        Assert.Equal(new BoundingBox(new Vector2(-1, -1), new Vector2(11, 11)), outside.Bounds);
    }

    [Fact]
    public void Offset_polyline_with_arc_keeps_tangency()
    {
        // Asola: due tratti dritti e due semicerchi di raggio 2.
        var slot = new PolylineEntity(_layer,
        [
            new(new Vector2(0, 0)), new(new Vector2(10, 0), 1), new(new Vector2(10, 4)), new(new Vector2(0, 4), 1),
        ], isClosed: true);

        var inside = Assert.IsType<PolylineEntity>(Modify.Offset(slot, 1, new Vector2(5, 1.5)));

        Assert.Equal(4, inside.Vertices.Count);
        AssertNear(new Vector2(0, 1), inside.Vertices[0].Position);
        AssertNear(new Vector2(10, 1), inside.Vertices[1].Position);
        Assert.Equal(1, inside.Vertices[1].Bulge, 9);
        var bounds = inside.Bounds;
        AssertNear(new Vector2(-1, 1), bounds.Min);
        AssertNear(new Vector2(11, 3), bounds.Max);
    }

    [Fact]
    public void Offset_command_repeats_with_the_remembered_distance()
    {
        Add(new LineEntity(_layer, new Vector2(0, 0), new Vector2(10, 0)));

        Type("O", "2.5");
        Click(5, 0);
        Click(5, 1);
        Type("");

        Assert.Equal(2.5, _editor.Settings.OffsetDistance);
        Assert.Contains(_document.ModelSpace.OfType<LineEntity>(), l => l.Start.IsAlmostEqual(new Vector2(0, 2.5)));
        Assert.False(_editor.IsCommandActive);
    }

    [Fact]
    public void Fillet_two_perpendicular_lines_adds_a_tangent_arc()
    {
        Add(new LineEntity(_layer, new Vector2(0, 0), new Vector2(10, 0)));
        Add(new LineEntity(_layer, new Vector2(12, 2), new Vector2(12, 10)));

        Type("F", "r", "3");
        Click(5, 0);
        Click(12, 6);

        var arc = Assert.Single(_document.ModelSpace.OfType<ArcEntity>());
        Assert.Equal(3, arc.Radius, 9);
        AssertNear(new Vector2(9, 3), arc.Center);
        var lines = _document.ModelSpace.OfType<LineEntity>().ToList();
        Assert.Contains(lines, l => l.End.IsAlmostEqual(new Vector2(9, 0)));
        Assert.Contains(lines, l => l.Start.IsAlmostEqual(new Vector2(12, 3)) && l.End.IsAlmostEqual(new Vector2(12, 10)));
        Assert.Equal(3, _editor.Settings.FilletRadius);
    }

    [Fact]
    public void Fillet_with_zero_radius_joins_lines_at_the_corner()
    {
        var a = new LineEntity(_layer, new Vector2(0, 0), new Vector2(8, 0));
        var b = new LineEntity(_layer, new Vector2(10, 2), new Vector2(10, 9));

        var (result, error) = Modify.Fillet(a, new Vector2(2, 0), b, new Vector2(10, 5), 0);

        Assert.Null(error);
        AssertNear(new Vector2(10, 0), result!.First.End);
        AssertNear(new Vector2(10, 0), result.Second.Start);
        Assert.Null(result.Joint);
    }

    [Fact]
    public void Fillet_of_parallel_lines_is_refused()
    {
        var a = new LineEntity(_layer, new Vector2(0, 0), new Vector2(10, 0));
        var b = new LineEntity(_layer, new Vector2(0, 5), new Vector2(10, 5));
        Assert.NotNull(Modify.Fillet(a, Vector2.Zero, b, new Vector2(0, 5), 1).Error);
    }

    [Fact]
    public void Fillet_polyline_rounds_every_corner_of_a_rectangle()
    {
        var rect = new PolylineEntity(_layer,
            [new(new Vector2(0, 0)), new(new Vector2(20, 0)), new(new Vector2(20, 10)), new(new Vector2(0, 10))], isClosed: true);

        var (result, filleted, skipped) = Modify.FilletPolyline(rect, 2);

        Assert.Equal(4, filleted);
        Assert.Equal(0, skipped);
        Assert.Equal(8, result.Vertices.Count);
        Assert.Equal(new BoundingBox(new Vector2(0, 0), new Vector2(20, 10)), result.Bounds);
        // Rettangolo antiorario: archi antiorari di 90°, bulge tan(22.5°).
        // Ogni spigolo diventa due vertici; il primo porta l'arco.
        Assert.Equal(Math.Tan(Math.PI / 8), result.Vertices[2].Bulge, 9);
        AssertNear(new Vector2(18, 0), result.Vertices[2].Position);
        AssertNear(new Vector2(20, 2), result.Vertices[3].Position);
    }

    [Fact]
    public void Chamfer_cuts_the_corner_with_a_line()
    {
        var a = new LineEntity(_layer, new Vector2(0, 0), new Vector2(10, 0));
        var b = new LineEntity(_layer, new Vector2(10, 0), new Vector2(10, 10));

        var (result, error) = Modify.Chamfer(a, new Vector2(2, 0), b, new Vector2(10, 8), 2, 3);

        Assert.Null(error);
        var joint = Assert.IsType<LineEntity>(result!.Joint);
        AssertNear(new Vector2(8, 0), joint.Start);
        AssertNear(new Vector2(10, 3), joint.End);
    }

    [Fact]
    public void Mirror_keeps_originals_by_default()
    {
        Add(new LineEntity(_layer, new Vector2(1, 1), new Vector2(3, 2)));

        Type("MI");
        _editor.Selection.Add(_document.ModelSpace);
        Type("");
        Type("0,0", "0,10", "");

        Assert.Equal(2, _document.ModelSpace.Count);
        Assert.Contains(_document.ModelSpace.OfType<LineEntity>(), l => l.Start.IsAlmostEqual(new Vector2(-1, 1)));
    }

    [Fact]
    public void Mirror_with_yes_replaces_originals()
    {
        Add(new LineEntity(_layer, new Vector2(1, 1), new Vector2(3, 2)));
        _editor.Selection.Add(_document.ModelSpace);

        Type("SPECCHIA", "0,0", "10,0", "s");

        var line = Assert.Single(_document.ModelSpace.OfType<LineEntity>());
        AssertNear(new Vector2(1, -1), line.Start);
    }

    [Fact]
    public void Scale_by_factor_and_by_reference()
    {
        var circle = Add(new CircleEntity(_layer, new Vector2(2, 0), 1));
        _editor.Selection.Add([circle]);
        Type("SC", "0,0", "2");
        var scaled = Assert.Single(_document.ModelSpace.OfType<CircleEntity>());
        Assert.Equal(2, scaled.Radius, 9);
        AssertNear(new Vector2(4, 0), scaled.Center);

        _editor.Selection.Add([scaled]);
        Type("SCALA", "0,0", "r", "4", "1");
        Assert.Equal(0.5, Assert.Single(_document.ModelSpace.OfType<CircleEntity>()).Radius, 9);
    }

    [Fact]
    public void Rectangular_array_creates_grid_of_copies()
    {
        var circle = Add(new CircleEntity(_layer, Vector2.Zero, 1));
        _editor.Selection.Add([circle]);

        Type("AR", "", "2", "3", "10", "5");

        var circles = _document.ModelSpace.OfType<CircleEntity>().ToList();
        Assert.Equal(6, circles.Count);
        Assert.Contains(circles, c => c.Center.IsAlmostEqual(new Vector2(10, 10)));
    }

    [Fact]
    public void Polar_array_spreads_copies_on_full_turn()
    {
        var circle = Add(new CircleEntity(_layer, new Vector2(10, 0), 1));
        _editor.Selection.Add([circle]);

        Type("SERIE", "p", "0,0", "4", "");

        var centers = _document.ModelSpace.OfType<CircleEntity>().Select(c => c.Center).ToList();
        Assert.Equal(4, centers.Count);
        Assert.Contains(centers, c => c.IsAlmostEqual(new Vector2(0, 10), 1e-9));
        Assert.Contains(centers, c => c.IsAlmostEqual(new Vector2(-10, 0), 1e-9));
    }

    [Fact]
    public void Explode_insert_moves_layer_zero_to_insert_layer()
    {
        var block = new BlockDefinition("B");
        block.Entities.Add(new LineEntity(_layer, Vector2.Zero, new Vector2(1, 0)) { Color = EntityColor.ByBlock });
        var red = _document.GetOrAddLayer("ROSSO");
        var insert = Add(new InsertEntity(red, block)
        {
            Transform = Matrix2D.Translation(new Vector2(5, 5)),
            Color = EntityColor.Explicit(new CadColor(0, 255, 0)),
        });
        _editor.Selection.Add([insert]);

        Type("X");

        var line = Assert.IsType<LineEntity>(Assert.Single(_document.ModelSpace));
        Assert.Same(red, line.Layer);
        Assert.Equal(new CadColor(0, 255, 0), line.Color.Value);
        AssertNear(new Vector2(6, 5), line.End);
    }

    [Fact]
    public void Explode_polyline_gives_lines_and_arcs()
    {
        var polyline = Add(new PolylineEntity(_layer, [new(new Vector2(0, 0)), new(new Vector2(10, 0), 1), new(new Vector2(10, 4))], isClosed: false));
        _editor.Selection.Add([polyline]);

        Type("ESPLODI");

        Assert.Single(_document.ModelSpace.OfType<LineEntity>());
        var arc = Assert.Single(_document.ModelSpace.OfType<ArcEntity>());
        Assert.Equal(2, arc.Radius, 9);
    }

    [Fact]
    public void Keywords_ignore_accents()
    {
        Assert.Equal("Sì", InputParser.MatchKeyword("si", ["Sì", "No"]));
        Assert.Equal("Sì", InputParser.MatchKeyword("S", ["Sì", "No"]));
    }
}

using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

/// <summary>Tracciamento polare ed ETrack, griglia, inserimento rapido, selezione ciclica e appunti.</summary>
public class DraftingAidTests
{
    private readonly CadDocument _document = new();
    private readonly Editor _editor;
    private readonly Layer _layer;
    private long _now;

    public DraftingAidTests()
    {
        _editor = new Editor(_document) { SnapEnabled = false, Clock = () => _now };
        _layer = _document.GetOrAddLayer("0");
    }

    private T Add<T>(T entity) where T : Entity
    {
        _document.Edit("test", e => e.Add(entity));
        return entity;
    }

    private void StartLine(Vector2 from)
    {
        _editor.RunCommand("LINEA");
        _editor.Click(from, 0.5);
    }

    [Fact]
    public void Polar_tracking_pulls_the_cursor_onto_the_nearest_angle()
    {
        _editor.PolarIncrementDegrees = 45;
        StartLine(Vector2.Zero);

        _editor.Hover(new Vector2(10, 10.3), 0.5);

        Assert.NotNull(_editor.CurrentTracking);
        Assert.Equal(10.15, _editor.Cursor.X, 9);
        Assert.Equal(10.15, _editor.Cursor.Y, 9);
        Assert.StartsWith("Polare:", _editor.CurrentTracking!.Label);
        Assert.Contains("< 45°", _editor.CurrentTracking.Label);
    }

    [Fact]
    public void Far_from_any_guide_the_cursor_is_free()
    {
        StartLine(Vector2.Zero);
        _editor.Hover(new Vector2(10, 4), 0.5);
        Assert.Null(_editor.CurrentTracking);
        Assert.Equal(new Vector2(10, 4), _editor.Cursor);
    }

    [Fact]
    public void Resting_on_a_snap_acquires_a_point_for_alignment_tracking()
    {
        Add(new CircleEntity(_layer, new Vector2(50, 30), 0.8));
        _editor.SnapEnabled = true;
        _editor.SnapModes = SnapModes.Center;
        _editor.PolarEnabled = false;
        StartLine(Vector2.Zero);

        _editor.Hover(new Vector2(50.2, 30.1), 1);
        _now += Editor.AcquireMilliseconds + 10;
        _editor.Hover(new Vector2(50.1, 30.2), 1);
        Assert.Contains(new Vector2(50, 30), _editor.AcquiredPoints);

        // Lontano dal cerchio ma allineato in verticale al centro: il cursore va sulla guida.
        _editor.Hover(new Vector2(50.4, 80), 1);
        Assert.Equal(50, _editor.Cursor.X, 9);
        Assert.StartsWith("Allineamento:", _editor.CurrentTracking!.Label);

        _editor.Click(_editor.Cursor, 1);
        var line = Assert.Single(_document.ModelSpace.OfType<LineEntity>());
        Assert.Equal(new Vector2(50, 80), line.End);
        Assert.Empty(_editor.AcquiredPoints);
    }

    [Fact]
    public void Grid_snap_rounds_the_point()
    {
        _editor.GridSnapEnabled = true;
        _editor.GridSpacing = 5;
        _editor.PolarEnabled = false;
        StartLine(new Vector2(1.2, 0.4));
        _editor.Click(new Vector2(13, 8.9), 0.5);
        var line = Assert.Single(_document.ModelSpace.OfType<LineEntity>());
        Assert.Equal(new Vector2(0, 0), line.Start);
        Assert.Equal(new Vector2(15, 10), line.End);
    }

    [Fact]
    public void Quick_input_locks_distance_then_takes_the_angle()
    {
        StartLine(new Vector2(10, 10));
        Assert.True(_editor.WantsPointFromBase);
        Assert.True(_editor.LockInput("20"));
        Assert.Equal(20, _editor.LockedDistance);

        _editor.Hover(new Vector2(40, 12), 0.5);
        Assert.Equal(20, Vector2.Distance(new Vector2(10, 10), _editor.Cursor), 9);

        _editor.SubmitText("90");
        var line = Assert.Single(_document.ModelSpace.OfType<LineEntity>());
        Assert.Equal(10, line.End.X, 9);
        Assert.Equal(30, line.End.Y, 9);
        Assert.Null(_editor.LockedDistance);
    }

    [Fact]
    public void Quick_input_with_locked_distance_accepts_the_cursor_direction_on_enter()
    {
        _editor.PolarEnabled = false;
        StartLine(Vector2.Zero);
        _editor.LockInput("10");
        _editor.Hover(new Vector2(0, 50), 0.5);
        _editor.SubmitText("");
        var line = Assert.Single(_document.ModelSpace.OfType<LineEntity>());
        Assert.Equal(0, line.End.X, 9);
        Assert.Equal(10, line.End.Y, 9);
    }

    [Fact]
    public void Clicking_again_on_overlapping_objects_cycles_the_selection()
    {
        var a = Add(new LineEntity(_layer, new Vector2(0, 0), new Vector2(10, 0)));
        var b = Add(new LineEntity(_layer, new Vector2(0, 0), new Vector2(10, 0.1)));

        _editor.Click(new Vector2(5, 0.02), 0.5);
        var first = Assert.Single(_editor.Selection.Items);
        _editor.Click(new Vector2(5, 0.02), 0.5);
        var second = Assert.Single(_editor.Selection.Items);
        Assert.NotSame(first, second);
        Assert.Contains(a, new[] { first, second });
        Assert.Contains(b, new[] { first, second });
    }

    [Fact]
    public void Select_all_skips_locked_layers()
    {
        var locked = _document.GetOrAddLayer("BLOCCATO");
        locked.IsLocked = true;
        Add(new LineEntity(_layer, Vector2.Zero, new Vector2(1, 0)));
        Add(new CircleEntity(_layer, Vector2.Zero, 2));
        Add(new CircleEntity(locked, Vector2.Zero, 3));

        _editor.SelectAll();
        Assert.Equal(2, _editor.Selection.Count);
    }

    [Fact]
    public void Copy_and_paste_moves_from_the_lower_left_corner()
    {
        ClipboardCommands.Clear();
        var circle = Add(new CircleEntity(_layer, new Vector2(10, 10), 2));
        _editor.Selection.Add([circle]);
        _editor.RunCommand("COPIACLIP");
        Assert.Equal(1, ClipboardCommands.Count);

        _editor.RunCommand("INCOLLACLIP");
        _editor.Click(new Vector2(100, 100), 0.5);
        var circles = _document.ModelSpace.OfType<CircleEntity>().ToList();
        Assert.Equal(2, circles.Count);
        Assert.Contains(circles, c => c.Center == new Vector2(102, 102));
    }

    [Fact]
    public void Paste_into_another_drawing_brings_layer_and_block()
    {
        ClipboardCommands.Clear();
        var axes = _document.GetOrAddLayer("ASSI");
        axes.Color = new CadColor(255, 0, 0);
        var block = _document.GetOrAddBlock("VITE");
        block.Entities.Add(new CircleEntity(axes, Vector2.Zero, 1));
        var insert = Add(new InsertEntity(axes, block) { Transform = Matrix2D.Translation(new Vector2(5, 5)) });
        _editor.Selection.Add([insert]);
        _editor.RunCommand("TAGLIACLIP");
        Assert.Empty(_document.ModelSpace);

        var other = new CadDocument();
        var otherEditor = new Editor(other) { SnapEnabled = false };
        otherEditor.RunCommand("INCOLLACLIP");
        otherEditor.Click(new Vector2(0, 0), 0.5);

        var pasted = Assert.Single(other.ModelSpace.OfType<InsertEntity>());
        Assert.Equal("ASSI", pasted.Layer.Name);
        Assert.Same(other.FindLayer("ASSI"), pasted.Layer);
        Assert.Equal(new CadColor(255, 0, 0), pasted.Layer.Color);
        Assert.NotSame(block, pasted.Block);
        Assert.Contains(other.Blocks, b => b.Name == "VITE");
        Assert.Same(other.FindLayer("ASSI"), pasted.Block.Entities[0].Layer);
    }

    [Fact]
    public void Grid_command_sets_spacing_and_shows_grid()
    {
        _editor.RunCommand("GRIGLIA");
        _editor.SubmitText("2.5");
        Assert.Equal(2.5, _editor.GridSpacing);
        Assert.True(_editor.GridVisible);
    }
}

public class MeasureCommandTests
{
    private readonly CadDocument _document = new();
    private readonly Editor _editor;
    private readonly List<string> _messages = [];

    public MeasureCommandTests()
    {
        _editor = new Editor(_document) { SnapEnabled = false, PolarEnabled = false };
        _editor.Message += _messages.Add;
    }

    [Fact]
    public void Distance_reports_length_angle_and_deltas()
    {
        _editor.RunCommand("DISTANZA");
        _editor.SubmitText("0,0");
        _editor.SubmitText("3,4");
        Assert.Contains("Distanza = 5, Angolo = 53.1301°, Delta X = 3, Delta Y = 4", _messages);
    }

    [Fact]
    public void Area_by_points_and_of_a_circle()
    {
        _editor.RunCommand("AREA");
        foreach (var point in new[] { "0,0", "10,0", "10,5", "0,5", "" })
        {
            _editor.SubmitText(point);
        }

        Assert.Contains("Area = 50, Perimetro = 30", _messages);

        _document.Edit("test", e => e.Add(new CircleEntity(_document.GetOrAddLayer("0"), Vector2.Zero, 1)));
        _editor.RunCommand("AREA");
        _editor.SubmitText("O");
        _editor.Click(new Vector2(1, 0), 0.1);
        Assert.Contains("Area = 3.1416, Circonferenza = 6.2832", _messages);
    }

    [Fact]
    public void Id_reports_coordinates()
    {
        _editor.RunCommand("ID");
        _editor.SubmitText("12.5,-3");
        Assert.Contains("X = 12.5  Y = -3", _messages);
    }
}

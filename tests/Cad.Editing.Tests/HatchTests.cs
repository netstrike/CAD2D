using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public class HatchTests
{
    private readonly CadDocument _document = new();
    private readonly Editor _editor;
    private readonly Layer _layer;

    public HatchTests()
    {
        _editor = new Editor(_document) { SnapEnabled = false };
        _layer = _document.GetOrAddLayer("0");
        _document.Edit("test", e =>
        {
            e.Add(new PolylineEntity(_layer,
                [new(new Vector2(0, 0)), new(new Vector2(100, 0)), new(new Vector2(100, 60)), new(new Vector2(0, 60))], isClosed: true));
            e.Add(new CircleEntity(_layer, new Vector2(30, 30), 10));
            e.Add(new CircleEntity(_layer, new Vector2(70, 30), 10));
        });
    }

    [Fact]
    public void Point_between_outline_and_holes_hatches_with_islands()
    {
        _editor.SubmitText("H");
        _editor.SubmitText("50,50");
        _editor.SubmitText("");

        var hatch = Assert.Single(_document.ModelSpace.OfType<HatchEntity>());
        Assert.Equal(3, hatch.Loops.Count);
        Assert.Equal("ANSI31", hatch.PatternName);
        Assert.False(_editor.IsCommandActive);
    }

    [Fact]
    public void Point_inside_a_hole_hatches_only_the_hole_with_chosen_pattern()
    {
        _editor.SubmitText("TRATTEGGIO");
        _editor.SubmitText("m");
        _editor.SubmitText("solid");
        _editor.SubmitText("70,30");
        _editor.SubmitText("");

        var hatch = Assert.Single(_document.ModelSpace.OfType<HatchEntity>());
        Assert.True(hatch.IsSolid);
        Assert.Single(hatch.Loops);
        Assert.Equal(new BoundingBox(new Vector2(60, 20), new Vector2(80, 40)), hatch.Bounds);
    }

    [Fact]
    public void Point_outside_everything_creates_nothing()
    {
        _editor.SubmitText("H");
        _editor.SubmitText("500,500");
        _editor.SubmitText("");

        Assert.Empty(_document.ModelSpace.OfType<HatchEntity>());
    }

    [Fact]
    public void Pattern_scale_and_angle_are_applied()
    {
        var hatch = Hatching.Create(_layer, [Hatching.ClosedLoop(_document.ModelSpace[1])!], "ANSI31", 2, Math.PI / 4);

        var line = Assert.Single(hatch.PatternLines);
        Assert.Equal(Math.PI / 2, line.Angle, 9);
        Assert.Equal(6.35, line.Offset.Length, 9);
    }

    [Fact]
    public void Color_and_linetype_commands_set_current_style_for_new_entities()
    {
        _editor.SubmitText("COL");
        _editor.SubmitText("rosso");
        _editor.SubmitText("LT");
        _editor.SubmitText("dashed");
        _editor.SubmitText("L");
        _editor.SubmitText("0,0");
        _editor.SubmitText("10,0");
        _editor.SubmitText("");

        var line = Assert.Single(_document.ModelSpace.OfType<LineEntity>());
        Assert.Equal(new CadColor(255, 0, 0), line.Color.Value);
        Assert.Equal("DASHED", line.Linetype?.Name);
    }

    [Fact]
    public void Color_command_with_selection_changes_selected_entities()
    {
        var circle = _document.ModelSpace.OfType<CircleEntity>().First();
        _editor.Selection.Add([circle]);

        _editor.SubmitText("COLORE");
        _editor.SubmitText("verde");

        var changed = Assert.Single(_editor.Selection.Items);
        Assert.Equal(new CadColor(0, 255, 0), changed.Color.Value);
        Assert.DoesNotContain(circle, _document.ModelSpace);
        Assert.Equal(ColorSource.ByLayer, _editor.CurrentColor.Source);

        _editor.SubmitText("U");
        Assert.Contains(circle, _document.ModelSpace);
    }
}

using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public class AnnotationTests
{
    private const double Aperture = 0.5;

    private readonly CadDocument _document = new();
    private readonly Editor _editor;
    private readonly Layer _layer;

    public AnnotationTests()
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

    [Fact]
    public void Linear_dimension_is_horizontal_when_placed_above()
    {
        Type("DLI", "0,0", "100,30", "50,50");
        var dimension = Assert.Single(_document.ModelSpace.OfType<DimensionEntity>());
        Assert.Equal(0, dimension.Rotation);
        Assert.Equal(100, dimension.Measurement, 9);
        Assert.Equal("100", dimension.Text);
    }

    [Fact]
    public void Linear_dimension_is_vertical_when_placed_aside()
    {
        Type("QLINEARE", "0,0", "100,30", "130,10");
        var dimension = Assert.Single(_document.ModelSpace.OfType<DimensionEntity>());
        Assert.Equal(Math.PI / 2, dimension.Rotation, 9);
        Assert.Equal(30, dimension.Measurement, 9);
    }

    [Fact]
    public void Aligned_dimension_measures_true_length_with_comma()
    {
        Type("DAL", "0,0", "3,4.5", "-5,5");
        var dimension = Assert.Single(_document.ModelSpace.OfType<DimensionEntity>());
        Assert.Equal(Math.Sqrt(9 + 20.25), dimension.Measurement, 9);
        Assert.Equal("5,41", dimension.Text);
    }

    [Fact]
    public void Text_override_keeps_measurement_placeholder()
    {
        Type("DLI", "0,0", "20,0", "T", "<> h7", "10,10");
        var dimension = Assert.Single(_document.ModelSpace.OfType<DimensionEntity>());
        Assert.Equal("20 h7", dimension.Text);
    }

    [Fact]
    public void Radius_and_diameter_dimensions_of_a_circle()
    {
        Add(new CircleEntity(_layer, new Vector2(0, 0), 12.5));
        Type("DRA");
        Click(12.5, 0);
        Type("20,20");
        Type("DDI");
        Click(0, 12.5);
        Type("-20,20");

        var dimensions = _document.ModelSpace.OfType<DimensionEntity>().ToList();
        Assert.Equal("R12,5", dimensions.Single(d => d.Kind == DimensionKind.Radius).Text);
        Assert.Equal("Ø25", dimensions.Single(d => d.Kind == DimensionKind.Diameter).Text);
    }

    [Fact]
    public void Angular_dimension_between_two_lines()
    {
        Add(new LineEntity(_layer, new Vector2(0, 0), new Vector2(100, 0)));
        Add(new LineEntity(_layer, new Vector2(0, 0), new Vector2(50, 50)));
        Type("DAN");
        Click(80, 0);
        Click(40, 40);
        Type("60,20");

        var dimension = Assert.Single(_document.ModelSpace.OfType<DimensionEntity>());
        Assert.Equal(DimensionKind.Angular, dimension.Kind);
        Assert.Equal(45, dimension.Measurement, 9);
        Assert.Equal("45°", dimension.Text);
    }

    [Fact]
    public void Exploded_dimension_has_lines_arrows_and_text()
    {
        Type("DLI", "0,0", "100,0", "50,20");
        var parts = Assert.Single(_document.ModelSpace.OfType<DimensionEntity>()).Explode();
        Assert.Equal(3, parts.OfType<LineEntity>().Count());
        Assert.Equal(2, parts.OfType<SolidEntity>().Count());
        Assert.Equal("100", Assert.Single(parts.OfType<TextEntity>()).Value);
    }

    [Fact]
    public void Explode_command_turns_a_dimension_into_plain_entities()
    {
        Type("DLI", "0,0", "100,0", "50,20");
        var dimension = Assert.Single(_document.ModelSpace.OfType<DimensionEntity>());
        _editor.Selection.Add([dimension]);
        Type("COL", "Rosso");
        _editor.Selection.Add(_document.ModelSpace.OfType<DimensionEntity>().ToList());
        Type("X");

        Assert.Empty(_document.ModelSpace.OfType<DimensionEntity>());
        Assert.Equal("100", Assert.Single(_document.ModelSpace.OfType<TextEntity>()).Value);
        Assert.All(_document.ModelSpace, e => Assert.Equal(EntityColor.Explicit(new CadColor(255, 0, 0)), e.Color));
    }

    [Fact]
    public void Dimension_follows_moved_geometry_grips()
    {
        Type("DLI", "0,0", "100,0", "50,20");
        var dimension = Assert.Single(_document.ModelSpace.OfType<DimensionEntity>());
        var moved = (DimensionEntity)dimension.WithGripMoved(1, new Vector2(80, 0));
        Assert.Equal(80, moved.Measurement, 9);
        Assert.Equal(100, dimension.Measurement, 9);
    }

    [Fact]
    public void Text_command_writes_one_entity_per_line()
    {
        Type("DT", "10,10", "5", "0", "Prima riga", "Seconda riga", "");
        var texts = _document.ModelSpace.OfType<TextEntity>().OrderByDescending(t => t.Position.Y).ToList();
        Assert.Equal(["Prima riga", "Seconda riga"], texts.Select(t => t.Value));
        Assert.Equal(5, texts[0].Height);
        Assert.True(texts[1].Position.Y < 10);
        Assert.False(_editor.IsCommandActive);
    }

    [Fact]
    public void Edit_text_proposes_the_old_value()
    {
        Add(new TextEntity(_layer, new Vector2(0, 0), 5, "Vecchio"));
        Type("ED");
        Click(1, 1);
        Assert.Equal("Vecchio", _editor.SuggestedInput);
        Type("Nuovo");
        Type("");
        Assert.Equal("Nuovo", Assert.Single(_document.ModelSpace.OfType<TextEntity>()).Value);
    }

    [Theory]
    [InlineData(50, 50, 0)]
    [InlineData(150, 10, Math.PI / 2)]
    [InlineData(50, -40, 0)]
    public void Automatic_rotation_depends_on_cursor(double x, double y, double expected)
    {
        Assert.Equal(expected, AnnotationCommands.AutomaticRotation(new Vector2(0, 0), new Vector2(100, 30), new Vector2(x, y)), 9);
    }
}

using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public class DrawingScaleTests
{
    private readonly CadDocument _document = new();
    private readonly Editor _editor;
    private readonly Layer _layer;

    public DrawingScaleTests()
    {
        _editor = new Editor(_document) { SnapEnabled = false, PolarEnabled = false, TrackingEnabled = false };
        _layer = _document.GetOrAddLayer("0");
    }

    private void Type(params string[] inputs)
    {
        foreach (var input in inputs)
        {
            _editor.SubmitText(input);
        }
    }

    [Theory]
    [InlineData("1:2", 2.0)]
    [InlineData("2:1", 0.5)]
    [InlineData("1/50", 50.0)]
    [InlineData("10", 10.0)]
    [InlineData("1:0", null)]
    [InlineData("scala", null)]
    public void Scales_are_parsed_as_real_units_per_paper_unit(string text, double? expected)
    {
        Assert.Equal(expected, CadDocument.ParseScale(text));
    }

    [Fact]
    public void Scales_are_written_the_usual_way()
    {
        Assert.Equal("1:1", CadDocument.FormatScale(1));
        Assert.Equal("1:50", CadDocument.FormatScale(50));
        Assert.Equal("5:1", CadDocument.FormatScale(0.2));
    }

    [Fact]
    public void Assign_scales_annotations_but_keeps_geometry_and_dimension_values()
    {
        var line = new LineEntity(_layer, Vector2.Zero, new Vector2(100, 0));
        _document.Edit("test", e => e.Add(line));
        Type("QLINEARE", "0,0", "100,0", "50,10");
        Type("TABELLA", "Colonne", "1", "Righe", "1", "0,-20", "Pos.", "Fine");
        var tableText = _document.ModelSpace.OfType<TextEntity>().Single();
        var tableBox = _document.Members(tableText.Group!).Aggregate(BoundingBox.Empty, (b, x) => b.Union(x.Bounds));

        Type("SCALADISEGNO", "Assegna", "1:2");

        Assert.Equal(2, _document.DrawingScale, 9);
        Assert.Equal(2, _document.CurrentDimensionStyle.Scale, 9);
        Assert.Equal(2, _document.LinetypeScale, 9);
        Assert.Same(line, _document.ModelSpace[0]);
        var dimension = _document.ModelSpace.OfType<DimensionEntity>().Single();
        Assert.Equal("100", dimension.Text);

        // La tabella raddoppia attorno al suo angolo in alto a sinistra.
        var text = _document.ModelSpace.OfType<TextEntity>().Single();
        Assert.Equal(tableText.Height * 2, text.Height, 9);
        var box = _document.Members(text.Group!).Aggregate(BoundingBox.Empty, (b, x) => b.Union(x.Bounds));
        Assert.Equal(tableBox.Min.X, box.Min.X, 9);
        Assert.Equal(tableBox.Max.Y, box.Max.Y, 9);
        Assert.Equal(tableBox.Width * 2, box.Width, 9);

        // Un solo ANNULLA riporta tutto com'era.
        _document.History.Undo();
        Assert.Equal(1, _document.DrawingScale, 9);
        Assert.Equal(1, _document.LinetypeScale, 9);
        Assert.Equal(tableText.Height, _document.ModelSpace.OfType<TextEntity>().Single().Height, 9);
    }

    [Fact]
    public void Resize_shrinks_the_geometry_but_dimensions_still_show_real_values()
    {
        _document.Edit("test", e => e.Add(new CircleEntity(_layer, new Vector2(100, 0), 20)));
        Type("QLINEARE", "0,0", "100,0", "50,10");
        Type("TESTO", "0,50", "2.5", "0", "Pezzo", "");

        Type("SCALADISEGNO", "Ridimensiona", "1:2", "0,0");

        Assert.Equal(2, _document.DrawingScale, 9);
        Assert.Equal(1, _document.CurrentDimensionStyle.Scale, 9);
        Assert.Equal(2, _document.CurrentDimensionStyle.LinearFactor, 9);
        var circle = _document.ModelSpace.OfType<CircleEntity>().Single();
        Assert.Equal(new Vector2(50, 0), circle.Center);
        Assert.Equal(10, circle.Radius, 9);
        var dimension = _document.ModelSpace.OfType<DimensionEntity>().Single();
        Assert.Equal(50, dimension.Measurement, 9);
        Assert.Equal("100", dimension.Text);

        // Il testo segue il disegno ma resta alto 2,5.
        var text = _document.ModelSpace.OfType<TextEntity>().Single();
        Assert.Equal(new Vector2(0, 25), text.Position);
        Assert.Equal(2.5, text.Height, 9);

        // Le quote nuove misurano già in scala reale.
        Type("QLINEARE", "0,0", "10,0", "5,5");
        Assert.Equal("20", _document.ModelSpace.OfType<DimensionEntity>().Last().Text);

        _document.History.Undo();
        _document.History.Undo();
        Assert.Equal(1, _document.CurrentDimensionStyle.LinearFactor, 9);
        Assert.Equal(20, _document.ModelSpace.OfType<CircleEntity>().Single().Radius, 9);
    }

    [Fact]
    public void Resize_then_assign_back_keeps_the_drawing_scale_consistent()
    {
        DrawingScaleCommands.Resize(_editor, 5, Vector2.Zero);
        Assert.Equal(5, _document.DrawingScale, 9);
        DrawingScaleCommands.Assign(_editor, 10);
        Assert.Equal(10, _document.DrawingScale, 9);
        Assert.Equal(2, _document.CurrentDimensionStyle.Scale, 9);
        Assert.Equal(5, _document.CurrentDimensionStyle.LinearFactor, 9);
    }
}

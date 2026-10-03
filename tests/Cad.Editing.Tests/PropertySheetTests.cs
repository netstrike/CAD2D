using Cad.Document;
using Cad.Editing;
using Cad.Geometry;

namespace Cad.Editing.Tests;

public class PropertySheetTests
{
    private readonly CadDocument _document = new();
    private readonly Editor _editor;
    private readonly Layer _layer;

    public PropertySheetTests()
    {
        _editor = new Editor(_document) { SnapEnabled = false };
        _layer = _document.GetOrAddLayer("0");
    }

    private T Add<T>(T entity) where T : Entity
    {
        _document.Edit("test", e => e.Add(entity));
        return entity;
    }

    private PropertyRow Row(IReadOnlyList<Entity> entities, string name) =>
        PropertySheet.For(_document, entities).Single(r => r.Name == name);

    private bool Set(IReadOnlyList<Entity> entities, string name, string value) =>
        PropertySheet.Apply(_editor, entities, Row(entities, name), value);

    [Fact]
    public void Circle_shows_geometry_and_changes_radius_around_center()
    {
        var circle = Add(new CircleEntity(_layer, new Vector2(10, 20), 5));
        Assert.Equal("10", Row([circle], "Centro X").Value);
        Assert.Equal("10", Row([circle], "Diametro").Value);
        Assert.Equal(PropertyKind.ReadOnly, Row([circle], "Area").Kind);

        Assert.True(Set([circle], "Raggio", "7,5"));
        var changed = Assert.Single(_document.ModelSpace.OfType<CircleEntity>());
        Assert.Equal(7.5, changed.Radius, 9);
        Assert.Equal(new Vector2(10, 20), changed.Center);
        Assert.Same(changed, Assert.Single(_editor.Selection.Items));
    }

    [Fact]
    public void Different_values_show_as_mixed_and_change_together()
    {
        var a = Add(new LineEntity(_layer, new Vector2(0, 0), new Vector2(10, 0)));
        var b = Add(new LineEntity(_layer, new Vector2(0, 5), new Vector2(20, 5)));
        Assert.True(Row([a, b], "Lunghezza").IsMixed);
        Assert.Equal("0", Row([a, b], "Inizio X").Value);

        Assert.True(Set([a, b], "Lunghezza", "30"));
        Assert.All(_document.ModelSpace.OfType<LineEntity>(), l => Assert.Equal(30, l.Segment.Length, 9));
        _editor.SubmitText("U");
        Assert.Contains(_document.ModelSpace.OfType<LineEntity>(), l => Math.Abs(l.Segment.Length - 10) < 1e-9);
    }

    [Fact]
    public void Mixed_types_show_only_general_properties()
    {
        var line = Add(new LineEntity(_layer, Vector2.Zero, new Vector2(1, 0)));
        var circle = Add(new CircleEntity(_layer, Vector2.Zero, 1));
        var rows = PropertySheet.For(_document, [line, circle]);
        Assert.All(rows, r => Assert.Equal(PropertySheet.General, r.Category));
        Assert.Equal("Tutti (2)", PropertySheet.Describe([line, circle]));
    }

    [Fact]
    public void Layer_color_and_linetype_change_from_choices()
    {
        _document.GetOrAddLayer("ASSI");
        var line = Add(new LineEntity(_layer, Vector2.Zero, new Vector2(1, 0)));
        Assert.Contains("ASSI", Row([line], "Layer").Choices);

        Assert.True(Set([line], "Layer", "ASSI"));
        var moved = _document.ModelSpace.OfType<LineEntity>().Single();
        Assert.True(Set([moved], "Colore", "Rosso"));
        moved = _document.ModelSpace.OfType<LineEntity>().Single();
        Assert.True(Set([moved], "Tipo di linea", "CENTER"));

        var result = _document.ModelSpace.OfType<LineEntity>().Single();
        Assert.Equal("ASSI", result.Layer.Name);
        Assert.Equal(EntityColor.Explicit(new CadColor(255, 0, 0)), result.Color);
        Assert.Equal("CENTER", result.Linetype?.Name);
    }

    [Fact]
    public void Invalid_values_change_nothing()
    {
        var circle = Add(new CircleEntity(_layer, Vector2.Zero, 1));
        Assert.False(Set([circle], "Raggio", "-3"));
        Assert.False(Set([circle], "Raggio", "abc"));
        Assert.False(Set([circle], "Layer", "INESISTENTE"));
        Assert.Same(circle, Assert.Single(_document.ModelSpace));
    }

    [Fact]
    public void Text_content_height_and_rotation()
    {
        var text = Add(new TextEntity(_layer, new Vector2(5, 5), 2.5, "Uno\nDue"));
        Assert.Equal("Uno\\PDue", Row([text], "Contenuto").Value);
        Assert.True(Set([text], "Contenuto", "Nuovo"));
        var current = _document.ModelSpace.OfType<TextEntity>().Single();
        Assert.True(Set([current], "Altezza", "5"));
        current = _document.ModelSpace.OfType<TextEntity>().Single();
        Assert.True(Set([current], "Rotazione", "90"));

        var result = _document.ModelSpace.OfType<TextEntity>().Single();
        Assert.Equal("Nuovo", result.Value);
        Assert.Equal(5, result.Height, 9);
        Assert.Equal(Math.PI / 2, result.Rotation, 9);
        Assert.Equal(new Vector2(5, 5), result.Position);
    }

    [Fact]
    public void Insert_scale_and_rotation_around_insertion_point()
    {
        var block = _document.GetOrAddBlock("B");
        block.Entities.Add(new LineEntity(_layer, Vector2.Zero, new Vector2(1, 0)));
        var insert = Add(new InsertEntity(_layer, block) { Transform = Matrix2D.Translation(new Vector2(10, 10)) });
        Assert.True(Set([insert], "Scala", "2"));
        insert = _document.ModelSpace.OfType<InsertEntity>().Single();
        Assert.True(Set([insert], "Rotazione", "90"));
        insert = _document.ModelSpace.OfType<InsertEntity>().Single();

        Assert.Equal("2", Row([insert], "Scala").Value);
        Assert.Equal("90", Row([insert], "Rotazione").Value);
        Assert.True(insert.Transform.Transform(new Vector2(1, 0)).IsAlmostEqual(new Vector2(10, 12), 1e-9));
    }

    [Fact]
    public void Polyline_area_counts_arcs()
    {
        // Quadrato 10x10 con un lato sostituito da un semicerchio verso l'esterno.
        var polyline = Add(new PolylineEntity(_layer,
            [new(new Vector2(0, 0)), new(new Vector2(10, 0)), new(new Vector2(10, 10)), new(new Vector2(0, 10), 1)], true));
        var expected = 100 + Math.PI * 25 / 2;
        Assert.Equal(expected, double.Parse(Row([polyline], "Area").Value!, System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal(30 + Math.PI * 5, PropertySheet.Length(polyline), 9);
    }

    [Fact]
    public void Dimension_text_override()
    {
        _editor.SubmitText("DLI");
        _editor.SubmitText("0,0");
        _editor.SubmitText("40,0");
        _editor.SubmitText("20,10");
        var dimension = _document.ModelSpace.OfType<DimensionEntity>().Single();
        Assert.Equal("40", Row([dimension], "Misura").Value);
        Assert.True(Set([dimension], "Testo", "<> H7"));
        Assert.Equal("40 H7", _document.ModelSpace.OfType<DimensionEntity>().Single().Text);
    }

    [Fact]
    public void Lineweight_is_a_general_property()
    {
        var line = Add(new LineEntity(_layer, Vector2.Zero, new Vector2(1, 0)));
        Assert.Equal("DaLayer", Row([line], "Spessore linea").Value);
        Assert.True(Set([line], "Spessore linea", "0.50 mm"));
        Assert.Equal(50, Assert.Single(_document.ModelSpace).LineWeight);
        Assert.False(Set([_document.ModelSpace[0]], "Spessore linea", "0.33"));
    }
}

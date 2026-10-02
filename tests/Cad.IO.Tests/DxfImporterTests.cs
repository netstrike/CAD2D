using Cad.Document;
using Cad.Geometry;
using Cad.IO;
using Cad.Rendering;
using Acad = ACadSharp;

namespace Cad.IO.Tests;

public class DxfImporterTests
{
    private static readonly string DemoPath = Path.Combine(AppContext.BaseDirectory, "samples", "demo.dxf");

    [Fact]
    public void Demo_file_loads_all_layers_with_their_state()
    {
        var document = DxfImporter.Load(DemoPath).Document;

        Assert.Equal(new CadColor(0, 255, 255), document.FindLayer("FORI")!.Color);
        Assert.Equal(new CadColor(255, 0, 0), document.FindLayer("ASSI")!.Color);
        Assert.False(document.FindLayer("NASCOSTO")!.IsOn);
        Assert.True(document.FindLayer("CONTORNO")!.IsVisible);
    }

    [Fact]
    public void Demo_file_has_no_unsupported_entities()
    {
        var document = DxfImporter.Load(DemoPath).Document;
        Assert.Empty(document.UnsupportedEntities);
    }

    [Fact]
    public void Demo_file_contains_expected_entities()
    {
        var model = DxfImporter.Load(DemoPath).Document.ModelSpace;

        var outline = Assert.Single(model.OfType<PolylineEntity>());
        Assert.True(outline.IsClosed);
        Assert.Equal(6, outline.Vertices.Count);
        Assert.Equal(2, model.OfType<ArcEntity>().Count());
        Assert.Single(model.OfType<EllipseEntity>());
        Assert.Single(model.OfType<PolylinePathEntity>());
        Assert.Equal(4, model.OfType<TextEntity>().Count());
        Assert.Equal(4, model.OfType<InsertEntity>().Count());

        // Le quote diventano quote vere, con la grafica del file.
        var dimensions = model.OfType<DimensionEntity>().ToList();
        Assert.Equal(2, dimensions.Count);
        Assert.All(dimensions, d => Assert.NotNull(d.Graphics));
        Assert.Contains(dimensions, d => d.Kind == DimensionKind.Linear && Math.Abs(d.Measurement - 200) < 1e-9);
        Assert.Contains(dimensions, d => d.Kind == DimensionKind.Linear && Math.Abs(d.Measurement - 120) < 1e-9);
    }

    [Fact]
    public void Demo_outline_bounds_match_the_plate()
    {
        var outline = DxfImporter.Load(DemoPath).Document.ModelSpace.OfType<PolylineEntity>().Single();
        Assert.True(outline.Bounds.Min.IsAlmostEqual(Vector2.Zero, 1e-9));
        Assert.True(outline.Bounds.Max.IsAlmostEqual(new Vector2(200, 120), 1e-9));
    }

    [Fact]
    public void Rotated_and_scaled_insert_places_hole_correctly()
    {
        var insert = DxfImporter.Load(DemoPath).Document.ModelSpace.OfType<InsertEntity>()
            .Single(i => i.Block.Name == "FORO" && i.Transform.Transform(Vector2.Zero).IsAlmostEqual(new Vector2(100, 90)));
        // Scala 2, rotazione 45°: il punto (9, 0) del blocco finisce a 18 unità lungo la diagonale.
        var expected = new Vector2(100, 90) + Vector2.FromPolar(18, Math.PI / 4);
        Assert.True(insert.Transform.Transform(new Vector2(9, 0)).IsAlmostEqual(expected, 1e-9));
    }

    [Fact]
    public void Centered_text_keeps_alignment()
    {
        var text = DxfImporter.Load(DemoPath).Document.ModelSpace.OfType<TextEntity>().Single(t => t.Value == "Centrato");
        Assert.Equal(TextHorizontalAlignment.Center, text.HorizontalAlignment);
        Assert.Equal(TextVerticalAlignment.Middle, text.VerticalAlignment);
        Assert.True(text.Position.IsAlmostEqual(new Vector2(100, 60)));
    }

    [Fact]
    public void Arc_with_negative_normal_is_mirrored()
    {
        var source = new Acad.CadDocument();
        source.Entities.Add(new Acad.Entities.Arc
        {
            Center = new CSMath.XYZ(10, 0, 0),
            Radius = 5,
            StartAngle = 0,
            EndAngle = Math.PI / 2,
            Normal = new CSMath.XYZ(0, 0, -1),
        });

        var arc = Assert.Single(DxfImporter.Convert(source).ModelSpace.OfType<ArcEntity>());
        // Nel sistema dell'oggetto l'arco va da (15,0) a (10,5); visto da +Z diventa da (-10,5) a (-15,0).
        Assert.True(arc.Center.IsAlmostEqual(new Vector2(-10, 0)));
        Assert.True(arc.Arc.StartPoint.IsAlmostEqual(new Vector2(-10, 5), 1e-9));
        Assert.True(arc.Arc.EndPoint.IsAlmostEqual(new Vector2(-15, 0), 1e-9));
    }

    [Fact]
    public void Unsupported_entities_are_counted()
    {
        var source = new Acad.CadDocument();
        source.Entities.Add(new Acad.Entities.Ray());
        var document = DxfImporter.Convert(source);
        Assert.Equal(1, document.UnsupportedEntities["RAY"]);
    }

    [Fact]
    public void Written_and_reread_file_keeps_geometry()
    {
        var source = new Acad.CadDocument();
        source.Entities.Add(new Acad.Entities.Line { StartPoint = new CSMath.XYZ(1, 2, 0), EndPoint = new CSMath.XYZ(3, 4, 0) });
        source.Entities.Add(new Acad.Entities.Circle { Center = new CSMath.XYZ(5, 5, 0), Radius = 2.5 });
        using var stream = new MemoryStream();
        using (var writer = new Acad.IO.DxfWriter(stream, source, binary: false))
        {
            writer.Write();
        }

        // DxfWriter chiude lo stream: si rilegge dai byte scritti.
        var model = DxfImporter.Load(new MemoryStream(stream.ToArray())).Document.ModelSpace;
        var line = Assert.Single(model.OfType<LineEntity>());
        Assert.Equal(new Vector2(3, 4), line.End);
        Assert.Equal(2.5, Assert.Single(model.OfType<CircleEntity>()).Radius);
    }

    [Fact]
    public void Demo_scene_hides_layers_that_are_off()
    {
        var document = DxfImporter.Load(DemoPath).Document;
        var before = SceneBuilder.Build(document).EntityCount;
        document.FindLayer("NASCOSTO")!.IsOn = true;
        Assert.Equal(before + 1, SceneBuilder.Build(document).EntityCount);
    }
}

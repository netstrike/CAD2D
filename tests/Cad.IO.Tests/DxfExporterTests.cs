using Cad.Document;
using Cad.Geometry;
using Cad.IO;
using Acad = ACadSharp;

namespace Cad.IO.Tests;

public sealed class DxfExporterTests : IDisposable
{
    private static readonly string DemoPath = Path.Combine(AppContext.BaseDirectory, "samples", "demo.dxf");
    private readonly string _folder = Directory.CreateTempSubdirectory("cad2d-test").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string TempFile(string name) => Path.Combine(_folder, name);

    private static CadDocument Reload(CadDocument document, string path)
    {
        DxfExporter.Save(document, path);
        return DxfImporter.Load(path).Document;
    }

    private static Dictionary<string, int> CountByType(CadDocument document) =>
        document.ModelSpace.GroupBy(e => e.GetType().Name).ToDictionary(g => g.Key, g => g.Count());

    private static void AssertSameBox(BoundingBox expected, BoundingBox actual, double tolerance = 1e-6)
    {
        Assert.True(expected.Min.IsAlmostEqual(actual.Min, tolerance), $"Min atteso {expected.Min}, ottenuto {actual.Min}");
        Assert.True(expected.Max.IsAlmostEqual(actual.Max, tolerance), $"Max atteso {expected.Max}, ottenuto {actual.Max}");
    }

    [Fact]
    public void Unchanged_demo_reopens_identical()
    {
        var original = DxfImporter.Load(DemoPath).Document;
        var reloaded = Reload(DxfImporter.Load(DemoPath).Document, TempFile("copia.dxf"));

        Assert.Equal(CountByType(original), CountByType(reloaded));
        AssertSameBox(original.Bounds, reloaded.Bounds);
        Assert.Equal(original.Layers.Select(l => (l.Name, l.Color, l.IsOn)).OrderBy(x => x.Name), reloaded.Layers.Select(l => (l.Name, l.Color, l.IsOn)).OrderBy(x => x.Name));
    }

    [Fact]
    public void Moved_entities_keep_their_type_and_land_in_the_new_place()
    {
        var document = DxfImporter.Load(DemoPath).Document;
        var before = document.Bounds;
        var counts = CountByType(document);
        var delta = new Vector2(100, 50);
        document.Edit("SPOSTA", e =>
        {
            foreach (var entity in document.ModelSpace.ToList())
            {
                e.Replace(entity, entity.Transformed(Matrix2D.Translation(delta)));
            }
        });

        var reloaded = Reload(document, TempFile("spostato.dxf"));
        Assert.Equal(counts, CountByType(reloaded));
        AssertSameBox(new BoundingBox(before.Min + delta, before.Max + delta), reloaded.Bounds);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(37)]
    public void Rotated_drawing_reopens_where_it_was_drawn(double degrees)
    {
        var document = DxfImporter.Load(DemoPath).Document;
        var rotation = Matrix2D.Rotation(degrees * Math.PI / 180, new Vector2(10, 20));
        document.Edit("RUOTA", e =>
        {
            foreach (var entity in document.ModelSpace.ToList())
            {
                e.Replace(entity, entity.Transformed(rotation));
            }
        });
        // L'ordine delle entità nel file non è garantito: si confrontano ordinate per tipo e posizione.
        static List<(string Type, BoundingBox Box)> Sorted(CadDocument d) => d.ModelSpace
            .Select(e => (Type: e.GetType().Name, Box: e.Bounds))
            .OrderBy(x => x.Type).ThenBy(x => Math.Round(x.Box.Min.X, 6)).ThenBy(x => Math.Round(x.Box.Min.Y, 6)).ThenBy(x => Math.Round(x.Box.Max.X, 6))
            .ToList();
        var expected = Sorted(document);

        var reloaded = Reload(document, TempFile("ruotato-tutto.dxf"));
        var actual = Sorted(reloaded);
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Type, actual[i].Type);
            Assert.True(
                expected[i].Box.Min.IsAlmostEqual(actual[i].Box.Min, 1e-6) && expected[i].Box.Max.IsAlmostEqual(actual[i].Box.Max, 1e-6),
                $"{actual[i].Type}: atteso {expected[i].Box.Min}-{expected[i].Box.Max}, ottenuto {actual[i].Box.Min}-{actual[i].Box.Max}");
        }
    }

    [Fact]
    public void Rotated_insert_is_written_as_a_rotated_block_reference()
    {
        var document = DxfImporter.Load(DemoPath).Document;
        var hole = document.ModelSpace.OfType<InsertEntity>().First(i => i.Block.Name == "FORO");
        var rotation = Matrix2D.Rotation(Math.PI / 2, hole.Position);
        var expected = hole.Transformed(rotation).Bounds;
        document.Edit("RUOTA", e => e.Replace(hole, hole.Transformed(rotation)));

        var reloaded = Reload(document, TempFile("ruotato.dxf"));
        var match = reloaded.ModelSpace.OfType<InsertEntity>().Where(i => i.Block.Name == "FORO")
            .Count(i => i.Bounds.Min.IsAlmostEqual(expected.Min, 1e-6) && i.Bounds.Max.IsAlmostEqual(expected.Max, 1e-6));
        Assert.Equal(1, match);
    }

    [Fact]
    public void Deleted_entities_disappear_from_the_file()
    {
        var document = DxfImporter.Load(DemoPath).Document;
        var outline = document.ModelSpace.OfType<PolylineEntity>().Single();
        document.Edit("CANCELLA", e => e.Remove(outline));

        var reloaded = Reload(document, TempFile("cancellato.dxf"));
        Assert.Empty(reloaded.ModelSpace.OfType<PolylineEntity>());
        Assert.Equal(document.ModelSpace.Count, reloaded.ModelSpace.Count);
    }

    [Fact]
    public void Grip_edited_entity_is_rewritten_with_the_new_shape()
    {
        var document = DxfImporter.Load(DemoPath).Document;
        var outline = document.ModelSpace.OfType<PolylineEntity>().Single();
        document.Edit("STIRA", e => e.Replace(outline, outline.WithGripMoved(0, new Vector2(-10, -10))));

        var reloaded = Reload(document, TempFile("stirato.dxf"));
        Assert.Equal(new Vector2(-10, -10), reloaded.ModelSpace.OfType<PolylineEntity>().Single().Vertices[0].Position);
    }

    [Fact]
    public void New_drawing_round_trips()
    {
        var document = new CadDocument();
        var walls = document.GetOrAddLayer("MURI");
        walls.Color = new CadColor(255, 0, 0);
        document.Edit("DISEGNO", e =>
        {
            e.Add(new LineEntity(walls, new Vector2(0, 0), new Vector2(100, 0)));
            e.Add(new CircleEntity(walls, new Vector2(50, 50), 10) { Color = EntityColor.Explicit(new CadColor(12, 34, 56)) });
            e.Add(new ArcEntity(walls, Vector2.Zero, 5, 0, Math.PI));
            e.Add(new PolylineEntity(walls, [new PolylineVertex(Vector2.Zero, 1), new PolylineVertex(new Vector2(10, 0))], isClosed: true));
            e.Add(new TextEntity(walls, new Vector2(1, 2), 3.5, "Ciao %%c") { Rotation = 0.5 });
            e.Add(new TextEntity(walls, new Vector2(1, 20), 2.5, "riga uno\nriga due") { VerticalAlignment = TextVerticalAlignment.Top });
        });

        var reloaded = Reload(document, TempFile("nuovo.dxf"));
        Assert.Equal(new CadColor(255, 0, 0), reloaded.FindLayer("MURI")!.Color);
        Assert.Equal(new Vector2(100, 0), reloaded.ModelSpace.OfType<LineEntity>().Single().End);
        var circle = reloaded.ModelSpace.OfType<CircleEntity>().Single();
        Assert.Equal(10, circle.Radius);
        Assert.Equal(EntityColor.Explicit(new CadColor(12, 34, 56)), circle.Color);
        Assert.Equal(Math.PI, reloaded.ModelSpace.OfType<ArcEntity>().Single().EndAngle, 9);
        Assert.Equal(1, reloaded.ModelSpace.OfType<PolylineEntity>().Single().Vertices[0].Bulge, 9);
        var texts = reloaded.ModelSpace.OfType<TextEntity>().ToList();
        Assert.Contains(texts, t => t.Value == "Ciao Ø" && Math.Abs(t.Rotation - 0.5) < 1e-9);
        Assert.Contains(texts, t => t.Value == "riga uno\nriga due" && t.VerticalAlignment == TextVerticalAlignment.Top);
        Assert.False(document.IsModified);
    }

    [Fact]
    public void Unsupported_entities_survive_a_save()
    {
        var source = new Acad.CadDocument();
        source.Entities.Add(new Acad.Entities.Ray { StartPoint = new CSMath.XYZ(1, 1, 0), Direction = new CSMath.XYZ(1, 0, 0) });
        source.Entities.Add(new Acad.Entities.Line { StartPoint = CSMath.XYZ.Zero, EndPoint = new CSMath.XYZ(1, 0, 0) });
        var document = DxfImporter.Convert(source);
        document.Edit("CANCELLA", e => e.Remove(document.ModelSpace.Single()));

        var reloaded = Reload(document, TempFile("ray.dxf"));
        Assert.Equal(1, reloaded.UnsupportedEntities["RAY"]);
        Assert.Empty(reloaded.ModelSpace);
    }

    [Fact]
    public void Saving_twice_does_not_duplicate_entities()
    {
        var document = new CadDocument();
        document.Edit("LINEA", e => e.Add(new LineEntity(document.GetOrAddLayer("0"), Vector2.Zero, Vector2.UnitX)));
        DxfExporter.Save(document, TempFile("1.dxf"));
        var reloaded = Reload(document, TempFile("2.dxf"));
        Assert.Single(reloaded.ModelSpace);
    }

    [Fact]
    public void Layer_switched_off_stays_off()
    {
        var document = DxfImporter.Load(DemoPath).Document;
        document.FindLayer("FORI")!.IsOn = false;
        var reloaded = Reload(document, TempFile("layer.dxf"));
        Assert.False(reloaded.FindLayer("FORI")!.IsOn);
        Assert.Equal(new CadColor(0, 255, 255), reloaded.FindLayer("FORI")!.Color);
    }

    [Fact]
    public void Matrix_conversion_matches_acadsharp_transform()
    {
        var m = Matrix2D.Rotation(0.7, new Vector2(3, 4)) * Matrix2D.Translation(new Vector2(-5, 2));
        var transform = new CSMath.Transform(DxfExporter.ToMatrix4(m));
        var p = transform.ApplyTransform(new CSMath.XYZ(1.5, -2, 0));
        Assert.True(m.Transform(new Vector2(1.5, -2)).IsAlmostEqual(new Vector2(p.X, p.Y), 1e-9));
    }
}

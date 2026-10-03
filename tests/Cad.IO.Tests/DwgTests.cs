using Cad.Document;
using Cad.Geometry;
using Cad.IO;

namespace Cad.IO.Tests;

/// <summary>Traguardo della fase 4: un DWG salvato da CAD2D si riapre intatto.</summary>
public sealed class DwgTests : IDisposable
{
    private static readonly string DemoPath = Path.Combine(AppContext.BaseDirectory, "samples", "demo.dxf");
    private readonly string _folder = Directory.CreateTempSubdirectory("cad2d-dwg").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static Dictionary<string, int> CountByType(CadDocument document) =>
        document.ModelSpace.GroupBy(e => e.GetType().Name).ToDictionary(g => g.Key, g => g.Count());

    private static void AssertSameBox(BoundingBox expected, BoundingBox actual, double tolerance = 1e-6)
    {
        Assert.True(expected.Min.IsAlmostEqual(actual.Min, tolerance), $"Min atteso {expected.Min}, ottenuto {actual.Min}");
        Assert.True(expected.Max.IsAlmostEqual(actual.Max, tolerance), $"Max atteso {expected.Max}, ottenuto {actual.Max}");
    }

    [Fact]
    public void Sheet_built_with_commands_survives_a_dwg_round_trip()
    {
        var (document, _) = SampleDrawing.Build();
        var counts = CountByType(document);
        var bounds = document.Bounds;
        var path = Path.Combine(_folder, "tavola.dwg");
        CadFile.Save(document, path);
        Assert.False(document.IsModified);

        var reloaded = CadFile.Load(path);
        Assert.Empty(reloaded.Errors);
        Assert.Empty(reloaded.Document.UnsupportedEntities);
        Assert.Equal(counts, CountByType(reloaded.Document));
        AssertSameBox(bounds, reloaded.Document.Bounds, 1e-3);

        var model = reloaded.Document.ModelSpace;
        Assert.Contains(model.OfType<DimensionEntity>(), d => d.Text == "Ø40");
        Assert.Contains(model.OfType<TextEntity>(), t => t.Value == "FLANGIA 120x80" && t.Style?.FontFamily == "Times New Roman");
        Assert.Equal("CENTER", reloaded.Document.FindLayer("ASSI")!.Linetype.Name);
        Assert.Contains(reloaded.Document.Groups, g => g.Description == AnnotationGroups.Table);
        Assert.Equal(document.DrawingScale, reloaded.Document.DrawingScale, 9);
    }

    [Fact]
    public void Dxf_opened_and_saved_as_dwg_and_back_keeps_everything()
    {
        var original = CadFile.Load(DemoPath).Document;
        var dwg = Path.Combine(_folder, "demo.dwg");
        CadFile.Save(original, dwg);
        Assert.Equal(dwg, original.FilePath);

        var fromDwg = CadFile.Load(dwg).Document;
        var dxf = Path.Combine(_folder, "demo.dxf");
        CadFile.Save(fromDwg, dxf);
        var back = CadFile.Load(dxf).Document;

        var expected = CountByType(CadFile.Load(DemoPath).Document);
        Assert.Equal(expected, CountByType(fromDwg));
        Assert.Equal(expected, CountByType(back));
        AssertSameBox(original.Bounds, back.Bounds, 1e-3);
    }

    [Fact]
    public void Edited_dwg_saves_the_changes()
    {
        var path = Path.Combine(_folder, "pezzo.dwg");
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("CONTORNI");
        layer.Color = new CadColor(255, 0, 0);
        document.Edit("test", e =>
        {
            e.Add(new LineEntity(layer, Vector2.Zero, new Vector2(100, 0)));
            e.Add(new CircleEntity(layer, new Vector2(50, 25), 10));
        });
        CadFile.Save(document, path);

        var reopened = CadFile.Load(path).Document;
        var circle = reopened.ModelSpace.OfType<CircleEntity>().Single();
        reopened.Edit("SPOSTA", e => e.Replace(circle, circle.Transformed(Matrix2D.Translation(new Vector2(0, 10)))));
        reopened.Edit("CANCELLA", e => e.Remove(reopened.ModelSpace.OfType<LineEntity>().Single()));
        CadFile.Save(reopened, path);

        var final = CadFile.Load(path).Document;
        Assert.Equal(new Vector2(50, 35), Assert.Single(final.ModelSpace.OfType<CircleEntity>()).Center);
        Assert.Empty(final.ModelSpace.OfType<LineEntity>());
        Assert.Equal(new CadColor(255, 0, 0), final.FindLayer("CONTORNI")!.Color);
    }

    [Fact]
    public void Images_survive_a_dwg_round_trip()
    {
        var picture = Path.Combine(_folder, "rilievo.png");
        var bytes = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 1, 0x90, 0, 0, 0, 0xC8 }.CopyTo(bytes, 0);
        File.WriteAllBytes(picture, bytes);
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("IMMAGINI");
        document.Edit("test", e => e.Add(new ImageEntity(layer, picture, new Vector2(10, 5), new Vector2(400, 0), new Vector2(0, 200), 400, 200) { Opacity = 0.5 }));

        var path = Path.Combine(_folder, "ricalco.dwg");
        CadFile.Save(document, path);
        var image = Assert.Single(CadFile.Load(path).Document.ModelSpace.OfType<ImageEntity>());
        Assert.Equal(picture, image.Path);
        Assert.True(image.U.IsAlmostEqual(new Vector2(400, 0)));
        Assert.Equal(0.5, image.Opacity, 9);
    }

    [Fact]
    public void New_drawings_are_saved_as_dwg_2000_and_opened_dwgs_keep_their_version()
    {
        var path = Path.Combine(_folder, "nuovo.dwg");
        var document = new CadDocument();
        document.Edit("test", e => e.Add(new LineEntity(document.GetOrAddLayer("0"), Vector2.Zero, new Vector2(1, 1))));
        CadFile.Save(document, path);
        var reopened = CadFile.Load(path).Document;
        Assert.Equal(ACadSharp.ACadVersion.AC1015, ((DxfSource)reopened.Source!).DwgVersion);

        // Un DWG 2018 resta 2018 anche dopo una modifica.
        var source = (DxfSource)reopened.Source!;
        source.DwgVersion = ACadSharp.ACadVersion.AC1032;
        CadFile.Save(reopened, path);
        Assert.Equal(ACadSharp.ACadVersion.AC1032, ((DxfSource)CadFile.Load(path).Document.Source!).DwgVersion);
    }

    [Theory]
    [InlineData(ACadSharp.ACadVersion.AC1015, ACadSharp.ACadVersion.AC1015)]
    [InlineData(ACadSharp.ACadVersion.AC1021, ACadSharp.ACadVersion.AC1032)]
    [InlineData(ACadSharp.ACadVersion.AC1012, ACadSharp.ACadVersion.AC1015)]
    [InlineData(ACadSharp.ACadVersion.AC1027, ACadSharp.ACadVersion.AC1027)]
    public void Unwritable_dwg_versions_fall_back_to_a_writable_one(ACadSharp.ACadVersion read, ACadSharp.ACadVersion written)
    {
        Assert.Equal(written, CadFile.WritableDwgVersion(read));
    }

    [Fact]
    public void Colours_survive_a_dwg_2000_round_trip()
    {
        // Il DWG 2000 ha solo i 255 colori indicizzati: quelli esatti restano tali, gli altri vanno al più vicino.
        var document = new CadDocument();
        var cyan = document.GetOrAddLayer("CIANO");
        cyan.Color = new CadColor(0, 255, 255);
        var grey = document.GetOrAddLayer("GRIGIO");
        grey.Color = new CadColor(128, 128, 128);
        var orange = new LineEntity(cyan, Vector2.Zero, new Vector2(10, 0)) { Color = EntityColor.Explicit(new CadColor(250, 130, 10)) };
        document.Edit("test", e => e.Add(orange));
        var path = Path.Combine(_folder, "colori.dwg");
        CadFile.Save(document, path);

        var reloaded = CadFile.Load(path).Document;
        Assert.Equal(new CadColor(0, 255, 255), reloaded.FindLayer("CIANO")!.Color);
        Assert.Equal(new CadColor(128, 128, 128), reloaded.FindLayer("GRIGIO")!.Color);
        var colour = reloaded.ModelSpace.Single().Color.Value;
        Assert.True(Math.Abs(colour.R - 250) < 40 && Math.Abs(colour.G - 130) < 40 && colour.B < 60, $"atteso arancione, ottenuto {colour}");
    }
}

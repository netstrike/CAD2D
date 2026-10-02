using Cad.Document;

namespace Cad.IO.Tests;

/// <summary>Traguardo della fase 3: una tavola completa fatta solo con i comandi, salvata e riletta.</summary>
public sealed class SampleDrawingTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("cad2d-test").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Commands_build_the_whole_sheet_without_errors()
    {
        var (document, messages) = SampleDrawing.Build();

        Assert.DoesNotContain(messages, m => m.Contains("sconosciuto") || m.Contains("non valid") || m.Contains("Serve") || m.Contains("errore"));
        var model = document.ModelSpace.ToList();
        Assert.Equal(8, model.OfType<DimensionEntity>().Count());
        Assert.Equal(4, model.OfType<InsertEntity>().Count(i => i.Block.Name == "FORO"));
        Assert.Equal(2, model.OfType<HatchEntity>().Count());
        Assert.Equal("CENTER", document.FindLayer("ASSI")!.Linetype.Name);
        Assert.Contains(model.OfType<DimensionEntity>(), d => d.Text == "R8");
        Assert.Contains(model.OfType<DimensionEntity>(), d => d.Text == "Ø40");
    }

    [Fact]
    public void Sheet_survives_save_and_reload()
    {
        var (document, _) = SampleDrawing.Build();
        var path = Path.Combine(_folder, "tavola.dxf");
        DxfExporter.Save(document, path);

        var reloaded = DxfImporter.Load(path);
        Assert.Empty(reloaded.Errors);
        Assert.Empty(reloaded.Document.UnsupportedEntities);
        var before = document.ModelSpace.GroupBy(e => (e.GetType(), e.Layer.Name)).ToDictionary(g => g.Key, g => g.Count());
        var after = reloaded.Document.ModelSpace.GroupBy(e => (e.GetType(), e.Layer.Name)).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(before, after);
        Assert.Equal(document.Bounds.Max.X, reloaded.Document.Bounds.Max.X, 6);
    }
}

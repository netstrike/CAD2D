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

public sealed class DxfDraftingRoundTripTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("cad2d-test").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Groups_survive_save_and_reload()
    {
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("0");
        var group = document.AddGroup("VITE")!;
        group.Description = "vite M8";
        var unnamed = document.AddGroup()!;
        document.Edit("test", e =>
        {
            e.Add(new LineEntity(layer, Vector2.Zero, new Vector2(10, 0)) { Group = group });
            e.Add(new CircleEntity(layer, Vector2.Zero, 4) { Group = group });
            e.Add(new CircleEntity(layer, new Vector2(20, 0), 4) { Group = unnamed });
            e.Add(new CircleEntity(layer, new Vector2(30, 0), 4));
        });

        var path = Path.Combine(_folder, "gruppi.dxf");
        DxfExporter.Save(document, path);
        var doc = DxfImporter.Load(path).Document;
        var reloaded = doc.FindGroup("VITE")!;
        Assert.Equal("vite M8", reloaded.Description);
        Assert.Equal(2, doc.Members(reloaded).Count());
        Assert.Single(doc.Groups, g => g.IsUnnamed);
        Assert.Single(doc.ModelSpace, e => e.Group is null);

        // Separati e risalvati: i gruppi spariscono dal file.
        doc.Edit("test", e =>
        {
            foreach (var entity in doc.ModelSpace.ToList())
            {
                var copy = entity.Transformed(Matrix2D.Identity);
                copy.Group = null;
                e.Replace(entity, copy);
            }
        });
        DxfExporter.Save(doc, path);
        Assert.Empty(DxfImporter.Load(path).Document.Groups);
    }

    [Fact]
    public void Leaders_new_dimensions_and_text_styles_survive_save_and_reload()
    {
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("0");
        var style = document.GetOrAddTextStyle("Titoli");
        style.FontFamily = "Times New Roman";
        style.Height = 5;
        style.ObliqueAngle = 15 * Math.PI / 180;
        document.CurrentTextStyle = style;
        var dimensionStyle = document.CurrentDimensionStyle;
        dimensionStyle.BaselineSpacing = 6;
        var group = document.AddGroup()!;
        group.Description = AnnotationGroups.Leader;
        document.Edit("test", e =>
        {
            e.Add(new LeaderEntity(layer, [Vector2.Zero, new Vector2(10, 10), new Vector2(15, 10)], dimensionStyle) { Group = group });
            e.Add(new TextEntity(layer, new Vector2(16, 10), 2.5, "Foro") { Style = style, Group = group });
            e.Add(new DimensionEntity(layer, DimensionKind.Ordinate, dimensionStyle)
            {
                Vertex = new Vector2(5, 5), First = new Vector2(30, 20), Second = new Vector2(30, 40), Location = new Vector2(30, 40), OrdinateX = true,
            });
            e.Add(new DimensionEntity(layer, DimensionKind.ArcLength, dimensionStyle)
            {
                Vertex = Vector2.Zero, First = new Vector2(10, 0), Second = new Vector2(0, 10), Location = new Vector2(15, 15),
            });
        });

        var path = Path.Combine(_folder, "annotazioni.dxf");
        DxfExporter.Save(document, path);
        // Un secondo salvataggio riscrive i gruppi senza nome senza conflitti di nomi.
        document.Edit("test", e => e.Add(new CircleEntity(layer, Vector2.Zero, 1) { Group = document.AddGroup() }));
        DxfExporter.Save(document, path);
        var doc = DxfImporter.Load(path).Document;

        var leader = Assert.Single(doc.ModelSpace.OfType<LeaderEntity>());
        Assert.Equal(3, leader.Vertices.Count);
        Assert.Equal(new Vector2(10, 10), leader.Vertices[1]);
        var text = Assert.Single(doc.ModelSpace.OfType<TextEntity>());
        Assert.Same(leader.Group, text.Group);
        Assert.Equal("Titoli", text.Style!.Name);
        Assert.Equal("Times New Roman", text.Style.FontFamily);
        Assert.Equal(5, text.Style.Height, 9);
        Assert.Equal(15 * Math.PI / 180, text.Style.ObliqueAngle, 6);
        Assert.Equal("Titoli", doc.CurrentTextStyle.Name);
        Assert.Equal(6, doc.CurrentDimensionStyle.BaselineSpacing, 9);

        var ordinate = Assert.Single(doc.ModelSpace.OfType<DimensionEntity>(), d => d.Kind == DimensionKind.Ordinate);
        Assert.True(ordinate.OrdinateX);
        Assert.Equal(25, ordinate.Measurement, 9);
        var arc = Assert.Single(doc.ModelSpace.OfType<DimensionEntity>(), d => d.Kind == DimensionKind.ArcLength);
        Assert.Equal(5 * Math.PI, arc.Measurement, 9);
    }

    [Fact]
    public void Lineweights_of_layers_and_entities_survive_save_and_reload()
    {
        var document = new CadDocument();
        var outline = document.GetOrAddLayer("CONTORNO");
        outline.LineWeight = 50;
        document.Edit("test", e =>
        {
            e.Add(new LineEntity(outline, Vector2.Zero, new Vector2(10, 0)));
            e.Add(new LineEntity(outline, Vector2.Zero, new Vector2(0, 10)) { LineWeight = 18 });
        });

        var path = Path.Combine(_folder, "spessori.dxf");
        DxfExporter.Save(document, path);
        var doc = DxfImporter.Load(path).Document;

        Assert.Equal(50, doc.FindLayer("CONTORNO")!.LineWeight);
        Assert.Equal(LineWeight.Default, doc.FindLayer("0")!.LineWeight);
        Assert.Equal([LineWeight.ByLayer, 18], doc.ModelSpace.Select(e => e.LineWeight).Order());

        // Cambio dello spessore di un'entità letta dal file: va scritto anche sulla copia dell'originale.
        var line = doc.ModelSpace.First(e => e.LineWeight == 18);
        var copy = line.Transformed(Matrix2D.Identity);
        copy.LineWeight = 35;
        doc.Edit("test", e => e.Replace(line, copy));
        DxfExporter.Save(doc, path);
        Assert.Contains(DxfImporter.Load(path).Document.ModelSpace, e => e.LineWeight == 35);
    }

    [Fact]
    public void Linetypes_hatches_and_solids_survive_save_and_reload()
    {
        var document = new CadDocument { LinetypeScale = 0.5 };
        var axes = document.GetOrAddLayer("ASSI");
        axes.Linetype = document.FindLinetype("CENTER")!;
        axes.Color = new CadColor(255, 0, 0);
        var layer0 = document.GetOrAddLayer("0");
        document.Edit("test", e =>
        {
            e.Add(new LineEntity(axes, new Vector2(-10, 0), new Vector2(110, 0)));
            e.Add(new LineEntity(layer0, Vector2.Zero, new Vector2(0, 50)) { Linetype = document.FindLinetype("HIDDEN"), LinetypeScale = 2 });
            e.Add(new SolidEntity(layer0, [new Vector2(0, 0), new Vector2(5, 0), new Vector2(5, 2)]));
            e.Add(new HatchEntity(layer0,
                [
                    [new(new Vector2(0, 0)), new(new Vector2(100, 0)), new(new Vector2(100, 50)), new(new Vector2(0, 50))],
                    [new(new Vector2(40, 25), 1), new(new Vector2(60, 25), 1)],
                ],
                "ANSI31", false, HatchPatterns.Build("ANSI31", 2, 0)) { PatternScale = 2 });
        });

        var path = Path.Combine(_folder, "tratteggi.dxf");
        DxfExporter.Save(document, path);
        var reloaded = DxfImporter.Load(path);

        Assert.Empty(reloaded.Errors);
        var doc = reloaded.Document;
        Assert.Equal(0.5, doc.LinetypeScale);
        Assert.Equal("CENTER", doc.FindLayer("ASSI")!.Linetype.Name);
        Assert.Equal(4, doc.FindLinetype("CENTER")!.Pattern.Count);
        var hidden = doc.ModelSpace.OfType<LineEntity>().Single(l => l.Layer.Name == "0");
        Assert.Equal("HIDDEN", hidden.Linetype?.Name);
        Assert.Equal(2, hidden.LinetypeScale);
        Assert.Null(doc.ModelSpace.OfType<LineEntity>().Single(l => l.Layer.Name == "ASSI").Linetype);

        var solid = Assert.Single(doc.ModelSpace.OfType<SolidEntity>());
        Assert.Equal(3, solid.Corners.Count);

        var hatch = Assert.Single(doc.ModelSpace.OfType<HatchEntity>());
        Assert.Equal("ANSI31", hatch.PatternName);
        Assert.Equal(2, hatch.Loops.Count);
        Assert.Equal(1, hatch.Loops[1][0].Bulge, 9);
        var line = Assert.Single(hatch.PatternLines);
        Assert.Equal(Math.PI / 4, line.Angle, 6);
        Assert.Equal(6.35, line.Offset.Length, 6);
        Assert.Equal(new BoundingBox(Vector2.Zero, new Vector2(100, 50)), hatch.Bounds);
    }

    [Fact]
    public void Dimensions_and_texts_survive_save_and_reload()
    {
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("QUOTE");
        var style = document.CurrentDimensionStyle;
        style.TextHeight = 3.5;
        document.Edit("test", e =>
        {
            e.Add(new DimensionEntity(layer, DimensionKind.Linear, style) { First = new Vector2(0, 0), Second = new Vector2(100, 30), Location = new Vector2(50, 50) });
            e.Add(new DimensionEntity(layer, DimensionKind.Aligned, style) { First = new Vector2(0, 0), Second = new Vector2(30, 40), Location = new Vector2(-10, 30), TextOverride = "<> max" });
            e.Add(new DimensionEntity(layer, DimensionKind.Radius, style) { First = new Vector2(200, 0), Second = new Vector2(210, 0), Location = new Vector2(220, 5) });
            e.Add(new DimensionEntity(layer, DimensionKind.Diameter, style) { First = new Vector2(200, 0), Second = new Vector2(200, 10), Location = new Vector2(200, 20) });
            e.Add(new DimensionEntity(layer, DimensionKind.Angular, style) { Vertex = new Vector2(0, 100), First = new Vector2(50, 100), Second = new Vector2(0, 150), Location = new Vector2(30, 130) });
            e.Add(new TextEntity(layer, new Vector2(0, -20), 5, "Riga uno\nRiga due"));
        });

        var path = Path.Combine(_folder, "quote.dxf");
        DxfExporter.Save(document, path);
        var reloaded = DxfImporter.Load(path);

        Assert.Empty(reloaded.Errors);
        var dimensions = reloaded.Document.ModelSpace.OfType<DimensionEntity>().ToList();
        Assert.Equal(5, dimensions.Count);
        Assert.Equal(100, dimensions.Single(d => d.Kind == DimensionKind.Linear).Measurement, 9);
        var aligned = dimensions.Single(d => d.Kind == DimensionKind.Aligned);
        Assert.Equal(50, aligned.Measurement, 9);
        Assert.Equal("50 max", aligned.Text);
        Assert.Equal(10, dimensions.Single(d => d.Kind == DimensionKind.Radius).Measurement, 9);
        Assert.Equal(20, dimensions.Single(d => d.Kind == DimensionKind.Diameter).Measurement, 9);
        Assert.Equal(90, dimensions.Single(d => d.Kind == DimensionKind.Angular).Measurement, 9);
        Assert.All(dimensions, d => Assert.Equal("QUOTE", d.Layer.Name));
        Assert.Equal(3.5, dimensions[0].Style.TextHeight, 9);
        Assert.All(dimensions, d => Assert.NotNull(d.Graphics));
        Assert.Equal("Riga uno\nRiga due", Assert.Single(reloaded.Document.ModelSpace.OfType<TextEntity>()).Value);
    }

    [Fact]
    public void New_block_and_its_inserts_survive_save_and_reload()
    {
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("FORI");
        var block = document.GetOrAddBlock("VITE");
        block.Entities.Add(new CircleEntity(layer, Vector2.Zero, 3));
        block.Entities.Add(new LineEntity(layer, new Vector2(-5, 0), new Vector2(5, 0)));
        document.Edit("test", e =>
        {
            e.Add(new InsertEntity(layer, block) { Transform = InsertEntity.BuildTransform(Vector2.Zero, new Vector2(10, 10), 1, 1, 0) });
            e.Add(new InsertEntity(layer, block) { Transform = InsertEntity.BuildTransform(Vector2.Zero, new Vector2(50, 10), 2, 2, Math.PI / 2) });
        });

        var path = Path.Combine(_folder, "blocchi.dxf");
        DxfExporter.Save(document, path);
        var reloaded = DxfImporter.Load(path);

        Assert.Empty(reloaded.Errors);
        var inserts = reloaded.Document.ModelSpace.OfType<InsertEntity>().ToList();
        Assert.Equal(2, inserts.Count);
        Assert.All(inserts, i => Assert.Equal("VITE", i.Block.Name));
        Assert.Equal(2, inserts[0].Block.Entities.Count);
        var big = inserts.Single(i => i.Position.IsAlmostEqual(new Vector2(50, 10)));
        Assert.True(big.Transform.Transform(new Vector2(5, 0)).IsAlmostEqual(new Vector2(50, 20), 1e-9));
    }

    [Fact]
    public void Renamed_layer_is_renamed_in_the_file_also_for_untouched_entities()
    {
        var path = Path.Combine(_folder, "rinomina.dxf");
        var source = new Acad.CadDocument();
        var acadLayer = new Acad.Tables.Layer("VECCHIO");
        source.Layers.Add(acadLayer);
        source.Entities.Add(new Acad.Entities.Line { StartPoint = new CSMath.XYZ(0, 0, 0), EndPoint = new CSMath.XYZ(10, 0, 0), Layer = acadLayer });
        using (var writer = new Acad.IO.DxfWriter(path, source, false))
        {
            writer.Write();
        }

        var document = DxfImporter.Load(path).Document;
        Assert.True(document.RenameLayer(document.FindLayer("VECCHIO")!, "NUOVO"));
        DxfExporter.Save(document, path);

        var reloaded = DxfImporter.Load(path).Document;
        Assert.Null(reloaded.FindLayer("VECCHIO"));
        Assert.Equal("NUOVO", Assert.Single(reloaded.ModelSpace.OfType<LineEntity>()).Layer.Name);
    }

    [Fact]
    public void Edited_text_from_file_keeps_the_new_value_after_save()
    {
        var path = Path.Combine(_folder, "testo.dxf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "samples", "demo.dxf"), path);
        var document = DxfImporter.Load(path).Document;
        var editor = new Cad.Editing.Editor(document) { SnapEnabled = false };
        editor.SubmitText("ED");
        editor.Click(new Vector2(5, 136), 0.5);
        editor.SubmitText("Modificato");
        editor.SubmitText("");
        Assert.Contains(document.ModelSpace.OfType<TextEntity>(), t => t.Value == "Modificato");

        DxfExporter.Save(document, path);
        var reloaded = DxfImporter.Load(path).Document;
        Assert.Contains(reloaded.ModelSpace.OfType<TextEntity>(), t => t.Value == "Modificato");
        Assert.DoesNotContain(reloaded.ModelSpace.OfType<TextEntity>(), t => t.Value.StartsWith("PIASTRA"));
    }
}

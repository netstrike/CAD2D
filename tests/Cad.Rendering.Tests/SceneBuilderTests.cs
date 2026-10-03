using Cad.Document;
using Cad.Geometry;
using Cad.Rendering;

namespace Cad.Rendering.Tests;

public class SceneBuilderTests
{
    private static readonly CadColor Red = new(255, 0, 0);
    private static readonly CadColor Green = new(0, 255, 0);
    private static readonly CadColor Blue = new(0, 0, 255);

    [Fact]
    public void Entities_on_hidden_layers_are_skipped()
    {
        var document = new CadDocument();
        var hidden = document.GetOrAddLayer("H");
        hidden.IsOn = false;
        document.ModelSpace.Add(new LineEntity(hidden, Vector2.Zero, new Vector2(1, 1)));
        document.ModelSpace.Add(new LineEntity(document.GetOrAddLayer("V"), Vector2.Zero, new Vector2(1, 1)));

        Assert.Equal(1, SceneBuilder.Build(document).EntityCount);
    }

    [Fact]
    public void Block_entities_on_layer_zero_take_the_insert_layer_color()
    {
        var document = new CadDocument();
        var layer0 = document.GetOrAddLayer(Layer.DefaultName);
        layer0.Color = Blue;
        var holes = document.GetOrAddLayer("FORI");
        holes.Color = Red;

        var block = document.GetOrAddBlock("B");
        block.Entities.Add(new LineEntity(layer0, Vector2.Zero, new Vector2(1, 0)));
        document.ModelSpace.Add(new InsertEntity(holes, block));

        var batch = Assert.Single(SceneBuilder.Build(document).Batches);
        Assert.Equal(Red, batch.Color);
    }

    [Fact]
    public void ByBlock_color_comes_from_the_insert()
    {
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("A");
        var block = document.GetOrAddBlock("B");
        block.Entities.Add(new LineEntity(layer, Vector2.Zero, new Vector2(1, 0)) { Color = EntityColor.ByBlock });
        document.ModelSpace.Add(new InsertEntity(layer, block) { Color = EntityColor.Explicit(Green) });

        Assert.Equal(Green, Assert.Single(SceneBuilder.Build(document).Batches).Color);
    }

    [Fact]
    public void Nested_inserts_compose_transforms()
    {
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("A");
        var inner = document.GetOrAddBlock("INNER");
        inner.Entities.Add(new LineEntity(layer, Vector2.Zero, new Vector2(1, 0)));
        var outer = document.GetOrAddBlock("OUTER");
        outer.Entities.Add(new InsertEntity(layer, inner) { Transform = Matrix2D.Translation(new Vector2(10, 0)) });
        document.ModelSpace.Add(new InsertEntity(layer, outer) { Transform = Matrix2D.Scaling(2, 2) });

        var line = Assert.Single(Assert.Single(SceneBuilder.Build(document).Batches).Polylines);
        Assert.True(line[0].IsAlmostEqual(new Vector2(20, 0)));
        Assert.True(line[1].IsAlmostEqual(new Vector2(22, 0)));
    }

    [Fact]
    public void Self_referencing_block_does_not_recurse_forever()
    {
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("A");
        var block = document.GetOrAddBlock("LOOP");
        block.Entities.Add(new LineEntity(layer, Vector2.Zero, new Vector2(1, 0)));
        block.Entities.Add(new InsertEntity(layer, block));
        document.ModelSpace.Add(new InsertEntity(layer, block));

        Assert.True(SceneBuilder.Build(document).EntityCount > 0);
    }

    [Fact]
    public void Circle_is_closed_and_stays_on_radius()
    {
        var points = SceneBuilder.Tessellate(new Arc2D(new Vector2(3, 4), 10, 0, Math.Tau), Matrix2D.Identity);
        Assert.True(points[0].IsAlmostEqual(points[^1], 1e-9));
        Assert.All(points, p => Assert.Equal(10, Vector2.Distance(p, new Vector2(3, 4)), 9));
    }

    [Fact]
    public void Multiline_text_becomes_one_render_line_per_row()
    {
        var document = new CadDocument();
        document.ModelSpace.Add(new TextEntity(document.GetOrAddLayer("T"), Vector2.Zero, 2, "uno\ndue\ntre")
        {
            VerticalAlignment = TextVerticalAlignment.Top,
        });

        var texts = SceneBuilder.Build(document).Texts;
        Assert.Equal(3, texts.Count);
        // Allineato in alto: la prima linea di base è un'altezza sotto il punto di inserimento, le altre scendono.
        Assert.Equal(-2, texts[0].Position.Y, 9);
        Assert.True(texts[1].Position.Y < texts[0].Position.Y);
    }
}

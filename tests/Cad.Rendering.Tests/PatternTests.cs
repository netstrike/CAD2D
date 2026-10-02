using Cad.Document;
using Cad.Geometry;
using Cad.Rendering;

namespace Cad.Rendering.Tests;

public class PatternTests
{
    [Fact]
    public void Dashed_line_alternates_dashes_and_gaps()
    {
        var dashes = Patterns.Dash([Vector2.Zero, new Vector2(100, 0)], [10, -5], 1);

        Assert.Equal(7, dashes.Count);
        Assert.Equal(new Vector2(15, 0), dashes[1][0]);
        Assert.Equal(new Vector2(25, 0), dashes[1][^1]);
        Assert.Equal(new Vector2(100, 0), dashes[^1][^1]);
    }

    [Fact]
    public void Dash_pattern_continues_around_corners()
    {
        var dashes = Patterns.Dash([Vector2.Zero, new Vector2(8, 0), new Vector2(8, 8)], [10, -5], 1);

        // Il primo tratto gira lo spigolo: 8 in orizzontale e 2 in verticale.
        Assert.Equal([Vector2.Zero, new Vector2(8, 0), new Vector2(8, 2)], dashes[0]);
    }

    [Fact]
    public void Too_dense_pattern_falls_back_to_continuous()
    {
        var line = new[] { Vector2.Zero, new Vector2(1e6, 0) };
        Assert.Single(Patterns.Dash(line, [1, -1], 0.001));
    }

    [Fact]
    public void Hatch_lines_fill_a_square_and_skip_the_hole()
    {
        var square = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) };
        var hole = new[] { new Vector2(4, 4), new Vector2(6, 4), new Vector2(6, 6), new Vector2(4, 6) };
        var horizontal = new HatchPatternLine(0, new Vector2(0, 0.5), new Vector2(0, 1), []);

        var segments = Patterns.HatchLines([square, hole], horizontal, new Ref<int>(1000)).ToList();

        // Linee a y = 0.5, 1.5 ... 9.5: le due che attraversano il buco (4.5 e 5.5) sono spezzate in due.
        Assert.Equal(12, segments.Count);
        Assert.Contains(segments, s => s.From == new Vector2(0, 4.5) && s.To == new Vector2(4, 4.5));
        Assert.Contains(segments, s => s.From == new Vector2(6, 4.5) && s.To == new Vector2(10, 4.5));
    }

    [Fact]
    public void Scene_draws_dashed_layer_and_hatch()
    {
        var document = new CadDocument();
        var hidden = document.GetOrAddLayer("NASCOSTE");
        hidden.Linetype = document.FindLinetype("DASHED")!;
        document.ModelSpace.Add(new LineEntity(hidden, Vector2.Zero, new Vector2(95.25, 0)));
        document.ModelSpace.Add(new HatchEntity(
            document.GetOrAddLayer("0"),
            [[new(new Vector2(0, 10)), new(new Vector2(10, 10)), new(new Vector2(10, 20)), new(new Vector2(0, 20))]],
            HatchPatterns.Solid,
            isSolid: true,
            []));

        var scene = SceneBuilder.Build(document);

        // DASHED = 12.7 tratto, 6.35 spazio: in 95.25 ci stanno 5 ripetizioni complete.
        Assert.Equal(5, scene.Batches.Sum(b => b.Polylines.Count));
        Assert.Single(Assert.Single(scene.Fills).Rings);
    }

    [Fact]
    public void Solid_fill_orients_holes_clockwise()
    {
        var outer = new[] { new Vector2(0, 0), new Vector2(0, 10), new Vector2(10, 10), new Vector2(10, 0) };
        var hole = new[] { new Vector2(4, 4), new Vector2(6, 4), new Vector2(6, 6), new Vector2(4, 6) };

        var rings = Patterns.OrientForWinding([outer, hole]);

        Assert.True(Patterns.SignedArea(rings[0]) > 0);
        Assert.True(Patterns.SignedArea(rings[1]) < 0);
    }
}

using Cad.Document;
using Cad.Geometry;
using Cad.Rendering;

namespace Cad.Rendering.Tests;

/// <summary>Archi esatti e pezzi di scena riusati: ciò che rende scorrevoli i disegni con centomila entità.</summary>
public class SceneSpeedTests
{
    private static Vector2 PointOf(RenderArc arc, double t) => arc.Center + Vector2.FromPolar(arc.Radius, arc.StartAngle + arc.Sweep * t);

    [Fact]
    public void Circles_and_arcs_become_exact_arcs_only_when_asked()
    {
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("0");
        document.ModelSpace.Add(new CircleEntity(layer, new Vector2(10, 0), 5));
        document.ModelSpace.Add(new ArcEntity(layer, Vector2.Zero, 3, 0, Math.PI / 2));

        var exact = Assert.Single(SceneBuilder.Build(document, exactArcs: true).Batches);
        Assert.Empty(exact.Polylines);
        Assert.Equal(2, exact.Arcs.Count);
        Assert.Contains(exact.Arcs, a => a.IsCircle && a.Radius == 5);

        var approximate = Assert.Single(SceneBuilder.Build(document).Batches);
        Assert.Empty(approximate.Arcs);
        Assert.Equal(2, approximate.Polylines.Count);
    }

    [Fact]
    public void Mirrored_block_arcs_keep_their_ends_and_dashed_circles_stay_polylines()
    {
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("0");
        var block = document.GetOrAddBlock("B");
        block.Entities.Add(new ArcEntity(layer, Vector2.Zero, 10, 0, Math.PI / 2));
        document.ModelSpace.Add(new InsertEntity(layer, block) { Transform = new Matrix2D(-1, 0, 0, 1, 100, 0) });
        var dashed = document.GetOrAddLayer("ASSI");
        dashed.Linetype = document.FindLinetype("CENTER")!;
        document.ModelSpace.Add(new CircleEntity(dashed, Vector2.Zero, 50));

        var scene = SceneBuilder.Build(document, exactArcs: true);
        var arc = Assert.Single(scene.Batches.SelectMany(b => b.Arcs));
        // L'arco da (10,0) a (0,10) specchiato attorno a x = 100 va da (90,0) a (100,10).
        var ends = new[] { PointOf(arc, 0), PointOf(arc, 1) };
        Assert.Contains(ends, p => p.IsAlmostEqual(new Vector2(90, 0), 1e-9));
        Assert.Contains(ends, p => p.IsAlmostEqual(new Vector2(100, 10), 1e-9));
        Assert.True(scene.Batches.Sum(b => b.Polylines.Count) > 1);
    }

    [Fact]
    public void Polyline_bulges_become_arcs_between_straight_runs()
    {
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("0");
        // Asola: due lati dritti e due semicerchi.
        document.ModelSpace.Add(new PolylineEntity(layer, [new(new Vector2(0, 0)), new(new Vector2(20, 0), 1), new(new Vector2(20, 10)), new(new Vector2(0, 10), 1)], isClosed: true));

        var batch = Assert.Single(SceneBuilder.Build(document, exactArcs: true).Batches);
        Assert.Equal(2, batch.Arcs.Count);
        Assert.Equal(2, batch.Polylines.Count);
        Assert.All(batch.Arcs, a => Assert.Equal(Math.PI, a.Sweep, 9));
        var bounds = SceneBuilder.Build(document, exactArcs: true).Bounds;
        Assert.True(bounds.Min.IsAlmostEqual(new Vector2(-5, 0), 1e-9));
        Assert.True(bounds.Max.IsAlmostEqual(new Vector2(25, 10), 1e-9));
    }

    [Fact]
    public void Cache_recomputes_only_what_changed()
    {
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("0");
        var lines = Enumerable.Range(0, 50).Select(i => new LineEntity(layer, new Vector2(i, 0), new Vector2(i, 10))).ToList();
        document.Edit("crea", e => lines.ForEach(e.Add));
        var cache = new SceneCache();
        SceneBuilder.Build(document, true, cache);
        Assert.Equal(50, cache.Computed);

        document.Edit("sposta", e => e.Replace(lines[3], lines[3].Transformed(Matrix2D.Translation(new Vector2(0, 5)))));
        var scene = SceneBuilder.Build(document, true, cache);
        Assert.Equal((49, 1), (cache.Reused, cache.Computed));
        Assert.Equal(50, scene.EntityCount);

        // Colore cambiato sull'entità stessa: il suo pezzo si rifà.
        lines[7].Color = EntityColor.Explicit(new CadColor(255, 0, 0));
        scene = SceneBuilder.Build(document, true, cache);
        Assert.Equal(1, cache.Computed);
        Assert.Equal(2, scene.Batches.Count);

        // Colore del layer: vale per tutti.
        layer.Color = new CadColor(0, 255, 0);
        scene = SceneBuilder.Build(document, true, cache);
        Assert.Equal(50, cache.Computed);
        Assert.Contains(scene.Batches, b => b.Color == new CadColor(0, 255, 0) && b.Polylines.Count == 49);
    }
}

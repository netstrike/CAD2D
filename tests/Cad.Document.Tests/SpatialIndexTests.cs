using Cad.Document;
using Cad.Geometry;

namespace Cad.Document.Tests;

public class SpatialIndexTests
{
    [Fact]
    public void Query_matches_brute_force_on_random_boxes()
    {
        var random = new Random(7);
        var items = Enumerable.Range(0, 5000).Select(i =>
        {
            var p = new Vector2(random.NextDouble() * 1000, random.NextDouble() * 1000);
            var box = BoundingBox.FromPoints(p, p + new Vector2(random.NextDouble() * 20, random.NextDouble() * 20));
            return (Item: i, Bounds: box);
        }).ToList();
        var index = new SpatialIndex<int>(items);

        for (var q = 0; q < 50; q++)
        {
            var p = new Vector2(random.NextDouble() * 1000, random.NextDouble() * 1000);
            var area = BoundingBox.FromPoints(p, p + new Vector2(random.NextDouble() * 150, random.NextDouble() * 150));
            var expected = items.Where(i => i.Bounds.Intersects(area)).Select(i => i.Item).Order();
            Assert.Equal(expected, index.Query(area).Order());
        }
    }

    [Fact]
    public void Empty_index_returns_nothing()
    {
        var index = new SpatialIndex<string>([]);
        Assert.Empty(index.Query(BoundingBox.FromPoints(Vector2.Zero, new Vector2(1, 1))));
        Assert.True(index.Bounds.IsEmpty);
    }

    [Fact]
    public void Items_without_extent_are_skipped()
    {
        var index = new SpatialIndex<string>([("vuoto", BoundingBox.Empty), ("pieno", BoundingBox.FromPoints(Vector2.Zero, new Vector2(1, 1)))]);
        Assert.Equal(1, index.Count);
    }
}

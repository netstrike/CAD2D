using Cad.Document;
using Cad.Geometry;

namespace Cad.Document.Tests;

/// <summary>Modifiche con migliaia di entità: la mappa delle posizioni deve dare gli stessi risultati della ricerca lineare.</summary>
public sealed class LargeEditTests
{
    [Fact]
    public void Random_adds_removes_and_replaces_keep_the_order_and_undo_restores_it()
    {
        var document = new CadDocument();
        var layer = document.GetOrAddLayer("0");
        var lines = Enumerable.Range(0, 300).Select(i => new LineEntity(layer, new Vector2(i, 0), new Vector2(i, 1))).ToList();
        document.Edit("crea", e => lines.ForEach(e.Add));
        var before = document.ModelSpace.ToList();

        var random = new Random(7);
        var expected = document.ModelSpace.ToList();
        document.Edit("mescola", e =>
        {
            for (var step = 0; step < 6000; step++)
            {
                switch (random.Next(3))
                {
                    case 0:
                        var added = new LineEntity(layer, new Vector2(-step, 0), new Vector2(-step, 1));
                        e.Add(added);
                        expected.Add(added);
                        break;
                    case 1 when expected.Count > 0:
                        var removed = expected[random.Next(expected.Count)];
                        e.Remove(removed);
                        expected.Remove(removed);
                        break;
                    case 2 when expected.Count > 0:
                        var index = random.Next(expected.Count);
                        var replacement = expected[index].Transformed(Matrix2D.Translation(new Vector2(0, 5)));
                        e.Replace(expected[index], replacement);
                        expected[index] = replacement;
                        break;
                }
            }
        });

        Assert.Equal(expected, document.ModelSpace);
        document.History.Undo();
        Assert.Equal(before, document.ModelSpace);
    }
}
